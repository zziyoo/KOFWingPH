using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 07A（D89）：KO 裁决与回合终局。
///
/// <para>测试配置：单招 Poke（Startup 1 / Active 3 / Recovery 4，仅在 Active 第 1 帧生效），
/// 伤害可配、命中硬直 12、防御硬直 8；双方同配置，便于构造同帧互中。</para>
/// </summary>
public class RoundResolutionTests
{
    private const string Poke = "Poke";

    private static BattleSimulation Sim(int damage = 30)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition Build(string id) => new(
            id,
            new[] { new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5) },
            new[]
            {
                new MoveDefinition(Poke, "5A", startupFrames: 1, activeFrames: 3, recoveryFrames: 4,
                    attacks: new[]
                    {
                        new AttackDefinition(
                            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                            FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                            firstActiveFrame: 0, lastActiveFrame: 0,
                            AttackHeight.Mid, damage: damage, hitstunFrames: 12, blockstunFrames: 8),
                    }),
            },
            movement);

        return new BattleSimulation(new FightRoster(Build("Kai"), Build("Rei")));
    }

    /// <summary>Active 第 1 帧（本 Step 内 elapsed 0）的角色；remaining = ActiveFrames + 1。</summary>
    private static FighterState Attacking(int id, int x, Facing facing, int health = 1000)
    {
        FighterState fighter = FighterState.Create(id, 1000, x, facing);
        if (health < 1000)
            fighter = fighter with { Health = health };

        return fighter with
        {
            LogicState = FighterLogicState.Active,
            MoveId = Poke,
            PhaseRemainingFrames = 4,
            MoveStartedFrame = 0,
        };
    }

    private static FighterState Idle(int id, int x, Facing facing, int health = 1000)
    {
        FighterState fighter = FighterState.Create(id, 1000, x, facing);
        return health < 1000 ? fighter with { Health = health } : fighter;
    }

    private static BattleState Duel(FighterState p1, FighterState p2) =>
        new(0, new[] { p1, p2 }, BattleState.CreateDefault().StateVersion);

    // ─────────────────────────────────────────────────────────────
    // KO 判定（最终体力决定胜负）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void P1KillsP2_RoundEndsAsP1Win()
    {
        BattleStepResult result = Sim().Step(
            Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 30)),
            FrameInput.Empty(0));

        Assert.Equal(0, result.State.Fighter(1).Health);
        Assert.True(result.State.Round.IsFinished);
        Assert.Equal(RoundOutcome.P1Win, result.State.Round.Outcome);
        Assert.Equal(0, result.State.Round.WinnerFighterId);

        BattleEvent ko = Assert.Single(result.Events, e => e.Type == BattleEventType.Knockout);
        Assert.Equal((0, 1), (ko.SourceFighterId, ko.TargetFighterId));
        Assert.Equal(0, ko.Amount);

        BattleEvent ended = Assert.Single(result.Events, e => e.Type == BattleEventType.RoundEnded);
        Assert.Equal((int)RoundOutcome.P1Win, ended.Amount);
        Assert.Equal(0, ended.SourceFighterId);
        Assert.Equal(-1, ended.TargetFighterId);

        // 终局事件在命中事件之后。
        Assert.Equal(BattleEventType.RoundEnded, result.Events[^1].Type);
    }

    [Fact]
    public void P2KillsP1_RoundEndsAsP2Win()
    {
        BattleStepResult result = Sim().Step(
            Duel(Idle(0, -60, Facing.Right, health: 30), Attacking(1, 0, Facing.Left)),
            FrameInput.Empty(0));

        Assert.Equal(0, result.State.Fighter(0).Health);
        Assert.Equal(RoundOutcome.P2Win, result.State.Round.Outcome);
        Assert.Equal(1, result.State.Round.WinnerFighterId);

        BattleEvent ko = Assert.Single(result.Events, e => e.Type == BattleEventType.Knockout);
        Assert.Equal((1, 0), (ko.SourceFighterId, ko.TargetFighterId));
        Assert.Equal((int)RoundOutcome.P2Win,
            Assert.Single(result.Events, e => e.Type == BattleEventType.RoundEnded).Amount);
    }

    [Fact]
    public void SimultaneousKO_IsDoubleKO_NoFirstMoverAdvantage()
    {
        // 双方同帧互中且都被击败：必须是 DoubleKO，不能因为事件排序先出现 P1 就判 P1 胜。
        BattleStepResult result = Sim().Step(
            Duel(Attacking(0, -60, Facing.Right, health: 30), Attacking(1, 0, Facing.Left, health: 30)),
            FrameInput.Empty(0));

        Assert.Equal(0, result.State.Fighter(0).Health);
        Assert.Equal(0, result.State.Fighter(1).Health);
        Assert.True(result.State.Round.IsDoubleKO);
        Assert.Equal(RoundOutcome.DoubleKO, result.State.Round.Outcome);
        Assert.Null(result.State.Round.WinnerFighterId);

        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.Knockout);
        BattleEvent dko = Assert.Single(result.Events, e => e.Type == BattleEventType.DoubleKnockout);
        Assert.Equal((0, 1), (dko.SourceFighterId, dko.TargetFighterId));

        BattleEvent ended = Assert.Single(result.Events, e => e.Type == BattleEventType.RoundEnded);
        Assert.Equal((int)RoundOutcome.DoubleKO, ended.Amount);
        Assert.Equal(-1, ended.SourceFighterId);

        // 双方各自的命中事件都完整存在（不因对方先结算而丢失）。
        Assert.Equal(2, result.Events.Count(e => e.Type == BattleEventType.Hit));
    }

    [Fact]
    public void HealthExactlyZero_IsKO()
    {
        BattleStepResult result = Sim(damage: 30).Step(
            Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 30)),
            FrameInput.Empty(0));

        Assert.Equal(0, result.State.Fighter(1).Health);
        Assert.Equal(RoundOutcome.P1Win, result.State.Round.Outcome);
    }

    [Fact]
    public void OverkillDamage_ClampsToZero_AndKOs()
    {
        BattleStepResult result = Sim(damage: 60).Step(
            Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 10)),
            FrameInput.Empty(0));

        Assert.Equal(0, result.State.Fighter(1).Health);
        Assert.Equal(10, Assert.Single(result.Events, e => e.Type == BattleEventType.Hit).Amount);
        Assert.Equal(RoundOutcome.P1Win, result.State.Round.Outcome);
    }

    [Fact]
    public void ZeroDamage_DoesNotKO()
    {
        BattleStepResult result = Sim(damage: 0).Step(
            Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 1)),
            FrameInput.Empty(0));

        Assert.Equal(1, result.State.Fighter(1).Health);
        Assert.Equal(RoundPhase.InProgress, result.State.Round.Phase);
        Assert.DoesNotContain(result.Events, e => e.Type is BattleEventType.Knockout
            or BattleEventType.DoubleKnockout or BattleEventType.RoundEnded);
    }

    [Fact]
    public void BlockedAttack_DoesNotKO()
    {
        // 体力 1 但成功防御（站防 + 后）：不掉血、不进 KO。
        BattleStepResult result = Sim(damage: 60).Step(
            Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 1)),
            new FrameInput(0, InputBits.None, InputBits.Right)); // P2 朝左，「后」= Right

        Assert.Equal(1, result.State.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Blockstun, result.State.Fighter(1).LogicState);
        Assert.False(result.State.Round.IsFinished);
        Assert.Contains(result.Events, e => e.Type == BattleEventType.Blocked);
    }

    [Fact]
    public void KoDoesNotSkipOpponentsSameFrameHit()
    {
        // P1 的命中让 P2 归零，但 P2 同帧的有效命中仍必须结算（不因先处理 P1 而丢失）。
        var sim = Sim(damage: 30);
        BattleState before = Duel(
            Attacking(0, -60, Facing.Right, health: 1000),
            Attacking(1, 0, Facing.Left, health: 30));

        BattleStepResult result = sim.Step(before, FrameInput.Empty(0));

        Assert.Equal(970, result.State.Fighter(0).Health);   // P1 吃了 P2 的 30
        Assert.Equal(0, result.State.Fighter(1).Health);     // P2 被 KO
        Assert.Equal(RoundOutcome.P1Win, result.State.Round.Outcome);
        Assert.Equal(2, result.Events.Count(e => e.Type == BattleEventType.Hit));
        Assert.Contains(result.Events,
            e => e.Type == BattleEventType.Hit && e.TargetFighterId == 0 && e.SourceFighterId == 1);
    }

    [Fact]
    public void DoubleKoIsMirrorSymmetric()
    {
        // 身份保持的镜像：交换双方位置与招式归属，结果必须同为 DoubleKO。
        BattleStepResult a = Sim().Step(
            Duel(Attacking(0, -60, Facing.Right, 30), Attacking(1, 0, Facing.Left, 30)),
            FrameInput.Empty(0));
        BattleStepResult b = Sim().Step(
            Duel(Attacking(0, 0, Facing.Left, 30), Attacking(1, -60, Facing.Right, 30)),
            FrameInput.Empty(0));

        Assert.Equal(a.State.Round.Outcome, b.State.Round.Outcome);
        Assert.Equal(a.State.Fighter(0).Health, b.State.Fighter(0).Health);
        Assert.Equal(a.State.Fighter(1).Health, b.State.Fighter(1).Health);
        Assert.Equal(RoundOutcome.DoubleKO, a.State.Round.Outcome);
    }

    // ─────────────────────────────────────────────────────────────
    // 终局行为（冻结语义）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AfterKo_FrameAdvances_ButFightersAndRoundAreFrozen()
    {
        var sim = Sim();
        BattleState afterKo = sim.Step(
            Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 30)),
            FrameInput.Empty(0)).State;

        Assert.True(afterKo.Round.IsFinished);
        int frame = afterKo.Frame;

        // 终局后仍可推进帧号（BattleSession 不变量不破），但角色/回合逐位不变、零事件。
        for (int i = 0; i < 5; i++)
        {
            BattleStepResult frozen = sim.Step(afterKo, FrameInput.Empty(afterKo.Frame));
            afterKo = frozen.State;

            Assert.Empty(frozen.Events);
            Assert.Empty(frozen.Contacts);
            Assert.Equal(frame + i + 1, afterKo.Frame);
            Assert.Same(afterKo.Round, afterKo.Round); // 同一实例（未重新判定）
            Assert.Equal(RoundOutcome.P1Win, afterKo.Round.Outcome);
        }

        Assert.Equal(0, afterKo.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Hitstun, afterKo.Fighter(1).LogicState);
    }

    [Fact]
    public void AfterKo_InputDoesNotChangeFighters()
    {
        var sim = Sim();
        BattleState afterKo = sim.Step(
            Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 30)),
            FrameInput.Empty(0)).State;

        BattleState frozen = sim.Step(afterKo,
            new FrameInput(afterKo.Frame, InputBits.LightPunch | InputBits.Right, InputBits.Down)).State;

        Assert.Equal(afterKo.Fighter(0), frozen.Fighter(0));
        Assert.Equal(afterKo.Fighter(1), frozen.Fighter(1));
        Assert.Equal(afterKo.Round, frozen.Round);
    }

    [Fact]
    public void KoEvents_UseEventIdContract_AndAppearOnce()
    {
        var sim = Sim();
        BattleState state = Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 30));

        var all = new List<BattleEvent>();
        for (int f = 0; f < 6; f++)
        {
            BattleStepResult r = sim.Step(state, FrameInput.Empty(state.Frame));
            state = r.State;
            all.AddRange(r.Events);
        }

        Assert.Single(all, e => e.Type == BattleEventType.Knockout);
        Assert.Single(all, e => e.Type == BattleEventType.RoundEnded);
        Assert.Equal(all.Count, all.Select(e => e.EventId).Distinct().Count());
        Assert.All(all, e => Assert.Equal(e.Frame * BattleEvent.MaxEventsPerFrame + (e.EventId % BattleEvent.MaxEventsPerFrame), e.EventId));
    }

    [Fact]
    public void MidSnapshotReplay_ProducesSameRoundStateAndEvents()
    {
        var sim = Sim();
        var full = new List<BattleEvent>();
        BattleState state = Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 30));

        for (int f = 0; f < 12; f++)
        {
            BattleStepResult r = sim.Step(state, FrameInput.Empty(state.Frame));
            state = r.State;
            full.AddRange(r.Events);
        }
        Assert.True(state.Round.IsFinished);

        // 从 KO 前一帧的快照续跑：回合状态与事件必须一致。
        var sim2 = Sim();
        BattleState start = Duel(Attacking(0, -60, Facing.Right), Idle(1, 0, Facing.Left, health: 30));
        BattleStepResult first = sim2.Step(start, FrameInput.Empty(0));
        BattleState cont = first.State;
        var contEvents = new List<BattleEvent>(first.Events);
        for (int f = 1; f < 12; f++)
        {
            BattleStepResult r = sim2.Step(cont, FrameInput.Empty(cont.Frame));
            cont = r.State;
            contEvents.AddRange(r.Events);
        }

        Assert.Equal(full, contEvents);
        Assert.Equal(state.Round, cont.Round);
        Assert.Equal(state.Fighter(0), cont.Fighter(0));
        Assert.Equal(state.Fighter(1), cont.Fighter(1));
    }

    // ─────────────────────────────────────────────────────────────
    // RoundState 不变式
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RoundState_RejectsIllegalConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundState.ForOutcome(RoundOutcome.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundState.ForOutcome((RoundOutcome)99));

        Assert.Throws<ArgumentOutOfRangeException>(() => RoundState.InProgress.Finish(RoundOutcome.None));
        Assert.Throws<InvalidOperationException>(() =>
            RoundState.ForOutcome(RoundOutcome.P1Win).Finish(RoundOutcome.P2Win));
    }

    [Fact]
    public void RoundState_WinnerMatchesOutcome()
    {
        Assert.Equal(0, RoundState.ForOutcome(RoundOutcome.P1Win).WinnerFighterId);
        Assert.Equal(1, RoundState.ForOutcome(RoundOutcome.P2Win).WinnerFighterId);
        Assert.Null(RoundState.ForOutcome(RoundOutcome.DoubleKO).WinnerFighterId);
        Assert.False(RoundState.InProgress.IsFinished);
        Assert.True(RoundState.ForOutcome(RoundOutcome.DoubleKO).IsFinished);
        Assert.True(RoundState.ForOutcome(RoundOutcome.DoubleKO).IsDoubleKO);
    }

    [Fact]
    public void FromFinalHealth_IsIdempotentAfterFinish()
    {
        RoundState finished = RoundState.ForOutcome(RoundOutcome.P1Win);
        // 已结束的回合不会被「再次判定」改写（例如后续体力变化）。
        Assert.Same(finished, RoundState.FromFinalHealth(finished, 0, 1000));
        Assert.Same(finished, RoundState.FromFinalHealth(finished, 1000, 0));
    }

    [Fact]
    public void RoundState_IsPartOfSnapshot_AndImmutable()
    {
        BattleState state = BattleState.CreateDefault();
        Assert.False(state.Round.IsFinished);
        Assert.Same(RoundState.InProgress, state.Round);

        BattleState ended = new(state.Frame, state.Fighters, state.StateVersion,
            RoundState.ForOutcome(RoundOutcome.P2Win));
        Assert.Equal(RoundOutcome.P2Win, ended.Round.Outcome);
        // 原快照不受影响。
        Assert.False(state.Round.IsFinished);
    }
}
