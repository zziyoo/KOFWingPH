using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 05 专项补修：高速配置下的地面防穿越（帧开始顺序约束，D71 修订）。
///
/// <para><b>缺陷（修复前实测复现）</b>：旧 <c>ApplySeparation</c> 只看运动后的候选坐标——
/// 双方单帧内交换左右位置时，交换结果被当作合法站位（P1=+30 / P2=-30，间距 60 但已穿越）。
/// 修复：帧开始双方都在地面（非 Airborne）时，<b>以帧开始相对顺序为约束</b>求解，
/// 禁止地面角色在一个逻辑帧内交换左右；涉及空中（合法越身/落地帧）时以候选顺序为准，
/// 新地面顺序在落地帧建立——空中越身不被禁止。</para>
///
/// <para>测试用高速配置：双方步行 32 单位/帧（<see cref="MovementDefinition"/> 允许的上限）。</para>
/// </summary>
public class GroundCrossingTests
{
    /// <summary>双方步行速度均为 32（每帧上限）的对称配置。</summary>
    private static FightRoster CreateFastRoster(int p1Speed = 32, int p2Speed = 32)
    {
        var movementP1 = new MovementDefinition(
            FixedPoint.FromInt(p1Speed), FixedPoint.FromInt(p1Speed),
            FixedPoint.FromInt(3), FixedPoint.FromFraction(4, 1), FixedPoint.FromFraction(13, 2),
            FixedPoint.FromFraction(1, 2), FixedPoint.FromInt(3));
        var movementP2 = new MovementDefinition(
            FixedPoint.FromInt(p2Speed), FixedPoint.FromInt(p2Speed),
            FixedPoint.FromInt(3), FixedPoint.FromFraction(4, 1), FixedPoint.FromFraction(13, 2),
            FixedPoint.FromFraction(1, 2), FixedPoint.FromInt(3));
        FighterDefinition kai = DemoRoster.CreateKai();
        FighterDefinition rei = DemoRoster.CreateRei();
        return new FightRoster(
            new FighterDefinition(kai.FighterId, kai.Commands, kai.Moves, movementP1),
            new FighterDefinition(rei.FighterId, rei.Commands, rei.Moves, movementP2));
    }

