using System.Text;
using Godot;
using NewKOF.BattleCore;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;

namespace NewKOF;

/// <summary>
/// 战斗场景的表现层根节点（阶段 09C 起接入完整比赛流程）。
///
/// <para><b>职责边界（不变）</b>：只读权威状态，绝不写回；不参与任何权威判定；
/// 不自己实现时间累积与补帧（交给 <see cref="FixedStepScheduler"/>）；
/// 不自己保存输入历史、不做 SOCD/指令识别（交给 <see cref="InputBuffer"/> 与内核）。</para>
///
/// <para><b>09C 新增</b>：表现层改为持有 <see cref="MatchSession"/> 与
/// <see cref="FighterCatalog"/>，负责选人界面、回合过渡、比赛终局与重开。
/// 胜场、倒计时、回合结果、比赛结果<b>全部只读</b> <see cref="MatchState"/> /
/// <see cref="BattleState"/>，表现层不自行统计。</para>
///
/// <para><b>输入隔离（D111）</b>：选人/过渡/终局阶段的界面按键在 <c>_Input</c> 中直接拦截，
/// 绝不传给 <see cref="KeyboardInputMap"/>；每回合 <see cref="MatchSession"/> 都会新建
/// <see cref="InputBuffer"/>，本类同步重建键盘映射，因此确认键不会变成开局首帧攻击，
/// 旧按住状态也不会跨回合/跨重开泄漏。</para>
/// </summary>
public partial class BattleScene : Node2D
{
    /// <summary>比赛规则（默认 2 胜 / 5 回合 / 99 秒；命令行调试参数只影响调试运行）。</summary>
    private MatchRules _rules = null!;

    /// <summary>角色目录（09C 为 C# 演示配置，09D 起由 JSON 加载器构建）。</summary>
    private FighterCatalog _catalog = null!;

    /// <summary>比赛级会话（权威状态唯一来源）。</summary>
    private MatchSession _match = null!;

    /// <summary>键盘映射（表现层唯一接触 Godot 输入 API 的地方；每回合重建）。</summary>
    private KeyboardInputMap _inputMap = null!;

    private Node2D? _fighter1Node;
    private Node2D? _fighter2Node;
    private Label? _hudLabel;
    private Label? _resultLabel;
    private DebugBoxDrawer _debugBoxes = null!;

    private bool _paused;
    private bool _showDebugBoxes = true;
    private bool _backlogWarningIssued;

    // 选人界面的表现层状态（权威选人身份在 MatchSession.SelectFighters 中落定）。
    private int _p1Index;
    private int _p2Index;
    private bool _p1Confirmed;
    private bool _p2Confirmed;

    /// <summary>单次渲染最多补的逻辑帧数。</summary>
    private const int MaxStepsPerAdvance = 5;

    public override void _Ready()
    {
        _fighter1Node = GetNodeOrNull<Node2D>("Fighter1");
        _fighter2Node = GetNodeOrNull<Node2D>("Fighter2");
        _hudLabel = GetNodeOrNull<Label>("Hud/HudLabel");
        _resultLabel = GetNodeOrNull<Label>("Result");
        // 大标题移到 HUD 右侧（HUD 面板右边界 = 400），避免遮挡规则/阶段文字。
        if (_resultLabel is not null)
            _resultLabel.Position = new Vector2(430f, 190f);

        _debugBoxes = new DebugBoxDrawer { Enabled = _showDebugBoxes };
        AddChild(_debugBoxes);

        _catalog = FighterCatalog.CreateDemo();
        _rules = RulesFromArgs();
        _p1Index = 0;
        _p2Index = _catalog.Entries.Count > 1 ? 1 : 0;
        _p1Confirmed = false;
        _p2Confirmed = false;

        CreateMatchSession();
        SyncViewFromState();
    }

