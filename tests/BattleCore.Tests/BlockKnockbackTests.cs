using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 07 最终补修（D95）：<b>地面击退摩擦的配置不变式</b>。
///
/// <para><b>修复前实测缺陷</b>：<see cref="AttackDefinition"/> 只要求
/// 「配置了 <c>GroundKnockback</c> 就必须有正摩擦」，却忽略了 <c>BlockKnockback</c> 同样会产生
/// 水平速度。于是
/// <c>groundKnockback: 0, blockKnockback: 4, groundKnockbackFriction: 0</c>
/// 构造期被接受，真实 <c>Step</c> 在防御结算后生成
/// <c>Blockstun + VelocityX=4 + KnockbackFriction=0</c>，触发
/// <see cref="FighterState.Validate"/> 的「速度与摩擦必须同时存在」不变式 → 整帧提交失败。</para>
///
/// <para><b>修复后规则（D95）</b>：<c>GroundKnockbackFriction</c> 是<b>地面击退通用摩擦</b>
/// （命中击退与防御击退共用）；只要两者任一为正，摩擦就必须为正——不自洽配置在
/// <b>数据构造期</b>被拒绝，而不是等到对局中才爆发。</para>
/// </summary>
public class BlockKnockbackTests
{
    private const string Jab = "Jab";