    private static BattleState CustomState(int p1X, Facing p1Facing, int p2X, Facing p2Facing) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, p1Facing),
            FighterState.Create(1, 1000, p2X, p2Facing),
        },
        BattleState.CreateDefault().StateVersion);

    private static FighterState P1(BattleState s) => s.Fighter(0);
    private static FighterState P2(BattleState s) => s.Fighter(1);

    // ─────────────────────────────────────────────────────────────
    // 复现与修复：相向高速冲刺
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void HighSpeedHeadOn_32Each_MustNotCross()
    {
        // 任务书复现场景：P1(-30, 朝右) 与 P2(+30, 朝左) 同帧各前进 32。
        // 未修正候选：P1=+2、P2=-2——已经交换位置。
        // 修复后：帧开始顺序（P1 左）为约束 → 双方停在 -30/+30（间距恰 60，无穿越）。
        var sim = new BattleSimulation(CreateFastRoster());
        BattleState state = CustomState(-30, Facing.Right, 30, Facing.Left);

        state = sim.Step(state, new FrameInput(0, InputBits.Right, InputBits.Left)).State;

        Assert.True(P1(state).PositionX < P2(state).PositionX, "地面角色不得在一个逻辑帧内交换左右");
        Assert.Equal(FixedPoint.FromInt(-30), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    [Fact]
    public void HighSpeedChase_32IntoStatic_MustNotCross()
    {
        // P1 速度 32 从 0 冲向静止的 P2(30)（构造初态间距 30）：
        // 候选 P1=32 越过 P2 → 修复后按帧开始顺序回投：P1=1、P2=61（间距 60）。
        var sim = new BattleSimulation(CreateFastRoster(p1Speed: 32, p2Speed: 32));
        BattleState state = CustomState(0, Facing.Right, 30, Facing.Left);

        state = sim.Step(state, new FrameInput(0, InputBits.Right, InputBits.None)).State;

        Assert.True(P1(state).PositionX < P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(1), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(61), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    [Fact]
    public void Speed32Versus1_HeadOn_ExactSeparationWithRemainderToP1()
    {
        // 32 vs 1 相向：候选 P1=+2、P2=+29（未交换，间距 27）。
        // deficit = 33（原始值 2162688 为偶 → 对半精确 16.5/16.5）→ P1=-14.5 / P2=45.5。
        // 余数规则只作用于奇数原始值 deficit（见 OneRawUnitDeficit 测试）。
        var sim = new BattleSimulation(CreateFastRoster(p1Speed: 32, p2Speed: 1));
        BattleState state = CustomState(-30, Facing.Right, 30, Facing.Left);

        state = sim.Step(state, new FrameInput(0, InputBits.Right, InputBits.Left)).State;

        Assert.Equal(FixedPoint.FromFraction(-29, 2), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromFraction(91, 2), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    [Fact]
    public void MirrorOfCrossing_PreservesMirroredOrder()
    {
        // 场景 1 的镜像：P1(+30, 朝左) 与 P2(-30, 朝右) 同帧各前进 32。
        // 帧开始顺序 P1 在右 → 修复后 P1=+30 / P2=-30（镜像位置，身份保持）。
        var sim = new BattleSimulation(CreateFastRoster());
        BattleState state = CustomState(30, Facing.Left, -30, Facing.Right);

        state = sim.Step(state, new FrameInput(0, InputBits.Left, InputBits.Right)).State;

        Assert.True(P1(state).PositionX > P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(-30), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P1(state).PositionX - P2(state).PositionX);
    }

    [Fact]
    public void HighSpeedApproach_100Frames_NoCrossingNoOscillation()
    {
        // 双方 32 相向从 ±120 连续推进 100 帧：f=0/1 尚未触及间距（176/112），
        // f=2 进入约束（候选间距 48 → 收敛 -30/+30），f=3 起候选每帧穿越、
        // 求解器每帧回投到 -30/+30 —— 位置恒定（无震荡、无穿越）。
        var sim = new BattleSimulation(CreateFastRoster());
        BattleState state = BattleState.CreateDefault();

        for (int f = 0; f < 100; f++)
        {
            state = sim.Step(state, new FrameInput(f, InputBits.Right, InputBits.Left)).State;
            Assert.True(P1(state).PositionX < P2(state).PositionX, $"帧 {f} 发生穿越");

            if (f >= 2)
            {
                Assert.Equal(FixedPoint.FromInt(-30), P1(state).PositionX);
                Assert.Equal(FixedPoint.FromInt(30), P2(state).PositionX);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 地面防穿越不阻止合法空中越身
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AirCrossing_StillAllowed_AndOrderEstablishedAtLanding()
    {
        // 空中越身必须仍然合法：P1 从 -60 前大跳越过 P2(0)（跳跃速度不受步行上限影响），
        // 落地帧建立新地面顺序（P1 在右），此后地面帧保持该顺序（不再被「帧开始顺序」拉回）。
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        BattleState state = CustomState(-60, Facing.Right, 0, Facing.Left);

        for (int f = 0; f <= 27; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.Up | InputBits.Right, InputBits.None)).State;

        // 落地帧（帧 27）：P1=37.5 在 P2=-22.5 右侧——越身后的新顺序。
        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.True(P1(state).PositionX > P2(state).PositionX);

        // 落地后 10 个地面帧：新顺序被保持（不被回投），间距恒 60。
        for (int f = 28; f < 38; f++)
        {
            state = sim.Step(state, new FrameInput(f, InputBits.None, InputBits.None)).State;
            Assert.True(P1(state).PositionX > P2(state).PositionX, $"帧 {f} 越身后的新顺序被错误回退");
            Assert.Equal(FixedPoint.FromInt(60), P1(state).PositionX - P2(state).PositionX);
        }
    }

    [Fact]
    public void BothGroundOneInMove_NoCrossing()
    {
        // 一方处于 Startup（5A）、另一方高速（32）冲入：双方都在地面 → 顺序约束生效，
        // 冲刺者不得穿越动作中的角色（动作者可被推挤位移，但左右关系不变）。
        var sim = new BattleSimulation(CreateFastRoster(p1Speed: 3, p2Speed: 32));
        BattleState state = CustomState(0, Facing.Right, 30, Facing.Left);

        // P1 帧 0 发动 5A（裸按键 A）→ Startup(3)；P2 同帧全速冲入。
        state = sim.Step(state, new FrameInput(0, InputBits.LightPunch, InputBits.Left)).State;

        Assert.Equal(FighterLogicState.Startup, P1(state).LogicState);
        Assert.True(P1(state).PositionX < P2(state).PositionX, "动作中的地面角色不得被穿越");
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    // ─────────────────────────────────────────────────────────────
    // 墙角与高速的组合
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void HighSpeedIntoWallCorner_SingleFrameResolution()
    {
        // P1（速度 32）从 620 冲向贴墙静止的 P2(640)（构造初态间距 20）：
        // 候选 P1=640（钳制）、间距 0 → 一次性求解（无迭代）：P1=580 / P2=640。
        var sim = new BattleSimulation(CreateFastRoster(p1Speed: 32, p2Speed: 32));
        BattleState state = CustomState(620, Facing.Right, 640, Facing.Left);

        state = sim.Step(state, new FrameInput(0, InputBits.Right, InputBits.None)).State;

        Assert.Equal(FixedPoint.FromInt(640), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(580), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);

        // 持续推进 100 帧：稳定在 580/640，无震荡。
        for (int f = 1; f < 100; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.Right, InputBits.None)).State;
        Assert.Equal(FixedPoint.FromInt(580), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(640), P2(state).PositionX);
    }

    [Fact]
    public void SameInputs_Twice_IdenticalResults()
    {
        var simA = new BattleSimulation(CreateFastRoster());
        var simB = new BattleSimulation(CreateFastRoster());
        BattleState a = CustomState(-30, Facing.Right, 30, Facing.Left);
        BattleState b = CustomState(-30, Facing.Right, 30, Facing.Left);

        for (int f = 0; f < 60; f++)
        {
            FrameInput input = new(f, InputBits.Right, InputBits.Left);
            a = simA.Step(a, input).State;
            b = simB.Step(b, input).State;
            Assert.Equal(a.Fighter(0), b.Fighter(0));
            Assert.Equal(a.Fighter(1), b.Fighter(1));
        }
    }
}
