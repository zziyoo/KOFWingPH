using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 08 最终专项补修（一）：
/// <list type="bullet">
/// <item><b>P1-A / D100</b>：发射者在飞行道具生成<b>之后</b>被普通命中 / 击飞 / 投技中断时，
///       <c>ProjectileSpawnMask</c> 必须随攻击实例一并清理，已发射实体独立存活。</item>
/// <item><b>P1-B / D101</b>：Hitstop 冻结角色的坐标不参与 D73 地面最小间距的对称分摊，
///       缺口由未冻结方承担；双方冻结时保持已有合法位置。</item>
/// </list>
///
/// <para>本文件的复现刻意把「生成帧」与「中断帧」分开：生成帧（帧 1）之后的所有 Active
/// 帧里发射者仍携带非零掩码，因此中断路径会真正走到 <c>FighterState.Validate</c>，
/// 而不是只证明「招式被打断、道具没出生」。</para>
/// </summary>
public class Stage08EdgeCaseTests
{
    private const string Fire = "Fire";
    private const string FireAgain = "FireAgain";
    private const string Ball = "Ball";
    private const string Ball2 = "Ball2";
    private const string Strike = "Strike";
    private const string Launcher = "Launcher";
    private const string Grab = "Grab";

    private const int Separation = 60;

    private enum Interrupt
    {
        Strike,
        Launch,
        Grab,
    }

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    // ─────────────────────────────────────────────────────────────
    // 配置：P1 发射招式（出生点在身后，避免生成后的头几帧就打到 P2）
    // ─────────────────────────────────────────────────────────────

    private static AttackDefinition FireHit() => new(
        offsetForward: FixedPoint.FromInt(-40), offsetUp: FixedPoint.FromInt(50),
        halfWidth: FixedPoint.FromInt(10), halfHeight: FixedPoint.FromInt(10),
        height: AttackHeight.Mid, damage: 5, hitstunFrames: 12, blockstunFrames: 8);

    private static ProjectileDefinition Spawn(string typeId) => new(
        typeId, FireHit(), speedX: FixedPoint.FromInt(5), lifetimeFrames: 60,
        maxAlive: 1, spawnAtActiveFrame: 0);

    private static AttackDefinition MeleeHit(bool launches) => new(
        offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
        halfWidth: FixedPoint.FromInt(30), halfHeight: FixedPoint.FromInt(30),
        firstActiveFrame: 0, lastActiveFrame: 0,
        height: AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10,
        launchVelocityX: launches ? FixedPoint.FromInt(2) : FixedPoint.Zero,
        launchVelocityY: launches ? FixedPoint.FromInt(6) : FixedPoint.Zero,
        knockdownFrames: launches ? 8 : 0,
        wakeupFrames: launches ? 5 : 0);

    /// <summary>
    /// P1：5C → Fire（Startup 1 / Active 4 / Recovery 6，Active 第 0 帧发射）；
    /// 可选取消到 5A → FireAgain（另一条独立发射招式）。
    /// P2：按 <paramref name="kind"/> 提供 5A（普通打击 / 击飞）或 5D（投技）。
    /// </summary>
    private static BattleSimulation InterruptSim(Interrupt kind, bool withCancel = false)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition P1() => new("Kai",
            new[]
            {
                new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick,
                    priority: 5, bufferLifetimeFrames: 24),
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[]
            {
                new MoveDefinition(Fire, "5C", 1, 4, 6,
                    projectiles: new[] { Spawn(Ball) },
                    cancels: withCancel
                        ? new[] { new CancelDefinition(FireAgain, 0, 8, allowWhiff: true) }
                        : null),
                new MoveDefinition(FireAgain, "5A", 1, 4, 6,
                    projectiles: new[] { Spawn(Ball2) }),
            },
            movement);

        MoveDefinition P2Move() => kind switch
        {
            Interrupt.Launch => new MoveDefinition(Launcher, "5A", 1, 3, 4,
                attacks: new[] { MeleeHit(launches: true) }),
            Interrupt.Grab => new MoveDefinition(Grab, "5D", 1, 3, 8,
                throws: new[]
                {
                    new ThrowDefinition("T", FixedPoint.FromInt(70),
                        damage: 60, knockdownFrames: 12, wakeupFrames: 8,
                        pushDistance: FixedPoint.FromInt(20)),
                }),
            _ => new MoveDefinition(Strike, "5A", 1, 3, 4,
                attacks: new[] { MeleeHit(launches: false) }),
        };

