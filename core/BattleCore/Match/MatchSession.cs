using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Match;

/// <summary>按选人身份产生本回合对局配置的工厂（纯函数；镜像角色允许返回同一不可变配置实例）。</summary>
public delegate FightRoster RosterFactory(string? p1FighterId, string? p2FighterId);

/// <summary>
/// <b>比赛级会话</b>（阶段 09A，D104/D105/D106）——方案一：一个 MatchSession 管理多个
/// <see cref="BattleSession"/>，每个回合拥有<b>独立</b>的 BattleSession / InputBuffer /
/// BattleState，Frame 从 0 开始、Scheduler.TotalSteps 也从 0 开始。
///
/// <para><b>为什么不选方案二（单一连续 BattleSession）</b>：那需要在 BattleSession 内部做
/// 「权威回合重置」——Frame 全局连续却要重置角色状态，会同时触碰帧同步契约
/// (<c>Frame − 初始帧 == TotalSteps</c>)、D89 的终局冻结与输入提交顺序。方案一让
/// <c>BattleSimulation.Step()</c> / <c>BattleSession</c> / <c>BattleState</c> 的既有语义
/// <b>一行都不改</b>，且天然隔离跨回合输入、攻击实例与飞行道具。</para>
///
/// <para><b>代价与对策</b>：每回合 Frame 重置 → EventId 跨回合会重复。比赛层用
/// <see cref="MatchEvent"/> 的 <c>(RoundNumber, EventId)</c> 组合身份包装事件，
/// 并在文档中明确「裸 EventId 不是全比赛唯一键」。</para>
///
/// <para><b>职责</b>：读取内核已结束回合的 <see cref="RoundState"/> 完成<b>一次</b>权威计分，
/// 管理选人、开新回合、比赛终局与重开；不判定伤害、不判定 KO、不自行统计画面文本。</para>
/// </summary>
public sealed class MatchSession
{
    private readonly RosterFactory _rosterFactory;
    private readonly Func<MatchRules, BattleState> _initialStateFactory;
    private readonly Action<MatchEvent>? _eventsCommitted;
    private readonly int _maxStepsPerAdvance;

    private BattleSession? _roundSession;
    private IReadOnlyList<MatchEvent> _lastEvents = Array.Empty<MatchEvent>();

    /// <summary>
    /// 是否正在向外部观察者投递本帧比赛事件（D118）。为 true 时禁止重入修改比赛会话：
    /// 回调里启动下一回合/重开/返回选人会与正在收尾的批次竞争，必须在入口显式拒绝，
    /// 而不是依赖偶然的调用顺序。
    /// </summary>
    private bool _publishing;