    /// <summary>受击方朝左、攻击方朝右的默认站位（间距 60 = 最小间距）。</summary>
    private static BattleState Duel(int p1X = -60, int p2X = 0) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, Facing.Right),
            FighterState.Create(1, 1000, p2X, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion);

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) =>
        new(frame, p1, p2);

    /// <summary>构造一份攻击数据（伤害 10、命中硬直 20、防御硬直 20）。</summary>
    private static AttackDefinition Attack(int groundKb, int blockKb, int friction) => new(
        FixedPoint.FromInt(45),
        FixedPoint.FromInt(60),
        FixedPoint.FromInt(30),
        FixedPoint.FromInt(30),
        0,
        0,
        AttackHeight.Mid,
        damage: 10,
        hitstunFrames: 20,
        blockstunFrames: 20,
        groundKnockback: FixedPoint.FromInt(groundKb),
        groundKnockbackFriction: FixedPoint.FromInt(friction),
        blockKnockback: FixedPoint.FromInt(blockKb));

    private static BattleSimulation Sim(int groundKb, int blockKb, int friction)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition Build(string id) => new(
            id,
            new[] { new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5) },
            new[]
            {
                new MoveDefinition(Jab, "5A", 1, 3, 4, attacks: new[]
                {
                    Attack(groundKb, blockKb, friction),
                }),
            },
            movement);

        return new BattleSimulation(new FightRoster(Build("Kai"), Build("Rei")));
    }

    /// <summary>帧 0 发动 5A、帧 1 按住「后」防御（P2 朝左 → 后 = 世界 +X）。</summary>
    private static BattleState BlockOnce(BattleSimulation sim, BattleState state)
    {
        state = sim.Step(state, F(state.Frame, InputBits.LightPunch)).State;
        return sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;
    }

    // ─────────────────────────────────────────────────────────────
    // 1. 构造期不变式（D95）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_RejectsBlockKnockbackWithoutFriction()
    {
        // 修复前：该组合被接受，随后真实对局在防御结算时抛异常。
        var ex = Assert.Throws<ArgumentException>(() => Attack(groundKb: 0, blockKb: 4, friction: 0));
        Assert.Contains("摩擦", ex.Message);
    }

    [Fact]
    public void Constructor_StillRejectsGroundKnockbackWithoutFriction()
    {
        // 既有规则回归：命中击退也必须配置摩擦。
        var ex = Assert.Throws<ArgumentException>(() => Attack(groundKb: 4, blockKb: 0, friction: 0));
        Assert.Contains("摩擦", ex.Message);
    }

    [Fact]
    public void Constructor_RejectsBothKnockbacksWithoutFriction()
    {
        Assert.Throws<ArgumentException>(() => Attack(groundKb: 4, blockKb: 4, friction: 0));
    }

    [Fact]
    public void Constructor_AcceptsNoKnockbackAtAll()
    {
        AttackDefinition attack = Attack(groundKb: 0, blockKb: 0, friction: 0);

        Assert.Equal(FixedPoint.Zero, attack.GroundKnockback);
        Assert.Equal(FixedPoint.Zero, attack.BlockKnockback);
        Assert.Equal(FixedPoint.Zero, attack.GroundKnockbackFriction);
    }

    [Fact]
    public void Constructor_AcceptsBlockOnlyKnockback()
    {
        AttackDefinition attack = Attack(groundKb: 0, blockKb: 4, friction: 1);

        Assert.Equal(FixedPoint.Zero, attack.GroundKnockback);
        Assert.Equal(FixedPoint.FromInt(4), attack.BlockKnockback);
        Assert.Equal(FixedPoint.FromInt(1), attack.GroundKnockbackFriction);
    }

    [Fact]
    public void Constructor_AcceptsBothKnockbacks()
    {
        AttackDefinition attack = Attack(groundKb: 4, blockKb: 4, friction: 1);

        Assert.Equal(FixedPoint.FromInt(4), attack.GroundKnockback);
        Assert.Equal(FixedPoint.FromInt(4), attack.BlockKnockback);
        Assert.Equal(FixedPoint.FromInt(1), attack.GroundKnockbackFriction);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 真实 Step 路径：防御击退结算与逐帧衰减
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void BlockedHit_KeepsHealth_EntersBlockstun_WithBlockKnockback()
    {
        // 修复前：这一帧直接抛「速度与摩擦必须同时为零或同时非零」——整场对局失败。
        var sim = Sim(groundKb: 0, blockKb: 4, friction: 1);
        BattleState state = BlockOnce(sim, Duel());

        FighterState p2 = state.Fighter(1);
        Assert.Equal(1000, p2.Health);
        Assert.Equal(FighterLogicState.Blockstun, p2.LogicState);
        Assert.Equal(FixedPoint.FromInt(4), p2.VelocityX);
        Assert.Equal(FixedPoint.FromInt(1), p2.KnockbackFriction);
        Assert.Equal(FixedPoint.Zero, p2.VelocityY);
        p2.Validate(); // 结算结果必须自洽
    }

    [Fact]
    public void BlockKnockback_DecaysFourThreeTwoOneZero()
    {
        // 防御击退与命中击退共用同一套 D92 逐帧位移：4,3,2,1,0（累计 10 单位）。
        var sim = Sim(groundKb: 0, blockKb: 4, friction: 1);
        BattleState state = BlockOnce(sim, Duel());
        int start = state.Fighter(1).PositionX.ToIntTruncated();

        int[] deltas = { 4, 3, 2, 1, 0 };
        int expected = start;
        for (int i = 0; i < deltas.Length; i++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;
            expected += deltas[i];

            Assert.Equal(expected, state.Fighter(1).PositionX.ToIntTruncated());
            Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);
            state.Fighter(1).Validate(); // 每一帧都必须合法
        }

        Assert.Equal(start + 10, state.Fighter(1).PositionX.ToIntTruncated());
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).VelocityX);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).KnockbackFriction);
    }

    [Fact]
    public void BlockKnockback_MirroredDirection()
    {
        // 镜像站位：P1 在右朝左、P2 在左朝右 → 「远离攻击者」= 世界 -X。
        var sim = Sim(groundKb: 0, blockKb: 4, friction: 1);
        BattleState state = new(
            0,
            new[]
            {
                FighterState.Create(0, 1000, 60, Facing.Left),
                FighterState.Create(1, 1000, 0, Facing.Right),
            },
            BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, F(0, InputBits.LightPunch)).State;
        state = sim.Step(state, F(1, InputBits.None, InputBits.Left)).State; // P2 朝右，「后」= 世界 -X

        Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.FromInt(-4), state.Fighter(1).VelocityX);
        Assert.Equal(FixedPoint.FromInt(1), state.Fighter(1).KnockbackFriction);

        int start = state.Fighter(1).PositionX.ToIntTruncated();
        int[] deltas = { 4, 3, 2, 1, 0 };
        int expected = start;
        for (int i = 0; i < deltas.Length; i++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Left)).State;
            expected -= deltas[i];
            Assert.Equal(expected, state.Fighter(1).PositionX.ToIntTruncated());
        }
    }

    [Fact]
    public void BlockKnockback_AtWall_ClampsAndKeepsOrder()
    {
        // P2 贴右墙（640）防御：击退被 D70 钳制，P1 不得越到 P2 右侧。
        var sim = Sim(groundKb: 0, blockKb: 4, friction: 1);
        BattleState state = BlockOnce(sim, Duel(p1X: 580, p2X: 640));

        Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);

        for (int i = 0; i < 6; i++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;
            Assert.Equal(FixedPoint.FromInt(640), state.Fighter(1).PositionX);
            Assert.True(state.Fighter(0).PositionX < state.Fighter(1).PositionX, "地面角色不得穿越");
        }
    }

    [Fact]
    public void BlockstunEnd_LeavesNoVelocityOrFriction()
    {
        // 防御硬直结束：不得残留速度/摩擦（否则下一帧的站立状态非法）。
        var sim = Sim(groundKb: 0, blockKb: 4, friction: 1);
        BattleState state = BlockOnce(sim, Duel());

        int guard = 0;
        while (state.Fighter(1).LogicState == FighterLogicState.Blockstun && guard++ < 40)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;
            state.Fighter(1).Validate();
        }

        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).VelocityX);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).KnockbackFriction);
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 推进路径与快照重放
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void BlockKnockback_StepExactAndAdvanceAgree()
    {
        BattleState initial = Duel();
        FrameInput Provider(int frame) => frame switch
        {
            0 => F(frame, InputBits.LightPunch),
            _ => F(frame, InputBits.None, InputBits.Right),
        };

        var exact = new BattleSession(
            initialState: initial,
            simulation: Sim(groundKb: 0, blockKb: 4, friction: 1),
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 8));
        exact.StepExact(10, inputProvider: Provider);

        var scheduled = new BattleSession(
            initialState: initial,
            simulation: Sim(groundKb: 0, blockKb: 4, friction: 1),
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 8));
        for (int i = 0; i < 10; i++)
            scheduled.Advance(FixedStepScheduler.FixedStepSeconds, inputProvider: Provider);

        Assert.Equal(exact.CurrentState.Frame, scheduled.CurrentState.Frame);
        Assert.Equal(exact.CurrentState.Fighter(0), scheduled.CurrentState.Fighter(0));
        Assert.Equal(exact.CurrentState.Fighter(1), scheduled.CurrentState.Fighter(1));
    }

    [Fact]
    public void BlockKnockback_MidSnapshotReplayIsIdentical()
    {
        BattleState initial = Duel();
        FrameInput Provider(int frame) => frame switch
        {
            0 => F(frame, InputBits.LightPunch),
            _ => F(frame, InputBits.None, InputBits.Right),
        };

        BattleState full = initial;
        var simFull = Sim(groundKb: 0, blockKb: 4, friction: 1);
        for (int f = 0; f < 12; f++)
            full = simFull.Step(full, Provider(f)).State;

        // 从帧 4 的中间快照续跑（新模拟实例，模拟进程重启）。
        BattleState head = initial;
        var simHead = Sim(groundKb: 0, blockKb: 4, friction: 1);
        for (int f = 0; f < 4; f++)
            head = simHead.Step(head, Provider(f)).State;

        var simTail = Sim(groundKb: 0, blockKb: 4, friction: 1);
        for (int f = 4; f < 12; f++)
            head = simTail.Step(head, Provider(f)).State;

        Assert.Equal(full.Fighter(0), head.Fighter(0));
        Assert.Equal(full.Fighter(1), head.Fighter(1));
        Assert.Equal(full.Round, head.Round);
    }
}