        FighterDefinition P2() => new("Rei",
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
                new CommandDefinition("5D", Array.Empty<CommandDirection>(), InputBits.HeavyKick,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[] { P2Move() },
            movement);

        return new BattleSimulation(new FightRoster(P1(), P2()));
    }

    /// <summary>标准初态：P1(-60) 朝右、P2(-10) 朝左，距离 50（近战可及、投技可及）。</summary>
    private static BattleState Duel() => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            FighterState.Create(1, 1000, -10, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion);

    /// <summary>
    /// 帧 0：P1 发动 Fire；帧 1：P1 进入 Active 第 0 帧并<b>成功生成实体</b>。
    /// 同帧可给 P2 按键，使其在帧 2（P1 仍在 Active）完成命中 / 抓取。
    /// </summary>
    private static (BattleState State, BattleStepResult Spawn) Emit(
        BattleSimulation sim, BattleState state, InputBits p2OnSpawnFrame = InputBits.None)
    {
        state = sim.Step(state, F(state.Frame, InputBits.LightKick)).State;
        BattleStepResult spawn = sim.Step(state, F(state.Frame, InputBits.None, p2OnSpawnFrame));
        return (spawn.State, spawn);
    }

    private static InputBits InterruptButton(Interrupt kind) => kind == Interrupt.Grab
        ? InputBits.HeavyKick
        : InputBits.LightPunch;

    // ─────────────────────────────────────────────────────────────
    // P1-A 失败复现（修复前：BattleState 构造期的 Validate 抛异常）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void P1A_EmitThenStruck_MaskCleared_EntityStillFlies()
    {
        var sim = InterruptSim(Interrupt.Strike);
        (BattleState spawned, BattleStepResult spawn) = Emit(sim, Duel(), InterruptButton(Interrupt.Strike));

        // ① ② ③ 生成帧：实体已入列、掩码非零、发射者仍在 Active。
        Assert.Equal(FighterLogicState.Active, spawned.Fighter(0).LogicState);
        Assert.NotEqual(0, spawned.Fighter(0).ProjectileSpawnMask);
        ProjectileState born = Assert.Single(spawned.Projectiles);
        Assert.Equal(Ball, born.TypeId);
        Assert.Equal(Fire, born.SourceMoveId);
        Assert.Equal(1, born.ProjectileId);

        // ④ 帧 2：P2 的近战在 P1 后续 Active 帧命中。
        BattleStepResult hit = sim.Step(spawned, F(spawned.Frame, InputBits.None, InputBits.None));
        BattleState after = hit.State;

        // ⑤ ⑥：进入 Hitstun、旧招式身份清空。
        Assert.Equal(FighterLogicState.Hitstun, after.Fighter(0).LogicState);
        Assert.Null(after.Fighter(0).MoveId);
        Assert.Equal(-1, after.Fighter(0).MoveStartedFrame);
        Assert.Equal(0, after.Fighter(0).MoveElapsedFrames);

        // ⑦ ⑧：掩码归零，权威快照校验通过（不再放宽 Validate）。
        Assert.Equal(0, after.Fighter(0).ProjectileSpawnMask);
        after.Fighter(0).Validate();

        // ⑨：已发射实体没有随招式一起消失，且继续按自身速度运动。
        ProjectileState alive = Assert.Single(after.Projectiles);
        Assert.True(alive.PositionX > born.PositionX);
        Assert.Equal(2, after.NextProjectileId);
        BattleState next = sim.Step(after, F(after.Frame, InputBits.None, InputBits.None)).State;
        Assert.Single(next.Projectiles);
        Assert.True(next.Projectiles[0].PositionX > alive.PositionX);
        Assert.Equal(2, next.NextProjectileId); // 没有幽灵二次生成
    }

