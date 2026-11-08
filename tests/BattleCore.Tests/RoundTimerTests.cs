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
/// 阶段 09B（D107–D109）：<b>权威逻辑帧计时、Time Over 与超时裁决</b>。
///
/// <para>计时器是 <see cref="BattleState.RoundTimerRemainingFrames"/>（int?，null = 旧单回合
/// 兼容路径）。只有成功推进一个进行中战斗逻辑帧才 −1；KO 优先于 Time Over；
/// 超时按剩余体力比例的<b>安全整数交叉相乘</b>裁决。</para>
/// </summary>
public class RoundTimerTests
{
    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    /// <summary>带权威时钟、可指定双方体力/上限的回合初态。</summary>
    private static BattleState Timed(
        int timer,
        int p1Health = 1000, int p1Max = 1000,
        int p2Health = 1000, int p2Max = 1000) => new(
        0,
        new[]
        {
            FighterState.Create(0, p1Max, -30, Facing.Right) with { Health = p1Health },
            FighterState.Create(1, p2Max, 30, Facing.Left) with { Health = p2Health },
        },
        BattleState.CreateDefault().StateVersion,
        round: null, projectiles: null, nextProjectileId: 1,
        roundTimerRemainingFrames: timer);

    private static BattleSimulation QuietSim() => new(); // 空配置：无攻击、无移动

    private static BattleState Step(BattleSimulation sim, BattleState state, int frames, InputBits? press = null)
    {
        for (int k = 0; k < frames; k++)
        {
            InputBits p1 = press.HasValue && k == 0 ? press.Value : InputBits.None;
            state = sim.Step(state, F(state.Frame, p1)).State;
        }
        return state;
    }

    private static AttackDefinition LethalHit() => new(
        offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
        halfWidth: FixedPoint.FromInt(30), halfHeight: FixedPoint.FromInt(30),
        damage: 1000, hitstunFrames: 5, blockstunFrames: 5);

    /// <summary>一击致死招式（1/1/1），用于 KO 与 Time Over 同帧优先级测试。</summary>
    private static BattleSimulation LethalSim()
    {
        FighterDefinition Build(string id) => new(id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[] { new MoveDefinition("Jab", "5A", 1, 1, 1, attacks: new[] { LethalHit() }) });

        return new BattleSimulation(new FightRoster(Build("Kai"), Build("Rei")));
    }