    private void CreateMatchSession()
    {
        _match = new MatchSession(
            _rules,
            (p1, p2) => _catalog.CreateRoster(
                p1 ?? _catalog.Entries[0].FighterId,
                p2 ?? _catalog.Entries[^1].FighterId),
            initialStateFactory: CreateInitialState,
            maxStepsPerAdvance: MaxStepsPerAdvance);

        _inputMap = new KeyboardInputMap(_match.RoundInputBuffer);
        _paused = false;
        _backlogWarningIssued = false;
    }

    /// <summary>
    /// 生成每回合的初始权威状态。<c>--hp=&lt;n&gt;</c> 与 <c>--match-time=&lt;s&gt;</c>
    /// 是<b>仅命令行</b>的调试验收参数：不传时满血 1000、99 秒，正常启动零副作用。
    /// </summary>
    private static BattleState CreateInitialState(MatchRules rules)
    {
        BattleState state = BattleState.CreateDefault();
        int health = InitialHealthFromArgs();
        int distance = InitialDistanceFromArgs();
        IReadOnlyList<FighterState> fighters = new[]
        {
            state.Fighter(0) with
            {
                Health = health,
                PositionX = FixedPoint.FromInt(-distance),
            },
            state.Fighter(1) with
            {
                Health = health,
                PositionX = FixedPoint.FromInt(distance),
            },
        };

        return new BattleState(state.Frame, fighters, state.StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: rules.EnableTimeout ? rules.RoundTimerFrames : null);
    }