    [Fact]
    public void P1A_EmitThenLaunched_MaskCleared_LandingPlanIntact()
    {
        var sim = InterruptSim(Interrupt.Launch);
        (BattleState spawned, _) = Emit(sim, Duel(), InterruptButton(Interrupt.Launch));

        Assert.Single(spawned.Projectiles);
        Assert.NotEqual(0, spawned.Fighter(0).ProjectileSpawnMask);

        BattleState after = sim.Step(spawned, F(spawned.Frame, InputBits.None, InputBits.None)).State;

        Assert.Equal(FighterLogicState.AirHitstun, after.Fighter(0).LogicState);
        Assert.Equal(0, after.Fighter(0).ProjectileSpawnMask);
        after.Fighter(0).Validate();

        // D93 不变式：击飞首帧 Y=0 + 正垂直速度，落地计划完整。
        Assert.Equal(FixedPoint.Zero, after.Fighter(0).PositionY);
        Assert.Equal(FixedPoint.FromInt(6), after.Fighter(0).VelocityY);
        // 击飞水平初速 = 远离攻击者：P2 在右，P1 被推向 −X。
        Assert.Equal(FixedPoint.FromInt(-2), after.Fighter(0).VelocityX);
        Assert.Equal(8, after.Fighter(0).LandingKnockdownFrames);
        Assert.Equal(5, after.Fighter(0).LandingWakeupFrames);
        Assert.Equal(FixedPoint.Zero, after.Fighter(0).KnockbackFriction);

        Assert.Single(after.Projectiles);
        Assert.Equal(2, after.NextProjectileId);
    }

    [Fact]
    public void P1A_EmitThenThrown_MaskCleared_ProjectileStillHitsThrower()
    {
        var sim = InterruptSim(Interrupt.Grab);
        (BattleState spawned, _) = Emit(sim, Duel(), InterruptButton(Interrupt.Grab));

        Assert.Single(spawned.Projectiles);
        Assert.NotEqual(0, spawned.Fighter(0).ProjectileSpawnMask);

        BattleState after = sim.Step(spawned, F(spawned.Frame, InputBits.None, InputBits.None)).State;

        // 被投：倒地 + 旧实例身份全清。
        Assert.Equal(FighterLogicState.Knockdown, after.Fighter(0).LogicState);
        Assert.Null(after.Fighter(0).MoveId);
        Assert.Equal(-1, after.Fighter(0).MoveStartedFrame);
        Assert.Equal(0, after.Fighter(0).MoveElapsedFrames);
        Assert.Equal(0, after.Fighter(0).ProjectileSpawnMask);
        after.Fighter(0).Validate();

        // 已发射实体仍在，并且之后仍能命中投技者（来源仍指向原发射招式）。
        BattleStepResult? projectileHit = null;
        BattleState state = after;
        for (int k = 0; k < 40 && projectileHit is null; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
            state = r.State;
            Assert.Equal(2, state.NextProjectileId); // 全程没有重新生成
            if (r.Events.Any(e => e.Type == BattleEventType.ProjectileHit))
                projectileHit = r;
        }

        Assert.NotNull(projectileHit);
        BattleEvent hit = Assert.Single(projectileHit!.Events, e => e.Type == BattleEventType.ProjectileHit);
        Assert.Equal((0, 1, Ball), (hit.SourceFighterId, hit.TargetFighterId, hit.MoveId));
        Assert.Empty(state.Projectiles);
    }

    // ─────────────────────────────────────────────────────────────
    // P1-A 回归：同一实例 / 新实例的掩码生命周期
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void P1A_ActiveToRecovery_KeepsSameInstanceMask_AndDoesNotRespawn()
    {
        var sim = InterruptSim(Interrupt.Strike);
        (BattleState state, _) = Emit(sim, Duel());

        // Active 共 4 帧：帧 5 进入 Recovery，仍是同一个攻击实例。
        while (state.Fighter(0).LogicState != FighterLogicState.Recovery)
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;

        Assert.Equal(Fire, state.Fighter(0).MoveId);
        Assert.NotEqual(0, state.Fighter(0).ProjectileSpawnMask);
        Assert.Single(state.Projectiles);
        Assert.Equal(2, state.NextProjectileId);
    }

