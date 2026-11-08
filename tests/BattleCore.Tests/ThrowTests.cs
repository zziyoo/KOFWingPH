using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 08B（D97）：<b>地面投技</b>。
///
/// <para><b>判定</b>：投技者处于 Active + 本招式投技窗口内 + 目标状态可投 + 目标在其面前且
/// 水平距离 ≤ 范围。<b>不是</b> Hitbox × Hurtbox，也不看防御（站防/蹲防都挡不住投技）。</para>
///
/// <para><b>冲突规则</b>：投技者被有效打击命中 → 投技被打断；目标被有效打击命中 → 抓不住；
/// 双方同帧互相抓 → <b>两边都不成立</b>（ThrowClash），不固定 P1 先手。</para>
/// </summary>
public class ThrowTests
{
    private const string Jab = "Jab";
    private const string Grab = "Grab";

    /// <summary>双方同配置：5A（Startup 2，与投技同帧生效）与 5D 投技（Startup 2）。</summary>
    private static BattleSimulation Sim(int throwHitstop = 0, int range = 70, int damage = 60, bool techable = false, bool withProjectile = false)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition Build(string id) => new(
            id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
                new CommandDefinition("5D", Array.Empty<CommandDirection>(), InputBits.HeavyKick,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[]
            {
                new MoveDefinition(Jab, "5A", 2, 3, 4,
                    attacks: new[]
                    {
                        new AttackDefinition(
                            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                            FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                            0, 0, AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10),
                    }, projectiles: withProjectile ? new[] {
                        new ProjectileDefinition("Bullet", new AttackDefinition(0, 60, 10, 10,
                            damage: 10, hitstunFrames: 5, blockstunFrames: 5), speedX: 5, lifetimeFrames: 200)
                    } : null),
                new MoveDefinition(Grab, "5D", 2, 2, 8,
                    throws: new[]
                    {
                        new ThrowDefinition(
                            throwId: "Throw",
                            range: FixedPoint.FromInt(range),
                            damage: damage,
                            knockdownFrames: 12,
                            wakeupFrames: 8,
                            pushDistance: FixedPoint.FromInt(20),
                            hitstopFrames: throwHitstop, techable: techable),
                    }),
            },
            movement);

        return new BattleSimulation(new FightRoster(Build("Kai"), Build("Rei")));
    }

