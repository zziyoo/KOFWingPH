using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 07C（D92/D93）：地面击退、击飞、空中受击、倒地与起身。
///
/// <para><b>测试配置</b>（双方同配置，镜像可验证）：</para>
/// <list type="bullet">
/// <item>Jab（5A，轻拳）：Startup 1 / Active 3 / Recovery 4；伤害 10、命中硬直 20；
///       地面击退 4 单位/帧、摩擦 1 —— 位置序列 4,3,2,1,0（合计 10 单位）。</item>
/// <item>Launch（5B，重拳）：同上时间线；伤害 20；击飞水平 2、垂直 6；
///       落地倒地 8 帧、起身 5 帧（重力沿用 0.5）。</item>
/// <item>Tap（5C，轻脚）：伤害 5、命中硬直 10、<b>无击退</b>。</item>
/// </list>
/// </summary>
public class KnockbackLaunchTests
{
    private const string Jab = "Jab";
    private const string Launch = "Launch";
    private const string Tap = "Tap";

    private static BattleSimulation Sim()
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition Build(string id) => new(
            id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5),
                new CommandDefinition("5B", Array.Empty<CommandDirection>(), InputBits.HeavyPunch, priority: 5),
                new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick, priority: 5),
            },
            new[]
            {
                new MoveDefinition(Jab, "5A", 1, 3, 4, attacks: new[]
                {
                    new AttackDefinition(
                        FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                        FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                        0, 0, AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10,
                        groundKnockback: FixedPoint.FromInt(4),
                        groundKnockbackFriction: FixedPoint.FromInt(1)),
                }),
                new MoveDefinition(Launch, "5B", 1, 3, 4, attacks: new[]
                {
                    new AttackDefinition(
                        FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                        FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                        0, 0, AttackHeight.Mid, damage: 20, hitstunFrames: 20, blockstunFrames: 10,
                        launchVelocityX: FixedPoint.FromInt(2),
                        launchVelocityY: FixedPoint.FromInt(6),
                        knockdownFrames: 8,
                        wakeupFrames: 5),
                }),
                new MoveDefinition(Tap, "5C", 1, 3, 4, attacks: new[]
                {
                    new AttackDefinition(
                        FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                        FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                        0, 0, AttackHeight.Mid, damage: 5, hitstunFrames: 10, blockstunFrames: 10),
                }),
            },
            movement);

        return new BattleSimulation(new FightRoster(Build("Kai"), Build("Rei")));
    }

    private static BattleState Duel(int p1X = -60, int p2X = 0, int p2Health = 1000)
    {
        FighterState p2 = FighterState.Create(1, 1000, p2X, Facing.Left);
        if (p2Health < 1000)
            p2 = p2 with { Health = p2Health };

        return new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, p1X, Facing.Right),
            p2,
        }, BattleState.CreateDefault().StateVersion);
    }

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) =>
        new(frame, p1, p2);

    /// <summary>用指定按钮发动一招并命中（帧 0 发动、帧 1 命中），返回命中后的状态。</summary>
    private static (BattleState state, BattleStepResult hit) Attack(BattleSimulation sim,
        BattleState state, InputBits button)
    {
        state = sim.Step(state, F(state.Frame, button)).State;
        BattleStepResult hit = sim.Step(state, F(state.Frame, InputBits.None));
        return (hit.State, hit);
    }

    // ─────────────────────────────────────────────────────────────
    // 地面击退（D92）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GroundHit_AppliesKnockbackThatDecaysToZero()
    {
        var sim = Sim();
        (BattleState state, _) = Attack(sim, Duel(), InputBits.LightPunch);

        // 命中当帧：速度 4（远离攻击者 = +X），摩擦 1，位置尚未移动。
        Assert.Equal(FixedPoint.FromInt(4), state.Fighter(1).VelocityX);
        Assert.Equal(FixedPoint.FromInt(1), state.Fighter(1).KnockbackFriction);
        Assert.Equal(0, state.Fighter(1).PositionX.ToIntTruncated());
        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(1).LogicState);

        // 逐帧位移 4,3,2,1，随后速度/摩擦归零，总计 +10 单位。
        int[] expected = { 4, 7, 9, 10, 10 };
        for (int i = 0; i < expected.Length; i++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            Assert.Equal(expected[i], state.Fighter(1).PositionX.ToIntTruncated());
        }

        Assert.Equal(FixedPoint.Zero, state.Fighter(1).VelocityX);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).KnockbackFriction);
        // 硬直仍在（20 帧），位置保持不变。
        state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(10, state.Fighter(1).PositionX.ToIntTruncated());
    }

    [Fact]
    public void AttackWithoutKnockback_DoesNotMoveTarget()
    {
        var sim = Sim();
        (BattleState state, _) = Attack(sim, Duel(), InputBits.LightKick);

        Assert.Equal(FixedPoint.Zero, state.Fighter(1).VelocityX);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).KnockbackFriction);

        for (int i = 0; i < 6; i++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Equal(0, state.Fighter(1).PositionX.ToIntTruncated());
    }

    [Fact]
    public void BlockedHit_UsesSeparateBlockKnockback_DefaultZero()
    {
        // 本测试配置没有 blockKnockback → 防御不掉位。
        var sim = Sim();
        BattleState state = Duel();
        state = sim.Step(state, F(0, InputBits.LightPunch)).State;
        state = sim.Step(state, F(1, InputBits.None, InputBits.Right)).State; // P2 朝左，「后」= Right

        Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).VelocityX);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).KnockbackFriction);

        // 命中帧之前 P2 仍是站立（会正常步行），防御后不再位移。
        int xAfterBlock = state.Fighter(1).PositionX.ToIntTruncated();
        for (int i = 0; i < 4; i++)
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;

        Assert.Equal(xAfterBlock, state.Fighter(1).PositionX.ToIntTruncated());
    }

    [Fact]
    public void WallCornerKnockback_ClampsAtStageEdge()
    {
        // 目标贴右墙（640）被击退：位置钳制在 640，不越界、不反向。
        var sim = Sim();
        BattleState state = Duel(p1X: 580, p2X: 640);
        (state, _) = Attack(sim, state, InputBits.LightPunch);

        for (int i = 0; i < 6; i++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            Assert.Equal(640, state.Fighter(1).PositionX.ToIntTruncated());
        }

        // 攻击者仍在场内侧，没有被推挤越过被击者。
        Assert.True(state.Fighter(0).PositionX.ToIntTruncated() <= 640);
    }

    [Fact]
    public void MutualHit_KnockbackPushesBothApart_Symmetrically()
    {
        var sim = Sim();
        BattleState state = Duel();
        state = sim.Step(state, F(0, InputBits.LightPunch, InputBits.LightPunch)).State;
        state = sim.Step(state, F(1, InputBits.None, InputBits.None)).State;

        // 双方互中：各自被推离对方（P1 向左、P2 向右），速度对称。
        Assert.Equal(FixedPoint.FromInt(-4), state.Fighter(0).VelocityX);
        Assert.Equal(FixedPoint.FromInt(4), state.Fighter(1).VelocityX);

        state = sim.Step(state, F(2, InputBits.None, InputBits.None)).State;
        Assert.Equal((-64, 4), (state.Fighter(0).PositionX.ToIntTruncated(),
            state.Fighter(1).PositionX.ToIntTruncated()));
    }

    // ─────────────────────────────────────────────────────────────
    // 击飞与空中受击（D93）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Launch_EntersAirHitstun_AndFollowsFixedTrajectory()
    {
        var sim = Sim();
        (BattleState state, _) = Attack(sim, Duel(), InputBits.HeavyPunch);

        // 击飞当帧：位置仍在地面（Y=0），垂直速度 6、水平 2（远离攻击者），无跳跃类型。
        Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).PositionY);
        Assert.Equal(FixedPoint.FromInt(6), state.Fighter(1).VelocityY);
        Assert.Equal(FixedPoint.FromInt(2), state.Fighter(1).VelocityX);
        Assert.Equal(JumpKind.None, state.Fighter(1).JumpKind);
        Assert.Equal(8, state.Fighter(1).LandingKnockdownFrames);
        Assert.Equal(5, state.Fighter(1).LandingWakeupFrames);

        // 逐帧积分：vy = 6−0.5k，y = Σvy；顶点 y = 33（第 11、12 帧）。
        decimal[] expectedY = { 5.5m, 10.5m, 15m, 19m, 22.5m, 25.5m, 28m, 30m, 31.5m, 32.5m, 33m, 33m };
        for (int i = 0; i < expectedY.Length; i++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            Assert.Equal(expectedY[i], (decimal)state.Fighter(1).PositionY.ToDouble());
            Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);
        }

        // 水平匀速：12 帧 × 2 = 24 单位。
        Assert.Equal(24, state.Fighter(1).PositionX.ToIntTruncated());
    }

    [Fact]
    public void Launch_LandsIntoKnockdown_ThenWakeup_ThenStanding()
    {
        var sim = Sim();
        (BattleState state, _) = Attack(sim, Duel(), InputBits.HeavyPunch);

        // 第 23 帧垂直位移归零 → 触地：状态转 Knockdown(8)，贴地零速度。
        int frames = 0;
        while (state.Fighter(1).LogicState == FighterLogicState.AirHitstun && frames < 60)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            frames++;
        }

        Assert.Equal(23, frames);
        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
        Assert.Equal(8, state.Fighter(1).StunRemainingFrames);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).PositionY);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).VelocityX);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).VelocityY);
        Assert.Equal(5, state.Fighter(1).LandingWakeupFrames);
        Assert.Equal(0, state.Fighter(1).LandingKnockdownFrames);

        // 倒地 8 帧 → 起身 5 帧 → 待机。
        for (int i = 0; i < 7; i++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Knockdown, state.Fighter(1).LogicState);
        Assert.Equal(1, state.Fighter(1).StunRemainingFrames);

        state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Wakeup, state.Fighter(1).LogicState);
        Assert.Equal(5, state.Fighter(1).StunRemainingFrames);
        Assert.Equal(0, state.Fighter(1).LandingWakeupFrames);

        for (int i = 0; i < 4; i++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Wakeup, state.Fighter(1).LogicState);

        state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
        Assert.Equal(0, state.Fighter(1).StunRemainingFrames);
    }

    [Fact]
    public void JumpingTarget_HitByGroundAttack_KeepsTrajectoryAndFalls()
    {
        // 空中（跳跃中）目标被普通攻击命中：伤害结算、转 AirHitstun、保持轨迹，
        // 落地后（无倒地计划）直接站起。
        var sim = Sim();
        BattleState state = new(0, new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.Airborne,
                PositionY = FixedPoint.FromInt(40),
                JumpKind = JumpKind.ShortHop,
                VelocityY = FixedPoint.FromInt(2),
            },
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, F(0, InputBits.LightKick)).State;
        state = sim.Step(state, F(1, InputBits.None)).State;

        Assert.Equal(995, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);
        Assert.Equal(JumpKind.None, state.Fighter(1).JumpKind);
        Assert.Equal(0, state.Fighter(1).LandingKnockdownFrames);

        int frames = 0;
        while (state.Fighter(1).LogicState == FighterLogicState.AirHitstun && frames < 60)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            frames++;
        }

        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState); // 无倒地计划 → 直接站起
    }

    [Fact]
    public void AirHitstun_SameInstanceHitsOnce_NewInstanceCanJuggle()
    {
        // 直接构造：P2 已在空中受击（y=20、下落中），P1 的 Launch 处于 Active 首帧。
        var sim = Sim();
        BattleState state = new(0, new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right) with
            {
                LogicState = FighterLogicState.Active,
                MoveId = Launch,
                PhaseRemainingFrames = 4,
                MoveStartedFrame = 0,
            },
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.AirHitstun,
                PositionY = FixedPoint.FromInt(20),
                VelocityY = FixedPoint.FromInt(2),
                LandingKnockdownFrames = 8,
                LandingWakeupFrames = 5,
            },
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, F(0, InputBits.None)).State;
        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);

        // 同一实例的下一个 Active 帧仍有接触，但 D81 去重 → 不再扣血。
        BattleStepResult second = sim.Step(state, F(1, InputBits.None));
        state = second.State;
        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);

        // 新实例（新的发动帧）可以继续追击。
        BattleState juggle = new(state.Frame, new[]
        {
            state.Fighter(0) with
            {
                LogicState = FighterLogicState.Active,
                MoveId = Launch,
                PhaseRemainingFrames = 4,
                MoveStartedFrame = state.Frame,
                // 新实例：旧实例的确认帧必须清空（D91 不变式）。
                HitConfirmFrame = -1,
                BlockConfirmFrame = -1,
            },
            state.Fighter(1),
        }, state.StateVersion, state.Round);

        juggle = sim.Step(juggle, F(juggle.Frame, InputBits.None)).State;
        Assert.Equal(960, juggle.Fighter(1).Health);
        Assert.Equal(FighterLogicState.AirHitstun, juggle.Fighter(1).LogicState);
    }

    [Fact]
    public void KnockdownAndWakeup_CannotAct()
    {
        var sim = Sim();
        (BattleState state, _) = Attack(sim, Duel(), InputBits.HeavyPunch);

        // 等到倒地。
        while (state.Fighter(1).LogicState != FighterLogicState.Knockdown)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        // 倒地/起身期间持续按攻击 + 前：不移动、不发动招式。
        // 例外是「倒计时归零那一帧」——它同帧回到待机更新并恢复行动（与硬直恢复同语义）。
        int guard = 0;
        while (guard++ < 40)
        {
            FighterState p2 = state.Fighter(1);
            if (p2.LogicState is not (FighterLogicState.Knockdown or FighterLogicState.Wakeup))
                break;

            bool recoversThisFrame = p2.StunRemainingFrames == 1
                && (p2.LogicState == FighterLogicState.Wakeup || p2.LandingWakeupFrames == 0);

            int xBefore = p2.PositionX.ToIntTruncated();
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None,
                InputBits.LightPunch | InputBits.Right));
            state = r.State;

            if (recoversThisFrame)
                continue;

            Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.MoveStarted);
            Assert.Equal(xBefore, state.Fighter(1).PositionX.ToIntTruncated());
        }

        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
    }

    [Fact]
    public void WakeupExpiryFrame_AllowsAction()
    {
        // 起身倒计时归零的那一帧同帧回到待机更新与消费（与硬直恢复同语义）。
        var sim = Sim();
        (BattleState state, _) = Attack(sim, Duel(), InputBits.HeavyPunch);

        int guard = 0;
        while (guard++ < 80)
        {
            bool lastWakeupFrame = state.Fighter(1).LogicState == FighterLogicState.Wakeup
                && state.Fighter(1).StunRemainingFrames == 1;
            InputBits p2 = lastWakeupFrame ? InputBits.HeavyPunch : InputBits.None;

            state = sim.Step(state, F(state.Frame, InputBits.None, p2)).State;

            if (lastWakeupFrame)
            {
                Assert.Equal(FighterLogicState.Startup, state.Fighter(1).LogicState);
                Assert.Equal(Launch, state.Fighter(1).MoveId);
                return;
            }
        }

        Assert.Fail("未能到达起身最后一帧");
    }

    // ─────────────────────────────────────────────────────────────
    // 与既有系统的交叉约束
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Launch_KeepsComboCounter()
    {
        // 帧 0 用 5A 命中（连段 1）；帧 8（5A 收招结束）再用 5B 追击 → 连段 2 并击飞。
        var sim = Sim();
        BattleState state = Duel();

        for (int f = 0; f <= 9; f++)
        {
            InputBits p1 = f == 0 ? InputBits.LightPunch
                : f == 8 ? InputBits.HeavyPunch
                : InputBits.None;
            state = sim.Step(state, F(f, p1)).State;
        }

        Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);
        Assert.Equal(2, state.Fighter(1).Combo.Count);
        Assert.Equal(0, state.Fighter(1).Combo.AttackerId);
        Assert.Equal(970, state.Fighter(1).Health);
    }

    [Fact]
    public void LaunchThatKills_StillResolvesKoOnTheHitFrame()
    {
        var sim = Sim();
        BattleState state = Duel(p2Health: 15);
        (state, BattleStepResult hit) = Attack(sim, state, InputBits.HeavyPunch);

        Assert.Equal(0, state.Fighter(1).Health);
        Assert.True(state.Round.IsFinished);
        Assert.Equal(RoundOutcome.P1Win, state.Round.Outcome);
        Assert.Contains(hit.Events, e => e.Type == BattleEventType.Knockout);
        // 终局冻结：即使被击飞者也停止运动（不再推进空中轨迹）。
        BattleState frozen = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(state.Fighter(1), frozen.Fighter(1));
    }

    [Fact]
    public void AirHitstun_ValidateRejectsIllegalCombinations()
    {
        // 空中受击但 Y=0 且垂直速度不为正 → 非法（地面受击不能伪装成空中受击）。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 1, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 0,
            Facing: Facing.Left, LogicState: FighterLogicState.AirHitstun,
            VelocityX: 2, VelocityY: 0, KnockbackFriction: 0,
            LandingKnockdownFrames: 8).Validate());

        // 空中受击携带跳跃类型 → 非法（击飞不是跳跃）。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 1, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 10,
            Facing: Facing.Left, LogicState: FighterLogicState.AirHitstun,
            VelocityX: 2, VelocityY: 3, JumpKind: JumpKind.ShortHop,
            LandingKnockdownFrames: 8).Validate());

        // 地面状态携带水平速度（非受击/防御）→ 非法。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 1, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 0,
            Facing: Facing.Left, LogicState: FighterLogicState.Standing,
            VelocityX: 2).Validate());

        // 受击状态：速度与摩擦必须同时存在。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 1, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 0,
            Facing: Facing.Left, LogicState: FighterLogicState.Hitstun,
            StunRemainingFrames: 5, KnockbackFriction: FixedPoint.FromInt(1)).Validate());

        // 倒地必须携带 ≥ 1 的剩余帧。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 1, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 0,
            Facing: Facing.Left, LogicState: FighterLogicState.Knockdown,
            StunRemainingFrames: 0).Validate());
    }

    [Fact]
    public void AirborneMovement_DoesNotLeakAcrossStates()
    {
        // 击飞→倒地→起身→待机 全流程结束后，速度/落地计划全部归零（不留脏数据）。
        var sim = Sim();
        (BattleState state, _) = Attack(sim, Duel(), InputBits.HeavyPunch);

        int guard = 0;
        while (state.Fighter(1).LogicState != FighterLogicState.Standing && guard++ < 80)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        FighterState p2 = state.Fighter(1);
        Assert.Equal(FighterLogicState.Standing, p2.LogicState);
        Assert.Equal(FixedPoint.Zero, p2.VelocityX);
        Assert.Equal(FixedPoint.Zero, p2.VelocityY);
        Assert.Equal(FixedPoint.Zero, p2.PositionY);
        Assert.Equal(JumpKind.None, p2.JumpKind);
        Assert.Equal(0, p2.StunRemainingFrames);
        Assert.Equal(0, p2.LandingKnockdownFrames);
        Assert.Equal(0, p2.LandingWakeupFrames);
        Assert.Equal(0, p2.KnockbackFriction);
    }

    [Fact]
    public void LongRun_LaunchHeavyScript_IsDeterministic()
    {
        static (BattleState state, List<BattleEvent> events) Run()
        {
            var sim = Sim();
            BattleState state = Duel();
            var events = new List<BattleEvent>();

            for (int f = 0; f < 1200; f++)
            {
                InputBits p1 = (f % 150) switch
                {
                    0 => InputBits.HeavyPunch,
                    40 => InputBits.LightPunch,
                    80 => InputBits.LightKick,
                    _ => InputBits.None,
                };
                InputBits p2 = (f % 200) switch
                {
                    20 => InputBits.HeavyPunch,
                    _ => InputBits.None,
                };

                BattleStepResult r = sim.Step(state, F(f, p1, p2));
                state = r.State;
                events.AddRange(r.Events);
            }

            return (state, events);
        }

        var a = Run();
        var b = Run();

        Assert.Equal(a.state.Fighter(0), b.state.Fighter(0));
        Assert.Equal(a.state.Fighter(1), b.state.Fighter(1));
        Assert.Equal(a.state.Round, b.state.Round);
        Assert.Equal(a.events, b.events);
        Assert.Equal(a.events.Count, a.events.Select(e => e.EventId).Distinct().Count());
    }
}