    [Fact]
    public void P1A_RecoveryToIdle_ClearsMask_EntityIndependent()
    {
        var sim = InterruptSim(Interrupt.Strike);
        (BattleState state, _) = Emit(sim, Duel());

        while (state.Fighter(0).LogicState != FighterLogicState.Standing)
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;

        Assert.Equal(0, state.Fighter(0).ProjectileSpawnMask);
        Assert.Equal(-1, state.Fighter(0).MoveStartedFrame);
        state.Fighter(0).Validate();
        Assert.Single(state.Projectiles);
        Assert.Equal(2, state.NextProjectileId);
    }

    [Fact]
    public void P1A_CancelToNewMove_ResetsMask_NewInstanceFiresAgain()
    {
        var sim = InterruptSim(Interrupt.Strike, withCancel: true);
        (BattleState state, _) = Emit(sim, Duel());
        Assert.Single(state.Projectiles);
        Assert.NotEqual(0, state.Fighter(0).ProjectileSpawnMask);

        int oldStart = state.Fighter(0).MoveStartedFrame;

        // 帧 2：空挥取消到 FireAgain，掩码随旧实例清零。
        BattleStepResult cancel = sim.Step(state, F(state.Frame, InputBits.LightPunch));
        state = cancel.State;
        Assert.Equal(FireAgain, state.Fighter(0).MoveId);
        Assert.Equal(FighterLogicState.Startup, state.Fighter(0).LogicState);
        Assert.NotEqual(oldStart, state.Fighter(0).MoveStartedFrame);
        Assert.Equal(0, state.Fighter(0).ProjectileSpawnMask);
        Assert.Contains(cancel.Events, e => e.Type == BattleEventType.MoveStarted && e.MoveId == FireAgain);

        // 帧 3：新实例进入 Active → 发射自己的实体；旧实例不重复发射。
        BattleStepResult newSpawn = sim.Step(state, F(state.Frame, InputBits.None));
        state = newSpawn.State;

        Assert.Equal(2, state.Projectiles.Count);
        Assert.Equal(new[] { Ball, Ball2 },
            new[] { state.Projectiles[0].TypeId, state.Projectiles[1].TypeId });
        Assert.Equal(3, state.NextProjectileId);
        Assert.NotEqual(0, state.Fighter(0).ProjectileSpawnMask);
        state.Fighter(0).Validate();
    }

    [Fact]
    public void P1A_MidSnapshotReplay_AfterInterrupt_IsIdentical()
    {
        static BattleState Advance(BattleSimulation sim, BattleState state, int toFrame)
        {
            while (state.Frame < toFrame)
                state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;
            return state;
        }

        var simA = InterruptSim(Interrupt.Strike);
        BattleState full = Emit(simA, Duel(), InterruptButton(Interrupt.Strike)).State;
        full = Advance(simA, full, 30);

        var simB = InterruptSim(Interrupt.Strike);
        BattleState head = Emit(simB, Duel(), InterruptButton(Interrupt.Strike)).State;
        head = Advance(simB, head, 5);
        BattleState resumed = Advance(simB, head, 30);

        Assert.Equal(full.Fighter(0), resumed.Fighter(0));
        Assert.Equal(full.Fighter(1), resumed.Fighter(1));
        Assert.Equal(full.Projectiles, resumed.Projectiles);
        Assert.Equal(full.NextProjectileId, resumed.NextProjectileId);
        Assert.Equal(full.Round, resumed.Round);
    }

