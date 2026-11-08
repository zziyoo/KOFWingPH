using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Team;

/// <summary>按双方当前出战队员产生对局配置的工厂（纯函数；镜像角色允许返回同一不可变配置实例）。</summary>
public delegate FightRoster TeamRosterFactory(string p1FighterId, string p2FighterId);

/// <summary>
/// 生成一场交锋的初始权威状态（阶段 10A，D120）。入参是<b>双方本场起始体力</b>
/// （胜方继承上一场剩余体力，新上场队员为其满血值），实现方负责位置、朝向与计时初值。
/// </summary>
public delegate BattleState TeamBoutStateFactory(
    TeamMatchRules rules, TeamSideState p1, TeamSideState p2);

/// <summary>
/// <b>队伍赛会话</b>（阶段 10A，D120）——与 09A 的 1v1 <c>MatchSession</c> 同构：
/// 一个 TeamMatchSession 管理多个 <see cref="BattleSession"/>，<b>每场交锋</b>一个，
/// Frame 与 Scheduler.TotalSteps 都从 0 开始。
///
/// <para><b>为什么不在 BattleState 里塞六名角色</b>：场上永远是 1v1，
/// <c>BattleSimulation</c> 只支持两人是正确且应保留的架构。3v3 属于比赛级组织：
/// 谁在场上、谁被淘汰、下一场谁上、体力怎么继承。</para>
///
/// <para><b>职责</b>：读取内核已结束交锋的 <see cref="RoundState"/> 完成<b>一次</b>淘汰与接替，
/// 组织下一场交锋；不判定伤害、不判定 KO、不自行统计画面文本。</para>
/// </summary>
public sealed class TeamMatchSession
{
    private readonly TeamRosterFactory _rosterFactory;
    private readonly TeamBoutStateFactory _initialStateFactory;
    private readonly Action<TeamMatchEvent>? _eventsCommitted;
    private readonly Action<PresentationFrame>? _framePresented;
    private readonly int _maxStepsPerAdvance;

    private BattleSession? _boutSession;
    private IReadOnlyList<TeamMatchEvent> _lastEvents = Array.Empty<TeamMatchEvent>();

    /// <summary>是否正在向外部观察者投递本帧事件（D118 同构）：期间禁止重入修改比赛会话。</summary>
    private bool _publishing;

    public TeamMatchSession(
        TeamMatchRules rules,
        TeamRosterFactory rosterFactory,
        TeamBoutStateFactory? initialStateFactory = null,
        Action<TeamMatchEvent>? eventsCommitted = null,
        int maxStepsPerAdvance = 5,
        Action<PresentationFrame>? framePresented = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(rosterFactory);
        if (maxStepsPerAdvance <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxStepsPerAdvance), maxStepsPerAdvance,
                "单次最多推进帧数必须为正。");

