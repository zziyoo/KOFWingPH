using System.Text;
using Godot;
using NewKOF.BattleCore;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using NewKOF.BattleCore.Team;

namespace NewKOF;

/// <summary>
/// 战斗场景的表现层根节点（阶段 10B 起同时支持 1v1 与 3v3 队伍赛）。
///
/// <para><b>职责边界（不变）</b>：只读权威状态，绝不写回；不参与任何权威判定；
/// 不自己实现时间累积与补帧（交给 <see cref="FixedStepScheduler"/>）；
/// 不自己保存输入历史、不做 SOCD/指令识别（交给 <see cref="InputBuffer"/> 与内核）。</para>
///
/// <para><b>两套比赛层</b>：<see cref="MatchSession"/>（1v1，阶段 09）与
/// <see cref="TeamMatchSession"/>（3v3，阶段 10A）。二者互不干扰，本类只是按模式选择其中一个
/// 作为「当前会话」。场上永远是 1v1 的 <see cref="BattleState"/>。</para>
///
/// <para><b>输入隔离（D111/D123）</b>：模式选择、选人、选队、交锋过渡与终局阶段的界面按键
/// 在 <c>_Input</c> 中直接拦截，绝不传给 <see cref="KeyboardInputMap"/>；每回合/每场交锋
/// 都新建 <see cref="InputBuffer"/>，本类同步重建键盘映射。</para>
/// </summary>
public partial class BattleScene : Node2D
{
    /// <summary>界面阶段（权威阶段仍由会话决定；本枚举只区分「在哪个界面」）。</summary>
    private enum Stage
    {
        ModeSelect,
        DuelSelect,
        TeamSelect,
        DuelBattle,
        TeamBattle,
    }

    private MatchRules _rules = null!;
    private TeamMatchRules _teamRules = null!;
    private FighterCatalog _catalog = null!;

    private MatchSession _match = null!;
    private TeamMatchSession _team = null!;
    private TeamSelectionModel _teamSelect = null!;

    private KeyboardInputMap _inputMap = null!;
    private IInputSource _tracedSource = null!;
    private readonly InputTrace _trace = new();
    private bool _diagnostics;

    private Node2D? _fighter1Node;
    private Node2D? _fighter2Node;
    private FighterVisual? _visual1;
    private FighterVisual? _visual2;
    private Dictionary<string, AnimationSet> _presentationSets = new(StringComparer.Ordinal);

    private readonly FeedbackDirector _feedback = new();
    private readonly HitShake _shake = new();
    private readonly PresentationFrameBuffer _frameBuffer = new();
    private CombatFeedbackLayer _fx = null!;
    private bool _fxEnabled = true;
    private (int X, int Y) _shakeOffset;
    private Label? _hudLabel;
    private Label? _resultLabel;
    private DebugBoxDrawer _debugBoxes = null!;

    private Stage _stage = Stage.ModeSelect;
    private int _modeIndex;              // 0 = 1v1, 1 = 3v3
    private int _p1Index;
    private int _p2Index;
    private bool _p1Confirmed;
    private bool _p2Confirmed;

    private bool _paused;
    private bool _showDebugBoxes = true;
    private bool _backlogWarningIssued;

    /// <summary>单次渲染最多补的逻辑帧数。</summary>
    private const int MaxStepsPerAdvance = 5;

    public override void _Ready()
    {
        _fighter1Node = GetNodeOrNull<Node2D>("Fighter1");
        _fighter2Node = GetNodeOrNull<Node2D>("Fighter2");
        _hudLabel = GetNodeOrNull<Label>("Hud/HudLabel");
        _resultLabel = GetNodeOrNull<Label>("Result");
        if (_resultLabel is not null)
            _resultLabel.Position = new Vector2(430f, 190f);

        _debugBoxes = new DebugBoxDrawer { Enabled = _showDebugBoxes };
        AddChild(_debugBoxes);

        _fx = new CombatFeedbackLayer { Name = "CombatFx" };
        AddChild(_fx);
        int hudIndex = GetNodeOrNull<Node>("Hud")?.GetIndex() ?? GetChildCount() - 1;
        MoveChild(_fx, Math.Max(0, hudIndex));

        _diagnostics = HasArg("--inputdiag");
        _trace.Enabled = _diagnostics;

        _catalog = LoadFighterCatalog();
        _presentationSets = LoadPresentationSets();
        CreateFighterVisuals();
        _rules = RulesFromArgs();
        _teamRules = TeamRulesFromArgs();
        _teamSelect = new TeamSelectionModel(_catalog);
        _p1Index = 0;
        _p2Index = _catalog.Entries.Count > 1 ? 1 : 0;

        CreateDuelSession();
        CreateTeamSession();
        _stage = Stage.ModeSelect;
        SyncViewFromState();
    }

