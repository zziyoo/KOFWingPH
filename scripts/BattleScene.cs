using System.Text;
using Godot;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;

namespace NewKOF;

/// <summary>
/// 战斗场景的表现层根节点。
///
/// 职责边界：
/// - 只读 <see cref="BattleSession.CurrentState"/>，绝不写回权威状态。
/// - 不参与任何权威判定；Godot 物理系统不决定战斗结果。
/// - <b>不自己实现时间累积与补帧</b>：全部委托给
///   <see cref="FixedStepScheduler"/>（纯 C#、可独立测试）。
///   本类只负责把 <c>delta</c> 交给调度器、把状态映射成画面。
/// - <b>不自己保存输入历史、不做 SOCD/指令识别</b>：键盘事件只转成
///   <see cref="InputBits"/> 交给 <see cref="InputBuffer"/>（内核层），
///   由它按逻辑帧号逐帧产出 <see cref="FrameInput"/>。
///
/// 阶段 03A：输入链路已接通。键位见 <see cref="KeyboardInputMap"/>，
/// F1 切换暂停（仅阻止时间驱动推进），HUD 显示最近一个已消费逻辑帧的双人输入。
///
/// 阶段 03B-1：指令识别已接入 <c>BattleCore</c>；HUD 显示识别结果。
///
/// 阶段 04：识别移入权威转换（<c>BattleSimulation.Step</c>），识别状态与动作状态
/// 都随权威快照携带。HUD 改为<b>只读权威状态</b>（<c>FighterState.LogicState/MoveId</c>
/// 与识别缓冲），本类不再持有独立的识别器、不做任何状态机或招式计时。
///
/// 阶段 02 修改原因（阶段 01 缺陷）：
/// 旧实现用 <c>(int)(delta*1e6)</c> 截断 + 整数 16666us 间隔，
/// 导致逻辑帧总数随渲染帧率漂移（240FPS 跑 600 秒少 5 帧）。
/// 现改为 double 秒累加 + 精确步长判定，且补帧上限显式可配。
/// </summary>
public partial class BattleScene : Node2D
{
    /// <summary>战斗会话：调度器 + 模拟（Kai 对 Rei 演示配置）+ 权威状态，全部在内核层。</summary>
    private BattleSession _session = null!;

    /// <summary>逻辑帧输入缓冲（内核层）。边沿闩锁与帧号连续性都在这里保证。</summary>
    private InputBuffer _inputBuffer = new();

    /// <summary>键盘映射（表现层）。本类是唯一接触 Godot 输入 API 的地方。</summary>
    private KeyboardInputMap _inputMap = null!;

    private Node2D? _fighter1Node;
    private Node2D? _fighter2Node;
    private Label? _hudLabel;

    /// <summary>终局横幅（P1 WIN / P2 WIN / DOUBLE KO），内容只来自权威 RoundState。</summary>
    private Label? _resultLabel;

    /// <summary>F1 切换的暂停状态（仅阻止时间驱动推进，不影响显式单步）。</summary>
    private bool _paused;

    /// <summary>
    /// 单次渲染最多补的逻辑帧数。超过则欠帧被计入
    /// <see cref="FixedStepScheduler.DroppedStepCount"/> 并显式告警。
    /// </summary>
    private const int MaxStepsPerAdvance = 5;

    /// <summary>确保积压告警只输出一次，避免每帧刷屏。</summary>
    private bool _backlogWarningIssued;

    /// <summary>判定框调试显示开关（F2 切换）：Hitbox 红 / Hurtbox 蓝 / Pushbox 绿框。</summary>
    private bool _showDebugBoxes = true;

    /// <summary>判定框调试绘制节点（渲染在所有子节点之上）。</summary>
    private DebugBoxDrawer _debugBoxes = null!;

    /// <summary>最近一帧的权威战斗事件（由 eventsCommitted 观察者投递，只读消费）。</summary>
    private IReadOnlyList<BattleEvent> _lastEvents = Array.Empty<BattleEvent>();

    /// <summary>本会话的角色配置（供判定框查询；只读）。</summary>
    private readonly FightRoster _roster = DemoRoster.CreateKaiVsRei();

    public override void _Ready()
    {
        _fighter1Node = GetNodeOrNull<Node2D>("Fighter1");
        _fighter2Node = GetNodeOrNull<Node2D>("Fighter2");
        _hudLabel = GetNodeOrNull<Label>("Hud/HudLabel");
        _resultLabel = GetNodeOrNull<Label>("Result");

        _debugBoxes = new DebugBoxDrawer();
        AddChild(_debugBoxes);

        StartNewRound();
    }