    /// <param name="rules">比赛规则（不可变，重开时保留）。</param>
    /// <param name="rosterFactory">按选人身份生成本回合配置的纯函数。</param>
    /// <param name="initialStateFactory">
    /// 生成每回合的初始 <see cref="BattleState"/>；默认满血、无实体、并按规则注入回合计时初值
    /// （09B 起由内核权威时钟承载）。</param>
    /// <param name="eventsCommitted">比赛级事件观察者（帧完整提交后投递，D83）。</param>
    /// <param name="maxStepsPerAdvance">单次 Advance 最多补的逻辑帧数。</param>
    public MatchSession(
        MatchRules rules,
        RosterFactory rosterFactory,
        Func<MatchRules, BattleState>? initialStateFactory = null,
        Action<MatchEvent>? eventsCommitted = null,
        int maxStepsPerAdvance = 5)
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
        _maxStepsPerAdvance = maxStepsPerAdvance;
        State = MatchState.NotStarted(rules);
        State.Validate();
    }

    /// <summary>本场比赛的规则。</summary>
    public MatchRules Rules { get; }

    /// <summary>当前权威比赛状态（只读）。</summary>
    public MatchState State { get; private set; }

    /// <summary>当前回合的 BattleSession（过渡/终局/未开始时为 null）。</summary>
    public BattleSession? CurrentRound => _roundSession;

    /// <summary>当前回合的权威战斗状态（过渡/终局时为 null）。</summary>
    public BattleState? CurrentBattleState => _roundSession?.CurrentState;

    /// <summary>
    /// 当前回合专属的输入缓冲。每回合 <b>新建</b>，绝不与旧回合共享同一个可变 InputBuffer；
    /// 表现层的键盘映射应绑定到它，并在回合切换后重新绑定。
    /// </summary>
    public InputBuffer RoundInputBuffer { get; private set; } = new();

    /// <summary>最近一个已提交逻辑帧的比赛级事件（只读）。</summary>
    public IReadOnlyList<MatchEvent> LastEvents => _lastEvents;

    /// <summary>选择/更换双方角色（仅 NotStarted 允许）。</summary>
    public void SelectFighters(string p1FighterId, string p2FighterId)
    {
        EnsureNotPublishing(nameof(SelectFighters));
        State = State.WithSelection(p1FighterId, p2FighterId);
        State.Validate();
    }

    /// <summary>开始整场比赛（NotStarted → 第 1 回合）。未显式选人时从对局配置取角色 ID。</summary>
    public void StartMatch()
    {
        EnsureNotPublishing(nameof(StartMatch));
        if (State.Phase != MatchPhase.NotStarted)
            throw new InvalidOperationException(
                $"StartMatch 只允许在 NotStarted 阶段调用，当前是 {State.Phase}。");

        if (State.P1FighterId is null || State.P2FighterId is null)
        {
            FightRoster probe = _rosterFactory(State.P1FighterId, State.P2FighterId);
            State = State.WithSelection(probe.Player1.FighterId, probe.Player2.FighterId);
        }

        BeginRound();
    }

    /// <summary>
    /// 开始下一回合（仅 BetweenRounds 允许）。MatchFinished 明确拒绝；
    /// 新回合重建 BattleSession / InputBuffer / BattleState，但保留规则、胜场、回合序号、
    /// 已结束回合数与选人（D106）。
    /// </summary>
    public void StartNextRound()
    {
        EnsureNotPublishing(nameof(StartNextRound));
        if (State.Phase != MatchPhase.BetweenRounds)
            throw new InvalidOperationException(
                $"StartNextRound 只允许在 BetweenRounds 阶段调用，当前是 {State.Phase}（比赛终局不能再开回合）。");

        BeginRound();
    }

    /// <summary>重新开始整场比赛：保留选人组合，清零比分、回合序号与所有回合内状态。</summary>
    public void RestartMatch()
    {
        EnsureNotPublishing(nameof(RestartMatch));
        State = State.Restart();
        State.Validate();
        StartMatch();
    }

    /// <summary>返回选人：清空选人身份、比分、回合序号与当前回合会话。</summary>
    public void ReturnToSelection()
    {
        EnsureNotPublishing(nameof(ReturnToSelection));
        State = State.ReturnToSelection();
        State.Validate();
        ClearRound();
    }

    /// <summary>用当前回合专属输入缓冲按渲染耗时推进，返回本次实际提交的逻辑帧数。</summary>
    public int Advance(double deltaSeconds) => Advance(deltaSeconds, RoundInputBuffer);

    /// <summary>
    /// 用自定义输入源推进（测试/回放用）。过渡与终局阶段不推进任何战斗帧。
    ///
    /// <para><b>批次边界（D117）</b>：底层 <see cref="BattleSession.Advance"/> 一次可能提交多帧，
    /// 但比赛层只允许提交到「回合终局帧」为止。因此传入<b>提交后终止谓词</b>：每帧完整提交
    /// （含 <see cref="OnFrameCommitted"/> 的计分）之后检查比赛阶段，一旦不再是进行中回合
    /// 就立即停止本批次，未使用的整帧预算记为丢弃预算——绝不会把终局冻结帧当成有效推进，
    /// 也不会把剩余预算带进下一回合（下一回合是全新的调度器）。</para>
    /// </summary>
    public int Advance(double deltaSeconds, IInputSource inputSource)
    {
        EnsureNotPublishing(nameof(Advance));
        if (!State.IsRoundLive || _roundSession is null)
            return 0;

        return _roundSession.Advance(
            deltaSeconds,
            inputSource: inputSource,
            stopAfterCommit: () => !State.IsRoundLive);
    }

    /// <summary>用当前回合专属输入缓冲显式推进指定帧数（测试/调试用）。</summary>
    public void StepExact(int count) => StepExactCore(count, RoundInputBuffer, null);

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
        if (!State.IsRoundLive || _roundSession is null)
            return; // 过渡/终局：不推进任何战斗逻辑帧。

        // 逐帧提交：回合一旦结束（KO / 超时）立即停止，剩余帧不会在过渡阶段推进。
        // 这与 BattleSession 的「提交一帧 → 记账一次」契约完全一致。
        for (int i = 0; i < count && State.IsRoundLive; i++)
            _roundSession.StepExact(1, inputProvider: inputProvider, inputSource: inputSource);
    }

    /// <summary>当前回合是否满足 BattleSession 的帧同步不变量。</summary>
    public bool IsFrameSyncIntact => _roundSession?.IsFrameSyncIntact ?? true;

    // ── 回合生命周期 ────────────────────────────────────────────────

    private void BeginRound()
    {
        State = State.StartRound();
        State.Validate();

        FightRoster roster = _rosterFactory(State.P1FighterId, State.P2FighterId);
        BattleState initialState = _initialStateFactory(Rules);

        // 每回合全新输入缓冲：旧按住状态、边沿闩锁、方向历史、消费标记一并清除（D106）。
        RoundInputBuffer = new InputBuffer();
        _lastEvents = Array.Empty<MatchEvent>();

        _roundSession = new BattleSession(
            initialState: initialState,
            scheduler: new FixedStepScheduler(_maxStepsPerAdvance),
            simulation: new BattleSimulation(roster),
            eventsCommitted: OnFrameCommitted);
    }

    private void ClearRound()
    {
        _roundSession = null;
        RoundInputBuffer = new InputBuffer();
        _lastEvents = Array.Empty<MatchEvent>();
    }

    /// <summary>
    /// 每个<b>已成功提交</b>的逻辑帧之后调用一次（D83/D118）：<b>先</b>推进回合计时、
    /// 若本回合刚结束则完成唯一一次权威计分并校验不变式，<b>再</b>包装与发布本帧比赛事件。
    ///
    /// <para><b>顺序不可交换</b>：观察者收到 <c>RoundEnded</c> 时必须能读到「已计分、
    /// 已进入过渡/终局、剩余时间已同步」的 <see cref="MatchState"/>；否则 UI、录像等
    /// 消费方会观察到和权威状态不一致的比赛快照。</para>
    ///
    /// <para>Step 抛异常时本回调不会执行，因此未提交的帧不会扣时间，也不会重复投递
    /// RoundEnded 或重复加分。观察者抛异常时：本帧战斗状态与比赛状态<b>均已落定</b>
    /// （不回滚、不二次计分），异常经 <see cref="BattleSession"/> 原样上抛并置故障封闭。</para>
    /// </summary>
    private void OnFrameCommitted(FrameInput input, BattleState stateBefore, IReadOnlyList<BattleEvent> events)
    {
        // 已经不在进行中回合：不再计分、也不再用「空事件」覆盖终局帧的事件快照。
        if (!State.IsRoundLive || _roundSession is null)
            return;

        int roundNumber = State.RoundNumber;
        BattleState current = _roundSession.CurrentState;

        // ① 权威比赛状态：时钟投影（09B，D107）。
        //    内核 BattleState 每成功提交一个逻辑帧递减 1，比赛层只读同步，
        //    不再另起一套计时；Step 抛异常时本回调不执行 → 未提交帧不扣时。
        if (current.RoundTimerRemainingFrames is int remaining)
            State = State.WithRemainingTime(remaining);

        // ② 唯一一次权威计分。结束原因直接取内核 RoundState
        //    （KO / DoubleKO / TimeOver / TimeOverDraw），比赛层不重新推断。
        if (current.Round.IsFinished)
        {
            RoundEndReason reason = current.Round.Reason != RoundEndReason.None
                ? current.Round.Reason
                : current.Round.IsDoubleKO ? RoundEndReason.DoubleKO : RoundEndReason.Knockout;
            State = State.RecordRoundResult(current.Round.Outcome, reason);
        }

        // ③ 不变式必须在发布之前成立：观察者不该看到未校验的状态。
        State.Validate();

        // ④ 权威状态落定后才包装并发布本帧事件（D83/D104/D118）。
        var wrapped = new MatchEvent[events.Count];
        for (int i = 0; i < events.Count; i++)
            wrapped[i] = new MatchEvent(roundNumber, events[i]);
        _lastEvents = Array.AsReadOnly(wrapped);

        if (_eventsCommitted is null || wrapped.Length == 0)
            return;

        _publishing = true;
        try
        {
            for (int i = 0; i < wrapped.Length; i++)
                _eventsCommitted.Invoke(wrapped[i]);
        }
        finally
        {
            // 观察者抛异常也必须解除，避免比赛会话被永久锁死（异常照常上抛）。
            _publishing = false;
        }
    }

    /// <summary>
    /// 禁止在比赛事件观察者中重入修改比赛会话（D118）。这类调用会与正在收尾的批次竞争：
    /// 可能让旧回合的后续帧推进到新会话、或把旧回合事件挂到新回合身份上。
    /// 显式拒绝比依赖偶然调用顺序安全。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前正在投递比赛事件。</exception>
    private void EnsureNotPublishing(string operation)
    {
        if (_publishing)
            throw new InvalidOperationException(
                $"{operation} 不能在比赛事件观察者中调用：观察者回调期间比赛会话正在收尾 " +
                "（旧回合的帧/事件身份不得串到新回合）。请在回调返回后再执行该操作。");
    }

    private static BattleState DefaultInitialState(MatchRules rules)
    {
        BattleState state = BattleState.CreateDefault();
        int? timer = rules.EnableTimeout ? rules.RoundTimerFrames : null;
        return new BattleState(
            state.Frame, state.Fighters, state.StateVersion, state.Round,
            state.Projectiles, state.NextProjectileId, timer);
    }
}