    /// <summary>从 <c>res://data/fighters/*.json</c> 一次性加载全部角色配置（D115）。</summary>
    private static FighterCatalog LoadFighterCatalog()
    {
        const string directory = "res://data/fighters";
        DirAccess? dir = DirAccess.Open(directory);
        if (dir is null)
            throw new InvalidOperationException(
                $"[BattleScene] 角色数据目录不存在：{directory}（错误码 {(int)DirAccess.GetOpenError()}）。");

        var documents = new List<FighterDataDocument>();
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".json", StringComparison.Ordinal))
                continue;

            string path = $"{directory}/{file}";
            Godot.FileAccess? access = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (access is null)
                throw new InvalidOperationException(
                    $"[BattleScene] 无法读取角色数据：{path}（错误码 {(int)Godot.FileAccess.GetOpenError()}）。");

            documents.Add(new FighterDataDocument(path, access.GetAsText()));
        }

        if (documents.Count == 0)
            throw new InvalidOperationException(
                $"[BattleScene] 角色数据目录没有 JSON 文件：{directory}。");

        return FighterDataLoader.LoadCatalog(documents);
    }

    private void CreateDuelSession()
    {
        _match = new MatchSession(
            _rules,
            (p1, p2) => BuildRoster(
                p1 ?? _catalog.Entries[0].FighterId,
                p2 ?? _catalog.Entries[^1].FighterId),
            initialStateFactory: CreateDuelInitialState,
            maxStepsPerAdvance: MaxStepsPerAdvance,
            framePresented: _frameBuffer.Enqueue);
        RebindInput(_match.RoundInputBuffer);
    }

    private void CreateTeamSession()
    {
        _team = new TeamMatchSession(
            _teamRules,
            BuildRoster,
            CreateBoutInitialState,
            maxStepsPerAdvance: MaxStepsPerAdvance,
            framePresented: _frameBuffer.Enqueue);
    }

    private FightRoster BuildRoster(string p1FighterId, string p2FighterId) =>
        _catalog.CreateRoster(p1FighterId, p2FighterId);

    /// <summary>当前激活的会话是否处于队伍赛。</summary>
    private bool TeamActive => _stage == Stage.TeamBattle;

    /// <summary>重新绑定键盘映射与（可选）诊断探针。</summary>
    private void RebindInput(InputBuffer buffer)
    {
        _inputMap = new KeyboardInputMap(buffer);
        _tracedSource = _diagnostics ? new TracingInputSource(buffer, _trace) : buffer;
        _paused = false;
    }

    /// <summary>1v1 每回合初态（<c>--hp</c> / <c>--duel</c> 只影响命令行调试）。</summary>
    private static BattleState CreateDuelInitialState(MatchRules rules)
    {
        int health = CapByDebugHealth(1000);
        int distance = InitialDistanceFromArgs();
        IReadOnlyList<FighterState> fighters = new[]
        {
            FighterState.Create(0, 1000, -distance, Facing.Right) with { Health = health },
            FighterState.Create(1, 1000, distance, Facing.Left) with { Health = health },
        };

        return new BattleState(0, fighters, BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: rules.EnableTimeout ? rules.RoundTimerFrames : null);
    }

    /// <summary>
    /// 3v3 每场交锋初态：我方队员的满血值与继承体力来自队伍状态；
    /// <c>--hp</c> 只在该值更小时生效（调试快速 KO）。
    /// </summary>
    private static BattleState CreateBoutInitialState(
        TeamMatchRules rules, TeamSideState p1, TeamSideState p2)
    {
        int distance = InitialDistanceFromArgs();
        FighterState f1 = FighterState.Create(0, p1.ActiveMaxHealth, -distance, Facing.Right) with
        {
            Health = Math.Min(p1.ActiveHealth, CapByDebugHealth(p1.ActiveHealth)),
        };
        FighterState f2 = FighterState.Create(1, p2.ActiveMaxHealth, distance, Facing.Left) with
        {
            Health = Math.Min(p2.ActiveHealth, CapByDebugHealth(p2.ActiveHealth)),
        };

        return new BattleState(0, new[] { f1, f2 }, BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: rules.EnableTimeout ? rules.BoutTimerFrames : null);
    }

    /// <summary>调试用体力上限：未传 <c>--hp</c> 时不改变（返回 <paramref name="health"/> 本身）。</summary>
    private static int CapByDebugHealth(int health)
    {
        int cap = InitialHealthFromArgs();
        return Math.Min(health, cap);
    }

    private static bool HasArg(string name) =>
        OS.GetCmdlineUserArgs().Any(a => a.Equals(name, StringComparison.Ordinal));

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

    private static TeamMatchRules TeamRulesFromArgs()
    {
        int seconds = 99;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--match-time=", StringComparison.Ordinal)
                && int.TryParse(arg["--match-time=".Length..], out int value)
                && value is >= 1 and <= TeamMatchRules.MaxRoundTimeSeconds)
            {
                seconds = value;
            }
        }
        return new TeamMatchRules(seconds);
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
    public int CurrentLogicFrame => TeamActive
        ? _team.CurrentBattleState?.Frame ?? 0
        : _match.CurrentBattleState?.Frame ?? 0;

    /// <summary>当前 1v1 比赛状态（只读；供调试/自动化断言）。</summary>
    public MatchState CurrentMatchState => _match.State;

    /// <summary>当前队伍赛状态（只读；供调试/自动化断言）。</summary>
    public TeamMatchState CurrentTeamState => _team.State;

    /// <summary>当前界面阶段名（只读；测试注入工具读取）。</summary>
    public string CurrentStageName => _stage.ToString();

    // ── 输入 ────────────────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && _diagnostics)
        {
            Trace("key",
                $"{key.Keycode} {(key.Pressed ? "down" : "up")} echo={key.Echo} " +
                $"held={DescribeHeldKeys()}");
        }

        if (@event is not InputEventKey pressed || pressed.Echo || !pressed.Pressed)
        {
            _inputMap.Handle(@event);
            if (@event is InputEventKey && _diagnostics)
                Trace("sample", DescribeHeldMasks());
            return;
        }

        if (pressed.Keycode == Key.F4)
        {
            _fxEnabled = !_fxEnabled;
            _fx.Enabled = _fxEnabled;
            if (!_fxEnabled)
            {
                _fx.Clear();
                _shake.Reset();
                _shakeOffset = (0, 0);
            }
            SyncViewFromState();
            return;
        }

        // 全局调试键（所有阶段可用）。
        if (pressed.Keycode == Key.F2)
        {
            _showDebugBoxes = !_showDebugBoxes;
            _debugBoxes.Enabled = _showDebugBoxes;
            _debugBoxes.Refresh();
            return;
        }
        if (pressed.Keycode == Key.F1)
        {
            TogglePause();
            return;
        }
        if (pressed.Keycode == Key.F3 && _diagnostics)
        {
            GD.Print("[InputTrace]\n" + _trace.Dump());
            SyncViewFromState();
            return;
        }

        switch (_stage)
        {
            case Stage.ModeSelect:
                HandleModeKey(pressed.Keycode);
                return;
            case Stage.DuelSelect:
                HandleDuelSelectKey(pressed.Keycode);
                return;
            case Stage.TeamSelect:
                HandleTeamSelectKey(pressed.Keycode);
                return;
            case Stage.DuelBattle:
                HandleDuelBattleKey(pressed.Keycode);
                return;
            case Stage.TeamBattle:
                HandleTeamBattleKey(pressed.Keycode);
                return;
        }
    }

    private void HandleModeKey(Key code)
    {
        switch (code)
        {
            case Key.A or Key.Left:
                _modeIndex = 0;
                break;
            case Key.D or Key.Right:
                _modeIndex = 1;
                break;
            case Key.Key1:
                _modeIndex = 0;
                ConfirmMode();
                return;
            case Key.Key2:
                _modeIndex = 1;
                ConfirmMode();
                return;
            case Key.J or Key.Comma or Key.Enter:
                ConfirmMode();
                return;
            case Key.Escape:
                break;
        }
        SyncViewFromState();
    }

    private void ConfirmMode()
    {
        if (_modeIndex == 1)
        {
            _teamSelect.Reset();
            _stage = Stage.TeamSelect;
        }
        else
        {
            _p1Confirmed = false;
            _p2Confirmed = false;
            _stage = Stage.DuelSelect;
        }
        SyncViewFromState();
    }

    private void HandleDuelSelectKey(Key code)
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
                    StartDuel();
                break;
            case Key.Escape:
                _stage = Stage.ModeSelect; break;
        }
        SyncViewFromState();
    }

    private void HandleTeamSelectKey(Key code)
    {
        switch (code)
        {
            case Key.W:
                _teamSelect.MoveCursor(TeamSelectionModel.Player1, -1); break;
            case Key.S:
                _teamSelect.MoveCursor(TeamSelectionModel.Player1, +1); break;
            case Key.A:
                _teamSelect.CycleCharacter(TeamSelectionModel.Player1, -1); break;
            case Key.D:
                _teamSelect.CycleCharacter(TeamSelectionModel.Player1, +1); break;
            case Key.J:
                _teamSelect.ToggleReady(TeamSelectionModel.Player1); break;
            case Key.Up:
                _teamSelect.MoveCursor(TeamSelectionModel.Player2, -1); break;
            case Key.Down:
                _teamSelect.MoveCursor(TeamSelectionModel.Player2, +1); break;
            case Key.Left:
                _teamSelect.CycleCharacter(TeamSelectionModel.Player2, -1); break;
            case Key.Right:
                _teamSelect.CycleCharacter(TeamSelectionModel.Player2, +1); break;
            case Key.Comma:
                _teamSelect.ToggleReady(TeamSelectionModel.Player2); break;
            case Key.Enter:
                if (_teamSelect.BothReady)
                    StartTeamMatch();
                break;
            case Key.Escape:
                _stage = Stage.ModeSelect; break;
        }
        SyncViewFromState();
    }

    private void HandleDuelBattleKey(Key code)
    {
        switch (code)
        {
            case Key.F5:
                _match.RestartMatch();
                _feedback.Reset();
                BindDuelRound();
                return;
            case Key.Escape:
                _match.ReturnToSelection();
                _feedback.Reset();
                ResetFeedbackLayer();
                _p1Confirmed = false;
                _p2Confirmed = false;
                RebindInput(_match.RoundInputBuffer);
                _stage = Stage.DuelSelect;
                SyncViewFromState();
                return;
        }

        MatchPhase phase = _match.State.Phase;
        if (phase == MatchPhase.BetweenRounds)
        {
            if (code == Key.Space)
            {
                _match.StartNextRound();
                BindDuelRound();
            }
            else
            {
                SyncViewFromState();
            }
            return;
        }

        if (phase == MatchPhase.MatchFinished)
        {
            SyncViewFromState();
            return;
        }

        _inputMap.Handle(BuildKeyEvent(code));
        if (_diagnostics) Trace("sample", DescribeHeldMasks());
        SyncViewFromState();
    }

    private void HandleTeamBattleKey(Key code)
    {
        switch (code)
        {
            case Key.F5:
                _team.RestartMatch();
                _feedback.Reset();
                BindTeamBout();
                return;
            case Key.Escape:
                _team.ReturnToSelection();
                _feedback.Reset();
                ResetFeedbackLayer();
                _teamSelect.ClearReady();
                RebindInput(_team.BoutInputBuffer);
                _stage = Stage.TeamSelect;
                SyncViewFromState();
                return;
        }

        TeamMatchPhase phase = _team.State.Phase;
        if (phase == TeamMatchPhase.BetweenBouts)
        {
            if (code == Key.Space)
            {
                _team.StartNextBout();
                BindTeamBout();
            }
            else
            {
                SyncViewFromState();
            }
            return;
        }

        if (phase == TeamMatchPhase.MatchFinished)
        {
            SyncViewFromState();
            return;
        }

        _inputMap.Handle(BuildKeyEvent(code));
        if (_diagnostics) Trace("sample", DescribeHeldMasks());
        SyncViewFromState();
    }

    /// <summary>把「已按下的键」重新包装成按键事件交给键盘映射（战斗阶段统一路径）。</summary>
    private static InputEventKey BuildKeyEvent(Key code) => new()
    {
        Keycode = code,
        PhysicalKeycode = code,
        Pressed = true,
    };

    private void Trace(string category, string detail)
    {
        _trace.Record(category, detail);
        GD.Print($"[InputTrace] {category}: {detail}");
    }

    private string DescribeHeldKeys() =>
        string.Join(",", _inputMap.HeldKeys.Select(k => k.ToString()));

    private string DescribeHeldMasks() =>
        $"P1={InputTrace.DescribeBits(_inputMap.HeldMask(0))} " +
        $"P2={InputTrace.DescribeBits(_inputMap.HeldMask(1))}";

    private int Cycle(int index, int delta) =>
        (index + delta + _catalog.Entries.Count) % _catalog.Entries.Count;

    private void TogglePause()
    {
        BattleSession? session = TeamActive ? _team.CurrentBout : _match.CurrentRound;
        bool live = TeamActive ? _team.State.IsBoutLive : _match.State.IsRoundLive;
        if (session is null || !live)
            return;

        _paused = !_paused;
        if (_paused) session.Scheduler.Pause();
        else session.Scheduler.Resume();
        SyncViewFromState();
    }

    private void StartDuel()
    {
        _match.SelectFighters(
            _catalog.Entries[_p1Index].FighterId,
            _catalog.Entries[_p2Index].FighterId);
        _match.StartMatch();
        _stage = Stage.DuelBattle;
        BindDuelRound();
    }

    private void StartTeamMatch()
    {
        _team.SelectTeams(
            _teamSelect.BuildTeam(TeamSelectionModel.Player1, "P1"),
            _teamSelect.BuildTeam(TeamSelectionModel.Player2, "P2"));
        _team.StartMatch();
        _stage = Stage.TeamBattle;
        BindTeamBout();
    }

    private void BindDuelRound()
    {
        RebindInput(_match.RoundInputBuffer);
        _backlogWarningIssued = false;
        if (_match.CurrentRound is BattleSession round
            && _match.State.P1FighterId is string p1 && _match.State.P2FighterId is string p2)
        {
            _debugBoxes.Bind(round, BuildRoster(p1, p2));
            _visual1?.SetAnimationSet(SetFor(p1));
            _visual2?.SetAnimationSet(SetFor(p2));
            _visual1?.Reset();
            _visual2?.Reset();
            ResetFeedbackLayer();
        }
        SyncViewFromState();
    }

    private void BindTeamBout()
    {
        RebindInput(_team.BoutInputBuffer);
        _backlogWarningIssued = false;
        TeamSideState? p1 = _team.State.P1;
        TeamSideState? p2 = _team.State.P2;
        if (_team.CurrentBout is BattleSession bout && p1 is not null && p2 is not null)
        {
            _debugBoxes.Bind(bout, BuildRoster(p1.ActiveFighterId, p2.ActiveFighterId));
            _visual1?.SetAnimationSet(SetFor(p1.ActiveFighterId));
            _visual2?.SetAnimationSet(SetFor(p2.ActiveFighterId));
            _visual1?.Reset();
            _visual2?.Reset();
            ResetFeedbackLayer();
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
        _shakeOffset = _shake.Tick();

        int steps = TeamActive
            ? _team.Advance(delta, _tracedSource)
            : _match.Advance(delta, _tracedSource);

        // 逐逻辑帧消费（D127/D128）：先按时间顺序推进两个动画时钟并生成命中特效，
        // 再渲染最终权威状态。一次 Advance 提交几帧就消费几帧，绝不只看最后一帧。
        DrainPresentationFrames();

        if (steps > 0)
        {
            if (_diagnostics)
                Trace("state", DescribeFrameState());
            SyncViewFromState();
        }

        long dropped = TeamActive
            ? _team.CurrentBout?.Scheduler.DroppedStepCount ?? 0
            : _match.CurrentRound?.Scheduler.DroppedStepCount ?? 0;
        if (dropped > 0 && !_backlogWarningIssued)
        {
            _backlogWarningIssued = true;
            GD.PushWarning(
                $"[BattleScene] 帧积压超过上限 {MaxStepsPerAdvance}，已丢弃 {dropped} 个逻辑帧。" +
                "请检查渲染性能。");
        }
    }

    private string DescribeFrameState()
    {
        BattleState? state = TeamActive ? _team.CurrentBattleState : _match.CurrentBattleState;
        if (state is null)
            return "-";
        FighterState p1 = state.Fighter(0);
        FighterState p2 = state.Fighter(1);
        return $"f{state.Frame} P1={p1.LogicState}/{p1.MoveId ?? "-"}/{p1.Health} " +
            $"P2={p2.LogicState}/{p2.MoveId ?? "-"}/{p2.Health}";
    }

    // ── 只读渲染 ────────────────────────────────────────────────────

    private void SyncViewFromState()
    {
        switch (_stage)
        {
            case Stage.ModeSelect:
                RenderSelection(null, null);
                break;
            case Stage.DuelSelect:
                RenderSelection(
                    FighterState.Create(0, 1000, -120, Facing.Right),
                    FighterState.Create(1, 1000, 120, Facing.Left));
                break;
            case Stage.TeamSelect:
                RenderSelection(
                    FighterState.Create(0, 1000, -140, Facing.Right),
                    FighterState.Create(1, 1000, 140, Facing.Left));
                break;
            case Stage.DuelBattle:
                RenderBattle(_match.CurrentBattleState);
                break;
            case Stage.TeamBattle:
                RenderBattle(_team.CurrentBattleState);
                break;
        }
    }

    private void RenderSelection(FighterState? p1, FighterState? p2)
    {
        BindSelectionVisuals();
        if (p1.HasValue && p2.HasValue)
            PlaceFighters(p1.Value, p2.Value);

        if (_hudLabel is not null)
            _hudLabel.Text = BuildHud();
        if (_resultLabel is not null)
        {
            _resultLabel.Text = BannerText();
            _resultLabel.Visible = !string.IsNullOrEmpty(_resultLabel.Text);
        }

        _debugBoxes.Refresh();
    }

    private void RenderBattle(BattleState? state)
    {
        FighterState p1 = state?.Fighter(0) ?? FighterState.Create(0, 1000, -120, Facing.Right);
        FighterState p2 = state?.Fighter(1) ?? FighterState.Create(1, 1000, 120, Facing.Left);
        PlaceFighters(p1, p2, state?.Frame ?? 0);

        if (_hudLabel is not null)
            _hudLabel.Text = BuildHud();
        if (_resultLabel is not null)
        {
            _resultLabel.Text = BannerText();
            _resultLabel.Visible = !string.IsNullOrEmpty(_resultLabel.Text);
        }

        _debugBoxes.Refresh();
    }

    private void PlaceFighters(FighterState p1, FighterState p2, int logicFrame = 0)
    {
        const float originX = 640f;
        const float groundY = 560f;

        // 抖动只是节点偏移（表现），渲染同一权威坐标时会被重新覆盖。
        var shake = new Vector2(_shakeOffset.X, _shakeOffset.Y);
        if (_fighter1Node is not null)
            _fighter1Node.Position = new Vector2(
                originX + (float)p1.PositionX.ToDouble(),
                groundY - (float)p1.PositionY.ToDouble()) + shake;
        if (_fighter2Node is not null)
            _fighter2Node.Position = new Vector2(
                originX + (float)p2.PositionX.ToDouble(),
                groundY - (float)p2.PositionY.ToDouble()) + shake;

        // 只读投影：状态 → 动画 → 姿态（不写回任何战斗状态）。
        _visual1?.Apply(p1, logicFrame);
        _visual2?.Apply(p2, logicFrame);
    }
}