        Rules = rules;
        _rosterFactory = rosterFactory;
        _initialStateFactory = initialStateFactory ?? DefaultInitialState;
        _eventsCommitted = eventsCommitted;
        _framePresented = framePresented;
        _maxStepsPerAdvance = maxStepsPerAdvance;
        State = TeamMatchState.NotStarted(rules);
        State.Validate();
    }

    /// <summary>本场比赛规则。</summary>
    public TeamMatchRules Rules { get; }

    /// <summary>当前权威队伍赛状态（只读）。</summary>
    public TeamMatchState State { get; private set; }

    /// <summary>当前交锋的 BattleSession（过渡/终局/未开始时为 null）。</summary>
    public BattleSession? CurrentBout => _boutSession;

    /// <summary>当前交锋的权威战斗状态（过渡/终局时为 null）。</summary>
    public BattleState? CurrentBattleState => _boutSession?.CurrentState;

    /// <summary>
    /// 当前交锋专属的输入缓冲。每场交锋 <b>新建</b>，绝不与上一场共享同一个可变 InputBuffer。
    /// </summary>
    public InputBuffer BoutInputBuffer { get; private set; } = new();

    /// <summary>最近一个已提交逻辑帧的队伍赛事件（只读）。</summary>
    public IReadOnlyList<TeamMatchEvent> LastEvents => _lastEvents;

    /// <summary>当前交锋是否满足 BattleSession 的帧同步不变量。</summary>
    public bool IsFrameSyncIntact => _boutSession?.IsFrameSyncIntact ?? true;

    /// <summary>选择/更换双方队伍（仅 NotStarted 允许）。</summary>
    public void SelectTeams(TeamDefinition p1Team, TeamDefinition p2Team)
    {
        EnsureNotPublishing(nameof(SelectTeams));
        State = State.WithTeams(p1Team, p2Team);
        State.Validate();
    }

    /// <summary>开始整场比赛（NotStarted → 第 1 场交锋）。</summary>
    public void StartMatch()
    {
        EnsureNotPublishing(nameof(StartMatch));
        if (State.Phase != TeamMatchPhase.NotStarted)
            throw new InvalidOperationException(
                $"StartMatch 只允许在 NotStarted 阶段调用，当前是 {State.Phase}。");
        if (State.P1 is null || State.P2 is null)
            throw new InvalidOperationException("开赛前必须先选择双方队伍。");

        BeginBout();
    }

    /// <summary>
    /// 开始下一场交锋（仅 BetweenBouts 允许）。新交锋重建 BattleSession / InputBuffer /
    /// BattleState，保留规则、双方队伍、淘汰记录与胜方剩余体力。
    /// </summary>
    public void StartNextBout()
    {
        EnsureNotPublishing(nameof(StartNextBout));
        if (State.Phase != TeamMatchPhase.BetweenBouts)
            throw new InvalidOperationException(
                $"StartNextBout 只允许在 BetweenBouts 阶段调用，当前是 {State.Phase}。");

        BeginBout();
    }

    /// <summary>重新开始整场队伍赛：保留双方队伍，清零淘汰与体力。</summary>
    public void RestartMatch()
    {
        EnsureNotPublishing(nameof(RestartMatch));
        State = State.Restart();
        State.Validate();
        StartMatch();
    }

    /// <summary>返回选队：清空队伍、淘汰记录与当前交锋会话。</summary>
    public void ReturnToSelection()
    {
        EnsureNotPublishing(nameof(ReturnToSelection));
        State = State.ReturnToSelection();
        State.Validate();
        ClearBout();
    }

    /// <summary>用当前交锋专属输入缓冲按渲染耗时推进，返回本次实际提交的逻辑帧数。</summary>
    public int Advance(double deltaSeconds) => Advance(deltaSeconds, BoutInputBuffer);

    /// <summary>
    /// 用自定义输入源推进（测试/回放用）。过渡与终局阶段不推进任何战斗帧。
    ///
    /// <para><b>批次边界（D117 同构）</b>：底层一次 <c>Advance</c> 可能提交多帧，
    /// 比赛层只允许提交到「本场交锋的终局帧」为止；未使用的整帧预算记为丢弃预算。</para>
    /// </summary>
    public int Advance(double deltaSeconds, IInputSource inputSource)
    {
        EnsureNotPublishing(nameof(Advance));
        if (!State.IsBoutLive || _boutSession is null)
            return 0;

        return _boutSession.Advance(
            deltaSeconds,
            inputSource: inputSource,
            stopAfterCommit: () => !State.IsBoutLive);
    }

    /// <summary>用当前交锋专属输入缓冲显式推进指定帧数（测试/调试用）。</summary>
    public void StepExact(int count) => StepExactCore(count, BoutInputBuffer, null);

    /// <summary>用自定义输入提供器显式推进指定帧数。过渡与终局阶段不推进任何战斗帧。</summary>
    public void StepExact(int count, Func<int, FrameInput> inputProvider) =>
        StepExactCore(count, null, inputProvider);

    /// <summary>用自定义两阶段输入源显式推进指定帧数（测试/回放用）。</summary>
    public void StepExact(int count, IInputSource inputSource) =>
        StepExactCore(count, inputSource, null);

    private void StepExactCore(
        int count, IInputSource? inputSource, Func<int, FrameInput>? inputProvider)
    {
        EnsureNotPublishing(nameof(StepExact));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "步数不能为负。");
        if (!State.IsBoutLive || _boutSession is null)
            return;

        // 逐帧提交：交锋一旦结束立即停止，剩余帧不会在过渡阶段推进。
        for (int i = 0; i < count && State.IsBoutLive; i++)
            _boutSession.StepExact(1, inputProvider: inputProvider, inputSource: inputSource);
    }

    // ── 交锋生命周期 ────────────────────────────────────────────────

    private void BeginBout()
    {
        State = State.StartBout();
        State.Validate();

        TeamSideState p1 = State.P1!;
        TeamSideState p2 = State.P2!;

        FightRoster roster = _rosterFactory(p1.ActiveFighterId, p2.ActiveFighterId);
        BattleState initialState = _initialStateFactory(Rules, p1, p2);

        // 每场交锋全新输入缓冲：旧按住状态、边沿闩锁、方向历史、消费标记一并清除。
        BoutInputBuffer = new InputBuffer();
        _lastEvents = Array.Empty<TeamMatchEvent>();

        _boutSession = new BattleSession(
            initialState: initialState,
            scheduler: new FixedStepScheduler(_maxStepsPerAdvance),
            simulation: new BattleSimulation(roster),
            eventsCommitted: OnFrameCommitted);
    }

    private void ClearBout()
    {
        _boutSession = null;
        BoutInputBuffer = new InputBuffer();
        _lastEvents = Array.Empty<TeamMatchEvent>();
    }

    /// <summary>
    /// 每个<b>已成功提交</b>的逻辑帧之后调用一次（D83/D118 同构）：<b>先</b>同步计时、
    /// 若本场交锋刚结束则完成唯一一次淘汰/接替与队伍终局判定并校验不变式，<b>再</b>发布事件。
    /// </summary>
    private void OnFrameCommitted(
        FrameInput input, BattleState stateBefore, IReadOnlyList<BattleEvent> events)
    {
        if (!State.IsBoutLive || _boutSession is null)
            return;

        int boutNumber = State.BoutNumber;
        BattleState current = _boutSession.CurrentState;

        if (current.RoundTimerRemainingFrames is int remaining)
            State = State.WithRemainingTime(remaining);

        if (current.Round.IsFinished)
        {
            RoundEndReason reason = current.Round.Reason != RoundEndReason.None
                ? current.Round.Reason
                : current.Round.IsDoubleKO ? RoundEndReason.DoubleKO : RoundEndReason.Knockout;

            State = State.RecordBoutResult(
                current.Round.Outcome, reason,
                current.Fighter(0).Health, current.Fighter(1).Health);
        }

        State.Validate();

        var wrapped = new TeamMatchEvent[events.Count];
        for (int i = 0; i < events.Count; i++)
            wrapped[i] = new TeamMatchEvent(boutNumber, events[i]);
        _lastEvents = Array.AsReadOnly(wrapped);

        // 逐逻辑帧表现快照（D127 同构）：Scope = 交锋号，无事件帧同样发布。
        // 先于逐事件观察者投递，且此时本场交锋的淘汰/体力已落定。
        _publishing = true;
        try
        {
            _framePresented?.Invoke(new PresentationFrame(
                boutNumber, stateBefore.Frame, stateBefore, current, events, _boutSession.LastCommittedStep?.EventAnchors));

            if (_eventsCommitted is not null)
                for (int i = 0; i < wrapped.Length; i++)
                    _eventsCommitted.Invoke(wrapped[i]);
        }
        finally
        {
            _publishing = false;
        }
    }

    /// <summary>禁止在事件观察者中重入修改比赛会话（与 D118 同一理由）。</summary>
    private void EnsureNotPublishing(string operation)
    {
        if (_publishing)
            throw new InvalidOperationException(
                $"{operation} 不能在队伍赛事件观察者中调用：观察者回调期间会话正在收尾。");
    }

    /// <summary>
    /// 默认交锋初态：双方以<b>给定体力</b>在 ±120 站位满状态开战，计时按规则注入。
    /// 不继承任何上一场的招式/硬直/击飞/飞行道具状态（那些都在新建的 BattleState 里清零）。
    /// </summary>
    private static BattleState DefaultInitialState(
        TeamMatchRules rules, TeamSideState p1, TeamSideState p2)
    {
        // 用各自队员的满血值重建（不假定恒为 1000），并只注入继承下来的体力与站位。
        FighterState fighter1 = FighterState.Create(
            id: 0, maxHealth: p1.ActiveMaxHealth, positionX: -120, facing: Facing.Right);
        FighterState fighter2 = FighterState.Create(
            id: 1, maxHealth: p2.ActiveMaxHealth, positionX: 120, facing: Facing.Left);

        IReadOnlyList<FighterState> fighters = new[]
        {
            fighter1 with { Health = p1.ActiveHealth },
            fighter2 with { Health = p2.ActiveHealth },
        };

        foreach (FighterState fighter in fighters)
            fighter.Validate();

        return new BattleState(
            frame: 0, fighters, stateVersion: BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: rules.EnableTimeout ? rules.BoutTimerFrames : null);
    }
}
