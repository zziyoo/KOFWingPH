using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 08 最终专项补修（二）：
/// <list type="bullet">
/// <item><b>P2-A / D102</b>：StrikeClash 的状态改写必须发生在<b>结算前事件快照之后</b>，
///       否则首个 Active 帧相杀时两条 AttackActive 会同时丢失。</item>
/// <item><b>P2-B / D103</b>：飞行道具被成功防御时同样要应用配置停顿（只作用于目标）。</item>
/// <item><b>场景 A–F</b>：四项补修的跨系统组合回归。</item>
/// </list>
/// </summary>
public class Stage08EventTests
{
    private const string PokeA = "PokeA";
    private const string PokeB = "PokeB";
    private const string Fireball = "Fireball";
    private const string Ball = "Ball";

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    private static BattleState Duel(int p1X, int p2X, int p1Health = 1000, int p2Health = 1000) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, Facing.Right) with { Health = p1Health },
            FighterState.Create(1, 1000, p2X, Facing.Left) with { Health = p2Health },
        },
        BattleState.CreateDefault().StateVersion);

    // ─────────────────────────────────────────────────────────────
    // P2-A：双方不同 MoveId、攻击框正面积重叠但打不到身体
    // ─────────────────────────────────────────────────────────────

    private static AttackDefinition PokeBox(int firstActiveFrame, int hitstop, int offsetForward = 45) => new(
        offsetForward: FixedPoint.FromInt(offsetForward), offsetUp: FixedPoint.FromInt(60),
        halfWidth: FixedPoint.FromInt(20), halfHeight: FixedPoint.FromInt(20),
        firstActiveFrame: firstActiveFrame, lastActiveFrame: int.MaxValue,
        height: AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10,
        hitstopFrames: hitstop);

    private static BattleSimulation ClashSim(
        int firstActiveFrame = 0, int hitstop = 0, bool staggered = false)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition Build(string id, string moveId, bool p1) => new(id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[]
            {
                new MoveDefinition(moveId, "5A", 1, 3, 4,
                    attacks: staggered && p1
                        ? new[] { PokeBox(0, hitstop), PokeBox(1, hitstop, offsetForward: 110) }
                        : new[] { PokeBox(firstActiveFrame, hitstop) }),
            },
            movement);

        return new BattleSimulation(new FightRoster(
            Build("Kai", PokeA, p1: true), Build("Rei", PokeB, p1: false)));
    }

    private static (BattleState State, BattleStepResult Result) BothPoke(
        BattleSimulation sim, BattleState state)
    {
        state = sim.Step(state, F(state.Frame, InputBits.LightPunch, InputBits.LightPunch)).State;
        BattleStepResult active = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
        return (active.State, active);
    }

    [Fact]
    public void P2A_FirstActiveFrameClash_EmitsBothAttackActive()
    {
        // 修复前实测：ApplyStrikeClash 先把双方改成 Recovery，CollectAttackActive 只认 Active
        // → 两条 AttackActive 全丢；本测试的第一条断言即失败。
        var sim = ClashSim();
        (BattleState state, BattleStepResult result) = BothPoke(sim, Duel(-60, 60));

        BattleEvent[] actives = result.Events
            .Where(e => e.Type == BattleEventType.AttackActive).ToArray();

        Assert.Equal(2, actives.Length);
        Assert.Equal(new[] { PokeA, PokeB }, new[] { actives[0].MoveId, actives[1].MoveId });
        Assert.Equal(new[] { 0, 1 }, new[] { actives[0].SourceFighterId, actives[1].SourceFighterId });

        Assert.Single(result.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.Hit);
        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(1).LogicState);

        // 事件阶段顺序：MoveStarted → AttackActive → StrikeClash（EventId 严格递增）。
        int lastAttackActiveId = actives[^1].EventId;
        BattleEvent clash = Assert.Single(result.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.True(lastAttackActiveId < clash.EventId);
        Assert.All(result.Events, e => Assert.Equal(e.Frame * BattleEvent.MaxEventsPerFrame
            + (e.EventId % BattleEvent.MaxEventsPerFrame), e.EventId));

        // 下一帧不重复产生 AttackActive 或 StrikeClash。
        BattleStepResult next = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
        Assert.DoesNotContain(next.Events, e => e.Type == BattleEventType.AttackActive);
        Assert.DoesNotContain(next.Events, e => e.Type == BattleEventType.StrikeClash);
    }

    [Fact]
    public void P2A_SecondActiveFrameClash_AlsoEmitsAttackActive()
    {
        // 攻击框只在 Active 第 1 帧（第二个生效帧）出现：首个 Active 帧无框、不相杀。
        var sim = ClashSim(firstActiveFrame: 1);
        (BattleState state, BattleStepResult first) = BothPoke(sim, Duel(-60, 60));

        Assert.DoesNotContain(first.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.DoesNotContain(first.Events, e => e.Type == BattleEventType.AttackActive);

        BattleStepResult second = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));

        Assert.Equal(2, second.Events.Count(e => e.Type == BattleEventType.AttackActive));
        Assert.Contains(second.Events, e => e.Type == BattleEventType.AttackActive && e.MoveId == PokeA);
        Assert.Contains(second.Events, e => e.Type == BattleEventType.AttackActive && e.MoveId == PokeB);
        Assert.Single(second.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(FighterLogicState.Recovery, second.State.Fighter(0).LogicState);
    }

    [Fact]
    public void P2A_ClashWithHitstop_NoRepeatedEventsDuringFreeze()
    {
        var sim = ClashSim(hitstop: 4);
        (BattleState state, BattleStepResult clash) = BothPoke(sim, Duel(-60, 60));

        Assert.Equal(2, clash.Events.Count(e => e.Type == BattleEventType.AttackActive));
        Assert.Single(clash.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(4, state.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(4, state.Fighter(1).HitstopRemainingFrames);

        for (int k = 0; k < 6; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
            state = r.State;
            Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.AttackActive);
            Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.StrikeClash);
        }
    }

    [Fact]
    public void P2A_MutualHit_IsTradeNotClash_AndStillEmitsAttackActive()
    {
        // 回归：d=60 双方真正打中身体 → D80 互中，不判相杀。
        var sim = ClashSim();
        (BattleState state, BattleStepResult result) = BothPoke(sim, Duel(-30, 30));

        Assert.Equal(990, state.Fighter(0).Health);
        Assert.Equal(990, state.Fighter(1).Health);
        Assert.Equal(2, result.Events.Count(e => e.Type == BattleEventType.Hit));
        Assert.Equal(2, result.Events.Count(e => e.Type == BattleEventType.AttackActive));
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.StrikeClash);
    }

    [Fact]
    public void P2A_StaggeredAttackWindows_OneAttackActivePerInstance()
    {
        // 回归：多攻击定义错开生效窗口，同一实例仍只有一条 AttackActive。
        var sim = ClashSim(staggered: true);
        BattleState state = Duel(-200, 60); // 远到打不到身体，P2 也不出招 → 无相杀
        state = sim.Step(state, F(0, InputBits.LightPunch)).State;

        int actives = 0;
        for (int k = 0; k < 10; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
            state = r.State;
            actives += r.Events.Count(e => e.Type == BattleEventType.AttackActive);
        }

        Assert.Equal(1, actives);
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    [Fact]
    public void P2A_MirrorClash_IsSymmetric()
    {
        var sim = ClashSim();
        BattleState state = Duel(60, -60);
        state = new BattleState(0, new[]
        {
            state.Fighter(0) with { Facing = Facing.Left },
            state.Fighter(1) with { Facing = Facing.Right },
        }, state.StateVersion, state.Round);

        (state, BattleStepResult result) = BothPoke(sim, state);

        Assert.Equal(2, result.Events.Count(e => e.Type == BattleEventType.AttackActive));
        Assert.Equal(new[] { PokeA, PokeB },
            result.Events.Where(e => e.Type == BattleEventType.AttackActive)
                .Select(e => e.MoveId).ToArray());
        Assert.Single(result.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    [Fact]
    public void P2A_MidSnapshotReplay_IsIdentical()
    {
        static BattleState Advance(BattleSimulation sim, BattleState state, int toFrame)
        {
            while (state.Frame < toFrame)
                state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;
            return state;
        }

        var simA = ClashSim();
        BattleState full = Duel(-60, 60);
        full = simA.Step(full, F(0, InputBits.LightPunch, InputBits.LightPunch)).State;
        full = Advance(simA, full, 20);

        var simB = ClashSim();
        BattleState head = Duel(-60, 60);
        head = simB.Step(head, F(0, InputBits.LightPunch, InputBits.LightPunch)).State;
        head = Advance(simB, head, 3);
        BattleState resumed = Advance(simB, head, 20);

        Assert.Equal(full.Fighter(0), resumed.Fighter(0));
        Assert.Equal(full.Fighter(1), resumed.Fighter(1));
        Assert.Equal(full.Round, resumed.Round);
    }

    // ─────────────────────────────────────────────────────────────
    // P2-B：飞行道具命中 / 防御停顿
    // ─────────────────────────────────────────────────────────────

    private static BattleSimulation ProjectileSim(
        int hitstop = 0, int damage = 20, int p1OffsetUp = 50, int p2OffsetUp = 50)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        AttackDefinition Hit(int offsetUp) => new(
            offsetForward: FixedPoint.FromInt(40), offsetUp: FixedPoint.FromInt(offsetUp),
            halfWidth: FixedPoint.FromInt(10), halfHeight: FixedPoint.FromInt(10),
            height: AttackHeight.Mid, damage: damage, hitstunFrames: 15, blockstunFrames: 8,
            groundKnockback: FixedPoint.FromInt(2),
            groundKnockbackFriction: FixedPoint.FromInt(1),
            hitstopFrames: hitstop);

        FighterDefinition Build(string id, int offsetUp) => new(id,
            new[]
            {
                new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[]
            {
                new MoveDefinition(Fireball, "5C", 2, 2, 6,
                    projectiles: new[]
                    {
                        new ProjectileDefinition(Ball, Hit(offsetUp),
                            speedX: FixedPoint.FromInt(5), lifetimeFrames: 120),
                    }),
            },
            movement);

        return new BattleSimulation(new FightRoster(
            Build("Kai", p1OffsetUp), Build("Rei", p2OffsetUp)));
    }

    /// <summary>帧 0 按键、帧 2 进入 Active 并生成实体。</summary>
    private static BattleState FireOnce(BattleSimulation sim, BattleState state, bool bothFire = false)
    {
        for (int f = 0; f < 3; f++)
        {
            InputBits p1 = f == 0 ? InputBits.LightKick : InputBits.None;
            InputBits p2 = bothFire && f == 0 ? InputBits.LightKick : InputBits.None;
            state = sim.Step(state, F(state.Frame, p1, p2)).State;
        }
        return state;
    }

    /// <summary>推进到某种飞行道具事件首次出现的那一帧。</summary>
    private static (BattleState State, BattleStepResult Result) Until(
        BattleSimulation sim, BattleState state, BattleEventType type, bool p2Guards = false)
    {
        for (int k = 0; k < 80; k++)
        {
            InputBits p2 = p2Guards ? InputBits.Right : InputBits.None; // P2 朝左，「后」= 世界 +X
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None, p2));
            state = r.State;
            if (r.Events.Any(e => e.Type == type))
                return (state, r);
        }
        throw new Xunit.Sdk.XunitException($"80 帧内未出现 {type} 事件。");
    }

    [Fact]
    public void P2B_ProjectileBlocked_AppliesHitstop_ThreeFrameTimeline()
    {
        // 修复前实测：防御分支提前 return，P2.HitstopRemainingFrames 恒为 0。
        var sim = ProjectileSim(hitstop: 3);
        BattleState state = FireOnce(sim, Duel(-100, 0));
        (state, BattleStepResult blockedFrame) = Until(sim, state,
            BattleEventType.ProjectileBlocked, p2Guards: true);

        BattleEvent blocked = Assert.Single(blockedFrame.Events,
            e => e.Type == BattleEventType.ProjectileBlocked);
        Assert.Equal((0, 1, Ball, 8),
            (blocked.SourceFighterId, blocked.TargetFighterId, blocked.MoveId, blocked.Amount));

        Assert.Equal(1000, state.Fighter(1).Health);                    // ① 不扣血
        Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState); // ②
        Assert.Equal(3, state.Fighter(1).HitstopRemainingFrames);       // ③
        Assert.Equal(0, state.Fighter(0).HitstopRemainingFrames);       // ④ 发射者不被冻结
        Assert.Empty(state.Projectiles);                               // ⑤ 道具消失
        Assert.DoesNotContain(blockedFrame.Events, e => e.Type == BattleEventType.ProjectileHit); // ⑩

        int blockstun = state.Fighter(1).StunRemainingFrames;
        Assert.Equal(8, blockstun);

        // ⑥ ⑦ 后续三帧冻结：硬直不倒计时、停顿逐帧 −1、不重复发事件。
        for (int k = 1; k <= 3; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right));
            state = r.State;
            Assert.Equal(3 - k, state.Fighter(1).HitstopRemainingFrames);
            Assert.Equal(blockstun, state.Fighter(1).StunRemainingFrames);
            Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.ProjectileBlocked);
        }

        // ⑧ 第四帧恢复 Blockstun 倒计时。
        BattleStepResult resume = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right));
        Assert.Equal(blockstun - 1, resume.State.Fighter(1).StunRemainingFrames);
        Assert.Equal(0, resume.State.Fighter(1).HitstopRemainingFrames);
        Assert.DoesNotContain(resume.Events, e => e.Type == BattleEventType.ProjectileBlocked);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void P2B_BlockedHitstop_ExactlyNFrames(int n)
    {
        var sim = ProjectileSim(hitstop: n);
        BattleState state = FireOnce(sim, Duel(-100, 0));
        (state, _) = Until(sim, state, BattleEventType.ProjectileBlocked, p2Guards: true);

        Assert.Equal(n, state.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(0, state.Fighter(0).HitstopRemainingFrames);

        for (int k = 1; k <= n; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;
            Assert.Equal(n - k, state.Fighter(1).HitstopRemainingFrames);
            Assert.Equal(8, state.Fighter(1).StunRemainingFrames);
        }

        state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;
        Assert.Equal(7, state.Fighter(1).StunRemainingFrames);
    }

    [Fact]
    public void P2B_ProjectileHit_AlsoFreezesOnlyTarget()
    {
        var sim = ProjectileSim(hitstop: 3);
        BattleState state = FireOnce(sim, Duel(-100, 0));
        (state, BattleStepResult hitFrame) = Until(sim, state, BattleEventType.ProjectileHit);

        Assert.Equal(3, state.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(0, state.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Single(hitFrame.Events, e => e.Type == BattleEventType.ProjectileHit);
        Assert.Empty(state.Projectiles);
    }

    [Fact]
    public void P2B_ExistingLongerHitstop_IsNotReduced()
    {
        // 单帧可控构造：目标已有更长停顿（初态注入 8），一枚近身弹在同一帧被成功防御。
        // 冻结帧按 D96 先 −1 变 7，远程停顿 3 帧用 max 合入，绝不覆盖成更小值。
        var sim = ProjectileSim(hitstop: 3);
        ProjectileState incoming = new(
            ProjectileId: 1,
            OwnerFighterId: 0,
            TypeId: Ball,
            SourceMoveId: Fireball,
            PositionX: FixedPoint.FromInt(-40),
            PositionY: FixedPoint.FromInt(50),
            VelocityX: FixedPoint.FromInt(5),
            VelocityY: FixedPoint.Zero,
            RemainingLifetime: 10);

        BattleState state = new(0, new[]
        {
            FighterState.Create(0, 1000, -100, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with { HitstopRemainingFrames = 8 },
        }, BattleState.CreateDefault().StateVersion, null, new[] { incoming }, 2);

        BattleStepResult r = sim.Step(state, F(0, InputBits.None, InputBits.Right));

        Assert.Single(r.Events, e => e.Type == BattleEventType.ProjectileBlocked);
        Assert.Equal(7, r.State.Fighter(1).HitstopRemainingFrames); // max(8 − 1, 3)
        Assert.True(r.State.Fighter(1).HitstopRemainingFrames >= 3);
        Assert.Equal(0, r.State.Fighter(0).HitstopRemainingFrames);
        Assert.Empty(r.State.Projectiles);
    }

    [Fact]
    public void P2B_FrozenFrames_DoNotConsumeBufferedInput()
    {
        var sim = ProjectileSim(hitstop: 3);
        BattleState state = FireOnce(sim, Duel(-100, 0));
        (state, _) = Until(sim, state, BattleEventType.ProjectileBlocked, p2Guards: true);

        for (int k = 0; k < 3; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None,
                InputBits.Right | InputBits.LightKick));
            state = r.State;
            Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.MoveStarted);
            Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.ProjectileBlocked);
        }
    }

    [Fact]
    public void P2B_BlockstunEndsExactlyAfterHitstop()
    {
        var sim = ProjectileSim(hitstop: 3);
        BattleState state = FireOnce(sim, Duel(-100, 0));
        (state, _) = Until(sim, state, BattleEventType.ProjectileBlocked, p2Guards: true);

        // 3 帧停顿 + 8 帧 Blockstun；逐帧推进直到回到 Standing，记录帧数。
        int frames = 0;
        while (state.Fighter(1).LogicState != FighterLogicState.Standing && frames < 40)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;
            frames++;
        }

        Assert.Equal(11, frames);
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
        Assert.Equal(0, state.Fighter(1).HitstopRemainingFrames);
    }

    [Fact]
    public void P2B_AppliedDamage_MatchesEventAmount()
    {
        var sim = ProjectileSim(hitstop: 2, damage: 20);
        BattleState state = FireOnce(sim, Duel(-100, 0, p2Health: 15));
        (state, BattleStepResult hitFrame) = Until(sim, state, BattleEventType.ProjectileHit);

        Assert.Equal(0, state.Fighter(1).Health);
        BattleEvent hit = Assert.Single(hitFrame.Events, e => e.Type == BattleEventType.ProjectileHit);
        Assert.Equal(15, hit.Amount); // 实扣 15，不是配置伤害 20
    }

    [Fact]
    public void P2B_MidSnapshotReplay_IsIdentical()
    {
        static BattleState Advance(BattleSimulation sim, BattleState state, int toFrame, bool guard)
        {
            while (state.Frame < toFrame)
            {
                InputBits p2 = guard ? InputBits.Right : InputBits.None;
                state = sim.Step(state, F(state.Frame, InputBits.None, p2)).State;
            }
            return state;
        }

        var simA = ProjectileSim(hitstop: 3);
        BattleState full = FireOnce(simA, Duel(-100, 0));
        full = Advance(simA, full, 30, guard: true);

        var simB = ProjectileSim(hitstop: 3);
        BattleState head = FireOnce(simB, Duel(-100, 0));
        head = Advance(simB, head, 6, guard: true);
        BattleState resumed = Advance(simB, head, 30, guard: true);

        Assert.Equal(full.Fighter(0), resumed.Fighter(0));
        Assert.Equal(full.Fighter(1), resumed.Fighter(1));
        Assert.Equal(full.Projectiles, resumed.Projectiles);
        Assert.Equal(full.NextProjectileId, resumed.NextProjectileId);
        Assert.Equal(full.Round, resumed.Round);
    }

    // ─────────────────────────────────────────────────────────────
    // 场景 E / F：防御远程攻击、同时 KO、事件提交后投递
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioE_BlockedProjectile_TargetOnlyHitstop_NoDamage()
    {
        var sim = ProjectileSim(hitstop: 5);
        BattleState state = FireOnce(sim, Duel(-100, 0));
        (state, BattleStepResult r) = Until(sim, state,
            BattleEventType.ProjectileBlocked, p2Guards: true);

        Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);
        Assert.Equal(5, state.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Equal(0, state.Fighter(0).HitstopRemainingFrames);
        Assert.Empty(state.Projectiles);
        Assert.Single(r.Events, e => e.Type == BattleEventType.ProjectileBlocked);
    }

    [Fact]
    public void ScenarioF_MeleeDoubleKO_RoundEndedOnce_AndFrozenAfterwards()
    {
        var sim = ClashSim();
        BattleState state = Duel(-30, 30, p1Health: 10, p2Health: 10);
        (state, BattleStepResult result) = BothPoke(sim, state);

        Assert.True(state.Round.IsDoubleKO);
        Assert.Equal(RoundOutcome.DoubleKO, state.Round.Outcome);
        Assert.Single(result.Events, e => e.Type == BattleEventType.DoubleKnockout);
        Assert.Single(result.Events, e => e.Type == BattleEventType.RoundEnded);

        // 终局后全体冻结：角色逐位不变、无新事件。
        BattleStepResult frozen = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
        Assert.Equal(state.Fighter(0), frozen.State.Fighter(0));
        Assert.Equal(state.Fighter(1), frozen.State.Fighter(1));
        Assert.Empty(frozen.Events);
    }

    [Fact]
    public void ScenarioF_ProjectileDoubleKO_BothHitsSameFrame()
    {
        // 两枚弹用不同高度避免 P×P 抵消；对称距离 → 同帧各自命中对方并双 KO。
        var sim = ProjectileSim(damage: 20, p1OffsetUp: 50, p2OffsetUp: 90);
        BattleState state = FireOnce(sim, Duel(-100, 100, p1Health: 20, p2Health: 20), bothFire: true);

        BattleStepResult? koFrame = null;
        for (int k = 0; k < 80 && koFrame is null; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
            state = r.State;
            if (r.Events.Any(e => e.Type == BattleEventType.RoundEnded))
                koFrame = r;
        }

        Assert.NotNull(koFrame);
        Assert.True(state.Round.IsDoubleKO);
        Assert.Equal(2, koFrame!.Events.Count(e => e.Type == BattleEventType.ProjectileHit));
        Assert.Single(koFrame.Events, e => e.Type == BattleEventType.DoubleKnockout);
        Assert.Single(koFrame.Events, e => e.Type == BattleEventType.RoundEnded);

        BattleStepResult frozen = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
        Assert.Equal(state.Fighter(0), frozen.State.Fighter(0));
        Assert.Equal(state.Fighter(1), frozen.State.Fighter(1));
        Assert.Equal(state.Projectiles, frozen.State.Projectiles);
        Assert.Empty(frozen.Events);
    }

    [Fact]
    public void ScenarioF_EventsDeliveredOnlyAfterCommit_EventIdsUnique()
    {
        var sim = ClashSim();
        var collected = new List<BattleEvent>();
        var session = new BattleSession(
            initialState: Duel(-60, 60),
            simulation: sim,
            eventsCommitted: (input, before, events) => collected.AddRange(events));

        session.StepExact(1, inputProvider: f => new FrameInput(f, InputBits.LightPunch, InputBits.LightPunch));
        session.StepExact(10, inputProvider: f => FrameInput.Empty(f));

        Assert.True(session.IsFrameSyncIntact);
        Assert.NotEmpty(collected);
        Assert.Contains(collected, e => e.Type == BattleEventType.AttackActive && e.MoveId == PokeA);
        Assert.Contains(collected, e => e.Type == BattleEventType.AttackActive && e.MoveId == PokeB);
        Assert.Contains(collected, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(collected.Count, collected.Select(e => e.EventId).Distinct().Count());
    }
}