// ── 只读文本（HUD / 横幅）──────────────────────────────────────────

public partial class BattleScene
{
    private string BuildHud() => _stage switch
    {
        Stage.ModeSelect => BuildModeHud(),
        Stage.DuelSelect => BuildDuelSelectHud(),
        Stage.TeamSelect => BuildTeamSelectHud(),
        Stage.DuelBattle => BuildDuelHud(),
        _ => BuildTeamHud(),
    };

    private string BuildModeHud()
    {
        string one = _modeIndex == 0 ? "< 1v1 MATCH >" : "  1v1 MATCH  ";
        string three = _modeIndex == 1 ? "< 3v3 TEAM >" : "  3v3 TEAM  ";
        return $"=== NEWKOF ===\n" +
            $"Mode:  {one}    {three}\n" +
            $"A / D or LEFT / RIGHT : switch mode\n" +
            $"J / ENTER : confirm     1 / 2 : quick select\n" +
            $"Catalog: {string.Join(", ", _catalog.Entries.Select(e => e.FighterId))}\n" +
            $"F1 pause  F2 hitboxes  F5 restart  ESC back\n" +
            (_diagnostics ? "Diagnostics: ON (F3 dumps input trace)\n" : string.Empty);
    }

    private string BuildDuelSelectHud()
    {
        string p1Name = _catalog.Entries[_p1Index].DisplayName;
        string p2Name = _catalog.Entries[_p2Index].DisplayName;
        string p1Tag = _p1Confirmed ? "[CONFIRMED]" : "[choose]";
        string p2Tag = _p2Confirmed ? "[CONFIRMED]" : "[choose]";

        return $"=== CHARACTER SELECT (1v1) ===\n" +
            $"P1: < {p1Name} >  {p1Tag}\n" +
            $"P2: < {p2Name} >  {p2Tag}\n" +
            $"Rules: first to {_rules.RoundsToWin} wins / max {_rules.MaxRounds} rounds / " +
            $"{_rules.RoundTimeSeconds}s per round\n" +
            $"A / D : P1 switch      LEFT / RIGHT : P2 switch\n" +
            $"J : P1 confirm         , : P2 confirm\n" +
            $"ENTER : start match (both confirmed)      ESC : back\n" +
            $"Catalog: {string.Join(", ", _catalog.Entries.Select(e => e.FighterId))}\n";
    }