    [Fact]
    public void P1A_Validate_StillRejectsMaskOutsideMove()
    {
        // 不放宽 FighterState.Validate：掩码的生命周期由结算负责，而不是靠删校验。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Hitstun,
            StunRemainingFrames: 10, ProjectileSpawnMask: 1).Validate());
    }

    // ─────────────────────────────────────────────────────────────
    // P1-B 配置：前后步行均 4 单位/帧（任务书例子的量级），空招式表
    // ─────────────────────────────────────────────────────────────

    private static BattleSimulation SeparationSim()
    {
        var movement = new MovementDefinition(
            FixedPoint.FromInt(4), FixedPoint.FromInt(4),
            FixedPoint.FromInt(3), FixedPoint.FromFraction(4, 1), FixedPoint.FromFraction(13, 2),
            FixedPoint.FromFraction(1, 2), FixedPoint.FromInt(3));

        FighterDefinition Empty(string id) => new(
            id, Array.Empty<CommandDefinition>(), Array.Empty<MoveDefinition>(), movement);

        return new BattleSimulation(new FightRoster(Empty("Kai"), Empty("Rei")));
    }

    private static BattleState HitstopDuel(
        int p1X, int p2X, int p1Hitstop, int p2Hitstop,
        Facing p1Facing = Facing.Right, Facing p2Facing = Facing.Left) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, p1Facing) with
            {
                HitstopRemainingFrames = p1Hitstop,
            },
            FighterState.Create(1, 1000, p2X, p2Facing) with
            {
                HitstopRemainingFrames = p2Hitstop,
            },
        },
        BattleState.CreateDefault().StateVersion);

    [Fact]
    public void P1B_OneFrozenOneMoving_FrozenKeepsX()
    {
        // 任务书场景 A：P1(0) 冻结、P2(60) 朝左前进 4 → 候选 56，缺口 4。
        // 旧 D73 对称分摊会把 P1 推到 −2；修复后 P1 保持 0，P2 回到 60。
        var sim = SeparationSim();
        BattleState state = HitstopDuel(0, 60, 3, 0);

        BattleState after = sim.Step(state, F(0, InputBits.None, InputBits.Left)).State;

        Assert.Equal(FixedPoint.Zero, after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), after.Fighter(1).PositionX);
        Assert.Equal(FixedPoint.FromInt(Separation),
            after.Fighter(1).PositionX - after.Fighter(0).PositionX);
        Assert.True(after.Fighter(0).PositionX < after.Fighter(1).PositionX, "不得地面穿越");
        Assert.Equal(2, after.Fighter(0).HitstopRemainingFrames);
        Assert.True(after.Fighter(0).PositionX >= FixedPoint.FromInt(-640));
        Assert.True(after.Fighter(1).PositionX <= FixedPoint.FromInt(640));
    }

    [Fact]
    public void P1B_MirrorWithP2Frozen_IsSymmetric()
    {
        // 场景 B 镜像：冻结方换到右侧（不能固定 P1 冻结优先权）。
        var sim = SeparationSim();
        BattleState state = HitstopDuel(-60, 0, 0, 3);

        BattleState after = sim.Step(state, F(0, InputBits.Right, InputBits.None)).State;

        Assert.Equal(FixedPoint.Zero, after.Fighter(1).PositionX);
        Assert.Equal(FixedPoint.FromInt(-60), after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(Separation),
            after.Fighter(1).PositionX - after.Fighter(0).PositionX);
        Assert.True(after.Fighter(0).PositionX < after.Fighter(1).PositionX);
        Assert.Equal(2, after.Fighter(1).HitstopRemainingFrames);
    }

    [Fact]
    public void P1B_BothFrozen_KeepPositionsAndDoNotRepeatContact()
    {
        var sim = SeparationSim();
        BattleState state = HitstopDuel(0, 60, 3, 3);

        BattleStepResult r = sim.Step(state, F(0, InputBits.None, InputBits.Left));

        Assert.Equal(FixedPoint.Zero, r.State.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), r.State.Fighter(1).PositionX);
        Assert.Equal(2, r.State.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(2, r.State.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(FighterLogicState.Standing, r.State.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Standing, r.State.Fighter(1).LogicState);
        Assert.Empty(r.Events);
    }

    [Fact]
    public void P1B_FrozenAtWallCorner_NoDriftNoOverflow()
    {
        // 场景 D：冻结方 580、未冻结方 640（贴右墙）持续尝试挤压。
        var sim = SeparationSim();
        BattleState state = HitstopDuel(580, 640, 10, 0);

        for (int k = 0; k < 5; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Left)).State;
            Assert.Equal(FixedPoint.FromInt(580), state.Fighter(0).PositionX);
            Assert.Equal(FixedPoint.FromInt(640), state.Fighter(1).PositionX);
            Assert.Equal(FixedPoint.FromInt(Separation),
                state.Fighter(1).PositionX - state.Fighter(0).PositionX);
            Assert.True(state.Fighter(1).PositionX <= FixedPoint.FromInt(640), "不得越过墙壁");
        }
    }

    [Fact]
    public void P1B_AirHitstunFrozen_NoIntegrationNoSeparation()
    {
        // 场景 E：空中受击 + Hitstop。冻结跳过空中积分，D94 让它也不参与地面推挤。
        var sim = SeparationSim();
        FighterState p1 = FighterState.Create(0, 1000, 0, Facing.Right) with
        {
            LogicState = FighterLogicState.AirHitstun,
            PositionY = FixedPoint.FromInt(20),
            VelocityX = FixedPoint.FromInt(2),
            VelocityY = FixedPoint.FromInt(6),
            LandingKnockdownFrames = 8,
            LandingWakeupFrames = 5,
            HitstopRemainingFrames = 3,
        };
        BattleState state = new(0,
            new[] { p1, FighterState.Create(1, 1000, 30, Facing.Left) },
            BattleState.CreateDefault().StateVersion);

        BattleState after = sim.Step(state, F(0, InputBits.None, InputBits.None)).State;

        Assert.Equal(FighterLogicState.AirHitstun, after.Fighter(0).LogicState);
        Assert.Equal(FixedPoint.Zero, after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(20), after.Fighter(0).PositionY);
        Assert.Equal(FixedPoint.FromInt(2), after.Fighter(0).VelocityX);
        Assert.Equal(FixedPoint.FromInt(6), after.Fighter(0).VelocityY);
        Assert.Equal(8, after.Fighter(0).LandingKnockdownFrames);
        Assert.Equal(5, after.Fighter(0).LandingWakeupFrames);
        Assert.Equal(2, after.Fighter(0).HitstopRemainingFrames);
    }

    [Fact]
    public void P1B_FrozenVersusKnockback_FrozenKeepsX()
    {
        // 场景 C：未冻结方携带击退速度（不是步行），仍由它承担全部缺口。
        var sim = SeparationSim();
        FighterState p2 = FighterState.Create(1, 1000, 60, Facing.Left) with
        {
            LogicState = FighterLogicState.Hitstun,
            StunRemainingFrames = 10,
            VelocityX = FixedPoint.FromInt(-4),
            KnockbackFriction = FixedPoint.FromInt(1),
        };
        BattleState state = new(0,
            new[]
            {
                FighterState.Create(0, 1000, 0, Facing.Right) with { HitstopRemainingFrames = 3 },
                p2,
            },
            BattleState.CreateDefault().StateVersion);

        BattleState after = sim.Step(state, F(0, InputBits.None, InputBits.None)).State;

        Assert.Equal(FixedPoint.Zero, after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), after.Fighter(1).PositionX);
        Assert.Equal(FixedPoint.FromInt(Separation),
            after.Fighter(1).PositionX - after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(-3), after.Fighter(1).VelocityX); // −4 按摩擦 1 向 0 衰减
    }

    [Fact]
    public void P1B_AfterHitstopEnds_SymmetricD73Resumes()
    {
        // 冻结结束后恢复既有 D73 对称分摊（逐位不变的行为）。
        var sim = SeparationSim();
        BattleState state = HitstopDuel(0, 60, 1, 0);

        BattleState frozenFrame = sim.Step(state, F(0, InputBits.None, InputBits.Left)).State;
        Assert.Equal(FixedPoint.Zero, frozenFrame.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), frozenFrame.Fighter(1).PositionX);
        Assert.Equal(0, frozenFrame.Fighter(0).HitstopRemainingFrames);

        BattleState resumed = sim.Step(frozenFrame, F(1, InputBits.None, InputBits.Left)).State;
        Assert.Equal(FixedPoint.FromInt(-2), resumed.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(58), resumed.Fighter(1).PositionX);
        Assert.Equal(FixedPoint.FromInt(Separation),
            resumed.Fighter(1).PositionX - resumed.Fighter(0).PositionX);
    }

    [Fact]
    public void P1B_NoHitstop_HeadOnSeparationUnchanged()
    {
        // 回归守门：双方都未冻结时，D73 求解器逐位沿用旧行为。
        var sim = SeparationSim();
        BattleState state = HitstopDuel(-30, 30, 0, 0);

        BattleState after = sim.Step(state, F(0, InputBits.Right, InputBits.Left)).State;

        Assert.Equal(FixedPoint.FromInt(-30), after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), after.Fighter(1).PositionX);
        Assert.Equal(FixedPoint.FromInt(Separation),
            after.Fighter(1).PositionX - after.Fighter(0).PositionX);
    }
}
