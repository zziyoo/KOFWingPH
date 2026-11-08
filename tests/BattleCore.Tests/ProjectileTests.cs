using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 08D（D99）：<b>飞行道具</b>——权威实体、独立运动、命中/防御/对撞与生命周期。
///
/// <para><b>时间线</b>：招式 Active 阶段、经过帧 == 出生帧的那一帧<b>只建立实体</b>
/// （不运动、不判定）；下一个逻辑帧开始按自身速度前进并参与命中。实体只依赖自己的快照，
/// 发射者进入收招/被击中都不影响它。</para>
/// </summary>
public class ProjectileTests
{
    private const string Fireball = "Fireball";
    private const string FireballType = "Fireball";
    private const string Jab = "Jab";

    private static BattleSimulation Sim(
        int lifetime = 30, int maxAlive = 1, int damage = 20, FixedPoint? speedX = null,
        int hitstop = 0, bool withJab = false, bool launches = false)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        AttackDefinition Hit() => new(
            offsetForward: FixedPoint.FromInt(40), offsetUp: FixedPoint.FromInt(50),
            halfWidth: FixedPoint.FromInt(10), halfHeight: FixedPoint.FromInt(10),
            height: AttackHeight.Mid, damage: damage, hitstunFrames: 15, blockstunFrames: 8,
            hitstopFrames: hitstop,
            groundKnockback: FixedPoint.FromInt(2),
            groundKnockbackFriction: FixedPoint.FromInt(1),
            launchVelocityX: launches ? FixedPoint.FromInt(2) : FixedPoint.Zero,
            launchVelocityY: launches ? FixedPoint.FromInt(6) : FixedPoint.Zero,
            knockdownFrames: launches ? 8 : 0,
            wakeupFrames: launches ? 5 : 0);

