using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 06B（D77/D78/D79/D80）：命中、伤害、防御与硬直。
///
/// <para>测试专用配置：双方同配置（镜像可验证），裸按键 5A/5B/5C（Mid/High/Low），
/// Startup 1 / Active 3（攻击生效范围 [1,1]——仅第 2 个 Active 帧生效，
/// 便于单次命中的精确伤害断言）/ Recovery 4。
/// 伤害：Mid 30/12/8、High 60/18/12、Low 40/14/10（伤害/命中硬直/防御硬直）。</para>
///
/// <para>已知边界（06C 修复前）：同一攻击实例多帧接触会重复结算——
/// 测试攻击限定单帧生效以隔离本阶段语义；06C 的攻击实例去重将消除该限制。</para>
/// </summary>
public class CombatSettlementTests
{
    private static FightRoster CreateTestRoster()
    {
        AttackDefinition Mid = new(
            FixedPoint.FromInt(45), FixedPoint.FromInt(70),
            FixedPoint.FromInt(20), FixedPoint.FromInt(20),
            firstActiveFrame: 1, lastActiveFrame: 1,
            AttackHeight.Mid, damage: 30, hitstunFrames: 12, blockstunFrames: 8);
        AttackDefinition High = new(
            FixedPoint.FromInt(45), FixedPoint.FromInt(70),
            FixedPoint.FromInt(20), FixedPoint.FromInt(20),
            firstActiveFrame: 1, lastActiveFrame: 1,
            AttackHeight.High, damage: 60, hitstunFrames: 18, blockstunFrames: 12);
        AttackDefinition Low = new(
            FixedPoint.FromInt(45), FixedPoint.FromInt(30),
            FixedPoint.FromInt(25), FixedPoint.FromInt(15),
            firstActiveFrame: 1, lastActiveFrame: 1,
            AttackHeight.Low, damage: 40, hitstunFrames: 14, blockstunFrames: 10);

        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition CreateFighter(string id) => new(
            id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5),
                new CommandDefinition("5B", Array.Empty<CommandDirection>(), InputBits.HeavyPunch, priority: 5),
                new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick, priority: 5),
            },
            new[]
            {
                new MoveDefinition("Mid", "5A", 1, 3, 4, attacks: new[] { Mid }),
                new MoveDefinition("High", "5B", 1, 3, 4, attacks: new[] { High }),
                new MoveDefinition("Low", "5C", 1, 3, 4, attacks: new[] { Low }),
            },
            movement);

        return new FightRoster(CreateFighter("Kai"), CreateFighter("Rei"));
    }

    private static BattleSimulation NewSim() => new(CreateTestRoster());

    /// <summary>间距 60 的默认对局（P1 -60 朝右 / P2 0 朝左，满血 1000）。</summary>
    private static BattleState DefaultPair() => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion);

    /// <summary>把一名角色置为 Active 招式（elapsed 1 在下一帧结算——攻击范围 [1,1]）。</summary>
    private static FighterState Active(string moveId, int id, int x, Facing facing) =>
        FighterState.Create(id, 1000, x, facing) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = moveId,
            PhaseRemainingFrames = 3, // ActiveFrames：下一帧 elapsed = 1 → 命中帧
            MoveStartedFrame = 0,     // 攻击实例身份（D81）：发动帧
        };

    /// <summary>构造「后」输入位（按该角色朝向：朝右 → Left 键；朝左 → Right 键）。</summary>
    private static InputBits BackOf(Facing facing) =>
        facing == Facing.Right ? InputBits.Left : InputBits.Right;

    private static FighterState P1(BattleState s) => s.Fighter(0);
    private static FighterState P2(BattleState s) => s.Fighter(1);

    // ─────────────────────────────────────────────────────────────
    // 命中与伤害
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Hit_AppliesDamageAndHitstun_InterruptsNothing()
    {
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, FrameInput.Empty(0)).State;

        // P2 被命中：扣 30、进入 Hitstun(12)；P1 攻击继续（constructed Active(3) → 结算帧后 Active(2)）。
        Assert.Equal(970, P2(state).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);
        Assert.Equal(12, P2(state).StunRemainingFrames);
        Assert.Null(P2(state).MoveId);
        Assert.Equal(1000, P1(state).Health);
        Assert.Equal(FighterLogicState.Active, P1(state).LogicState);
        Assert.Equal(2, P1(state).PhaseRemainingFrames);
    }

    [Fact]
    public void Hit_InterruptsDefenderStartup_MoveCanceled()
    {
        // 动作中受击：招式被中断（MoveId/阶段清空）并进入 Hitstun。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.Startup,
                MoveId = "Mid",
                PhaseRemainingFrames = 1,
                MoveStartedFrame = 0,
            },
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, FrameInput.Empty(0)).State;

        Assert.Equal(970, P2(state).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);
        Assert.Null(P2(state).MoveId);
        Assert.Equal(0, P2(state).PhaseRemainingFrames);
    }

    [Fact]
    public void Hit_HealthClampedAtZero_NeverNegative()
    {
        // 伤害超过剩余体力：扣至 0，不变为负。
        var roster = CreateTestRoster();
        var sim = new BattleSimulation(roster);
        var huge = new AttackDefinition(
            FixedPoint.FromInt(45), FixedPoint.FromInt(70),
            FixedPoint.FromInt(20), FixedPoint.FromInt(20),
            firstActiveFrame: 1, lastActiveFrame: 1,
            AttackHeight.Mid, damage: int.MaxValue, hitstunFrames: 12, blockstunFrames: 8);
        var fighterKai = new FighterDefinition("Kai", roster.Fighter(0).Commands,
            new[]
            {
                new MoveDefinition("Mid", "5A", 1, 3, 4, attacks: new[] { huge }),
            },
            MovementDefinition.CreateDefault());
        var hugeRoster = new FightRoster(fighterKai, roster.Fighter(1));
        var hugeSim = new BattleSimulation(hugeRoster);

        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        state = hugeSim.Step(state, FrameInput.Empty(0)).State;

        Assert.Equal(0, P2(state).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);
    }

    [Fact]
    public void NoContact_NoDamageNoStun()
    {
        // 攻击未接触（距离超出攻击框）：不扣血、无硬直。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 200, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        BattleStepResult result = sim.Step(state, FrameInput.Empty(0));

        Assert.Empty(result.Contacts);
        Assert.Equal(1000, P2(result.State).Health);
        Assert.Equal(FighterLogicState.Standing, P2(result.State).LogicState);
    }

    [Fact]
    public void AirborneDefender_SettlementSkipped()
    {
        // 空中被击显式跳过（06B 不做空中受击）：接触存在但不结算——
        // 不掉血、不进入地面 Hitstun（禁止产生 Y>0 的非法地面状态）。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.Airborne,
                PositionY = FixedPoint.FromInt(50),
                JumpKind = JumpKind.ShortHop,
                VelocityY = FixedPoint.FromInt(2),
            },
        }, BattleState.CreateDefault().StateVersion);

        BattleStepResult result = sim.Step(state, FrameInput.Empty(0));

        Assert.Single(result.Contacts); // 几何接触存在
        Assert.Equal(1000, P2(result.State).Health);
        Assert.Equal(FighterLogicState.Airborne, P2(result.State).LogicState);
    }

    // ─────────────────────────────────────────────────────────────
    // 防御与高度表
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StandingGuard_BlocksMid()
    {
        // P2 面朝左，按住 Right（= P2 的「后」）→ 站立防御 Mid：不掉血、Blockstun(8)。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, new FrameInput(0, InputBits.None, BackOf(Facing.Left))).State;

        Assert.Equal(1000, P2(state).Health);
        Assert.Equal(FighterLogicState.Blockstun, P2(state).LogicState);
        Assert.Equal(8, P2(state).StunRemainingFrames);
    }

    [Fact]
    public void StandingGuard_FailsLow()
    {
        // 站防 Low 失败：扣 40、Hitstun(14)。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            Active("Low", 1, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, new FrameInput(0, BackOf(Facing.Right), InputBits.None)).State;

        Assert.Equal(960, P1(state).Health);
        Assert.Equal(FighterLogicState.Hitstun, P1(state).LogicState);
        Assert.Equal(14, P1(state).StunRemainingFrames);
    }

    [Fact]
    public void CrouchingGuard_BlocksLowAndMid()
    {
        // 蹲防 Low 成功（Blockstun 10，不掉血）；蹲防 Mid 成功（Blockstun 8）。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            Active("Low", 1, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        InputBits p1CrouchGuard = BackOf(Facing.Right) | InputBits.Down;
        state = sim.Step(state, new FrameInput(0, p1CrouchGuard, InputBits.None)).State;
        Assert.Equal(1000, P1(state).Health);
        Assert.Equal(FighterLogicState.Blockstun, P1(state).LogicState);
        Assert.Equal(10, P1(state).StunRemainingFrames);

        // 蹲防 Mid。
        BattleState state2 = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);
        state2 = sim.Step(state2, new FrameInput(0, InputBits.None, BackOf(Facing.Left) | InputBits.Down)).State;
        Assert.Equal(1000, P2(state2).Health);
        Assert.Equal(FighterLogicState.Blockstun, P2(state2).LogicState);
        Assert.Equal(8, P2(state2).StunRemainingFrames);
    }

    [Fact]
    public void CrouchingGuard_FailsHigh()
    {
        // 蹲防 High 失败：扣 60、Hitstun(18)。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("High", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, new FrameInput(0, InputBits.None, BackOf(Facing.Left) | InputBits.Down)).State;

        Assert.Equal(940, P2(state).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);
        Assert.Equal(18, P2(state).StunRemainingFrames);
    }

    [Fact]
    public void Guard_RequiresBackInput()
    {
        // 站立但不按「后」：不防御 → 命中。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, FrameInput.Empty(0)).State;

        Assert.Equal(970, P2(state).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);
    }

    [Fact]
    public void Guard_NotAvailableDuringMove()
    {
        // 动作中（Startup）不能凭「后」临时防御 → 命中并中断。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.Startup,
                MoveId = "Mid",
                PhaseRemainingFrames = 1,
                MoveStartedFrame = 0,
            },
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, new FrameInput(0, InputBits.None, BackOf(Facing.Left))).State;

        Assert.Equal(970, P2(state).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);
    }

    // ─────────────────────────────────────────────────────────────
    // 双方同帧互中（D80）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MutualHits_BothDamaged_Simultaneously()
    {
        var sim = NewSim();
        BattleState before = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            Active("Mid", 1, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        BattleState after = sim.Step(before, FrameInput.Empty(0)).State;

        // 双方各被命中一次 Mid：970 + Hitstun(12)。
        Assert.Equal(970, P1(after).Health);
        Assert.Equal(970, P2(after).Health);
        Assert.Equal(FighterLogicState.Hitstun, P1(after).LogicState);
        Assert.Equal(FighterLogicState.Hitstun, P2(after).LogicState);
        Assert.Equal(12, P1(after).StunRemainingFrames);
        Assert.Equal(12, P2(after).StunRemainingFrames);
    }

    [Fact]
    public void MutualHits_MirrorSymmetric()
    {
        // 完全镜像（身份保持：P1 的攻击/被击映射到镜像位置，攻击类型不互换）
        // → 结果逐位镜像一致：A.P1（吃 P2 的 High）= 940/18；A.P2（吃 P1 的 Mid）= 970/12。
        var sim = NewSim();
        BattleState a = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            Active("High", 1, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);
        BattleState b = new BattleState(0, new[]
        {
            Active("Mid", 0, 0, Facing.Left),
            Active("High", 1, -60, Facing.Right),
        }, BattleState.CreateDefault().StateVersion);

        BattleState afterA = sim.Step(a, FrameInput.Empty(0)).State;
        BattleState afterB = sim.Step(b, FrameInput.Empty(0)).State;

        Assert.Equal(P1(afterA).Health, P1(afterB).Health);
        Assert.Equal(P2(afterA).Health, P2(afterB).Health);
        Assert.Equal(P1(afterA).StunRemainingFrames, P1(afterB).StunRemainingFrames);
        Assert.Equal(P2(afterA).StunRemainingFrames, P2(afterB).StunRemainingFrames);
        Assert.Equal(940, P1(afterA).Health);
        Assert.Equal(970, P2(afterA).Health);
    }

    [Fact]
    public void OneHits_OtherBlocks()
    {
        // P1 命中、P2 成功防御：P2 掉 Blockstun 不掉血，P1 不受影响。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            Active("Mid", 0, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, new FrameInput(0, InputBits.None, BackOf(Facing.Left))).State;

        Assert.Equal(1000, P2(state).Health);
        Assert.Equal(FighterLogicState.Blockstun, P2(state).LogicState);
        Assert.Equal(1000, P1(state).Health);
        Assert.Equal(FighterLogicState.Active, P1(state).LogicState);
        Assert.Equal(2, P1(state).PhaseRemainingFrames);
    }

    // ─────────────────────────────────────────────────────────────
    // 硬直行为
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Hitstun_CountsDown_BlocksActions_ThenRecovers()
    {
        // 命中 → Hitstun(3)：期间步行/起跳/攻击/防御全部无效；归零同帧回待机更新。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.Hitstun,
                StunRemainingFrames = 3,
            },
        }, BattleState.CreateDefault().StateVersion);

        // 移动/防御尝试（不含攻击键）：硬直期间全部无效。
        // （攻击键缓冲会在硬直结束帧按 D60 同帧消费——由 FullChain/状态机测试覆盖该语义。）
        InputBits p2Everything = BackOf(Facing.Left) | InputBits.Down | InputBits.Up | InputBits.Right;

        // 帧 0–1：硬直倒计时——位置冻结、状态保持、一切动作输入无效。
        for (int f = 0; f < 2; f++)
        {
            state = sim.Step(state, new FrameInput(f, InputBits.None, p2Everything)).State;
            Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);
            Assert.Equal(0, P2(state).PositionX.ToIntTruncated());
            Assert.True(P2(state).StunRemainingFrames >= 1);
        }

        // 帧 2（剩余 1 → 0）：归零同帧回待机；本帧用中立输入 → 站立
        // （若该帧按住 A，会按 D60 同帧消费进入 Startup——由状态机测试覆盖）。
        state = sim.Step(state, new FrameInput(2, InputBits.None, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Standing, P2(state).LogicState);
        Assert.Equal(0, P2(state).StunRemainingFrames);
        Assert.Equal(0, P2(state).PositionX.ToIntTruncated()); // 硬直期间零位移
    }

    [Fact]
    public void HitstunCompletion_WithDownHeld_CrouchesSameFrame()
    {
        // 硬直归零同帧按待机更新（「下」按住 → 蹲下）——与 Recovery 完成同语义。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.Hitstun,
                StunRemainingFrames = 1,
            },
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, new FrameInput(0, InputBits.None, InputBits.Down)).State;
        Assert.Equal(FighterLogicState.Crouching, P2(state).LogicState);
        Assert.Equal(0, P2(state).StunRemainingFrames);
    }

    [Fact]
    public void Blockstun_CountsDownAndRecovers()
    {
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.Blockstun,
                StunRemainingFrames = 2,
            },
        }, BattleState.CreateDefault().StateVersion);

        state = sim.Step(state, new FrameInput(0, InputBits.None, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Blockstun, P2(state).LogicState);
        Assert.Equal(1, P2(state).StunRemainingFrames);

        state = sim.Step(state, new FrameInput(1, InputBits.None, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Standing, P2(state).LogicState);
        Assert.Equal(0, P2(state).StunRemainingFrames);
    }

    [Fact]
    public void Validate_StunInvariants()
    {
        // Hitstun/Blockstun 必须 ≥ 1；其他状态必须为 0。
        new FighterState(Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -60, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Hitstun,
            StunRemainingFrames: 3).Validate();
        new FighterState(Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -60, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Blockstun,
            StunRemainingFrames: 1).Validate();

        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -60, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Hitstun,
            StunRemainingFrames: 0).Validate());
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -60, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Standing,
            StunRemainingFrames: 2).Validate());
    }

    // ─────────────────────────────────────────────────────────────
    // 完整识别链路（场景 1 的确定性版本）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FullChain_RecognitionToDamage_ExactFrame()
    {
        // P1 在 -60 按 A（裸按键 5A）：帧 0 消费 → Startup(1)（帧 0）→
        // 帧 1 = Active(3) elapsed 0（范围 [1,1] 不生效）→ 帧 2 elapsed 1 → 命中。
        var sim = NewSim();
        BattleState state = DefaultPair();

        state = sim.Step(state, new FrameInput(0, InputBits.LightPunch, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Startup, P1(state).LogicState);
        Assert.Equal(1000, P2(state).Health);

        state = sim.Step(state, new FrameInput(1, InputBits.None, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Active, P1(state).LogicState);
        Assert.Equal(1000, P2(state).Health); // elapsed 0：范围 [1,1] 未生效

        state = sim.Step(state, new FrameInput(2, InputBits.None, InputBits.None)).State;
        Assert.Equal(970, P2(state).Health); // elapsed 1：命中
        Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);

        // 帧 3：elapsed 2 → 攻击框已过 → 不重复结算（单帧生效范围）。
        state = sim.Step(state, new FrameInput(3, InputBits.None, InputBits.None)).State;
        Assert.Equal(970, P2(state).Health);
    }
}