    private string BuildTeamSelectHud()
    {
        string p1Tag = _teamSelect.P1Ready ? "[READY]" : "[choose]";
        string p2Tag = _teamSelect.P2Ready ? "[READY]" : "[choose]";

        return $"=== 3v3 TEAM SELECT ===\n" +
            $"P1: {_teamSelect.DescribeLine(TeamSelectionModel.Player1)}  {p1Tag}\n" +
            $"P2: {_teamSelect.DescribeLine(TeamSelectionModel.Player2)}  {p2Tag}\n" +
            $"Rules: {_teamRules.RoundTimeSeconds}s per bout / last fighter standing\n" +
            $"P1: W/S slot  A/D fighter  J ready\n" +
            $"P2: UP/DOWN slot  LEFT/RIGHT fighter  , ready\n" +
            $"ENTER : start team match (both ready)      ESC : back\n" +
            $"Catalog: {string.Join(", ", _catalog.Entries.Select(e => e.FighterId))}\n";
    }

    private string BuildDuelHud()
    {
        MatchState m = _match.State;
        BattleState? state = _match.CurrentBattleState;
        FighterState p1 = state?.Fighter(0) ?? FighterState.Create(0, 1000, -120, Facing.Right);
        FighterState p2 = state?.Fighter(1) ?? FighterState.Create(1, 1000, 120, Facing.Left);

        string p1Name = m.P1FighterId is null ? "-" : _catalog.DisplayName(m.P1FighterId);
        string p2Name = m.P2FighterId is null ? "-" : _catalog.DisplayName(m.P2FighterId);
        FrameInput last = _match.RoundInputBuffer.LastInput ?? FrameInput.Empty(state?.Frame ?? 0);
        int seconds = (m.RemainingTimeFrames + BattleCoreInfo.LogicFrameRate - 1)
            / BattleCoreInfo.LogicFrameRate;

        var text = new StringBuilder(640);
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
        AppendDiagnostics(text);
        return text.ToString();
    }