        FighterDefinition Build(string id) => new(
            id,
            withJab
                ? new[]
                {
                    new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick,
                        priority: 5, bufferLifetimeFrames: 24),
                    new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                        priority: 5, bufferLifetimeFrames: 24),
                }
                : new[]
                {
                    new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick,
                        priority: 5, bufferLifetimeFrames: 24),
                },
            withJab
                ? new[]
                {
                    new MoveDefinition(Fireball, "5C", 2, 2, 6,
                        projectiles: new[]
                        {
                            new ProjectileDefinition(FireballType, Hit(),
                                speedX: speedX ?? FixedPoint.FromInt(5),
                                lifetimeFrames: lifetime, maxAlive: maxAlive),
                        }),
                    new MoveDefinition(Jab, "5A", 1, 3, 4,
                        attacks: new[]
                        {
                            new AttackDefinition(
                                FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                                FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                                0, 0, AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10),
                        }),
                }
                : new[]
                {
                    new MoveDefinition(Fireball, "5C", 2, 2, 6,
                        projectiles: new[]
                        {
                            new ProjectileDefinition(FireballType, Hit(),
                                speedX: speedX ?? FixedPoint.FromInt(5),
                                lifetimeFrames: lifetime, maxAlive: maxAlive),
                        }),
                },
            movement);

        return new BattleSimulation(new FightRoster(Build("Kai"), Build("Rei")));
    }

    private static BattleState Duel(int p1X = -100, int p2X = 0, int p2Health = 1000) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, Facing.Right),
            FighterState.Create(1, 1000, p2X, Facing.Left) with { Health = p2Health },
        },
        BattleState.CreateDefault().StateVersion);

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    /// <summary>帧 0 按键，帧 2 进入 Active 并生成实体 —— 返回生成帧后的状态。</summary>
    private static BattleState FireOnce(BattleSimulation sim, BattleState state, bool p2Fires = false)
    {
        for (int f = 0; f < 3; f++)
        {
            InputBits p1 = f == 0 ? InputBits.LightKick : InputBits.None;
            InputBits p2 = p2Fires ? (f == 0 ? InputBits.LightKick : InputBits.None) : InputBits.None;
            state = sim.Step(state, F(state.Frame, p1, p2)).State;
        }

        return state;
    }

    // ─────────────────────────────────────────────────────────────
    // 1. 生成与运动
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Spawn_CreatesEntityWithOwnerTypeAndVelocity()
    {
        var sim = Sim();
        BattleState state = FireOnce(sim, Duel());

        ProjectileState projectile = Assert.Single(state.Projectiles);
        Assert.Equal(1, projectile.ProjectileId);
        Assert.Equal(0, projectile.OwnerFighterId);
        Assert.Equal(FireballType, projectile.TypeId);
        Assert.Equal(Fireball, projectile.SourceMoveId);

        // 出生位置 = 发射者 + 前向 40 / 向上 50；速度 = 朝向 × 5。
        Assert.Equal(-60, projectile.PositionX.ToIntTruncated());
        Assert.Equal(50, projectile.PositionY.ToIntTruncated());
        Assert.Equal(FixedPoint.FromInt(5), projectile.VelocityX);
        Assert.Equal(FixedPoint.Zero, projectile.VelocityY);
        Assert.Equal(30, projectile.RemainingLifetime);
        Assert.Equal(2, state.NextProjectileId);
    }

    [Fact]
    public void SpawnFrame_DoesNotMoveOrHit()
    {
        // 生成当帧只建立实体：位置仍是出生点（P1 在 -60 + 前向 40 = -20），目标不扣血
        // ——即使实体已经贴到对方受击框边缘也要等到下一帧才判定。
        var sim = Sim();
        BattleState state = FireOnce(sim, Duel(p1X: -60, p2X: 0));

        ProjectileState projectile = Assert.Single(state.Projectiles);
        Assert.Equal(-20, projectile.PositionX.ToIntTruncated());
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    [Fact]
    public void MovesAtFixedSpeedEveryFrame()
    {
        var sim = Sim();
        BattleState state = FireOnce(sim, Duel(p1X: -400, p2X: 400));

        int expected = -360;
        for (int k = 0; k < 10; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            expected += 5;
            Assert.Equal(expected, Assert.Single(state.Projectiles).PositionX.ToIntTruncated());
            Assert.Equal(30 - (k + 1), state.Projectiles[0].RemainingLifetime);
        }
    }

    [Fact]
    public void LifetimeExpiry_RemovesEntityExactly()
    {
        // 寿命 5：生成帧记 1 帧，之后 4 帧移动 → 第 5 帧后消失。
        var sim = Sim(lifetime: 5);
        BattleState state = FireOnce(sim, Duel(p1X: -400, p2X: 400));

        Assert.Equal(5, Assert.Single(state.Projectiles).RemainingLifetime);

        for (int k = 1; k <= 4; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            Assert.Single(state.Projectiles);
            Assert.Equal(5 - k, state.Projectiles[0].RemainingLifetime);
        }

        state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Empty(state.Projectiles);
    }

    [Fact]
    public void OutOfBounds_RemovesEntity()
    {
        // 从贴右墙处向右发射：出界即删除。
        var sim = Sim();
        BattleState state = Duel(p1X: 580, p2X: -200);
        state = FireOnce(sim, state);

        for (int k = 0; k < 20 && state.Projectiles.Count > 0; k++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Empty(state.Projectiles);
    }

    [Fact]
    public void Entity_SurvivesOwnerRecoveryAndIdle()
    {
        var sim = Sim(lifetime: 60);
        BattleState state = FireOnce(sim, Duel(p1X: -400, p2X: 400));

        // 发射者走完 Recovery 回到待机，实体仍在飞。
        for (int k = 0; k < 12; k++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Equal(FighterLogicState.Standing, state.Fighter(0).LogicState);
        Assert.Single(state.Projectiles);
    }

    [Fact]
    public void MaxAlive_BlocksExtraSpawns()
    {
        var sim = Sim(lifetime: 60, maxAlive: 1);
        BattleState state = Duel(p1X: -400, p2X: 400);

        state = FireOnce(sim, state);
        // 等回站立再发一次。
        while (state.Fighter(0).LogicState != FighterLogicState.Standing)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        state = FireOnce(sim, state);

        Assert.Single(state.Projectiles);
        Assert.Equal(2, state.NextProjectileId); // 第二次没有分配新 ID
    }

    [Fact]
    public void MaxAliveTwo_AllowsTwoEntities()
    {
        var sim = Sim(lifetime: 60, maxAlive: 2);
        BattleState state = Duel(p1X: -400, p2X: 400);

        state = FireOnce(sim, state);
        while (state.Fighter(0).LogicState != FighterLogicState.Standing)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        state = FireOnce(sim, state);

        Assert.Equal(2, state.Projectiles.Count);
        Assert.Equal(new[] { 1, 2 }, new[] { state.Projectiles[0].ProjectileId, state.Projectiles[1].ProjectileId });
        Assert.Equal(3, state.NextProjectileId);
    }

    [Fact]
    public void BothSidesFireSameFrame_GetDistinctIds()
    {
        var sim = Sim(lifetime: 60);
        BattleState state = Duel(p1X: -400, p2X: 400);
        state = FireOnce(sim, state, p2Fires: true);

        Assert.Equal(2, state.Projectiles.Count);
        Assert.Equal(0, state.Projectiles[0].OwnerFighterId);
        Assert.Equal(1, state.Projectiles[1].OwnerFighterId);
        Assert.NotEqual(state.Projectiles[0].ProjectileId, state.Projectiles[1].ProjectileId);
        Assert.Equal(3, state.NextProjectileId);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 命中与防御
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void HitsStandingTarget_DealsDamageAndDisappears()
    {
        var sim = Sim();
        BattleState state = FireOnce(sim, Duel());

        int frames = 0;
        while (state.Projectiles.Count > 0 && frames++ < 60)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Empty(state.Projectiles);
        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(1).LogicState);
        Assert.Equal(15, state.Fighter(1).StunRemainingFrames);
    }

    [Fact]
    public void Hit_EmitsProjectileHitWithAppliedDamage()
    {
        var sim = Sim();
        BattleState state = FireOnce(sim, Duel());
        BattleEvent? hit = null;

        for (int k = 0; k < 60 && hit is null; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None));
            state = r.State;
            foreach (BattleEvent e in r.Events)
                if (e.Type == BattleEventType.ProjectileHit)
                    hit = e;
        }

        Assert.NotNull(hit);
        Assert.Equal((0, 1, FireballType, 20),
            (hit!.Value.SourceFighterId, hit.Value.TargetFighterId, hit.Value.MoveId, hit.Value.Amount));
    }

    [Fact]
    public void Blocked_NoDamage_ProjectileDisappears()
    {
        var sim = Sim();
        BattleState state = FireOnce(sim, Duel());
        BattleEvent? blocked = null;

        for (int k = 0; k < 60 && blocked is null; k++)
        {
            // P2 朝左，「后」= 世界 +X。
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right));
            state = r.State;
            foreach (BattleEvent e in r.Events)
                if (e.Type == BattleEventType.ProjectileBlocked)
                    blocked = e;
        }

        Assert.NotNull(blocked);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);
        Assert.Empty(state.Projectiles);
    }

    [Fact]
    public void HitsAirborneTarget_EntersAirHitstun()
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
        }, state.StateVersion, state.Round, state.Projectiles, state.NextProjectileId);

        state = FireOnce(sim, state);
        for (int k = 0; k < 60 && state.Projectiles.Count > 0; k++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);
    }

    [Fact]
    public void LaunchingProjectile_PutsTargetIntoAirHitstun()
    {
        var sim = Sim(launches: true);
        BattleState state = FireOnce(sim, Duel());
        for (int k = 0; k < 60 && state.Projectiles.Count > 0; k++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.FromInt(6), state.Fighter(1).VelocityY);
        Assert.Equal(8, state.Fighter(1).LandingKnockdownFrames);
    }

    [Fact]
    public void DoesNotHitOwner()
    {
        // 发射者贴着自己的出生点站着：实体只认「另一个角色」。
        var sim = Sim(lifetime: 60);
        BattleState state = FireOnce(sim, Duel(p1X: -400, p2X: 400));

        for (int k = 0; k < 10; k++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Single(state.Projectiles);
    }

    [Fact]
    public void ProjectileThatKills_ResolvesKo()
    {
        var sim = Sim();
        BattleState state = FireOnce(sim, Duel(p2Health: 15));
        for (int k = 0; k < 60 && state.Projectiles.Count > 0; k++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Equal(0, state.Fighter(1).Health);
        Assert.True(state.Round.IsFinished);
        Assert.Equal(RoundOutcome.P1Win, state.Round.Outcome);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 对撞与多来源
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TwoProjectilesCollide_BothDisappear_NoDamage()
    {
        var sim = Sim(lifetime: 200);
        BattleState state = Duel(p1X: -200, p2X: 200);
        state = FireOnce(sim, state, p2Fires: true);
        Assert.Equal(2, state.Projectiles.Count);

        int clashes = 0;
        for (int k = 0; k < 120 && state.Projectiles.Count > 0; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None));
            state = r.State;
            clashes += r.Events.Count(e => e.Type == BattleEventType.ProjectileClash);
        }

        Assert.Equal(1, clashes);
        Assert.Empty(state.Projectiles);
        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    [Theory]
    [InlineData(-200, 200, 5, -5)]
    [InlineData(-300, 180, 7, -3)]
    public void ClashAnchors_UseMovedProjectilePositions(int left, int right, int vx, int otherVx)
    {
        var sim = Sim(lifetime: 200, maxAlive: 2);
        BattleState source = FireOnce(sim, Duel(p1X: left, p2X: right), p2Fires: true);
        var a = source.Projectiles[0] with { PositionX = 90, VelocityX = vx };
        var b = source.Projectiles[1] with { PositionX = 100, VelocityX = otherVx };
        var c = a with { ProjectileId = 3, PositionX = -100 };
        var d = b with { ProjectileId = 4, PositionX = -90 };
        var state = new BattleState(source.Frame, source.Fighters, source.StateVersion,
            source.Round, new[] { a, b, c, d }, 5);
        var result = sim.Step(state, FrameInput.Empty(state.Frame));
        var clashes = result.Events.Where(e => e.Type == BattleEventType.ProjectileClash).ToArray();
        Assert.Equal(2, clashes.Length);
        Assert.Equal(2, result.EventAnchors.Count);
        for (int j = 0; j < 2; j++)
        {
            var anchor = result.EventAnchors[j];
            Assert.Equal(clashes[j].EventId, anchor.EventId);
            var first = j == 0 ? a : c;
            var second = j == 0 ? b : d;
            Assert.Equal((first.PositionX + first.VelocityX + second.PositionX + second.VelocityX) / FixedPoint.FromInt(2), anchor.X);
            Assert.Equal(first.PositionY, anchor.Y);
            var frame = new PresentationFrame(1, state.Frame, state, result.State, result.Events, result.EventAnchors);
            var director = new FeedbackDirector();
            Assert.True(director.TryConsume(1, clashes[j], out var cue));
            Assert.Equal((anchor.X.ToDouble(), anchor.Y.ToDouble()), FeedbackGeometry.Anchor(cue, frame));
        }
        Assert.Empty(result.State.Projectiles);
    }

    [Fact]
    public void SameOwnerProjectiles_DoNotCollide()
    {
        var sim = Sim(lifetime: 200, maxAlive: 2);
        BattleState state = Duel(p1X: -400, p2X: 400);
        state = FireOnce(sim, state);
        while (state.Fighter(0).LogicState != FighterLogicState.Standing)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        state = FireOnce(sim, state);

        for (int k = 0; k < 5; k++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Equal(2, state.Projectiles.Count);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 快照约束与确定性
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProjectileCollection_IsReadOnly()
    {
        var sim = Sim();
        BattleState state = FireOnce(sim, Duel());

        Assert.NotNull(state.Projectiles);
        Assert.IsNotAssignableFrom<List<ProjectileState>>(state.Projectiles);
        Assert.IsNotAssignableFrom<ProjectileState[]>(state.Projectiles);

        IList<ProjectileState> list = (IList<ProjectileState>)state.Projectiles;
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }

    [Fact]
    public void BattleState_RejectsDuplicateProjectileIds()
    {
        ProjectileState p = new(1, 0, FireballType, Fireball,
            FixedPoint.Zero, FixedPoint.FromInt(50), FixedPoint.FromInt(5), FixedPoint.Zero, 10);

        Assert.Throws<ArgumentException>(() => new BattleState(
            0, new[] { FighterState.Create(0, 1000, 0, Facing.Right), FighterState.Create(1, 1000, 100, Facing.Left) },
            BattleState.CreateDefault().StateVersion, null, new[] { p, p }, 2));

        Assert.Throws<ArgumentException>(() => new BattleState(
            0, new[] { FighterState.Create(0, 1000, 0, Facing.Right), FighterState.Create(1, 1000, 100, Facing.Left) },
            BattleState.CreateDefault().StateVersion, null, new[] { p }, 1)); // 分配器未越过已有 ID
    }

    [Fact]
    public void Definition_RejectsIllegalConfiguration()
    {
        static AttackDefinition Hit() => new(
            FixedPoint.FromInt(40), FixedPoint.FromInt(50),
            FixedPoint.FromInt(10), FixedPoint.FromInt(10));

        Assert.Throws<ArgumentException>(() =>
            new ProjectileDefinition(" ", Hit(), FixedPoint.FromInt(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectileDefinition("P", Hit(), FixedPoint.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectileDefinition("P", Hit(), FixedPoint.FromInt(ProjectileDefinition.MaxSpeedUnits + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectileDefinition("P", Hit(), FixedPoint.FromInt(5), lifetimeFrames: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectileDefinition("P", Hit(), FixedPoint.FromInt(5), maxAlive: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectileDefinition("P", Hit(), FixedPoint.FromInt(5), spawnAtActiveFrame: -1));
    }

    [Fact]
    public void MoveDefinition_RejectsInvalidProjectileSpawn()
    {
        static ProjectileDefinition Projectile() => new(
            "P",
            new AttackDefinition(FixedPoint.FromInt(40), FixedPoint.FromInt(50),
                FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            FixedPoint.FromInt(5));

        // Active 只有 2 帧：出生帧 2 越界。
        Assert.Throws<ArgumentException>(() => new MoveDefinition(
            "F", "5C", 2, 2, 6,
            projectiles: new[]
            {
                new ProjectileDefinition("P",
                    new AttackDefinition(FixedPoint.FromInt(40), FixedPoint.FromInt(50),
                        FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
                    FixedPoint.FromInt(5), spawnAtActiveFrame: 2),
            }));

        // 同一招式内类型 ID 重复。
        Assert.Throws<ArgumentException>(() => new MoveDefinition(
            "F", "5C", 2, 2, 6, projectiles: new[] { Projectile(), Projectile() }));
    }

    [Fact]
    public void LongRun_10000Frames_IsDeterministic()
    {
        static (BattleState state, List<int> eventIds) Run()
        {
            var sim = Sim(lifetime: 90, maxAlive: 2);
            BattleState state = Duel(p1X: -200, p2X: 200);
            var eventIds = new List<int>();

            for (int f = 0; f < 10000; f++)
            {
                InputBits p1 = f % 97 == 0 ? InputBits.LightKick : InputBits.None;
                InputBits p2 = f % 131 == 0 ? InputBits.LightKick : InputBits.None;
                BattleStepResult r = sim.Step(state, new FrameInput(f, p1, p2));
                state = r.State;
                eventIds.AddRange(r.Events.Select(e => e.EventId));
            }

            return (state, eventIds);
        }

        (BattleState a, List<int> aEvents) = Run();
        (BattleState b, List<int> bEvents) = Run();

        Assert.Equal(a.Fighter(0), b.Fighter(0));
        Assert.Equal(a.Fighter(1), b.Fighter(1));
        Assert.Equal(a.Projectiles, b.Projectiles);
        Assert.Equal(a.NextProjectileId, b.NextProjectileId);
        Assert.Equal(a.Round, b.Round);
        Assert.Equal(aEvents, bEvents);
        Assert.Equal(aEvents.Count, aEvents.Distinct().Count());
    }

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
        BattleState full = FireOnce(simA, Duel());
        full = Advance(simA, full, 40);

        var simB = Sim();
        BattleState head = FireOnce(simB, Duel());
        head = Advance(simB, head, 8);
        BattleState resumed = Advance(simB, head, 40);

        Assert.Equal(full.Fighter(0), resumed.Fighter(0));
        Assert.Equal(full.Fighter(1), resumed.Fighter(1));
        Assert.Equal(full.Projectiles, resumed.Projectiles);
        Assert.Equal(full.NextProjectileId, resumed.NextProjectileId);
        Assert.Equal(full.Round, resumed.Round);
    }
}