    private static MatchRules RulesFromArgs()
    {
        int seconds = 99;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--match-time=", StringComparison.Ordinal)
                && int.TryParse(arg["--match-time=".Length..], out int value)
                && value is >= 1 and <= MatchRules.MaxRoundTimeSeconds)
            {
                seconds = value;
            }
        }
        return new MatchRules(roundsToWin: 2, maxRounds: 5, roundTimeSeconds: seconds);
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
    /// 仅命令行的调试站位（半距离，默认 120 = 双方 ±120）。下限 30 保证 D73 最小间距 60；
    /// 正常启动不传该参数，位置与阶段 01–08 完全一致。
    /// </summary>
    private static int InitialDistanceFromArgs()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--duel=", StringComparison.Ordinal)
                && int.TryParse(arg["--duel=".Length..], out int d) && d is >= 30 and <= 600)
                return d;
        }
        return 120;
    }

    /// <summary>当前逻辑帧号（只读；调试工具读取权威值）。</summary>
    public int CurrentLogicFrame => _match.CurrentBattleState?.Frame ?? 0;

    /// <summary>当前比赛状态（只读；供调试/自动化断言）。</summary>
    public MatchState CurrentMatchState => _match.State;

    // ── 输入 ────────────────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || key.Echo || !key.Pressed)
        {
            _inputMap.Handle(@event);
            return;
        }

        // 全局调试键（所有阶段可用）。
        if (key.Keycode == Key.F2)
        {
            _showDebugBoxes = !_showDebugBoxes;
            _debugBoxes.Enabled = _showDebugBoxes;
            _debugBoxes.Refresh();
            return;
        }
        if (key.Keycode == Key.F1)
        {
            TogglePause();
            return;
        }
        if (key.Keycode == Key.F5)
        {
            Restart();
            return;
        }
        if (key.Keycode == Key.Escape)
        {
            ReturnToSelection();
            return;
        }

        MatchPhase phase = _match.State.Phase;

        // 选人阶段：全部界面按键在此拦截，绝不进入战斗输入缓冲（D111）。
        if (phase == MatchPhase.NotStarted)
        {
            HandleSelectionKey(key.Keycode);
            return;
        }

        // 过渡阶段：只允许显式确认进入下一回合，不推进任何战斗帧。
        if (phase == MatchPhase.BetweenRounds)
        {
            if (key.Keycode == Key.Space)
                BeginNextRound();
            else
                SyncViewFromState();
            return;
        }

        // 终局：不接收任何战斗输入。
        if (phase == MatchPhase.MatchFinished)
        {
            SyncViewFromState();
            return;
        }

        _inputMap.Handle(@event);
    }

    private void HandleSelectionKey(Key code)
    {
        switch (code)
        {
            case Key.A:
                _p1Index = Cycle(_p1Index, -1); _p1Confirmed = false; break;
            case Key.D:
                _p1Index = Cycle(_p1Index, +1); _p1Confirmed = false; break;
            case Key.Left:
                _p2Index = Cycle(_p2Index, -1); _p2Confirmed = false; break;
            case Key.Right:
                _p2Index = Cycle(_p2Index, +1); _p2Confirmed = false; break;
            case Key.J:
                _p1Confirmed = true; break;
            case Key.Comma:
                _p2Confirmed = true; break;
            case Key.Enter:
                if (_p1Confirmed && _p2Confirmed)
                    StartMatchFromSelection();
                break;
        }
        SyncViewFromState();
    }

    private int Cycle(int index, int delta) =>
        (index + delta + _catalog.Entries.Count) % _catalog.Entries.Count;

    private void TogglePause()
    {
        BattleSession? round = _match.CurrentRound;
        if (round is null || !_match.State.IsRoundLive)
            return;

        _paused = !_paused;
        if (_paused) round.Scheduler.Pause();
        else round.Scheduler.Resume();
        SyncViewFromState();
    }

    private void StartMatchFromSelection()
    {
        _match.SelectFighters(
            _catalog.Entries[_p1Index].FighterId,
            _catalog.Entries[_p2Index].FighterId);
        _match.StartMatch();
        BindNewRound();
    }

    private void BeginNextRound()
    {
        _match.StartNextRound();
        BindNewRound();
    }

    private void Restart()
    {
        if (_match.State.Phase == MatchPhase.NotStarted)
        {
            _p1Confirmed = false;
            _p2Confirmed = false;
        }
        else
        {
            _match.RestartMatch(); // 保留角色组合，清零比分
            BindNewRound();
        }
        SyncViewFromState();
    }

    private void ReturnToSelection()
    {
        _match.ReturnToSelection();
        _p1Confirmed = false;
        _p2Confirmed = false;
        _inputMap = new KeyboardInputMap(_match.RoundInputBuffer);
        _paused = false;
        SyncViewFromState();
    }

    /// <summary>新回合：重新绑定输入映射与判定框绘制（会话/缓冲/配置全是新实例）。</summary>
    private void BindNewRound()
    {
        _inputMap = new KeyboardInputMap(_match.RoundInputBuffer);
        _paused = false;
        _backlogWarningIssued = false;

        BattleSession? round = _match.CurrentRound;
        if (round is not null && _match.State.P1FighterId is string p1
            && _match.State.P2FighterId is string p2)
        {
            _debugBoxes.Bind(round, _catalog.CreateRoster(p1, p2));
        }
        SyncViewFromState();
    }

    /// <summary>窗口失焦时清空按住状态，避免松开事件丢失导致「永远按住」。</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
            _inputMap?.ReleaseAll();
    }

    public override void _Process(double delta)
    {
        // 比赛层在过渡/终局阶段返回 0，不推进任何战斗逻辑帧。
        int steps = _match.Advance(delta);
        if (steps > 0)
            SyncViewFromState();

        long dropped = _match.CurrentRound?.Scheduler.DroppedStepCount ?? 0;
        if (dropped > 0 && !_backlogWarningIssued)
        {
            _backlogWarningIssued = true;
            GD.PushWarning(
                $"[BattleScene] 帧积压超过上限 {MaxStepsPerAdvance}，已丢弃 {dropped} 个逻辑帧。" +
                "请检查渲染性能。");
        }
    }

    // ── 只读渲染 ────────────────────────────────────────────────────

    private void SyncViewFromState()
    {
        MatchState matchState = _match.State;
        BattleState? state = _match.CurrentBattleState;

        const float originX = 640f;
        const float groundY = 560f;

        FighterState p1 = state?.Fighter(0) ?? FighterState.Create(0, 1000, -120, Facing.Right);
        FighterState p2 = state?.Fighter(1) ?? FighterState.Create(1, 1000, 120, Facing.Left);

        if (_fighter1Node is not null)
            _fighter1Node.Position = new Vector2(
                originX + (float)p1.PositionX.ToDouble(),
                groundY - (float)p1.PositionY.ToDouble());
        if (_fighter2Node is not null)
            _fighter2Node.Position = new Vector2(
                originX + (float)p2.PositionX.ToDouble(),
                groundY - (float)p2.PositionY.ToDouble());

        if (_hudLabel is not null)
            _hudLabel.Text = BuildHud(matchState, state, p1, p2);

        if (_resultLabel is not null)
        {
            _resultLabel.Text = ResultBanner(matchState);
            _resultLabel.Visible = !string.IsNullOrEmpty(_resultLabel.Text);
        }

        _debugBoxes.Refresh();
    }

    private string BuildHud(MatchState m, BattleState? state, FighterState p1, FighterState p2)
    {
        if (m.Phase == MatchPhase.NotStarted)
            return BuildSelectionHud();

        string p1Name = m.P1FighterId is null ? "-" : _catalog.DisplayName(m.P1FighterId);
        string p2Name = m.P2FighterId is null ? "-" : _catalog.DisplayName(m.P2FighterId);
        FrameInput last = _match.RoundInputBuffer.LastInput ?? FrameInput.Empty(state?.Frame ?? 0);
        int seconds = (m.RemainingTimeFrames + BattleCoreInfo.LogicFrameRate - 1)
            / BattleCoreInfo.LogicFrameRate;

        var text = new StringBuilder(512);
        text.Append($"=== MATCH (Round {m.RoundNumber}/{m.Rules.MaxRounds}) ===\n");
        text.Append($"Phase: {m.Phase}   Time: {seconds}s   Wins: P1 {m.P1Wins} / P2 {m.P2Wins}\n");
        text.Append($"P1 [{p1Name}]: {p1.Health}/{p1.MaxHealth}   P2 [{p2Name}]: {p2.Health}/{p2.MaxHealth}\n");
        text.Append($"Round Result: {DescribeRoundResult(m)}\n");
        if (m.Phase == MatchPhase.MatchFinished)
            text.Append($"Match Result: {DescribeMatchResult(m)}\n");
        text.Append($"Logic Frame: {state?.Frame ?? 0}{(_paused ? "  [PAUSED · F1]" : string.Empty)}\n");
        text.Append($"P1: {Describe(last.Player1)}  P2: {Describe(last.Player2)}\n");
        text.Append($"P1 Pos: {DescribePosition(p1)}   P2 Pos: {DescribePosition(p2)}\n");
        text.Append($"P1 State: {DescribeState(p1)}   P2 State: {DescribeState(p2)}\n");
        text.Append($"P1 Move: {DescribeMove(p1)}   P2 Move: {DescribeMove(p2)}\n");
        text.Append($"Combo: P1 {p1.Combo} | P2 {p2.Combo}\n");
        text.Append($"Projectiles: {DescribeProjectiles(state)}\n");
        text.Append($"Event: {DescribeLastMatchEvent(_match.LastEvents)}\n");
        text.Append(ControlsHint(m.Phase));
        return text.ToString();
    }

    private string BuildSelectionHud()
    {
        string p1Name = _catalog.Entries[_p1Index].DisplayName;
        string p2Name = _catalog.Entries[_p2Index].DisplayName;
        string p1Tag = _p1Confirmed ? "[CONFIRMED]" : "[choose]";
        string p2Tag = _p2Confirmed ? "[CONFIRMED]" : "[choose]";

        return $"=== CHARACTER SELECT (1v1 debug) ===\n" +
            $"P1: < {p1Name} >  {p1Tag}\n" +
            $"P2: < {p2Name} >  {p2Tag}\n" +
            $"Rules: first to {_rules.RoundsToWin} wins / max {_rules.MaxRounds} rounds / " +
            $"{_rules.RoundTimeSeconds}s per round\n" +
            $"A / D : P1 switch      LEFT / RIGHT : P2 switch\n" +
            $"J : P1 confirm         , : P2 confirm\n" +
            $"ENTER : start match (both confirmed)\n" +
            $"F1 pause  F2 hitboxes  F5 restart  ESC back to select\n" +
            $"Catalog: {string.Join(", ", _catalog.Entries.Select(e => e.FighterId))}\n";
    }

    private static string ControlsHint(MatchPhase phase) => phase switch
    {
        MatchPhase.RoundInProgress => "Controls: P1 WASD + J/K/U/I | P2 arrows + , . / ; | F1/F2/F5/ESC",
        MatchPhase.BetweenRounds => "SPACE : next round | F5 restart | ESC back to select",
        MatchPhase.MatchFinished => "F5 restart match | ESC back to select",
        _ => string.Empty,
    };

    private static string ResultBanner(MatchState m) => m.Phase switch
    {
        MatchPhase.NotStarted => "CHARACTER SELECT",
        MatchPhase.BetweenRounds =>
            $"ROUND {m.RoundNumber} WINNER\n{DescribeRoundResult(m)}\nWINS  P1 {m.P1Wins} : P2 {m.P2Wins}",
        MatchPhase.MatchFinished => DescribeMatchResult(m),
        _ => string.Empty,
    };

    private static string DescribeMatchResult(MatchState m) => m.Outcome switch
    {
        MatchOutcome.P1Win => "P1 MATCH WIN",
        MatchOutcome.P2Win => "P2 MATCH WIN",
        MatchOutcome.Draw => "MATCH DRAW",
        _ => string.Empty,
    };

    private static string DescribeRoundResult(MatchState m)
    {
        if (m.CurrentRoundOutcome == RoundOutcome.None)
            return "-";

        string winner = m.CurrentRoundOutcome switch
        {
            RoundOutcome.P1Win => "P1 WIN",
            RoundOutcome.P2Win => "P2 WIN",
            RoundOutcome.DoubleKO => "DOUBLE KO",
            RoundOutcome.Draw => "DRAW",
            _ => "?",
        };
        string reason = m.CurrentRoundReason switch
        {
            RoundEndReason.Knockout => "KO",
            RoundEndReason.DoubleKO => "DOUBLE KO",
            RoundEndReason.TimeOver => "TIME OVER",
            RoundEndReason.TimeOverDraw => "TIME OVER DRAW",
            _ => "-",
        };
        return $"{winner} ({reason})";
    }

    private static string DescribeProjectiles(BattleState? state)
    {
        if (state is null || state.Projectiles.Count == 0)
            return "-";
        ProjectileState first = state.Projectiles[0];
        string text = $"{state.Projectiles.Count} [{first.TypeId}@P{first.OwnerFighterId + 1} " +
            $"({first.PositionX.ToDouble():0.##}, {first.PositionY.ToDouble():0.##}) " +
            $"life={first.RemainingLifetime}]";
        return state.Projectiles.Count > 1 ? text + " +" + (state.Projectiles.Count - 1) : text;
    }

    private static string DescribeLastMatchEvent(IReadOnlyList<MatchEvent> events)
    {
        if (events.Count == 0)
            return "-";
        MatchEvent last = events[events.Count - 1];
        BattleEvent e = last.RoundEvent;
        string actors = e.SourceFighterId == e.TargetFighterId
            ? $"P{e.SourceFighterId + 1}"
            : $"P{e.SourceFighterId + 1}→P{e.TargetFighterId + 1}";
        string amount = e.Amount > 0 ? $" {e.Amount}" : string.Empty;
        string move = e.MoveId is null ? string.Empty : $" {e.MoveId}";
        return $"{e.Type} {actors}{move}{amount} (R{last.RoundNumber}:{e.EventId})";
    }

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

    private static string DescribePosition(FighterState fighter) =>
        $"({fighter.PositionX.ToDouble():0.##}, {fighter.PositionY.ToDouble():0.##})";

    private static string DescribeMove(FighterState fighter) =>
        fighter.IsInMove ? $"{fighter.MoveId} / {fighter.LogicState} {fighter.PhaseRemainingFrames}" : "-";

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