    private string BuildTeamHud()
    {
        TeamMatchState m = _team.State;
        BattleState? state = _team.CurrentBattleState;
        FighterState p1 = state?.Fighter(0) ?? FighterState.Create(0, 1000, -120, Facing.Right);
        FighterState p2 = state?.Fighter(1) ?? FighterState.Create(1, 1000, 120, Facing.Left);
        FrameInput last = _team.BoutInputBuffer.LastInput ?? FrameInput.Empty(state?.Frame ?? 0);
        int seconds = (m.RemainingTimeFrames + BattleCoreInfo.LogicFrameRate - 1)
            / BattleCoreInfo.LogicFrameRate;

        var text = new StringBuilder(720);
        int teamSize = m.P1?.Team.Count ?? TeamDefinition.TeamSize;
        text.Append($"=== TEAM MATCH (Bout {m.BoutNumber}/{teamSize * 2 - 1}) ===\n");
        text.Append($"Phase: {m.Phase}   Time: {seconds}s\n");
        text.Append($"P1: {DescribeTeamSide(m.P1)}\n");
        text.Append($"P2: {DescribeTeamSide(m.P2)}\n");
        if (m.P1 is not null && m.P2 is not null)
        {
            text.Append($"Active: P1 [{m.P1.ActiveFighterId}] {p1.Health}/{p1.MaxHealth}   " +
                $"P2 [{m.P2.ActiveFighterId}] {p2.Health}/{p2.MaxHealth}\n");
            text.Append($"Left: P1 {m.P1.RemainingCount} / P2 {m.P2.RemainingCount}\n");
        }
        text.Append($"Last Bout: {DescribeBoutResult(m)}\n");
        if (m.Phase == TeamMatchPhase.MatchFinished)
            text.Append($"Team Result: {DescribeTeamOutcome(m.Outcome)}\n");
        text.Append($"Logic Frame: {state?.Frame ?? 0}{(_paused ? "  [PAUSED · F1]" : string.Empty)}\n");
        text.Append($"P1: {Describe(last.Player1)}  P2: {Describe(last.Player2)}\n");
        text.Append($"P1 State: {DescribeState(p1)}   P2 State: {DescribeState(p2)}\n");
        text.Append($"P1 Move: {DescribeMove(p1)}   P2 Move: {DescribeMove(p2)}\n");
        text.Append($"Combo: P1 {p1.Combo} | P2 {p2.Combo}\n");
        text.Append($"Projectiles: {DescribeProjectiles(state)}\n");
        text.Append($"Event: {DescribeLastTeamEvent(_team.LastEvents)}\n");
        text.Append(ControlsHint(m.Phase));
        AppendDiagnostics(text);
        return text.ToString();
    }