    /// <summary>
    /// 开新回合（阶段 07A，D89）：重建会话、输入缓冲与调试绘制绑定——
    /// 旧输入历史/硬直/攻击实例/事件显示全部清除，权威状态回到 <see cref="BattleState.CreateDefault"/>。
    /// 表现层只负责「请求重开」，胜负与状态仍由内核判定。
    /// </summary>
    private void StartNewRound()
    {
        _session = new BattleSession(
            initialState: CreateInitialState(),
            scheduler: new FixedStepScheduler(MaxStepsPerAdvance),
            simulation: new BattleSimulation(_roster),
            eventsCommitted: (input, state, events) => _lastEvents = events);

        // 新输入缓冲：清空按住/边沿/历史，避免上一局的按键被新回合首帧消费。
        _inputBuffer = new InputBuffer();
        _inputMap = new KeyboardInputMap(_inputBuffer);
        _lastEvents = Array.Empty<BattleEvent>();
        _paused = false;

        _debugBoxes.Bind(_session, _roster);
        SyncViewFromState();
    }

    /// <summary>
    /// 初始权威状态。调试验收参数 <c>--hp=&lt;n&gt;</c> 可覆盖双方体力
    /// （仅命令行传入时生效；正常启动为满血 1000，零副作用），用于在图形运行中快速触发 KO。
    /// </summary>
    private static BattleState CreateInitialState()
    {
        BattleState state = BattleState.CreateDefault();
        int health = InitialHealthFromArgs();
        if (health == state.Fighter(0).MaxHealth)
            return state;

        return new BattleState(state.Frame, new[]
        {
            state.Fighter(0) with { Health = health },
            state.Fighter(1) with { Health = health },
        }, state.StateVersion);
    }

