using System.Reflection;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 09 最终专项补修（D117–D119）：
/// <list type="number">
/// <item><b>P1-A</b> <see cref="MatchSession.Advance"/> 多帧批次越过回合终局；</item>
/// <item><b>P1-B</b> 比赛事件发布早于权威 <see cref="MatchState"/> 更新；</item>
/// <item><b>P2-A</b> <see cref="MatchState"/> 接受不自洽的 RoundOutcome / RoundEndReason。</item>
/// </list>
/// 每项都先写真实失败复现，确认根因后做最小修复。
/// </summary>
public class Stage09EdgeCaseTests
{
    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    private static AttackDefinition LethalJab() => new(
        offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
        halfWidth: FixedPoint.FromInt(30), halfHeight: FixedPoint.FromInt(30),
        damage: 1000, hitstunFrames: 5, blockstunFrames: 5);

    /// <summary>1/1/1 裸轻拳、伤害 1000 的「两帧 KO」配置；<paramref name="startupFrames"/> 可调慢。</summary>
    private static FightRoster QuickRoster(int startupFrames = 1, int activeFrames = 1)
    {
        FighterDefinition Build(string id) => new(id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[]
            {
                new MoveDefinition("Jab", "5A", startupFrames, activeFrames, 1,
                    attacks: new[] { LethalJab() }),
            });

        return new FightRoster(Build("Kai"), Build("Rei"));
    }