    private void AppendDiagnostics(StringBuilder text)
    {
        if (!_diagnostics)
            return;

        string[] lines = _trace.Lines.ToArray();
        int start = Math.Max(0, lines.Length - 4);
        text.Append("--- inputdiag (last 4) ---\n");
        for (int i = start; i < lines.Length; i++)
            text.Append(lines[i]).Append('\n');
    }

    private static string ControlsHint(MatchPhase phase) => phase switch
    {
        MatchPhase.RoundInProgress => "Controls: P1 WASD + J/K/U/I | P2 arrows + , . / ; | F1/F2/F5/ESC\n",
        MatchPhase.BetweenRounds => "SPACE : next round | F5 restart | ESC back to select\n",
        MatchPhase.MatchFinished => "F5 restart match | ESC back to select\n",
        _ => string.Empty,
    };

    private static string ControlsHint(TeamMatchPhase phase) => phase switch
    {
        TeamMatchPhase.BoutInProgress =>
            "Controls: P1 WASD + J/K/U/I | P2 arrows + , . / ; | F1/F2/F5/ESC\n",
        TeamMatchPhase.BetweenBouts => "SPACE : next fighter | F5 restart | ESC back to teams\n",
        TeamMatchPhase.MatchFinished => "F5 restart team match | ESC back to teams\n",
        _ => string.Empty,
    };

    private string BannerText() => _stage switch
    {
        Stage.ModeSelect => "NEWKOF\nSELECT MODE",
        Stage.DuelSelect => "CHARACTER SELECT",
        Stage.TeamSelect => "3v3 TEAM SELECT",
        Stage.DuelBattle => DuelBanner(_match.State),
        Stage.TeamBattle => TeamBanner(_team.State),
        _ => string.Empty,
    };

    private static string DuelBanner(MatchState m) => m.Phase switch
    {
        MatchPhase.BetweenRounds =>
            $"ROUND {m.RoundNumber} WINNER\n{DescribeRoundResult(m)}\nWINS  P1 {m.P1Wins} : P2 {m.P2Wins}",
        MatchPhase.MatchFinished => DescribeMatchResult(m),
        _ => string.Empty,
    };

    private static string TeamBanner(TeamMatchState m) => m.Phase switch
    {
        TeamMatchPhase.BetweenBouts =>
            $"BOUT {m.BoutNumber} RESULT\n{DescribeBoutResult(m)}\n" +
            $"NEXT  P1 {m.P1?.ActiveFighterId ?? "-"} : P2 {m.P2?.ActiveFighterId ?? "-"}",
        TeamMatchPhase.MatchFinished => DescribeTeamOutcome(m.Outcome),
        _ => string.Empty,
    };

    private static string DescribeTeamOutcome(TeamMatchOutcome outcome) => outcome switch
    {
        TeamMatchOutcome.P1TeamWin => "P1 TEAM WIN",
        TeamMatchOutcome.P2TeamWin => "P2 TEAM WIN",
        TeamMatchOutcome.TeamDraw => "TEAM DRAW",
        _ => string.Empty,
    };