    private static BattleState DuelWithTimer(int timer) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion,
        round: null, projectiles: null, nextProjectileId: 1,
        roundTimerRemainingFrames: timer);

    // ─────────────────────────────────────────────────────────────
    // 1. 99 秒 / 5940 帧与逐帧语义
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultRule_IsExactly5940LogicFrames()
    {
        Assert.Equal(5940, MatchRules.CreateDefault().RoundTimerFrames);
        Assert.Equal(99 * BattleCoreInfo.LogicFrameRate, MatchRules.CreateDefault().RoundTimerFrames);
    }

    [Fact]
    public void Timer_DecrementsExactlyOnePerCommittedFrame()
    {
        var sim = QuietSim();
        BattleState state = Timed(100);

        for (int k = 1; k <= 10; k++)
        {
            state = sim.Step(state, FrameInput.Empty(state.Frame)).State;
            Assert.Equal(100 - k, state.RoundTimerRemainingFrames!.Value);
            Assert.False(state.Round.IsFinished);
        }
    }

    [Fact]
    public void Full5940Frames_ReachesZeroAndResolvesTimeOver()
    {
        var sim = QuietSim();
        BattleState state = Timed(5940);
        int roundEnded = 0;
        int timeOver = 0;

        for (int k = 0; k < 5940; k++)
        {
            BattleStepResult r = sim.Step(state, FrameInput.Empty(state.Frame));
            state = r.State;
            roundEnded += r.Events.Count(e => e.Type == BattleEventType.RoundEnded);
            timeOver += r.Events.Count(e => e.Type == BattleEventType.TimeOver);
            Assert.Equal(5940 - (k + 1), state.RoundTimerRemainingFrames!.Value);
        }

        Assert.Equal(0, state.RoundTimerRemainingFrames!.Value);
        Assert.True(state.Round.IsFinished);
        Assert.True(state.Round.IsTimeOverDraw); // 双方满血 → 比例相同
        Assert.Equal(RoundEndReason.TimeOverDraw, state.Round.Reason);
        Assert.Equal(1, roundEnded);
        Assert.Equal(1, timeOver);
    }

    [Fact]
    public void Timer_OneFrameBoundary_ResolvesOnThatFrameWithoutDamage()
    {
        var sim = QuietSim();
        BattleState state = Timed(1, p1Health: 700, p2Health: 400);

        BattleStepResult r = sim.Step(state, FrameInput.Empty(0));

        Assert.Equal(0, r.State.RoundTimerRemainingFrames!.Value);
        Assert.True(r.State.Round.IsFinished);
        Assert.Equal(RoundOutcome.P1Win, r.State.Round.Outcome); // 70% > 40%
        Assert.Equal(RoundEndReason.TimeOver, r.State.Round.Reason);
        Assert.Equal(700, r.State.Fighter(0).Health); // 超时裁决不产生虚假伤害
        Assert.Equal(400, r.State.Fighter(1).Health);
        Assert.Single(r.Events, e => e.Type == BattleEventType.TimeOver);
        Assert.Single(r.Events, e => e.Type == BattleEventType.RoundEnded);
        Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.Hit);
    }

    [Fact]
    public void Timer_NullClock_KeepsLegacySingleRoundBehavior()
    {
        var sim = QuietSim();
        BattleState state = new(0, Timed(10).Fighters, BattleState.CreateDefault().StateVersion);

        for (int k = 0; k < 50; k++)
        {
            BattleStepResult r = sim.Step(state, FrameInput.Empty(state.Frame));
            state = r.State;
            Assert.Null(state.RoundTimerRemainingFrames);
            Assert.False(state.Round.IsFinished);
            Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.TimeOver);
        }
    }

    [Fact]
    public void BattleState_RejectsIllegalTimerValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Timed(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Timed(BattleCoreInfo.MaxRoundTimerFrames + 1));
        Assert.Equal(0, Timed(0).RoundTimerRemainingFrames!.Value);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. Hitstop / 暂停 / 未提交 / 丢弃预算
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void HitstopFrames_StillCostOneTimerFrameEach()
    {
        var sim = QuietSim();
        BattleState state = new(0, new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right) with { HitstopRemainingFrames = 3 },
            FighterState.Create(1, 1000, 30, Facing.Left),
        }, BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1, roundTimerRemainingFrames: 10);

        for (int k = 1; k <= 3; k++)
        {
            state = sim.Step(state, FrameInput.Empty(state.Frame)).State;
            Assert.Equal(10 - k, state.RoundTimerRemainingFrames!.Value);
            Assert.Equal(3 - k, state.Fighter(0).HitstopRemainingFrames);
        }
    }

    [Fact]
    public void UncommittedStep_DoesNotCostTime()
    {
        var sim = QuietSim();
        BattleState state = Timed(10);

        Assert.Throws<InvalidOperationException>(() =>
            sim.Step(state, FrameInput.Empty(state.Frame + 5))); // 帧号不匹配 → Step 失败
        Assert.Equal(10, state.RoundTimerRemainingFrames!.Value);
    }

    [Fact]
    public void PausedSession_DoesNotCostTime()
    {
        var match = NewTimedMatch();
        match.StartMatch();
        int before = match.CurrentBattleState!.RoundTimerRemainingFrames!.Value;

        match.CurrentRound!.Scheduler.Pause();
        for (int i = 0; i < 20; i++)
            match.Advance(FixedStepScheduler.FixedStepSeconds);

        Assert.Equal(before, match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);
        Assert.Equal(0, match.CurrentRound.Scheduler.TotalSteps);
    }

    [Fact]
    public void DroppedTimeBudget_DoesNotCostExtraTimerFrames()
    {
        var match = NewTimedMatch();
        match.StartMatch();

        // 一次超大 delta：调度器只提交 MaxStepsPerAdvance 帧，其余记为丢弃预算。
        match.Advance(5.0);
        long submitted = match.CurrentRound!.Scheduler.TotalSteps;
        long dropped = match.CurrentRound.Scheduler.DroppedStepCount;

        Assert.True(dropped > 0);
        Assert.Equal(5L, submitted);
        Assert.Equal(60 - (int)submitted,
            match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);
        Assert.Equal((int)submitted, 60 - match.State.RemainingTimeFrames);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 体力比例裁决
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TimeOver_P1WinsByHealthRatio()
    {
        // P1 500/1000 = 50%，P2 600/1500 = 40% → P1 胜（绝对 HP 反而更低）。
        BattleState state = QuietSim().Step(
            Timed(1, p1Health: 500, p1Max: 1000, p2Health: 600, p2Max: 1500),
            FrameInput.Empty(0)).State;

        Assert.Equal(RoundOutcome.P1Win, state.Round.Outcome);
        Assert.Equal(RoundEndReason.TimeOver, state.Round.Reason);
        Assert.Equal(0, state.Round.WinnerFighterId);
    }

    [Fact]
    public void TimeOver_P2WinsByHealthRatio()
    {
        BattleState state = QuietSim().Step(
            Timed(1, p1Health: 400, p1Max: 1000, p2Health: 800, p2Max: 1000),
            FrameInput.Empty(0)).State;

        Assert.Equal(RoundOutcome.P2Win, state.Round.Outcome);
        Assert.Equal(1, state.Round.WinnerFighterId);
    }

    [Fact]
    public void TimeOver_EqualRatio_IsDraw()
    {
        BattleState state = QuietSim().Step(
            Timed(1, p1Health: 500, p1Max: 1000, p2Health: 500, p2Max: 1000),
            FrameInput.Empty(0)).State;

        Assert.Equal(RoundOutcome.Draw, state.Round.Outcome);
        Assert.True(state.Round.IsTimeOverDraw);
        Assert.Null(state.Round.WinnerFighterId);
        Assert.Equal(RoundEndReason.TimeOverDraw, state.Round.Reason);
    }

    [Fact]
    public void TimeOver_DifferentAbsoluteHealth_SameRatio_IsDraw()
    {
        // 300/1000 = 30%，600/2000 = 30%
        BattleState state = QuietSim().Step(
            Timed(1, p1Health: 300, p1Max: 1000, p2Health: 600, p2Max: 2000),
            FrameInput.Empty(0)).State;

        Assert.Equal(RoundOutcome.Draw, state.Round.Outcome);
    }

    [Fact]
    public void TimeOver_SameAbsoluteHealth_DifferentRatio_P1Wins()
    {
        // 500/1000 = 50%，500/2000 = 25%
        BattleState state = QuietSim().Step(
            Timed(1, p1Health: 500, p1Max: 1000, p2Health: 500, p2Max: 2000),
            FrameInput.Empty(0)).State;

        Assert.Equal(RoundOutcome.P1Win, state.Round.Outcome);
    }

    [Fact]
    public void TimeOverResolver_RejectsIllegalInput()
    {
        Assert.Equal(RoundOutcome.Draw, TimeOverResolver.Resolve(0, 1000, 0, 1000));
        Assert.Equal(RoundOutcome.P1Win, TimeOverResolver.Resolve(1, 1000, 0, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeOverResolver.Resolve(-1, 1000, 500, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeOverResolver.Resolve(500, 0, 500, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeOverResolver.Resolve(1001, 1000, 500, 1000));
    }

    // ─────────────────────────────────────────────────────────────
    // 4. KO / DoubleKO 与 Time Over 同帧优先级
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void KO_OnTimerZeroFrame_TakesPriority()
    {
        var sim = LethalSim();
        BattleState state = DuelWithTimer(2);

        BattleStepResult first = sim.Step(state, F(0, InputBits.LightPunch));
        Assert.Equal(1, first.State.RoundTimerRemainingFrames!.Value);
        Assert.False(first.State.Round.IsFinished);

        BattleStepResult ko = sim.Step(first.State, FrameInput.Empty(1));

        Assert.Equal(0, ko.State.RoundTimerRemainingFrames!.Value);
        Assert.Equal(RoundOutcome.P1Win, ko.State.Round.Outcome);
        Assert.Equal(RoundEndReason.Knockout, ko.State.Round.Reason);
        Assert.DoesNotContain(ko.Events, e => e.Type == BattleEventType.TimeOver);
        Assert.Single(ko.Events, e => e.Type == BattleEventType.Knockout);
        Assert.Single(ko.Events, e => e.Type == BattleEventType.RoundEnded);
    }

    [Fact]
    public void DoubleKO_OnTimerZeroFrame_TakesPriority()
    {
        var sim = LethalSim();
        BattleState state = DuelWithTimer(2);

        BattleStepResult first = sim.Step(state, F(0, InputBits.LightPunch, InputBits.LightPunch));
        BattleStepResult ko = sim.Step(first.State, FrameInput.Empty(1));

        Assert.Equal(0, ko.State.RoundTimerRemainingFrames!.Value);
        Assert.Equal(RoundOutcome.DoubleKO, ko.State.Round.Outcome);
        Assert.Equal(RoundEndReason.DoubleKO, ko.State.Round.Reason);
        Assert.DoesNotContain(ko.Events, e => e.Type == BattleEventType.TimeOver);
        Assert.Single(ko.Events, e => e.Type == BattleEventType.DoubleKnockout);
        Assert.Single(ko.Events, e => e.Type == BattleEventType.RoundEnded);
    }

    [Fact]
    public void AfterTimeOver_NoFurtherDamageOrEvents()
    {
        var sim = LethalSim();
        BattleState state = Timed(1); // 满血 → TimeOverDraw

        BattleStepResult timeUp = sim.Step(state, FrameInput.Empty(0));
        state = timeUp.State;

        for (int k = 1; k < 20; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.LightPunch, InputBits.LightPunch));
            state = r.State;
            Assert.Equal(1000, state.Fighter(0).Health);
            Assert.Equal(1000, state.Fighter(1).Health);
            Assert.Equal(0, state.RoundTimerRemainingFrames!.Value);
            Assert.Empty(r.Events);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 事件顺序、RoundState 原因与比赛层
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TimeOver_EventOrder_AndExactlyOneRoundEnded()
    {
        BattleStepResult r = QuietSim().Step(Timed(1), FrameInput.Empty(0));

        BattleEvent timeUp = Assert.Single(r.Events, e => e.Type == BattleEventType.TimeOver);
        BattleEvent ended = Assert.Single(r.Events, e => e.Type == BattleEventType.RoundEnded);
        Assert.True(timeUp.EventId < ended.EventId);
        Assert.Equal((int)RoundOutcome.Draw, timeUp.Amount);
        Assert.Equal((int)RoundOutcome.Draw, ended.Amount);
        Assert.Equal(-1, ended.SourceFighterId); // 平局无单一胜者
        Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.Knockout);
        Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.DoubleKnockout);
        Assert.Equal(2, r.Events.Count); // 只有 TimeOver + RoundEnded
    }

    [Fact]
    public void RoundState_DistinguishesKnockoutFromTimeOver()
    {
        RoundState ko = RoundState.ForOutcome(RoundOutcome.P1Win);
        RoundState time = RoundState.ForOutcome(RoundOutcome.P1Win, RoundEndReason.TimeOver);

        Assert.Equal(RoundEndReason.Knockout, ko.Reason);
        Assert.Equal(RoundEndReason.TimeOver, time.Reason);
        Assert.False(ko.IsTimeOver);
        Assert.True(time.IsTimeOver);
        Assert.Equal(RoundOutcome.P1Win, time.Outcome);

        Assert.Throws<InvalidOperationException>(() =>
            RoundState.ForOutcome(RoundOutcome.DoubleKO, RoundEndReason.TimeOver));
        Assert.Throws<InvalidOperationException>(() =>
            RoundState.ForOutcome(RoundOutcome.Draw, RoundEndReason.Knockout));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RoundState.ForOutcome((RoundOutcome)99));
    }

    private static MatchSession NewTimedMatch() => new(
        new MatchRules(roundsToWin: 2, maxRounds: 5, roundTimeSeconds: 1), // 60 帧，便于快速触发
        (_, _) => new FightRoster(
            new FighterDefinition("Kai", Array.Empty<CommandDefinition>(), Array.Empty<MoveDefinition>()),
            new FighterDefinition("Rei", Array.Empty<CommandDefinition>(), Array.Empty<MoveDefinition>())),
        eventsCommitted: null);

    [Fact]
    public void MatchSession_NewRound_ResetsTimerAndKeepsScore()
    {
        var match = NewTimedMatch();
        match.StartMatch();
        Assert.Equal(60, match.State.RemainingTimeFrames);
        Assert.Equal(60, match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);

        match.StepExact(60, f => FrameInput.Empty(f));

        Assert.Equal(0, match.State.RemainingTimeFrames);
        Assert.Equal(0, match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);
        Assert.Equal(RoundOutcome.Draw, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.TimeOverDraw, match.State.CurrentRoundReason);
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);

        match.StartNextRound();
        Assert.Equal(60, match.State.RemainingTimeFrames);
        Assert.Equal(60, match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);
        Assert.Equal(1, match.State.RoundsPlayed);
    }

    [Fact]
    public void MatchSession_TimeOverWin_ScoresCorrectly()
    {
        var rules = new MatchRules(roundsToWin: 2, maxRounds: 5, roundTimeSeconds: 1);
        var match = new MatchSession(rules,
            (_, _) => new FightRoster(
                new FighterDefinition("Kai", Array.Empty<CommandDefinition>(), Array.Empty<MoveDefinition>()),
                new FighterDefinition("Rei", Array.Empty<CommandDefinition>(), Array.Empty<MoveDefinition>())),
            _ => new BattleState(0, new[]
            {
                FighterState.Create(0, 1000, -30, Facing.Right) with { Health = 900 },
                FighterState.Create(1, 1000, 30, Facing.Left) with { Health = 400 },
            }, BattleState.CreateDefault().StateVersion,
                round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: rules.RoundTimerFrames));

        match.StartMatch();
        match.StepExact(60, f => FrameInput.Empty(f));

        Assert.Equal(RoundOutcome.P1Win, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.TimeOver, match.State.CurrentRoundReason);
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
    }

    [Fact]
    public void StepExactAndAdvance_Agree_AndMidSnapshotReplay_IsIdentical()
    {
        static (BattleState State, List<int> Events) Run(bool split, bool useAdvance)
        {
            var sim = QuietSim();
            BattleState state = Timed(20, p1Health: 700, p2Health: 400);
            var ids = new List<int>();

            for (int k = 0; k < 20; k++)
            {
                if (split && k == 10)
                    state = new BattleState(state.Frame, state.Fighters, state.StateVersion,
                        state.Round, state.Projectiles, state.NextProjectileId,
                        state.RoundTimerRemainingFrames);

                BattleStepResult r = useAdvance
                    ? SimulateOneFrame(sim, state)
                    : sim.Step(state, FrameInput.Empty(state.Frame));
                state = r.State;
                ids.AddRange(r.Events.Select(e => e.EventId));
            }
            return (state, ids);
        }

        (BattleState a, List<int> aIds) = Run(false, false);
        (BattleState b, List<int> bIds) = Run(true, false);
        (BattleState c, List<int> cIds) = Run(false, true);

        Assert.Equal(a.RoundTimerRemainingFrames, b.RoundTimerRemainingFrames);
        Assert.Equal(a.Round, b.Round);
        Assert.Equal(aIds, bIds);
        Assert.Equal(a.RoundTimerRemainingFrames, c.RoundTimerRemainingFrames);
        Assert.Equal(a.Round, c.Round);
        Assert.Equal(aIds, cIds);
    }

    /// <summary>通过 BattleSession.Advance 提交恰好一帧（验证 StepExact/Advance 一致）。</summary>
    private static BattleStepResult SimulateOneFrame(BattleSimulation sim, BattleState state)
    {
        var captured = new List<BattleEvent>();
        var session = new BattleSession(
            initialState: state,
            simulation: sim,
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1),
            eventsCommitted: (input, before, events) => captured.AddRange(events));
        session.Advance(FixedStepScheduler.FixedStepSeconds,
            inputProvider: f => FrameInput.Empty(f));
        return new BattleStepResult(state.Frame, session.CurrentState.Frame,
            FrameInput.Empty(state.Frame), session.CurrentState, events: captured);
    }
}