    private static BattleState Duel(int health = 1000, int? timerFrames = null) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right) with { Health = health },
            FighterState.Create(1, 1000, 30, Facing.Left) with { Health = health },
        },
        BattleState.CreateDefault().StateVersion,
        round: null, projectiles: null, nextProjectileId: 1,
        roundTimerRemainingFrames: timerFrames);

    /// <summary>P1 已处于 Active 首帧（frame 0 即命中）的初态。</summary>
    private static BattleState ActiveP1(int activeFrames = 2) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right) with
            {
                LogicState = FighterLogicState.Active,
                MoveId = "Jab",
                PhaseRemainingFrames = activeFrames,
                MoveStartedFrame = 0,
            },
            FighterState.Create(1, 1000, 30, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion);

    /// <summary>记录 Prepare / Commit 的两阶段输入源，可让指定帧 Commit 抛出。</summary>
    private sealed class RecordingInput : IInputSource
    {
        private readonly Func<int, FrameInput> _provider;
        private readonly int _failingCommitFrame;

        public RecordingInput(Func<int, FrameInput> provider, int failingCommitFrame = -1)
        {
            _provider = provider;
            _failingCommitFrame = failingCommitFrame;
        }

        public List<int> Prepared { get; } = new();
        public List<int> Committed { get; } = new();

        public FrameInput Prepare(int frame)
        {
            Prepared.Add(frame);
            return _provider(frame);
        }

        public void Commit(FrameInput input)
        {
            if (input.Frame == _failingCommitFrame)
                throw new InvalidOperationException($"输入提交失败（帧 {input.Frame}）。");
            Committed.Add(input.Frame);
        }
    }

    private static MatchSession NewMatch(
        MatchRules? rules = null,
        Action<MatchEvent>? events = null,
        Func<MatchRules, BattleState>? initial = null,
        Func<MatchRules, FightRoster>? roster = null,
        int maxStepsPerAdvance = 5) =>
        new(rules ?? MatchRules.CreateDefault(),
            (_, _) => (roster ?? (_ => QuickRoster()))(rules ?? MatchRules.CreateDefault()),
            initial ?? (_ => Duel()),
            events,
            maxStepsPerAdvance);

    private static FrameInput PressAt0(int frame) =>
        frame == 0 ? F(frame, InputBits.LightPunch) : FrameInput.Empty(frame);

    private static FrameInput PressBothAt0(int frame) =>
        frame == 0 ? F(frame, InputBits.LightPunch, InputBits.LightPunch) : FrameInput.Empty(frame);

    private static double Steps(int count) => count * FixedStepScheduler.FixedStepSeconds;

    // ═════════════════════════════════════════════════════════════
    // 1. P1-A：批量 Advance 的回合终局边界
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void BatchAdvance_StopsAtTheKoFrame_AndDoesNotSubmitFrozenFrames()
    {
        var match = NewMatch();
        match.StartMatch();
        var input = new RecordingInput(PressAt0);

        int committed = match.Advance(Steps(5), input);

        // 第 0 帧发动、第 1 帧命中 KO：只提交 2 帧，第 3–5 帧既不准备也不提交。
        Assert.Equal(2, committed);
        Assert.Equal(2, match.CurrentBattleState!.Frame);
        Assert.Equal(2L, match.CurrentRound!.Scheduler.TotalSteps);
        Assert.True(match.IsFrameSyncIntact);
        Assert.Equal(new[] { 0, 1 }, input.Prepared);
        Assert.Equal(new[] { 0, 1 }, input.Committed);

        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(1, match.State.P1Wins);

        // 终局帧事件保留，不被后续空事件覆盖。
        Assert.Contains(match.LastEvents, e => e.Type == BattleEventType.Knockout);
        Assert.Contains(match.LastEvents, e => e.Type == BattleEventType.RoundEnded);

        // 未使用的整帧预算记为丢弃，不计成已模拟帧。
        Assert.Equal(3L, match.CurrentRound.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void BatchAdvance_FirstFrameKo_SubmitsSingleFrame()
    {
        var match = NewMatch(
            roster: _ => QuickRoster(activeFrames: 2),
            initial: _ => ActiveP1(activeFrames: 2));
        match.StartMatch();
        var input = new RecordingInput(f => FrameInput.Empty(f));

        int committed = match.Advance(Steps(5), input);

        Assert.Equal(1, committed);
        Assert.Equal(1, match.CurrentBattleState!.Frame);
        Assert.Equal(1L, match.CurrentRound!.Scheduler.TotalSteps);
        Assert.Equal(new[] { 0 }, input.Prepared);
        Assert.Equal(new[] { 0 }, input.Committed);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        Assert.Equal(4L, match.CurrentRound.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void BatchAdvance_KoOnLastFrame_ConsumesWholeBudget()
    {
        // Startup=4：第 0 帧发动，第 4 帧生效命中 —— 恰好是 5 帧预算的最后一帧。
        var match = NewMatch(roster: _ => QuickRoster(startupFrames: 4));
        match.StartMatch();
        var input = new RecordingInput(PressAt0);

        int committed = match.Advance(Steps(5), input);

        Assert.Equal(5, committed);
        Assert.Equal(5, match.CurrentBattleState!.Frame);
        Assert.Equal(5L, match.CurrentRound!.Scheduler.TotalSteps);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, input.Committed);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        Assert.Equal(0L, match.CurrentRound.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void BatchAdvance_DoubleKoStopsBatch()
    {
        var match = NewMatch();
        match.StartMatch();
        var input = new RecordingInput(PressBothAt0);

        int committed = match.Advance(Steps(5), input);

        Assert.Equal(2, committed);
        Assert.Equal(2L, match.CurrentRound!.Scheduler.TotalSteps);
        Assert.Equal(new[] { 0, 1 }, input.Committed);
        Assert.Equal(RoundOutcome.DoubleKO, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.DoubleKO, match.State.CurrentRoundReason);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
    }

    [Fact]
    public void BatchAdvance_TimeOverStopsBatch()
    {
        var rules = new MatchRules(roundsToWin: 2, maxRounds: 5, roundTimeSeconds: 1);
        var match = NewMatch(rules, initial: _ => Duel(timerFrames: 3));
        match.StartMatch();
        var input = new RecordingInput(f => FrameInput.Empty(f));

        int committed = match.Advance(Steps(5), input);

        Assert.Equal(3, committed); // 计时 3 → 0，第 3 帧（序号 2）归零即终局
        Assert.Equal(3, match.CurrentBattleState!.Frame);
        Assert.Equal(0, match.State.RemainingTimeFrames);
        Assert.Equal(RoundOutcome.Draw, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.TimeOverDraw, match.State.CurrentRoundReason);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        Assert.Equal(2L, match.CurrentRound!.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void BatchAdvance_NoKo_CommitsWholeBudget()
    {
        var match = NewMatch();
        match.StartMatch();
        var input = new RecordingInput(f => FrameInput.Empty(f));

        int committed = match.Advance(Steps(5), input);

        Assert.Equal(5, committed);
        Assert.Equal(5L, match.CurrentRound!.Scheduler.TotalSteps);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, input.Prepared);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, input.Committed);
        Assert.Equal(MatchPhase.RoundInProgress, match.State.Phase);
        Assert.Equal(0L, match.CurrentRound.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void BatchAdvance_PausedScheduler_DoesNotAdvance()
    {
        var match = NewMatch();
        match.StartMatch();
        match.CurrentRound!.Scheduler.Pause();
        var input = new RecordingInput(PressAt0);

        Assert.Equal(0, match.Advance(Steps(5), input));
        Assert.Empty(input.Prepared);
        Assert.Empty(input.Committed);
        Assert.Equal(0, match.CurrentBattleState!.Frame);
        Assert.Equal(MatchPhase.RoundInProgress, match.State.Phase);
    }

    [Fact]
    public void BatchAdvance_CommitFailure_KeepsOriginalExceptionAndAccounting()
    {
        var match = NewMatch();
        match.StartMatch();
        var input = new RecordingInput(f => FrameInput.Empty(f), failingCommitFrame: 3);

        var ex = Assert.Throws<InvalidOperationException>(() => match.Advance(Steps(5), input));

        Assert.Contains("输入提交失败", ex.Message);
        // 已提交 3 帧（0/1/2），第 3 帧 Commit 失败 → 候选状态不落地。
        Assert.Equal(3, match.CurrentBattleState!.Frame);
        Assert.Equal(3L, match.CurrentRound!.Scheduler.TotalSteps);
        Assert.True(match.IsFrameSyncIntact);
        Assert.Equal(2L, match.CurrentRound.Scheduler.DroppedStepCount);
        Assert.Equal(MatchPhase.RoundInProgress, match.State.Phase);
    }

    [Fact]
    public void BatchAdvance_NextRoundDoesNotInheritBudget()
    {
        var match = NewMatch();
        match.StartMatch();
        match.Advance(Steps(5), new RecordingInput(PressAt0));

        match.StartNextRound();

        Assert.Equal(0, match.CurrentBattleState!.Frame);
        Assert.Equal(0L, match.CurrentRound!.Scheduler.TotalSteps);
        Assert.Equal(0L, match.CurrentRound.Scheduler.DroppedStepCount);
        Assert.Equal(0.0, match.CurrentRound.Scheduler.AccumulatedSeconds, 12);
        Assert.Empty(match.LastEvents);
        Assert.Equal(1, match.State.P1Wins); // 胜场保留

        // 新回合输入帧号从 0 重新开始。
        var fresh = new RecordingInput(f => FrameInput.Empty(f));
        Assert.Equal(1, match.Advance(Steps(1), fresh));
        Assert.Equal(new[] { 0 }, fresh.Prepared);
        Assert.Equal(new[] { 0 }, fresh.Committed);
    }

    [Fact]
    public void BatchAdvance_AgreesWithStepExactAtTerminalFrame()
    {
        static (int Frame, MatchPhase Phase, int P1Wins, RoundOutcome Outcome) Run(bool useAdvance)
        {
            var match = NewMatch();
            match.StartMatch();
            if (useAdvance)
                match.Advance(Steps(5), new RecordingInput(PressAt0));
            else
                match.StepExact(5, PressAt0);

            return (match.CurrentBattleState!.Frame, match.State.Phase,
                match.State.P1Wins, match.State.CurrentRoundOutcome);
        }

        Assert.Equal(Run(useAdvance: false), Run(useAdvance: true));
    }

    [Fact]
    public void SingleRoundBattleSession_StillAdvancesFrozenFrames()
    {
        // D89 未改动：直接驱动单回合 BattleSession 时，终局帧之后仍可推进帧号（战斗冻结）。
        var session = new BattleSession(
            initialState: Duel(),
            scheduler: new FixedStepScheduler(5),
            simulation: new BattleSimulation(QuickRoster()));

        int committed = session.Advance(Steps(5), inputProvider: PressAt0);

        Assert.Equal(5, committed);
        Assert.Equal(5, session.CurrentState.Frame);
        Assert.Equal(5L, session.Scheduler.TotalSteps);
        Assert.True(session.CurrentState.Round.IsFinished);
        Assert.Equal(0, session.CurrentState.Fighter(1).Health);
    }

    // ═════════════════════════════════════════════════════════════
    // 2. P1-B：比赛事件发布必须晚于权威 MatchState 更新
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void RoundEndedObserver_SeesScoredAndAdvancedMatchState()
    {
        MatchSession? match = null;
        var observed = new List<MatchState>();
        match = NewMatch(events: e =>
        {
            if (e.Type == BattleEventType.RoundEnded)
                observed.Add(match!.State);
        }, initial: _ => Duel(timerFrames: MatchRules.CreateDefault().RoundTimerFrames));

        match.StartMatch();
        match.StepExact(2, PressAt0);

        MatchState seen = Assert.Single(observed);
        BattleState committedState = match.CurrentBattleState!;

        Assert.Equal(MatchPhase.BetweenRounds, seen.Phase);
        Assert.Equal(1, seen.P1Wins);
        Assert.Equal(0, seen.P2Wins);
        Assert.Equal(1, seen.RoundsPlayed);
        Assert.Equal(1, seen.RoundNumber);
        Assert.Equal(RoundOutcome.P1Win, seen.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.Knockout, seen.CurrentRoundReason);
        Assert.Equal(5938, seen.RemainingTimeFrames); // 5940 − 2 个已提交帧
        Assert.Equal(committedState.RoundTimerRemainingFrames!.Value, seen.RemainingTimeFrames);
        Assert.True(committedState.Round.IsFinished);
    }

    [Fact]
    public void MatchFinishedObserver_SeesFinalMatchOutcome()
    {
        MatchSession? match = null;
        var observed = new List<MatchState>();
        match = NewMatch(events: e =>
        {
            if (e.Type == BattleEventType.RoundEnded)
                observed.Add(match!.State);
        });

        match.StartMatch();
        match.StepExact(2, PressAt0);
        match.StartNextRound();
        match.StepExact(2, PressAt0);

        Assert.Equal(2, observed.Count);
        MatchState final = observed[1];
        Assert.Equal(MatchPhase.MatchFinished, final.Phase);
        Assert.Equal(MatchOutcome.P1Win, final.Outcome);
        Assert.Equal(2, final.P1Wins);
        Assert.Equal(2, final.RoundsPlayed);
    }

    [Fact]
    public void DoubleKoObserver_SeesCorrectScoring()
    {
        MatchSession? match = null;
        MatchState? seen = null;
        match = NewMatch(events: e =>
        {
            if (e.Type == BattleEventType.RoundEnded)
                seen = match!.State;
        });

        match.StartMatch();
        match.StepExact(2, PressBothAt0);

        Assert.NotNull(seen);
        Assert.Equal(MatchPhase.BetweenRounds, seen!.Phase);
        Assert.Equal(0, seen.P1Wins);
        Assert.Equal(0, seen.P2Wins);
        Assert.Equal(1, seen.RoundsPlayed);
        Assert.Equal(RoundOutcome.DoubleKO, seen.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.DoubleKO, seen.CurrentRoundReason);
    }

    [Fact]
    public void TimeOverObserver_SeesCorrectReason()
    {
        MatchSession? match = null;
        MatchState? seen = null;
        var rules = new MatchRules(roundsToWin: 2, maxRounds: 5, roundTimeSeconds: 1);
        match = NewMatch(rules, events: e =>
        {
            if (e.Type == BattleEventType.RoundEnded)
                seen = match!.State;
        }, initial: _ => Duel(timerFrames: 3));

        match.StartMatch();
        match.StepExact(3, f => FrameInput.Empty(f));

        Assert.NotNull(seen);
        Assert.Equal(RoundEndReason.TimeOverDraw, seen!.CurrentRoundReason);
        Assert.Equal(RoundOutcome.Draw, seen.CurrentRoundOutcome);
        Assert.Equal(0, seen.RemainingTimeFrames);
        Assert.Equal(1, seen.RoundsPlayed);
    }

    [Fact]
    public void TerminalFrameEvents_AllSeeTheSameCommittedMatchState()
    {
        MatchSession? match = null;
        var seen = new List<(int Frame, BattleEventType Type, MatchState State)>();
        match = NewMatch(events: e => seen.Add((e.RoundEvent.Frame, e.Type, match!.State)));

        match.StartMatch();
        match.StepExact(2, PressAt0);

        Assert.NotEmpty(seen);
        foreach (var frameGroup in seen.GroupBy(item => item.Frame))
            Assert.All(frameGroup, item => Assert.Equal(frameGroup.First().State, item.State));

        var terminal = seen.Where(item => item.Type == BattleEventType.RoundEnded).ToList();
        Assert.NotEmpty(terminal);
        Assert.All(terminal, item => Assert.Equal(MatchPhase.BetweenRounds, item.State.Phase));
        Assert.All(terminal, item => Assert.Equal(1, item.State.P1Wins));
    }

    [Fact]
    public void ObserverException_KeepsScoring_Propagates_AndFaultsSession()
    {
        MatchSession? match = null;
        int calls = 0;
        match = NewMatch(events: e =>
        {
            if (e.Type != BattleEventType.RoundEnded)
                return;
            calls++;
            throw new InvalidOperationException("observer boom");
        });

        match.StartMatch();
        var ex = Assert.Throws<InvalidOperationException>(() => match.StepExact(2, PressAt0));

        Assert.Equal("observer boom", ex.Message);
        Assert.Equal(1, calls);
        // 计分在发布之前完成，观察者异常不回滚。
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        // 会话进入既有故障封闭状态。
        Assert.True(match.CurrentRound!.IsFaulted);
        Assert.Equal(1, match.CurrentRound.FaultedFrame);

        // 过渡阶段比赛层直接短路（不推进、不计分）；
        // 直接驱动已故障的回合会话则命中既有故障封闭。
        Assert.Equal(0, match.Advance(Steps(1), new RecordingInput(f => FrameInput.Empty(f))));
        Assert.Throws<InvalidOperationException>(() =>
            match.CurrentRound.Advance(Steps(1), inputProvider: f => FrameInput.Empty(f)));
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(1, match.State.RoundsPlayed);
    }

    [Fact]
    public void ObserverException_MidBatch_DoesNotAdvanceRemainingBudget()
    {
        MatchSession? match = null;
        match = NewMatch(events: e =>
        {
            if (e.Type == BattleEventType.RoundEnded)
                throw new InvalidOperationException("observer boom");
        });

        match.StartMatch();
        Assert.Throws<InvalidOperationException>(() => match.Advance(Steps(5), new RecordingInput(PressAt0)));

        Assert.Equal(2, match.CurrentBattleState!.Frame); // 第 3–5 帧未提交
        Assert.Equal(2L, match.CurrentRound!.Scheduler.TotalSteps);
        Assert.Equal(3L, match.CurrentRound.Scheduler.DroppedStepCount);
        Assert.Equal(1, match.State.P1Wins);
    }

    [Theory]
    [InlineData("StartNextRound")]
    [InlineData("RestartMatch")]
    [InlineData("ReturnToSelection")]
    public void ObserverReentrancy_IsExplicitlyRejected(string operation)
    {
        MatchSession? match = null;
        Exception? inner = null;
        match = NewMatch(events: e =>
        {
            if (e.Type != BattleEventType.RoundEnded)
                return;
            try
            {
                switch (operation)
                {
                    case "StartNextRound": match!.StartNextRound(); break;
                    case "RestartMatch": match!.RestartMatch(); break;
                    default: match!.ReturnToSelection(); break;
                }
            }
            catch (Exception ex)
            {
                inner = ex;
                throw;
            }
        });

        match.StartMatch();
        Assert.Throws<InvalidOperationException>(() => match.StepExact(2, PressAt0));

        Assert.IsType<InvalidOperationException>(inner);
        Assert.Contains("观察者", inner!.Message);
        // 重入被拒绝：比赛仍停在刚结束的回合，胜场没有被清零也没有进入下一回合。
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(1, match.State.RoundNumber);
    }

    [Fact]
    public void MatchEvents_CarryRoundNumberOfTheEndingRound()
    {
        var collected = new List<MatchEvent>();
        var match = NewMatch(events: collected.Add);
        match.StartMatch();
        match.StepExact(2, PressAt0);
        match.StartNextRound();
        collected.Clear();
        match.StepExact(2, PressAt0);

        Assert.NotEmpty(collected);
        Assert.All(collected, e => Assert.Equal(2, e.RoundNumber));
        Assert.Contains(collected, e => e.Type == BattleEventType.RoundEnded);
    }

    // ═════════════════════════════════════════════════════════════
    // 3. P2-A：RoundOutcome / RoundEndReason 组合合法性
    // ═════════════════════════════════════════════════════════════

    private static MatchState Live() =>
        MatchState.NotStarted(MatchRules.CreateDefault(), "Kai", "Rei").StartRound();

    [Theory]
    [InlineData(RoundOutcome.P1Win, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.P2Win, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.P1Win, RoundEndReason.TimeOver)]
    [InlineData(RoundOutcome.P2Win, RoundEndReason.TimeOver)]
    [InlineData(RoundOutcome.DoubleKO, RoundEndReason.DoubleKO)]
    [InlineData(RoundOutcome.Draw, RoundEndReason.TimeOverDraw)]
    public void LegalOutcomeReasonPairs_AreAccepted(RoundOutcome outcome, RoundEndReason reason)
    {
        MatchState after = Live().RecordRoundResult(outcome, reason);
        after.Validate();
        Assert.Equal(outcome, after.CurrentRoundOutcome);
        Assert.Equal(reason, after.CurrentRoundReason);
    }

    [Theory]
    [InlineData(RoundOutcome.DoubleKO, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.Draw, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.P1Win, RoundEndReason.DoubleKO)]
    [InlineData(RoundOutcome.P2Win, RoundEndReason.DoubleKO)]
    [InlineData(RoundOutcome.P1Win, RoundEndReason.TimeOverDraw)]
    [InlineData(RoundOutcome.P2Win, RoundEndReason.TimeOverDraw)]
    [InlineData(RoundOutcome.DoubleKO, RoundEndReason.TimeOver)]
    [InlineData(RoundOutcome.Draw, RoundEndReason.TimeOver)]
    [InlineData(RoundOutcome.P1Win, RoundEndReason.None)]
    [InlineData(RoundOutcome.None, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.None, RoundEndReason.None)]
    [InlineData((RoundOutcome)99, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.P1Win, (RoundEndReason)99)]
    public void IllegalOutcomeReasonPairs_AreRejected(RoundOutcome outcome, RoundEndReason reason)
    {
        Assert.ThrowsAny<Exception>(() => Live().RecordRoundResult(outcome, reason));
    }

    [Fact]
    public void AllEnumPairs_AgreeAcrossRoundStateAndMatchState()
    {
        foreach (RoundOutcome outcome in Enum.GetValues<RoundOutcome>())
        {
            foreach (RoundEndReason reason in Enum.GetValues<RoundEndReason>())
            {
                bool sharedRule = RoundState.IsValidOutcomeReasonPair(outcome, reason);

                bool roundStateAccepts = true;
                try
                {
                    RoundState.ForOutcome(outcome, reason);
                }
                catch (Exception)
                {
                    roundStateAccepts = false;
                }

                bool matchStateAccepts = true;
                try
                {
                    Live().RecordRoundResult(outcome, reason);
                }
                catch (Exception)
                {
                    matchStateAccepts = false;
                }

                Assert.Equal(sharedRule, roundStateAccepts);
                Assert.Equal(sharedRule, matchStateAccepts);
            }
        }
    }

    /// <summary>
    /// <see cref="MatchState.Validate"/> 必须自行拒绝不自洽的结果/原因组合。
    /// 公开路径已被 <c>RecordRoundResult</c> 拦住，因此用反射构造一个「本不应存在」的状态，
    /// 证明校验器本身也没有漏掉这一条（而不是只靠入口处的一次检查）。
    /// </summary>
    [Theory]
    [InlineData(RoundOutcome.DoubleKO, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.Draw, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.P1Win, RoundEndReason.DoubleKO)]
    [InlineData(RoundOutcome.P2Win, RoundEndReason.TimeOverDraw)]
    public void MatchStateValidate_RejectsInconsistentPair(
        RoundOutcome outcome, RoundEndReason reason)
    {
        MatchState inconsistent = WithPair(outcome, reason);
        Assert.Throws<InvalidOperationException>(() => inconsistent.Validate());
    }

    [Theory]
    [InlineData(RoundOutcome.P1Win, RoundEndReason.Knockout)]
    [InlineData(RoundOutcome.P2Win, RoundEndReason.TimeOver)]
    [InlineData(RoundOutcome.DoubleKO, RoundEndReason.DoubleKO)]
    [InlineData(RoundOutcome.Draw, RoundEndReason.TimeOverDraw)]
    public void MatchStateValidate_AcceptsConsistentPair(
        RoundOutcome outcome, RoundEndReason reason)
    {
        WithPair(outcome, reason).Validate();
    }

    [Fact]
    public void NewRound_ClearsPreviousResultAndReason()
    {
        MatchState state = Live().RecordRoundResult(RoundOutcome.P1Win, RoundEndReason.TimeOver);
        Assert.Equal(RoundEndReason.TimeOver, state.CurrentRoundReason);

        MatchState next = state.StartRound();
        next.Validate();
        Assert.Equal(RoundOutcome.None, next.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.None, next.CurrentRoundReason);
    }

    [Fact]
    public void MatchFinished_KeepsLegalResultAndReason()
    {
        var rules = new MatchRules(roundsToWin: 1, maxRounds: 1, roundTimeSeconds: 99);
        MatchState state = MatchState.NotStarted(rules, "Kai", "Rei").StartRound()
            .RecordRoundResult(RoundOutcome.P2Win, RoundEndReason.TimeOver);

        state.Validate();
        Assert.Equal(MatchPhase.MatchFinished, state.Phase);
        Assert.Equal(MatchOutcome.P2Win, state.Outcome);
        Assert.Equal(RoundOutcome.P2Win, state.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.TimeOver, state.CurrentRoundReason);
    }

    /// <summary>用私有构造器造一个过渡阶段的 MatchState（唯一能触达 Validate 非法组合的方式）。</summary>
    private static MatchState WithPair(RoundOutcome outcome, RoundEndReason reason)
    {
        ConstructorInfo ctor = typeof(MatchState)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 12);

        return (MatchState)ctor.Invoke(new object?[]
        {
            MatchRules.CreateDefault(),
            MatchPhase.BetweenRounds,
            1, 1,
            outcome == RoundOutcome.P1Win ? 1 : 0,
            outcome == RoundOutcome.P2Win ? 1 : 0,
            outcome, reason,
            MatchOutcome.None,
            0,
            "Kai", "Rei",
        });
    }
}