    private static BattleState Duel(int p1X = -60, int p2X = 0, int p2Health = 1000) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, Facing.Right),
            FighterState.Create(1, 1000, p2X, Facing.Left) with { Health = p2Health },
        },
        BattleState.CreateDefault().StateVersion);

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    /// <summary>帧 0 发动投技、帧 1 继续 Startup、帧 2 投技生效 —— 返回生效帧结果。</summary>
    private static (BattleState state, BattleStepResult result) Throw(
        BattleSimulation sim, BattleState state, InputBits p2 = InputBits.None)
    {
        state = sim.Step(state, F(state.Frame, InputBits.HeavyKick, p2)).State;
        state = sim.Step(state, F(state.Frame, InputBits.None, p2)).State;
        BattleStepResult grab = sim.Step(state, F(state.Frame, InputBits.None, p2));
        return (grab.State, grab);
    }

    // ─────────────────────────────────────────────────────────────
    // 1. 基本成功率与几何
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3, 990, false)]
    [InlineData(4, 930, true)]
    public void ProjectileInterrupt_HasExplicitFinalFramePrecedence(int hitFrame, int health, bool throwHits)
    {
        var sim = Sim(techable: true, withProjectile: true);
        var state = Duel();
        (state, _) = Throw(sim, state);
        while (state.Frame < hitFrame) state = sim.Step(state, FrameInput.Empty(state.Frame)).State;
        var projectile = new ProjectileState(1,0,"Bullet","Jab",state.Fighter(1).PositionX,60,0,0,100);
        state = new BattleState(state.Frame,state.Fighters,state.StateVersion,state.Round,new[]{projectile},2,
            pendingThrow: state.PendingThrow);
        var result = sim.Step(state,FrameInput.Empty(state.Frame));
        Assert.Contains(result.Events,e=>e.Type==BattleEventType.ProjectileHit);
        Assert.Equal(throwHits,result.Events.Any(e=>e.Type==BattleEventType.ThrowHit));
        Assert.Equal(health,result.State.Fighter(1).Health);
        Assert.Null(result.State.PendingThrow);
    }

    [Fact]
    public void PendingThrowSnapshot_RejectsMismatchedInstance()
    {
        var sim = Sim(techable: true);
        var (state, _) = Throw(sim,Duel());
        var fighters = state.Fighters.ToArray();
        fighters[0] = fighters[0] with { MoveStartedFrame = 1 };
        Assert.Throws<ArgumentException>(() => new BattleState(state.Frame,fighters,state.StateVersion,
            pendingThrow: state.PendingThrow));
    }

    [Fact]
    public void PreheldChord_RealPendingThrowCannotAutoTech()
    {
        var sim = Sim(techable: true);
        var state = Duel();
        const InputBits chord = InputBits.LightPunch | InputBits.LightKick;
        for (int f = 0; f < 30; f++) state = sim.Step(state, F(f, InputBits.None, chord)).State;
        state = sim.Step(state, F(30, InputBits.HeavyKick, chord)).State;
        for (int f = 31; f <= 34; f++) state = sim.Step(state, F(f, InputBits.None, chord)).State;
        Assert.Equal(940, state.Fighter(1).Health);
        Assert.Null(state.PendingThrow);
        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void TechWindow_AdvanceAndExactAgreeOver10000Frames(int fps)
    {
        FrameInput Input(int f) => F(f, f == 0 ? InputBits.HeavyKick : InputBits.None,
            f == 4 ? InputBits.LightPunch | InputBits.LightKick : InputBits.None);
        var expected = new BattleSession(Duel(), simulation: Sim(techable: true));
        expected.StepExact(10000, inputProvider: Input);
        var actual = new BattleSession(Duel(), simulation: Sim(techable: true));
        while (actual.CurrentState.Frame < 10000)
        {
            int remain = 10000 - actual.CurrentState.Frame;
            actual.Advance(Math.Min(1d / fps, remain * FixedStepScheduler.FixedStepSeconds), inputProvider: Input);
        }
        Assert.Equal(expected.CurrentState.Fighters.ToArray(), actual.CurrentState.Fighters.ToArray());
        Assert.Equal(expected.CurrentState.PendingThrow, actual.CurrentState.PendingThrow);
    }

    [Theory]
    [InlineData(0, -60, 0)]
    [InlineData(1, -60, 0)]
    [InlineData(0, 580, 640)]
    [InlineData(1, -640, -580)]
    public void Tech_SeparatesLegallyAndIdentifiesOriginalThrow(int owner, int left, int right)
    {
        var sim = Sim(techable: true);
        var state = Duel(p1X: left, p2X: right);
        var events = new List<BattleEvent>();
        for (int f = 0; f <= 4; f++) {
            InputBits attacker = f == 0 ? InputBits.HeavyKick : InputBits.None;
            InputBits defender = f == 4 ? InputBits.LightPunch | InputBits.LightKick : InputBits.None;
            var result = sim.Step(state, F(f, owner == 0 ? attacker : defender, owner == 1 ? attacker : defender));
            state = result.State; events.AddRange(result.Events);
        }
        var tech = Assert.Single(events, e => e.Type == BattleEventType.ThrowTech);
        Assert.Equal(1-owner, tech.SourceFighterId);
        Assert.Equal(owner, tech.TargetFighterId);
        Assert.Equal("Grab", tech.MoveId);
        Assert.Equal("Throw", tech.ThrowId);
        Assert.Equal(4, tech.Frame);
        Assert.True(state.Fighter(1).PositionX - state.Fighter(0).PositionX >= FixedPoint.FromInt(60));
        Assert.All(state.Fighters, fighter => { fighter.Validate(); Assert.InRange(fighter.PositionX.ToDouble(), -640, 640); Assert.Equal(1000, fighter.Health); Assert.Equal(0, fighter.ProjectileSpawnMask); });
    }

    [Theory]
    [InlineData(InputBits.LightPunch)]
    [InlineData(InputBits.LightKick)]
    [InlineData(InputBits.HeavyPunch | InputBits.HeavyKick)]
    [InlineData(InputBits.LightPunch | InputBits.LightKick)]
    public void HeldOrWrongButtons_DoNotTech(InputBits held)
    {
        var sim = Sim(techable: true);
        var state = Duel();
        var events = new List<BattleEvent>();
        for (int f = 0; f < 5; f++) {
            var result = sim.Step(state, F(f, f == 0 ? InputBits.HeavyKick : InputBits.None, held));
            state = result.State; events.AddRange(result.Events);
        }
        Assert.DoesNotContain(events, e => e.Type == BattleEventType.ThrowTech);
        // LP may interrupt a same-frame grab through the pre-existing D97 strike rule.
        if (held == InputBits.LightKick) Assert.Contains(events, e => e.Type == BattleEventType.ThrowHit);
        if ((held & InputBits.HeavyKick) != 0) Assert.Contains(events, e => e.Type == BattleEventType.ThrowClash);
    }

    [Fact]
    public void PendingThrow_SnapshotResumesAndHasNoPrematureKo()
    {
        var sim = Sim(throwHitstop: 3, damage: 1000, techable: true);
        var state = Duel();
        (state, _) = Throw(sim, state);
        Assert.False(state.Round.IsFinished);
        Assert.NotNull(state.PendingThrow);
        var saved = state;
        for (int f = 3; f <= 4; f++) state = sim.Step(state, FrameInput.Empty(f)).State;
        var replay = saved;
        for (int f = 3; f <= 4; f++) replay = sim.Step(replay, FrameInput.Empty(f)).State;
        Assert.Equal(state.Fighters.ToArray(), replay.Fighters.ToArray());
        Assert.True(state.Round.IsFinished);
        Assert.Null(state.PendingThrow);
        Assert.Equal(3, state.Fighter(1).HitstopRemainingFrames);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void TechableThrow_ThreeFrameWindow(int techFrame, bool succeeds)
    {
        var sim = Sim(techable: true);
        var state = Duel();
        var events = new List<BattleEvent>();
        for (int f = 0; f < 6; f++) {
            var result = sim.Step(state, F(f, f == 0 ? InputBits.HeavyKick : InputBits.None,
                f == techFrame ? InputBits.LightPunch | InputBits.LightKick : InputBits.None));
            state = result.State;
            events.AddRange(result.Events);
            if (f == 2 && techFrame != 2) {
                Assert.NotNull(state.PendingThrow);
                Assert.Equal(1000, state.Fighter(1).Health);
            }
        }
        Assert.Equal(succeeds ? 1000 : 940, state.Fighter(1).Health);
        Assert.Equal(succeeds ? 1 : 0, events.Count(e => e.Type == BattleEventType.ThrowTech));
        Assert.Equal(succeeds ? 0 : 1, events.Count(e => e.Type == BattleEventType.ThrowHit));
        Assert.Null(state.PendingThrow);
    }

    [Fact]
    public void P1_GrabsP2_DealsDamageAndKnocksDown()
    {
        var sim = Sim();
        (BattleState state, BattleStepResult result) = Throw(sim, Duel());

        Assert.Equal(940, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
        Assert.Equal(12, state.Fighter(1).StunRemainingFrames);
        Assert.Equal(8, state.Fighter(1).LandingWakeupFrames);
        Assert.Equal(0, state.Fighter(1).LandingKnockdownFrames);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).PositionY);

        // 位移 = 远离投技者（+20），且不越过场地边界 / 不破坏最小间距。
        Assert.Equal(20, state.Fighter(1).PositionX.ToIntTruncated());
        Assert.True(state.Fighter(1).PositionX - state.Fighter(0).PositionX
            >= FixedPoint.FromInt(60));

        BattleEvent hit = Assert.Single(result.Events, e => e.Type == BattleEventType.ThrowHit);
        Assert.Equal((0, 1, Grab, 60),
            (hit.SourceFighterId, hit.TargetFighterId, hit.MoveId, hit.Amount));
    }

    [Fact]
    public void P2_GrabsP1_Mirrored()
    {
        // 默认站位的镜像：P1 在 +60 朝左、P2 在 0 朝右（双方仍然面向对手）。
        var sim = Sim();
        BattleState state = Duel(p1X: 60, p2X: 0);
        state = new BattleState(0, new[]
        {
            state.Fighter(0) with { Facing = Facing.Left },
            state.Fighter(1) with { Facing = Facing.Right },
        }, state.StateVersion, state.Round);

        BattleStepResult grab = default;
        state = sim.Step(state, F(0, InputBits.None, InputBits.HeavyKick)).State;
        state = sim.Step(state, F(1, InputBits.None, InputBits.None)).State;
        grab = sim.Step(state, F(2, InputBits.None, InputBits.None));
        state = grab.State;

        Assert.Equal(940, state.Fighter(0).Health);
        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(0).LogicState);
        Assert.Equal(80, state.Fighter(0).PositionX.ToIntTruncated()); // 远离 P2（世界 +X）
        Assert.Contains(grab.Events, e => e.Type == BattleEventType.ThrowHit
            && e.SourceFighterId == 1 && e.TargetFighterId == 0);
    }

    [Fact]
    public void OutOfRange_DoesNotGrab_ButStillReportsAttempt()
    {
        // 80 > 范围 70：抓不到（只有 ThrowAttempt，没有 ThrowHit）。
        var sim = Sim();
        (BattleState state, BattleStepResult result) = Throw(sim, Duel(p1X: -80, p2X: 0));

        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
        Assert.Contains(result.Events, e => e.Type == BattleEventType.ThrowAttempt);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    [Fact]
    public void ExactlyAtRange_Grabs_OneTenthBeyondDoesNot()
    {
        var sim = Sim();
        (BattleState inside, _) = Throw(sim, Duel(p1X: -70, p2X: 0));
        Assert.Equal(940, inside.Fighter(1).Health);

        (BattleState outside, _) = Throw(Sim(), Duel(p1X: -71, p2X: 0));
        Assert.Equal(1000, outside.Fighter(1).Health);
    }

    [Fact]
    public void TargetBehindThrower_IsNotGrabbed()
    {
        // 目标在背后（投技者朝右、目标在左）：即使距离很近也抓不到。
        var sim = Sim();
        BattleState state = Duel(p1X: 0, p2X: -40);
        (state, BattleStepResult result) = Throw(sim, state);

        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 可投状态资格
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StandingGuard_DoesNotStopThrow()
    {
        var sim = Sim();
        // P2 朝左 → 「后」= 世界 +X；按住后是防御姿势。
        (BattleState state, BattleStepResult result) = Throw(sim, Duel(), InputBits.Right);

        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
        Assert.Equal(940, state.Fighter(1).Health);
        Assert.Contains(result.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    [Fact]
    public void CrouchGuard_DoesNotStopThrow()
    {
        var sim = Sim();
        (BattleState state, BattleStepResult result) = Throw(
            sim, Duel(), InputBits.Down | InputBits.Right);

        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
        Assert.Equal(940, state.Fighter(1).Health);
        Assert.Contains(result.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    [Fact]
    public void AirborneTarget_IsNotThrowable()
    {
        var sim = Sim();
        BattleState state = Duel();
        state = new BattleState(0, new[]
        {
            state.Fighter(0),
            state.Fighter(1) with
            {
                LogicState = FighterLogicState.Airborne,
                PositionY = FixedPoint.FromInt(30),
                JumpKind = JumpKind.ShortHop,
                VelocityY = FixedPoint.FromInt(2),
            },
        }, state.StateVersion, state.Round);

        (state, BattleStepResult result) = Throw(sim, state);

        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Airborne, state.Fighter(1).LogicState);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    [Fact]
    public void KnockdownTarget_IsNotThrowable()
    {
        var sim = Sim();
        BattleState state = Duel();
        state = new BattleState(0, new[]
        {
            state.Fighter(0),
            state.Fighter(1) with
            {
                LogicState = FighterLogicState.Knockdown,
                StunRemainingFrames = 10,
                LandingWakeupFrames = 5,
            },
        }, state.StateVersion, state.Round);

        (state, BattleStepResult result) = Throw(sim, state);

        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    [Fact]
    public void SameThrowInstance_CannotGrabTheSameTargetTwice()
    {
        // 直接构造：目标状态可投且静止，但已经记录了本次投技实例 → 不再结算。
        var sim = Sim();
        BattleState state = Duel();
        state = new BattleState(0, new[]
        {
            state.Fighter(0),
            state.Fighter(1) with { LastThrowStartFrame = 0 }, // 与投技者 MoveStartedFrame 相同
        }, state.StateVersion, state.Round);

        (state, BattleStepResult result) = Throw(sim, state);

        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 冲突仲裁
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MutualThrows_SameFrame_ClashWithoutDamage()
    {
        var sim = Sim();
        BattleState state = Duel();
        state = sim.Step(state, F(0, InputBits.HeavyKick, InputBits.HeavyKick)).State;
        state = sim.Step(state, F(1, InputBits.None, InputBits.None)).State;
        BattleStepResult clash = sim.Step(state, F(2, InputBits.None, InputBits.None));
        state = clash.State;

        // 双方都不成立：不掉血、不倒地（招式继续走自己的时间线）。
        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Active, state.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Active, state.Fighter(1).LogicState);

        Assert.Single(clash.Events, e => e.Type == BattleEventType.ThrowClash);
        Assert.DoesNotContain(clash.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    [Fact]
    public void StrikeAndThrow_SameFrame_StrikeWins()
    {
        // 双方都是 Startup 2：P1 的投技与 P2 的 5A 在同帧生效 → 有效打击中断投技。
        var sim = Sim();
        BattleState state = Duel();
        state = sim.Step(state, F(0, InputBits.HeavyKick, InputBits.LightPunch)).State;
        state = sim.Step(state, F(1, InputBits.None, InputBits.None)).State;
        BattleStepResult result = sim.Step(state, F(2, InputBits.None, InputBits.None));
        state = result.State;

        // P1 被打中（10 伤害、进入 Hitstun），P2 毫发无伤。
        Assert.Equal(990, state.Fighter(0).Health);
        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(0).LogicState);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.ThrowHit);
        Assert.Contains(result.Events, e => e.Type == BattleEventType.Hit);
    }

    [Fact]
    public void ThrowIntoGuard_StillHits_ButBlockedStrikeDoesNotInterruptThrow()
    {
        // 投技者的招式若被防御（不是「被打中」），不构成打断——本帧没有打击来源，
        // 这里验证的是「防御不会保护被投者」：P2 全程按后，仍然被投。
        var sim = Sim();
        BattleState state = Duel(p1X: -60, p2X: 0);
        for (int f = 0; f < 3; f++)
            state = sim.Step(state, F(f, f == 0 ? InputBits.HeavyKick : InputBits.None, InputBits.Right)).State;

        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
        Assert.Equal(940, state.Fighter(1).Health);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 击退、KO、停顿
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ThrowPush_IsClampedByStageBoundary()
    {
        var sim = Sim();
        (BattleState state, _) = Throw(sim, Duel(p1X: 580, p2X: 640));

        Assert.Equal(640, state.Fighter(1).PositionX.ToIntTruncated()); // 贴墙不越界
        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
    }

    [Fact]
    public void ThrowThatKills_ResolvesKoOnTheGrabFrame()
    {
        var sim = Sim();
        (BattleState state, BattleStepResult result) = Throw(sim, Duel(p2Health: 30));

        Assert.Equal(0, state.Fighter(1).Health);
        Assert.True(state.Round.IsFinished);
        Assert.Equal(RoundOutcome.P1Win, state.Round.Outcome);
        Assert.Contains(result.Events, e => e.Type == BattleEventType.Knockout);

        // 实扣伤害 = 30（被钳制），不是配置的 60。
        BattleEvent hit = Assert.Single(result.Events, e => e.Type == BattleEventType.ThrowHit);
        Assert.Equal(30, hit.Amount);
    }

    [Fact]
    public void ThrowHitstop_FreezesBothSides()
    {
        var sim = Sim(throwHitstop: 4);
        (BattleState state, _) = Throw(sim, Duel());

        Assert.Equal(4, state.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(4, state.Fighter(1).HitstopRemainingFrames);

        int stun = state.Fighter(1).StunRemainingFrames;
        for (int k = 0; k < 4; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            Assert.Equal(stun, state.Fighter(1).StunRemainingFrames);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 确定性、事件与构造期校验
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MidSnapshotReplay_IsIdentical()
    {
        static BattleState Advance(BattleSimulation sim, BattleState state, int toFrame)
        {
            while (state.Frame < toFrame)
                state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            return state;
        }

        var simA = Sim();
        BattleState full = Duel();
        full = simA.Step(full, F(0, InputBits.HeavyKick)).State;
        full = Advance(simA, full, 30);

        var simB = Sim();
        BattleState head = Duel();
        head = simB.Step(head, F(0, InputBits.HeavyKick)).State;
        head = Advance(simB, head, 10);
        BattleState resumed = Advance(simB, head, 30);

        Assert.Equal(full.Fighter(0), resumed.Fighter(0));
        Assert.Equal(full.Fighter(1), resumed.Fighter(1));
        Assert.Equal(full.Round, resumed.Round);
    }

    [Fact]
    public void ThrowAttempt_IsEmittedOncePerInstance()
    {
        var sim = Sim();
        BattleState state = Duel(p1X: -200, p2X: 0); // 远距离：投技挥空
        int attempts = 0;

        for (int f = 0; f < 12; f++)
        {
            BattleStepResult r = sim.Step(state, F(f, f == 0 ? InputBits.HeavyKick : InputBits.None));
            state = r.State;
            attempts += r.Events.Count(e => e.Type == BattleEventType.ThrowAttempt);
        }

        Assert.Equal(1, attempts);
        Assert.Contains(state.Fighter(0).LogicState,
            new[] { FighterLogicState.Recovery, FighterLogicState.Standing });
    }

    [Fact]
    public void ThrowDefinition_RejectsIllegalConfiguration()
    {
        static ThrowDefinition Make(int range = 70, int first = 0, int last = int.MaxValue,
            int damage = 0, int knockdown = 12, int hitstop = 0) => new(
                "T", FixedPoint.FromInt(range), first, last, damage, knockdown, 8,
                FixedPoint.FromInt(20), hitstop);

        Assert.Throws<ArgumentException>(() => new ThrowDefinition(" ", FixedPoint.FromInt(70)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(range: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(range: ThrowDefinition.MaxRangeUnits + 1));
        Assert.Throws<ArgumentException>(() => Make(first: 3, last: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(damage: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(knockdown: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(hitstop: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Make(hitstop: AttackDefinition.MaxHitstopFrames + 1));
    }

    [Fact]
    public void MoveDefinition_RejectsThrowWindowOutsideActive()
    {
        // Active 只有 2 帧：窗口 3..4 越界。
        Assert.Throws<ArgumentException>(() => new MoveDefinition(
            "Grab", "5D", 2, 2, 8,
            throws: new[] { new ThrowDefinition("T", FixedPoint.FromInt(70), 3, 4) }));

        // 同一招式内 ThrowId 重复。
        Assert.Throws<ArgumentException>(() => new MoveDefinition(
            "Grab", "5D", 2, 2, 8,
            throws: new[]
            {
                new ThrowDefinition("T", FixedPoint.FromInt(70)),
                new ThrowDefinition("T", FixedPoint.FromInt(70)),
            }));
    }
}