    private static string DescribeBoutResult(TeamMatchState m)
    {
        if (m.LastBoutOutcome == RoundOutcome.None)
            return "-";

        string winner = m.LastBoutOutcome switch
        {
            RoundOutcome.P1Win => "P1 WIN",
            RoundOutcome.P2Win => "P2 WIN",
            RoundOutcome.DoubleKO => "DOUBLE KO",
            RoundOutcome.Draw => "DRAW",
            _ => "?",
        };
        string reason = m.LastBoutReason switch
        {
            RoundEndReason.Knockout => "KO",
            RoundEndReason.DoubleKO => "DOUBLE KO",
            RoundEndReason.TimeOver => "TIME OVER",
            RoundEndReason.TimeOverDraw => "TIME OVER DRAW",
            _ => "-",
        };
        return $"{winner} ({reason})";
    }

    /// <summary>把一侧队伍渲染成 <c>&gt; Kai(700) | Rei | X Kai(0)</c>（&gt; 出战、X 淘汰）。</summary>
    private static string DescribeTeamSide(TeamSideState? side)
    {
        if (side is null)
            return "-";

        var parts = new List<string>(side.Team.Count);
        for (int i = 0; i < side.Team.Count; i++)
        {
            string name = side.Team.MemberAt(i).FighterId;
            if (side.Eliminated[i])
                parts.Add($"X {name}({side.Health[i]})");
            else if (i == side.ActiveSlot)
                parts.Add($"> {name}({side.Health[i]})");
            else
                parts.Add($"  {name}({side.Health[i]})");
        }
        return string.Join(" | ", parts);
    }

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
        return DescribeEvent(last.RoundEvent, $"R{last.RoundNumber}");
    }

    private static string DescribeLastTeamEvent(IReadOnlyList<TeamMatchEvent> events)
    {
        if (events.Count == 0)
            return "-";
        TeamMatchEvent last = events[events.Count - 1];
        return DescribeEvent(last.BoutEvent, $"B{last.BoutNumber}");
    }

    private static string DescribeEvent(BattleEvent e, string scope)
    {
        string actors = e.SourceFighterId == e.TargetFighterId
            ? $"P{e.SourceFighterId + 1}"
            : $"P{e.SourceFighterId + 1}→P{e.TargetFighterId + 1}";
        string amount = e.Amount > 0 ? $" {e.Amount}" : string.Empty;
        string move = e.MoveId is null ? string.Empty : $" {e.MoveId}";
        return $"{e.Type} {actors}{move}{amount} ({scope}:{e.EventId})";
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

// ── 表现资源（阶段 10C，D125）─────────────────────────────────────

public partial class BattleScene
{
    /// <summary>
    /// 一次性加载 <c>res://data/presentation/*.json</c>。
    /// <b>目录缺失 = 允许降级</b>（用内置 fallback 姿态）；<b>配置非法 = 明确报错</b>（带路径与字段）。
    /// </summary>
    private static Dictionary<string, AnimationSet> LoadPresentationSets()
    {
        const string directory = "res://data/presentation";
        var sets = new Dictionary<string, AnimationSet>(StringComparer.Ordinal);
        if (!DirAccess.DirExistsAbsolute(directory))
            return sets;

        DirAccess? dir = DirAccess.Open(directory);
        if (dir is null)
            return sets;

        var documents = new List<PresentationDataDocument>();
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".json", StringComparison.Ordinal))
                continue;

            string path = $"{directory}/{file}";
            Godot.FileAccess? access = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (access is null)
                throw new InvalidOperationException(
                    $"[BattleScene] 无法读取表现配置：{path}" +
                    $"（错误码 {(int)Godot.FileAccess.GetOpenError()}）。");

            documents.Add(new PresentationDataDocument(path, access.GetAsText()));
        }

        if (documents.Count == 0)
            return sets;

        try
        {
            return new Dictionary<string, AnimationSet>(
                PresentationDataLoader.LoadCatalog(documents), StringComparer.Ordinal);
        }
        catch (PresentationDataException ex)
        {
            throw new InvalidOperationException($"[BattleScene] 表现配置非法：{ex.Message}", ex);
        }
    }

    private void CreateFighterVisuals()
    {
        if (_fighter1Node?.GetNodeOrNull<ColorRect>("Body") is ColorRect body1)
            _visual1 = new FighterVisual(_fighter1Node, body1, SetFor(DefaultFighterId(0)));
        if (_fighter2Node?.GetNodeOrNull<ColorRect>("Body") is ColorRect body2)
            _visual2 = new FighterVisual(_fighter2Node, body2, SetFor(DefaultFighterId(1)));
    }

    private string DefaultFighterId(int player) =>
        _catalog.Entries[Math.Min(player, _catalog.Entries.Count - 1)].FighterId;

    /// <summary>按角色取表现配置；缺失时用显式 fallback（不改变战斗结果）。</summary>
    private AnimationSet SetFor(string fighterId) =>
        _presentationSets.TryGetValue(fighterId, out AnimationSet? set)
            ? set
            : AnimationSet.CreateFallback(fighterId, FallbackColor(fighterId));

    /// <summary>缺资源时的稳定可区分颜色（同一角色每次相同）。</summary>
    private static string FallbackColor(string fighterId)
    {
        int hash = 0;
        foreach (char c in fighterId)
            hash = (hash * 31 + c) & 0xFFFFFF;

        int r = 96 + (hash & 0x7F);
        int g = 96 + ((hash >> 8) & 0x7F);
        int b = 96 + ((hash >> 16) & 0x7F);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>选人/选队阶段：把当前槽位角色对应的表现配置绑到两个角色节点上。</summary>
    private void BindSelectionVisuals()
    {
        switch (_stage)
        {
            case Stage.ModeSelect:
                _visual1?.SetAnimationSet(SetFor(_catalog.Entries[0].FighterId));
                _visual2?.SetAnimationSet(SetFor(_catalog.Entries[^1].FighterId));
                break;
            case Stage.DuelSelect:
                _visual1?.SetAnimationSet(SetFor(_catalog.Entries[_p1Index].FighterId));
                _visual2?.SetAnimationSet(SetFor(_catalog.Entries[_p2Index].FighterId));
                break;
            case Stage.TeamSelect:
                _visual1?.SetAnimationSet(
                    SetFor(_teamSelect.CharacterAt(TeamSelectionModel.Player1, _teamSelect.P1Cursor).FighterId));
                _visual2?.SetAnimationSet(
                    SetFor(_teamSelect.CharacterAt(TeamSelectionModel.Player2, _teamSelect.P2Cursor).FighterId));
                break;
        }
    }
}

