using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 05B：场地边界（D70）、双人最小间距与推挤（D71）、自动朝向（D72）。
///
/// <para>默认场地：X ∈ [-640, 640]、地面 0、最小中心间距 60。
/// 推挤规则：仅双方都非空中时强制；按中点对称分配；完全重叠平局 = P1 向左 / P2 向右；
/// 推挤后重新钳制边界（墙优先于间距）。朝向：帧末更新，仅地面待机转向，
/// 等距保持原朝向，本帧输入按帧开始朝向解析（D46 不变）。</para>
/// </summary>
public class StageBoundaryTests
{
    private static BattleSimulation NewSim() => new(DemoRoster.CreateKaiVsRei());

    private static BattleState Run(BattleState state, Func<int, FrameInput> inputFor, int frames)
    {
        var sim = NewSim();
        for (int i = 0; i < frames; i++)
            state = sim.Step(state, inputFor(state.Frame)).State;
        return state;
    }

    private static Func<int, FrameInput> Hold(InputBits p1, InputBits p2) =>
        frame => new FrameInput(frame, p1, p2);

    private static FighterState P1(BattleState s) => s.Fighter(0);
    private static FighterState P2(BattleState s) => s.Fighter(1);

    /// <summary>自定义初始对局（坐标为世界单位整数）。</summary>
    private static BattleState CustomState(int p1X, Facing p1Facing, int p2X, Facing p2Facing) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, p1Facing),
            FighterState.Create(1, 1000, p2X, p2Facing),
        },
        BattleState.CreateDefault().StateVersion);

    // ─────────────────────────────────────────────────────────────
    // 边界钳制（5.1）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LeftWall_ClampsExactly_NoPenetrationNoOscillation()
    {
        // P1 背对 P2 向左走：远离对方，无推挤干扰；-120 − 1.5f 会越过 -640。
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.Left, InputBits.None), 400);

        Assert.Equal(FixedPoint.FromInt(-640), P1(state).PositionX);
        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(FixedPoint.Zero, P1(state).PositionY); // 不因顶墙改变纵向

        // 持续顶墙 100 帧后仍在边界上（不穿墙、不反弹、不振荡）。
        BattleState longer = Run(BattleState.CreateDefault(), Hold(InputBits.Left, InputBits.None), 500);
        Assert.Equal(FixedPoint.FromInt(-640), P1(longer).PositionX);
    }

    [Fact]
    public void RightWall_ClampsExactly_MirroredScenario()
    {
        // 镜像场景：P1 站在右侧面向左，按「后」（右键）背向 P2 退到右墙。
        BattleState state = Run(
            CustomState(120, Facing.Left, -120, Facing.Right),
            Hold(InputBits.Right, InputBits.None), 400);

        Assert.Equal(FixedPoint.FromInt(640), P1(state).PositionX);
    }

    [Fact]
    public void AirborneX_ClampedToWalls_LandsAtBound()
    {
        // 空中同样受边界钳制：P1 在 600 起前大跳（水平 3.0、27 个积分帧），
        // 无钳制会到 681——空中越过 640 的每一帧都被钳制，落地恰在 640。
        BattleState state = Run(
            CustomState(600, Facing.Right, -100, Facing.Left),
            Hold(InputBits.Up | InputBits.Right, InputBits.None), 29);

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(FixedPoint.FromInt(640), P1(state).PositionX);
        Assert.Equal(FixedPoint.Zero, P1(state).PositionY);

        // 空中帧同样不越界：跑一次逐帧扫描。
        var sim = NewSim();
        BattleState scan = CustomState(600, Facing.Right, -100, Facing.Left);
        for (int f = 0; f < 29; f++)
        {
            scan = sim.Step(scan, new FrameInput(f, InputBits.Up | InputBits.Right, InputBits.None)).State;
            Assert.True(P1(scan).PositionX <= FixedPoint.FromInt(640));
            Assert.True(P1(scan).PositionX >= FixedPoint.FromInt(-640));
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 双人间距与推挤（5.2，D71）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ApproachingStationaryOpponent_PushesBoth_Deterministically()
    {
        // P2 原地不动，P1 持续前进：f=91 起进入推挤——
        // 每帧候选 P1 +2、P2 +0 → 重叠 2 → 对称各推 1 → 双方每帧 +1（P2 被推着走）。
        // 穿越永远不会发生。
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.Right, InputBits.None), 100);

        Assert.Equal(FixedPoint.FromInt(70), P1(state).PositionX);   // 60 + 10×1
        Assert.Equal(FixedPoint.FromInt(130), P2(state).PositionX);  // 120 + 10×1
        Assert.True(P1(state).PositionX < P2(state).PositionX);      // 永不穿越
    }

    [Fact]
    public void BothWalkingTowardEachOther_StopAtSeparation()
    {
        // 双方以不同速度相向移动（Kai 前 2.0、Rei 前 1.75）：
        // f=47 间距恰为 60；f=48 起每帧候选重叠 3.75 → 对称各推 1.875；
        // 之后 P1 持续推进使整体以 0.125/帧确定性地向 P2 后方蠕动，间距恒 60。
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.Right, InputBits.Left), 50);

        Assert.Equal(FixedPoint.FromFraction(-95, 4), P1(state).PositionX);  // -23.75
        Assert.Equal(FixedPoint.FromFraction(145, 4), P2(state).PositionX);  // 36.25
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);

        // 继续推进 100 帧：间距恒 60、不穿越、蠕动方向确定（P1 追着 P2 向右）。
        BattleState longer = Run(BattleState.CreateDefault(), Hold(InputBits.Right, InputBits.Left), 150);
        Assert.Equal(FixedPoint.FromInt(60), P2(longer).PositionX - P1(longer).PositionX);
        Assert.True(P1(longer).PositionX > P1(state).PositionX);
        Assert.True(P2(longer).PositionX > P2(state).PositionX);
    }

    [Fact]
    public void PushAgainstWall_WallPriorityOverSeparation_ConvergesStable()
    {
        // 专项补修后的正确行为（D71 修订）：P2 贴墙无法承担外推份额时，
        // 剩余重叠量由 P1 承担——最终 580/640，间距恰 60。
        // （修复前的旧算法长期稳定在 582/640、间距 58——旧断言接受残留重叠，是错误预期。）
        BattleState state = Run(
            CustomState(500, Facing.Right, 640, Facing.Left),
            Hold(InputBits.Right, InputBits.None), 150);

        Assert.Equal(FixedPoint.FromInt(640), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(580), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);

        // 稳定性：再推 100 帧位置逐位不变（无震荡）。
        BattleState longer = Run(
            CustomState(500, Facing.Right, 640, Facing.Left),
            Hold(InputBits.Right, InputBits.None), 250);
        Assert.Equal(P1(state).PositionX, P1(longer).PositionX);
        Assert.Equal(P2(state).PositionX, P2(longer).PositionX);
    }

    [Fact]
    public void LeftWall_Mirror_PusherTakesResidual()
    {
        // 左墙镜像：P1 贴左墙（-640），P2 从右侧持续向左推进（1.75/帧，接触在 f≈400）
        // → P1=-640 / P2=-580，间距恰 60。
        BattleState state = Run(
            CustomState(-640, Facing.Right, 120, Facing.Left),
            Hold(InputBits.None, InputBits.Left), 600);

        Assert.Equal(FixedPoint.FromInt(-640), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(-580), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    [Fact]
    public void BothPushTowardRightWall_StableSeparation()
    {
        // 双方同时向右墙推进：P1 步行 2.0 推着 P2（后退 1.25 也向右）→
        // P2 先贴墙（640），P1 收敛到 580。
        BattleState state = Run(
            BattleState.CreateDefault(), Hold(InputBits.Right, InputBits.Right), 1200);

        Assert.Equal(FixedPoint.FromInt(640), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(580), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    [Fact]
    public void BothPushTowardLeftWall_StableSeparation()
    {
        // 双方同时向左墙推进：P2 前进 1.75 快于 P1 后退 1.5 → P2 追上并推 P1 贴左墙
        // → P1=-640 / P2=-580。
        BattleState state = Run(
            BattleState.CreateDefault(), Hold(InputBits.Left, InputBits.Left), 1200);

        Assert.Equal(FixedPoint.FromInt(-640), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(-580), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    [Fact]
    public void ExactSeparationDistance_NoDisplacement()
    {
        // 距离恰好等于最小间距（60）：不推挤，位置恒定。
        BattleState state = Run(CustomState(-30, Facing.Right, 30, Facing.Left), Hold(InputBits.None, InputBits.None), 100);

        Assert.Equal(FixedPoint.FromInt(-30), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), P2(state).PositionX);
    }

    [Fact]
    public void OneRawUnitDeficit_CorrectedExactly_NoPermanentResidue()
    {
        // 间距仅差 1 个 Q16.16 原始单位：deficit = 1 raw（奇数）→ 余数归 P1
        // → P1 恰好补进 1 raw，间距精确回到 60，无永久残差。
        // （修复前：旧算法把 1 raw 对半截断为 0，间距永久保持 60−1 raw。）
        var state = new BattleState(0, new[]
        {
            new FighterState(Id: 0, MaxHealth: 1000, Health: 1000,
                PositionX: FixedPoint.FromRaw(1), PositionY: FixedPoint.Zero, Facing: Facing.Right),
            new FighterState(Id: 1, MaxHealth: 1000, Health: 1000,
                PositionX: FixedPoint.FromInt(60), PositionY: FixedPoint.Zero, Facing: Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        BattleState after = Run(state, Hold(InputBits.None, InputBits.None), 10);

        Assert.Equal(FixedPoint.FromRaw(0), P1(after).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(after).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(after).PositionX - P1(after).PositionX);
    }

    [Fact]
    public void LandingOverlapAtWall_ResidualGoesToLander()
    {
        // 一方靠墙、另一方跳跃落地造成重叠：P1 从 525 前大跳（水平 3.0×25 积分帧）
        // 落地候选 X=600，P2 贴墙 640 → 间距 40：落地帧一次性求解 → P1=580 / P2=640。
        // （帧 27 = 落地帧；Run 需 28 帧把它包含进来。）
        BattleState state = Run(
            CustomState(525, Facing.Right, 640, Facing.Left),
            Hold(InputBits.Up | InputBits.Right, InputBits.None), 28);

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(FighterLogicState.Standing, P2(state).LogicState);
        Assert.Equal(FixedPoint.FromInt(640), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(580), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    // ─────────────────────────────────────────────────────────────
    // GroundY 配置限制（P2 专项补修：当前场地模型只支持 Y=0 平地）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public void StageDefinition_RejectsNonZeroGroundY(int groundY)
    {
        Assert.Throws<ArgumentException>(() => new StageDefinition(
            FixedPoint.FromInt(-640), FixedPoint.FromInt(640),
            FixedPoint.FromInt(groundY), FixedPoint.FromInt(60)));
    }

    [Fact]
    public void StageDefinition_RejectsFractionalGroundY()
    {
        Assert.Throws<ArgumentException>(() => new StageDefinition(
            FixedPoint.FromInt(-640), FixedPoint.FromInt(640),
            FixedPoint.FromFraction(1, 2), FixedPoint.FromInt(60)));
    }

    [Fact]
    public void StageDefinition_ZeroGroundY_AndDefaultStage_AreAccepted()
    {
        var explicitZero = new StageDefinition(
            FixedPoint.FromInt(-640), FixedPoint.FromInt(640),
            FixedPoint.Zero, FixedPoint.FromInt(60));
        Assert.Equal(FixedPoint.Zero, explicitZero.GroundY);

        var def = StageDefinition.CreateDefault();
        Assert.Equal(FixedPoint.Zero, def.GroundY);
        Assert.Equal(FixedPoint.FromInt(-640), def.MinX);
        Assert.Equal(FixedPoint.FromInt(640), def.MaxX);
        Assert.Equal(FixedPoint.FromInt(60), def.MinimumFighterSeparation);
    }

    [Fact]
    public void ExactOverlap_TieBreak_IsDeterministic_P1LeftP2Right()
    {
        // 完全重叠的异常初态：平局规则固定——P1 向左、P2 向右，各让 30。
        BattleState state = Run(CustomState(0, Facing.Right, 0, Facing.Left), Hold(InputBits.None, InputBits.None), 1);

        Assert.Equal(FixedPoint.FromInt(-30), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), P2(state).PositionX);

        // 重复模拟结果一致。
        BattleState again = Run(CustomState(0, Facing.Right, 0, Facing.Left), Hold(InputBits.None, InputBits.None), 1);
        Assert.Equal(P1(state).PositionX, P1(again).PositionX);
        Assert.Equal(P2(state).PositionX, P2(again).PositionX);
    }

    [Fact]
    public void LandingOnOpponent_ResolvedDeterministically()
    {
        // 落地位置冲突：P1 从 -100 前大跳（水平 3.0 × 25 个积分帧 = 75），P2 站在 0。
        // 落地候选 X = -25 落在 P2 身上：落地帧（双方都在地面）立即推挤——
        // overlap 35 → 各 17.5 → P1 -42.5 / P2 +17.5（空中越身允许，落地冲突按规则解决）。
        // 落地帧 27：P1 = -42.5、P2 = +17.5（落地候选 -25，overlap 35 → 各 17.5）。
        BattleState state = Run(
            CustomState(-100, Facing.Right, 0, Facing.Left),
            Hold(InputBits.Up | InputBits.Right, InputBits.None), 28);

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(FighterLogicState.Standing, P2(state).LogicState);
        Assert.Equal(FixedPoint.FromFraction(-85, 2), P1(state).PositionX);
        Assert.Equal(FixedPoint.FromFraction(35, 2), P2(state).PositionX);
        Assert.Equal(FixedPoint.FromInt(60), P2(state).PositionX - P1(state).PositionX);
    }

    // ─────────────────────────────────────────────────────────────
    // 朝向更新（5.3，D72）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AirCross_FacingFrozenInAir_UpdatesAfterLanding()
    {
        // P1 从 -60 前大跳越过站在 0 的 P2（初始间距恰 60，不触发地面推挤；空中越身允许）：
        // - 越身发生在帧 22（X 首次 > 0）：P1 朝向冻结（仍向右）；P2 是地面待机，
        //   越身当帧就转身向右；
        // - 落地候选 X = 15（-60 + 3×25）——P1 已越过 P2（X > 0）：dx > 0 →
        //   对称推开各 22.5 → P1 37.5 / P2 -22.5（背向分离，间距 60）。
        // - 落地帧末：P1 转身向左面对 P2，P2 面向右（越身后互脸）。
        var sim = NewSim();
        BattleState state = CustomState(-60, Facing.Right, 0, Facing.Left);

        for (int f = 0; f <= 23; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.Up | InputBits.Right, InputBits.None)).State;

        // 帧 23（空中 n=20，X = -60+3×21 = 3 > 0）：已越身——P1 朝向冻结仍向右。
        Assert.Equal(FighterLogicState.Airborne, P1(state).LogicState);
        Assert.True(P1(state).PositionX > P2(state).PositionX); // 已在 P2 身后
        Assert.Equal(Facing.Right, P1(state).Facing);   // 空中冻结
        Assert.Equal(Facing.Right, P2(state).Facing);   // 地面待机按最终位置转向

        // 落地帧 27：P1 = 37.5、P2 = -22.5（落地后背向分离），互朝对方。
        BattleState landed = Run(
            CustomState(-60, Facing.Right, 0, Facing.Left),
            Hold(InputBits.Up | InputBits.Right, InputBits.None), 28);
        Assert.Equal(FixedPoint.FromFraction(75, 2), P1(landed).PositionX);
        Assert.Equal(FixedPoint.FromFraction(-45, 2), P2(landed).PositionX);
        // 越身后 P1 已在 P2 右侧：|间距| = 60，方向翻转。
        Assert.Equal(FixedPoint.FromInt(60), P1(landed).PositionX - P2(landed).PositionX);
        Assert.Equal(Facing.Left, P1(landed).Facing);   // 转身面对身后的 P2
        Assert.Equal(Facing.Right, P2(landed).Facing);  // 面对越到身后的 P1
    }

    [Fact]
    public void Facing_FrozenDuringMoveAndPreJump()
    {
        // 动作与预备跳期间朝向冻结（P2 用无输入移动制造位置差——这里直接验证
        // 动作期间不转向：P1 在 5A 期间 P2 位置不变，P1 朝向本来就正确；
        // 改用构造状态验证 PreJump 期间不转向）。
        var sim = NewSim();
        BattleState state = CustomState(-200, Facing.Right, 100, Facing.Left);
        state = sim.Step(state, new FrameInput(0, InputBits.Up, InputBits.None)).State;
        Assert.Equal(FighterLogicState.PreJump, P1(state).LogicState);
        Assert.Equal(Facing.Right, P1(state).Facing); // PreJump 不转向
    }

    [Fact]
    public void Facing_EqualPositions_KeepsCurrentFacing_NoOscillation()
    {
        // 两人同位置（平局推挤后间距 60，不再相等）——直接构造同位且间距规则
        // 关闭的场地验证「等距保持」：使用 separation 0 的场地，两人同点重叠。
        var noPush = new StageDefinition(
            FixedPoint.FromInt(-640), FixedPoint.FromInt(640), FixedPoint.Zero, FixedPoint.Zero);
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei(), noPush);
        BattleState state = CustomState(0, Facing.Right, 0, Facing.Left);

        for (int f = 0; f < 10; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.None, InputBits.None)).State;

        // X 恒相等 → 朝向逐帧保持初始值（不抖动）。
        Assert.Equal(P1(state).PositionX, P2(state).PositionX);
        Assert.Equal(Facing.Right, P1(state).Facing);
        Assert.Equal(Facing.Left, P2(state).Facing);
    }

    // ─────────────────────────────────────────────────────────────
    // 镜像对称性（5.2 / D 组）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MirrorScenario_ProducesMirroredTrajectory()
    {
        // 场景 A：默认站位，双方各自前进（P1 右 / P2 左）50 帧。
        // 场景 B：完全镜像（P1 在右侧面向左前进，P2 在左侧面向右前进）。
        // 逐帧断言：B.P1 == -A.P1、B.P2 == -A.P2、朝向互为镜像（含推挤阶段）。
        var simA = NewSim();
        var simB = NewSim();
        BattleState a = BattleState.CreateDefault();
        BattleState b = CustomState(120, Facing.Left, -120, Facing.Right);

        for (int f = 0; f < 50; f++)
        {
            a = simA.Step(a, new FrameInput(f, InputBits.Right, InputBits.Left)).State;
            b = simB.Step(b, new FrameInput(f, InputBits.Left, InputBits.Right)).State;

            Assert.Equal(-a.Fighter(0).PositionX, b.Fighter(0).PositionX);
            Assert.Equal(-a.Fighter(1).PositionX, b.Fighter(1).PositionX);
            // 镜像映射：X → −X、朝向取反、P1 ↔ P2。
            Assert.Equal(a.Fighter(0).Facing, b.Fighter(1).Facing);
            Assert.Equal(a.Fighter(1).Facing, b.Fighter(0).Facing);
        }

        // 终点：对称推挤后以 0.125/帧蠕动（帧 49：-23.75，见 BothWalkingTowardEachOther）。
        Assert.Equal(FixedPoint.FromFraction(-95, 4), a.Fighter(0).PositionX);
    }
}