    private static int InitialHealthFromArgs()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--hp=", StringComparison.Ordinal)
                && int.TryParse(arg["--hp=".Length..], out int hp) && hp is > 0 and <= 1000)
                return hp;
        }

        return 1000;
    }

    /// <summary>
    /// 当前逻辑帧号（只读）。仅供调试工具读取，权威值仍在内核状态里。
    /// </summary>
    public int CurrentLogicFrame => _session.CurrentState.Frame;

    /// <summary>
    /// 键盘事件入口。按事件（而不是每渲染帧轮询）采样，
    /// 才能捕获「同一渲染帧内按下又松开」的边沿。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && !key.Echo && key.Pressed && key.Keycode == Key.F1)
        {
            _paused = !_paused;
            if (_paused)
                _session.Scheduler.Pause();
            else
                _session.Scheduler.Resume();

            SyncViewFromState();
            return;
        }

        if (@event is InputEventKey f2 && !f2.Echo && f2.Pressed && f2.Keycode == Key.F2)
        {
            _showDebugBoxes = !_showDebugBoxes;
            _debugBoxes.Enabled = _showDebugBoxes;
            _debugBoxes.Refresh();
            return;
        }

        if (@event is InputEventKey f5 && !f5.Echo && f5.Pressed && f5.Keycode == Key.F5)
        {
            StartNewRound();
            return;
        }

        _inputMap.Handle(@event);
    }

    /// <summary>窗口失焦时清空按住状态，避免松开事件丢失导致「永远按住」。</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
            _inputMap?.ReleaseAll();
    }

    public override void _Process(double delta)
    {
        // 输入用两阶段提交：模拟成功后才写入输入历史，失败不留幻影帧。
        int steps = _session.Advance(delta, inputSource: _inputBuffer);

        if (steps > 0)
            SyncViewFromState();

        // 只在“首次发生丢弃”时告警一次，避免每帧刷屏。
        long dropped = _session.Scheduler.DroppedStepCount;
        if (dropped > 0 && !_backlogWarningIssued)
        {
            _backlogWarningIssued = true;
            GD.PushWarning(
                $"[BattleScene] 帧积压超过上限 {MaxStepsPerAdvance}，已丢弃 {dropped} 个逻辑帧。" +
                "请检查渲染性能。");
        }
    }

    /// <summary>把权威状态映射到画面。表现层只读。</summary>
    private void SyncViewFromState()
    {
        BattleState state = _session.CurrentState;

        // 1 世界单位 = 1 像素；屏幕原点对齐到视口中线，地面 Y = 560。
        // 权威坐标是 Q16.16 定点数（世界坐标，Y 轴向上、地面为 0）；
        // 这里只做「定点 → 浮点」的显示转换，浮点结果绝不写回权威状态（D63）。
        const float originX = 640f;
        const float groundY = 560f;

        // 角色节点是 Node2D 容器（子节点 ColorRect 从容器原点向上 120px 绘制），
        // 因此容器 Y 设为地面高度即可让色块底边贴合地面；
        // 世界 Y 向上 → 屏幕 Y 向下：screenY = groundY - worldY。
        FighterState p1 = state.Fighter(0);
        FighterState p2 = state.Fighter(1);

        if (_fighter1Node is not null)
            _fighter1Node.Position = new Vector2(
                originX + (float)p1.PositionX.ToDouble(),
                groundY - (float)p1.PositionY.ToDouble());

        if (_fighter2Node is not null)
            _fighter2Node.Position = new Vector2(
                originX + (float)p2.PositionX.ToDouble(),
                groundY - (float)p2.PositionY.ToDouble());

        if (_hudLabel is not null)
        {
            // 显示「最近一个已消费逻辑帧」的输入，而不是此刻的物理按键状态：
            // 本阶段要证明的是输入进入了正确的逻辑帧，HUD 必须反映已提交的数据。
            FrameInput last = _inputBuffer.LastInput ?? FrameInput.Empty(state.Frame);
            string pauseTag = _paused ? "  [PAUSED · F1]" : string.Empty;
            _hudLabel.Text =
                $"Logic Frame: {state.Frame}{pauseTag}\n" +
                $"P1: {Describe(last.Player1)}  HP: {p1.Health}/{p1.MaxHealth}\n" +
                $"P2: {Describe(last.Player2)}  HP: {p2.Health}/{p2.MaxHealth}\n" +
                $"P1 Pos: {DescribePosition(p1)}\n" +
                $"P1 Vel: {DescribeVelocity(p1)}\n" +
                $"P2 Pos: {DescribePosition(p2)}\n" +
                $"P2 Vel: {DescribeVelocity(p2)}\n" +
                $"P1 Command: {DescribeCommand(LatestCommand(state, 0))}\n" +
                $"P2 Command: {DescribeCommand(LatestCommand(state, 1))}\n" +
                $"P1 State: {DescribeState(p1)}\n" +
                $"P1 Move: {DescribeMove(p1)}\n" +
                $"P2 State: {DescribeState(p2)}\n" +
                $"P2 Move: {DescribeMove(p2)}\n" +
                $"Combo: P1 {p1.Combo} | P2 {p2.Combo}\n" +
                $"P1 Cancel: {DescribeCancel(p1, 0)}\n" +
                $"P2 Cancel: {DescribeCancel(p2, 1)}\n" +
                $"Projectiles: {DescribeProjectiles(state)}\n" +
                $"Round: {DescribeRound(state.Round)}\n" +
                $"Event: {DescribeLastEvent(_lastEvents)}";
        }

        if (_resultLabel is not null)
        {
            // 横幅内容只来自权威 RoundState（表现层不判定胜负）。
            _resultLabel.Text = state.Round.Outcome switch
            {
                RoundOutcome.P1Win => "P1 WIN",
                RoundOutcome.P2Win => "P2 WIN",
                RoundOutcome.DoubleKO => "DOUBLE KO",
                _ => string.Empty,
            };
            _resultLabel.Visible = state.Round.IsFinished;
        }

        _debugBoxes.Refresh(); // 判定框调试绘制随权威状态刷新。
    }

    /// <summary>飞行道具只读摘要（D99）：数量 + 第一枚的位置/寿命（权威值）。</summary>
    private static string DescribeProjectiles(BattleState state)
    {
        if (state.Projectiles.Count == 0)
            return "-";

        ProjectileState first = state.Projectiles[0];
        string text = $"{state.Projectiles.Count} [{first.TypeId}@P{first.OwnerFighterId + 1}" +
            $" ({first.PositionX.ToDouble():0.##}, {first.PositionY.ToDouble():0.##})" +
            $" life={first.RemainingLifetime}]";

        return state.Projectiles.Count > 1 ? text + " +" + (state.Projectiles.Count - 1) : text;
    }

    /// <summary>回合状态文本：进行中显示 InProgress，终局附结果与重开提示。</summary>
    private static string DescribeRound(RoundState round) =>
        round.IsFinished ? $"{round}  [F5 重开]" : round.ToString();

    /// <summary>
    /// 取消相关调试文本（只读）：当前招式 / 经过帧数 / 是否处于窗口 / 命中与防御确认帧。
    /// 窗口判定走内核 <see cref="FighterStateMachine.IsInCancelWindow"/>，表现层不复刻规则。
    /// </summary>
    private string DescribeCancel(FighterState fighter, int player)
    {
        if (!fighter.IsInMove || fighter.MoveId is null || fighter.MoveStartedFrame < 0)
            return "-";

        // 时间基准 = 动作有效经过帧数（D96）：Hitstop 冻结帧不推进它。
        int elapsed = fighter.MoveElapsedFrames;
        bool window = FighterStateMachine.IsInCancelWindow(fighter, _roster.Fighter(player));
        string hit = fighter.HitConfirmFrame >= 0 ? $"hit@{fighter.HitConfirmFrame}" : "hit@-";
        string block = fighter.BlockConfirmFrame >= 0 ? $" blk@{fighter.BlockConfirmFrame}" : string.Empty;

        return $"{fighter.MoveId} @{elapsed} {(window ? "WINDOW" : "closed")} {hit}{block}";
    }

    /// <summary>把最近一帧的权威事件渲染成 HUD 文本（只读消费，按 EventId 语义展示）。</summary>
    private static string DescribeLastEvent(IReadOnlyList<BattleEvent> events)
    {
        if (events.Count == 0)
            return "-";

        BattleEvent last = events[events.Count - 1];
        string actors = last.SourceFighterId == last.TargetFighterId
            ? $"P{last.SourceFighterId + 1}"
            : $"P{last.SourceFighterId + 1}→P{last.TargetFighterId + 1}";
        string amount = last.Amount > 0 ? $" {last.Amount}" : string.Empty;
        string move = last.MoveId is null ? string.Empty : $" {last.MoveId}";
        return $"{last.Type} {actors}{move}{amount} (id={last.EventId})";
    }

    /// <summary>状态行：带倒计时的状态附加剩余帧数（06B 硬直 / 07C 倒地·起身 / 08A 停顿）。</summary>
    private static string DescribeState(FighterState fighter)
    {
        string state = fighter.LogicState is FighterLogicState.Hitstun or FighterLogicState.Blockstun
            or FighterLogicState.Knockdown or FighterLogicState.Wakeup
            ? $"{fighter.LogicState}({fighter.StunRemainingFrames})"
            : fighter.LogicState.ToString();

        return fighter.HitstopRemainingFrames > 0
            ? $"{state} HITSTOP({fighter.HitstopRemainingFrames})"
            : state;
    }

    /// <summary>把权威定点坐标渲染成 HUD 文本（世界坐标，Y 向上，地面为 0；显示用浮点）。</summary>
    private static string DescribePosition(FighterState fighter) =>
        $"({fighter.PositionX.ToDouble():0.##}, {fighter.PositionY.ToDouble():0.##})";

    /// <summary>把权威速度与跳跃类型渲染成 HUD 文本（速度 = 世界单位/帧；显示用浮点）。</summary>
    private static string DescribeVelocity(FighterState fighter) =>
        $"({fighter.VelocityX.ToDouble():0.##}, {fighter.VelocityY.ToDouble():0.##})" +
        (fighter.JumpKind == JumpKind.None ? string.Empty : $"  Jump: {fighter.JumpKind}");

    /// <summary>从权威识别快照取最近一次识别结果（阶段 04：识别在内核，Godot 只读）。</summary>
    private static CommandMatch? LatestCommand(BattleState state, int player) =>
        state.Fighter(player).RecognitionState.Last;

    /// <summary>把识别结果渲染成 HUD 文本；从未识别到时显示 "-"。</summary>
    private static string DescribeCommand(CommandMatch? match) => match?.CommandId ?? "-";

    /// <summary>把动作状态渲染成 HUD 文本；待机显示 "-"，动作中显示「招式 / 阶段 剩余帧」。</summary>
    private static string DescribeMove(FighterState fighter) =>
        fighter.IsInMove ? $"{fighter.MoveId} / {fighter.LogicState} {fighter.PhaseRemainingFrames}" : "-";

    /// <summary>把输入位渲染成紧凑的调试文本；无输入显示 "-"。</summary>
    private static string Describe(InputBits bits)
    {
        if (bits == InputBits.None)
            return "-";

        var text = new StringBuilder(16);
        if ((bits & InputBits.Up) != 0) text.Append("U ");
        if ((bits & InputBits.Down) != 0) text.Append("D ");
        if ((bits & InputBits.Left) != 0) text.Append("L ");
        if ((bits & InputBits.Right) != 0) text.Append("R ");
        if ((bits & InputBits.LightPunch) != 0) text.Append("A ");
        if ((bits & InputBits.HeavyPunch) != 0) text.Append("B ");
        if ((bits & InputBits.LightKick) != 0) text.Append("C ");
        if ((bits & InputBits.HeavyKick) != 0) text.Append("D ");

        return text.ToString(0, text.Length - 1);
    }
}