// ── 打击反馈（阶段 10D，D126；阶段 10 最终补修，D127/D128）──────────

public partial class BattleScene
{
    /// <summary>
    /// 逐逻辑帧消费表现快照：先推进动画时钟（每个已提交帧一次），再生成反馈。
    ///
    /// <para><b>为什么不能只读 LastEvents</b>：一次 <see cref="_Process"/> 可能推进多个逻辑帧，
    /// 命中可能发生在其中较早的一帧；只读最后一帧会丢掉事件，用最终状态取坐标会把火花
    /// 画在击退之后的位置。</para>
    ///
    /// <para>反馈身份 = <c>(scope, EventId)</c>；位置取事件帧提交后的权威状态。
    /// 队列每渲染帧整批取走并清空，不会跨帧残留。</para>
    /// </summary>
    private void DrainPresentationFrames()
    {
        IReadOnlyList<PresentationFrame> frames = _frameBuffer.Drain();
        if (frames.Count == 0)
            return;

        // ① 动画时钟：无论本帧有没有事件都要观察（冻结/恢复信息在无事件帧里）。
        for (int i = 0; i < frames.Count; i++)
        {
            _visual1?.ObserveFrame(frames[i], 0);
            _visual2?.ObserveFrame(frames[i], 1);
        }

        if (!_fxEnabled)
            return; // F4 关闭特效：只推进时钟，不生成火花/抖动/闪白（权威状态不受影响）。

        // ② 反馈：同一帧的多个事件全部消费；同一事件只出一次特效。
        for (int i = 0; i < frames.Count; i++)
        {
            PresentationFrame frame = frames[i];
            for (int e = 0; e < frame.Events.Count; e++)
            {
                if (!_feedback.TryConsume(frame.Scope, frame.Events[e], out FeedbackCue cue))
                    continue;
                ApplyCue(cue, frame);
            }
        }
    }

    private void ApplyCue(FeedbackCue cue, in PresentationFrame frame)
    {
        (Color color, int size, int shake) = CueStyle(cue.Kind);
        if (size > 0)
        {
            // 位置来自事件帧的权威状态（不是当前渲染帧的最终位置）。
            (double x, double y) = FeedbackGeometry.Anchor(cue, frame.StateAfter);
            var position = new Vector2(640f + (float)x, 560f - (float)y);

            // 固定方向序列（身份 → 角度），保证同一回放产生同样画面。
            float angle = (cue.Identity % 6) * (Mathf.Pi / 3f);
            var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.6f;
            _fx.Spawn(position, color, size, velocity);
        }

        _shake.Trigger(shake);

        // 闪白按逻辑帧计时；取「事件帧 / 当前渲染帧」较大值，低帧率下也不会整段跳过。
        if (cue.Kind is FeedbackKind.Hit or FeedbackKind.ThrowImpact)
            TargetVisual(cue.TargetFighterId)?.FlashUntil(
                Math.Max(frame.LogicFrame, CurrentLogicFrame) + 5);
    }

    private FighterVisual? TargetVisual(int fighterId) => fighterId switch
    {
        0 => _visual1,
        1 => _visual2,
        _ => null,
    };

    /// <summary>反馈样式表：颜色、尺寸与抖动幅度（0 = 不抖动）。</summary>
    private static (Color Color, int Size, int Shake) CueStyle(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Hit => (Color.FromHtml("#FFE58A"), 26, 3),
        FeedbackKind.Blocked => (Color.FromHtml("#7FC0FF"), 20, 1),
        FeedbackKind.ThrowImpact => (Color.FromHtml("#FFB347"), 34, 4),
        FeedbackKind.ThrowClash => (Color.FromHtml("#FFFFFF"), 30, 3),
        FeedbackKind.StrikeClash => (Color.FromHtml("#FFFFFF"), 30, 3),
        FeedbackKind.ProjectileImpact => (Color.FromHtml("#FFD166"), 24, 2),
        FeedbackKind.ProjectileBlocked => (Color.FromHtml("#8FD3FF"), 20, 1),
        FeedbackKind.ProjectileClash => (Color.FromHtml("#FFFFFF"), 28, 2),
        FeedbackKind.Knockout => (Color.FromHtml("#FF3B3B"), 48, 6),
        FeedbackKind.DoubleKnockout => (Color.FromHtml("#FF3B3B"), 48, 6),
        FeedbackKind.TimeOver => (Colors.Transparent, 0, 0),
        _ => (Colors.Transparent, 0, 0),
    };

    /// <summary>清空瞬时表现（新回合/换人/关闭特效）：不动权威状态，也不清事件去重。</summary>
    private void ResetFeedbackLayer()
    {
        _frameBuffer.Clear();
        _fx.Clear();
        _shake.Reset();
        _shakeOffset = (0, 0);
        _visual1?.Reset();
        _visual2?.Reset();
    }
}
