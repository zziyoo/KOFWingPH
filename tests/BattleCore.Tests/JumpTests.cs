using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 05B：跳跃系统（B1–B5，D67/D68/D69）。
///
/// <para>基于演示配置 Kai 的精确数值逐帧断言：预备窗口 3 帧、小跳初速 4.0、
/// 大跳初速 6.5、重力 0.5、水平跳速 3.0。由此可解析预测：</para>
/// <list type="bullet">
/// <item>小跳：帧 0 按「上」→ 帧 0–2 为 PreJump（剩余 3/2/1）→ 帧 3 起跳（离地帧）→
///       帧 9/10 达最高点 14 → 帧 17 落地（腾空 14 个积分帧，落地高度恰为 0）；</item>
/// <item>大跳：帧 3 起跳 → 帧 26/27 最高点 45.5 → 帧 29 落地（腾空 26 帧）。</item>
/// </list>
/// <para>垂直积分顺序（D69）：vy' = vy − g（起跳帧同样施加重力）→ y' = y + vy' → 触地判定。</para>
/// </summary>
public class JumpTests
{
    private static BattleSimulation NewSim() => new(DemoRoster.CreateKaiVsRei());

    private static FighterState P1(BattleState s) => s.Fighter(0);
    private static FighterState P2(BattleState s) => s.Fighter(1);

    /// <summary>从默认状态跑 frames 帧，每帧输入由 inputFor(frame) 提供。</summary>
    private static BattleState Run(BattleState state, Func<int, FrameInput> inputFor, int frames)
    {
        var sim = NewSim();
        for (int i = 0; i < frames; i++)
            state = sim.Step(state, inputFor(state.Frame)).State;
        return state;
    }

    /// <summary>恒定按住输入。</summary>
    private static Func<int, FrameInput> Hold(InputBits p1, InputBits p2) =>
        frame => new FrameInput(frame, p1, p2);

    /// <summary>只在指定帧集合按「上」，其余帧无输入。</summary>
    private static Func<int, FrameInput> UpDuring(params int[] frames)
    {
        var set = new HashSet<int>(frames);
        return frame => new FrameInput(
            frame, set.Contains(frame) ? InputBits.Up : InputBits.None, InputBits.None);
    }

    // ─────────────────────────────────────────────────────────────
    // B1：PreJump 窗口与离地帧
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void UpEdge_EntersPreJump_ForExactlyConfiguredFrames()
    {
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();

        // 帧 0：「上」边沿 → PreJump(3)。帧 1/2：倒计时 2/1。帧 3：起跳。
        state = sim.Step(state, new FrameInput(0, InputBits.Up, InputBits.None)).State;
        Assert.Equal(FighterLogicState.PreJump, P1(state).LogicState);
        Assert.Equal(3, P1(state).PhaseRemainingFrames);
        Assert.Equal(FixedPoint.Zero, P1(state).PositionY); // 预备期未离地

        state = sim.Step(state, new FrameInput(1, InputBits.None, InputBits.None)).State;
        Assert.Equal((FighterLogicState.PreJump, 2), (P1(state).LogicState, P1(state).PhaseRemainingFrames));

        state = sim.Step(state, new FrameInput(2, InputBits.None, InputBits.None)).State;
        Assert.Equal((FighterLogicState.PreJump, 1), (P1(state).LogicState, P1(state).PhaseRemainingFrames));

        // 帧 3：预备窗口归零 → 起跳（离地帧）；小跳初速 4.0，起跳帧施加重力 → vy = 3.5。
        state = sim.Step(state, new FrameInput(3, InputBits.None, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Airborne, P1(state).LogicState);
        Assert.Equal(JumpKind.ShortHop, P1(state).JumpKind); // 帧 3 的「上」已松开 → 小跳（D68）
        Assert.Equal(FixedPoint.FromFraction(7, 2), P1(state).PositionY);
        Assert.Equal(FixedPoint.FromFraction(7, 2), P1(state).VelocityY);
        Assert.Equal(FixedPoint.Zero, P1(state).VelocityX); // 中立跳无水平速度
        Assert.Equal(FixedPoint.FromInt(-120), P1(state).PositionX);
    }

    [Fact]
    public void PreJump_IsNotCancellable()
    {
        // 预备窗口内：蹲下输入不改变状态、移动输入不位移、指令缓冲不消费（B1）。
        // 帧 0 起：上 + 下 + 右 + A 同时按住。
        BattleState state = Run(
            BattleState.CreateDefault(),
            Hold(InputBits.Up | InputBits.Down | InputBits.Right | InputBits.LightPunch, InputBits.None), 2);

        Assert.Equal(FighterLogicState.PreJump, P1(state).LogicState);
        Assert.Equal(FixedPoint.FromInt(-120), P1(state).PositionX);
        Assert.Equal(FixedPoint.Zero, P1(state).PositionY);
    }

    // ─────────────────────────────────────────────────────────────
    // B2：小跳与大跳
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ShortHop_Rises_Apexes_Falls_Lands_WithExactTrajectory()
    {
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();
        state = sim.Step(state, new FrameInput(0, InputBits.Up, InputBits.None)).State; // 请求跳跃

        // 帧 1..16：无输入（「上」已松开 → 小跳）。
        for (int f = 1; f <= 16; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.None, InputBits.None)).State;

        // 轨迹断言（D69 解析可预测）：vy(n) = 3.5 − 0.5n，y(n) = (n+1)(3.5 − 0.25n)。
        // 帧 16 末（n=13）：y = 3.5，处于下降段。
        Assert.Equal(FighterLogicState.Airborne, P1(state).LogicState);
        Assert.Equal(FixedPoint.FromFraction(7, 2), P1(state).PositionY);
        Assert.True(P1(state).VelocityY < FixedPoint.Zero); // 下降段

        // 最高点 14：帧 9/10（n=6/7）；速度恰好在帧 10（n=7）过零。
        BattleState apex = Run(BattleState.CreateDefault(), UpDuring(0), 11);
        Assert.Equal(FixedPoint.FromInt(14), P1(apex).PositionY);
        Assert.Equal(FixedPoint.Zero, P1(apex).VelocityY);
        Assert.Equal(FixedPoint.FromInt(14), P1(Run(BattleState.CreateDefault(), UpDuring(0), 10)).PositionY);

        // 上升 → 下降单调性抽查。
        Assert.True(
            P1(Run(BattleState.CreateDefault(), UpDuring(0), 8)).PositionY
                < P1(Run(BattleState.CreateDefault(), UpDuring(0), 9)).PositionY);
        Assert.True(
            P1(Run(BattleState.CreateDefault(), UpDuring(0), 12)).PositionY
                < P1(Run(BattleState.CreateDefault(), UpDuring(0), 11)).PositionY);

        // 帧 17：落地（第 14 个积分帧 y 恰为 0）——贴地、速度归零、回站立（D69）。
        BattleState landed = Run(BattleState.CreateDefault(), UpDuring(0), 18);
        Assert.Equal(FighterLogicState.Standing, P1(landed).LogicState);
        Assert.Equal(FixedPoint.Zero, P1(landed).PositionY);
        Assert.Equal(FixedPoint.Zero, P1(landed).VelocityY);
        Assert.Equal(FixedPoint.Zero, P1(landed).VelocityX);
        Assert.Equal(JumpKind.None, P1(landed).JumpKind);
        Assert.Equal(FixedPoint.FromInt(-120), P1(landed).PositionX); // 中立跳无水平位移

        // 帧 16 仍应处于空中（落地前一帧）。
        Assert.Equal(FighterLogicState.Airborne,
            P1(Run(BattleState.CreateDefault(), UpDuring(0), 17)).LogicState);
    }

    [Fact]
    public void HighJump_WhenUpHeldThroughPreJumpWindow()
    {
        // 「上」按住跨过预备窗口末帧（帧 3）→ 大跳：初速 6.5（起跳帧含重力 → vy=6）、
        // 帧 27 落地（25 个积分帧）、最高点 39（帧 14/15）。
        BattleState takeoff = Run(BattleState.CreateDefault(), Hold(InputBits.Up, InputBits.None), 4);
        Assert.Equal(FighterLogicState.Airborne, P1(takeoff).LogicState);
        Assert.Equal(JumpKind.HighJump, P1(takeoff).JumpKind);
        Assert.Equal(FixedPoint.FromInt(6), P1(takeoff).VelocityY);      // 6.5 − 0.5
        Assert.Equal(FixedPoint.FromInt(6), P1(takeoff).PositionY);

        // 最高点 39，速度在帧 15（n=12）过零。
        BattleState apex = Run(BattleState.CreateDefault(), Hold(InputBits.Up, InputBits.None), 16);
        Assert.Equal(FixedPoint.FromInt(39), P1(apex).PositionY);
        Assert.Equal(FixedPoint.Zero, P1(apex).VelocityY);
        Assert.Equal(FixedPoint.FromInt(39), P1(Run(BattleState.CreateDefault(), Hold(InputBits.Up, InputBits.None), 15)).PositionY);

        // 帧 27 落地回站立；帧 26 仍在空中。
        BattleState landed = Run(BattleState.CreateDefault(), Hold(InputBits.Up, InputBits.None), 28);
        Assert.Equal(FighterLogicState.Standing, P1(landed).LogicState);
        Assert.Equal(FixedPoint.Zero, P1(landed).PositionY);
        Assert.Equal(FighterLogicState.Airborne,
            P1(Run(BattleState.CreateDefault(), Hold(InputBits.Up, InputBits.None), 27)).LogicState);

        // 大跳明显高于小跳（B2）：39 vs 14。
        BattleState shortApex = Run(BattleState.CreateDefault(), UpDuring(0), 11);
        Assert.True(P1(apex).PositionY > P1(shortApex).PositionY);
    }

    [Fact]
    public void ReleaseUpOnLastPreJumpFrame_StillClassifiesAsShortHop()
    {
        // 分类边界（B2）：「上」按到帧 2、起跳帧（帧 3）松开 → 分类看帧 3 电平 = false → 小跳。
        BattleState state = Run(BattleState.CreateDefault(), UpDuring(0, 1, 2), 4);
        Assert.Equal(JumpKind.ShortHop, P1(state).JumpKind);

        // 按到帧 3（含）→ 帧 3 电平 = true → 大跳。
        BattleState high = Run(BattleState.CreateDefault(), UpDuring(0, 1, 2, 3), 4);
        Assert.Equal(JumpKind.HighJump, P1(high).JumpKind);
    }

    // ─────────────────────────────────────────────────────────────
    // B3：水平方向跳跃（起跳锁定，空中无控制）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InputBits.Right, -75)]  // 前小跳：-120 + 3×15 个积分帧（帧 3..17，含落地帧）
    [InlineData(InputBits.Left, -165)]  // 后小跳
    public void DirectionalShortHop_LocksHorizontalVelocityAtTakeoff(InputBits direction, int expectedX)
    {
        // 帧 0：上 + 水平方向；帧 1..：只按水平方向（「上」松开 → 小跳；
        // 空中反向也改变不了已锁定的速度——B3 推荐语义）。
        InputBits first = direction | InputBits.Up;
        BattleState state = Run(
            BattleState.CreateDefault(),
            f => new FrameInput(f, f == 0 ? first : direction, InputBits.None), 18);

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(FixedPoint.FromInt(expectedX), P1(state).PositionX);
        Assert.Equal(FixedPoint.Zero, P1(state).PositionY);
    }

    [Theory]
    [InlineData(InputBits.Right, -45)]   // 前大跳：-120 + 3×25（帧 3..27）
    [InlineData(InputBits.Left, -195)]   // 后大跳
    public void DirectionalHighJump_LocksHorizontalVelocityAtTakeoff(InputBits direction, int expectedX)
    {
        InputBits hold = direction | InputBits.Up;
        BattleState state = Run(BattleState.CreateDefault(), Hold(hold, InputBits.None), 28);

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(FixedPoint.FromInt(expectedX), P1(state).PositionX);
    }

    [Fact]
    public void AirHorizontalVelocity_IsConstant_NoAirControl()
    {
        // 起跳帧（帧 3）仍按住前方向 → 锁定 +3；帧 4 起空中按住反方向：
        // 水平速度不变（X 严格线性 +3/帧），B3「空中无控制」。
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();
        state = sim.Step(state, new FrameInput(0, InputBits.Up | InputBits.Right, InputBits.None)).State;
        for (int f = 1; f <= 3; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.Right, InputBits.None)).State;

        Assert.Equal(FighterLogicState.Airborne, P1(state).LogicState);
        Assert.Equal(FixedPoint.FromInt(3), P1(state).VelocityX);

        FixedPoint previousX = P1(state).PositionX;
        for (int f = 4; f <= 16; f++)
        {
            state = sim.Step(state, new FrameInput(f, InputBits.Left, InputBits.None)).State;
            if (P1(state).LogicState == FighterLogicState.Airborne)
            {
                Assert.Equal(FixedPoint.FromInt(3), P1(state).VelocityX);
                Assert.Equal(previousX + FixedPoint.FromInt(3), P1(state).PositionX);
                previousX = P1(state).PositionX;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // B5：防自动连跳
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void HoldingUp_AfterLanding_DoesNotAutoRejump()
    {
        // 持住「上」60 帧：只发生一次跳跃（帧 17 落地后不再起跳，B5）。
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.Up, InputBits.None), 60);

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(FixedPoint.Zero, P1(state).PositionY);
        // 帧 18..60 全部在地面（若发生第二次跳跃，帧 20+ 会重新离地）。
        BattleState probe = Run(BattleState.CreateDefault(), Hold(InputBits.Up, InputBits.None), 30);
        Assert.Equal(FighterLogicState.Standing, P1(probe).LogicState);
    }

    [Fact]
    public void ReleasingThenRepressingUp_AllowsNewJump()
    {
        // 落地 → 松开「上」→ 重新按下 → 允许新一次起跳（B5）。
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();

        // 第一次跳（帧 0 请求，帧 17 落地）。
        state = sim.Step(state, new FrameInput(0, InputBits.Up, InputBits.None)).State;
        state = Run(state, f => new FrameInput(f, InputBits.None, InputBits.None), 17);
        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);

        // 帧 18：松开；帧 19：重新按下 → 新的 PreJump；帧 22 起跳。
        state = sim.Step(state, new FrameInput(18, InputBits.None, InputBits.None)).State;
        state = sim.Step(state, new FrameInput(19, InputBits.Up, InputBits.None)).State;
        Assert.Equal(FighterLogicState.PreJump, P1(state).LogicState);
        state = Run(state, f => new FrameInput(f, InputBits.None, InputBits.None), 3);
        Assert.Equal(FighterLogicState.Airborne, P1(state).LogicState);
    }

    [Fact]
    public void UpHeldDuringMove_DoesNotJump_AfterRecoveryEither()
    {
        // 动作（Startup/Active/Recovery）期间按「上」：不起跳（任务书第四部分）；
        // 动作结束时「上」仍按住（无新边沿）→ 也不起跳；松开再按才起跳。
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();

        // 帧 0：5A（裸按键 A）→ Startup(3)/Active(2)/Recovery(6)，帧 11 回站立。
        state = sim.Step(state, new FrameInput(0, InputBits.LightPunch, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Startup, P1(state).LogicState);

        for (int f = 1; f <= 11; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.Up, InputBits.None)).State;

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(FixedPoint.Zero, P1(state).PositionY); // 全程未离地

        // 帧 12：松开「上」；帧 13：重新按下 → 正常起跳。
        state = sim.Step(state, new FrameInput(12, InputBits.None, InputBits.None)).State;
        state = sim.Step(state, new FrameInput(13, InputBits.Up, InputBits.None)).State;
        Assert.Equal(FighterLogicState.PreJump, P1(state).LogicState);
    }

    // ─────────────────────────────────────────────────────────────
    // 状态机/招式兼容（任务书第四部分）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AirGroundMove_CannotConsumeDuringAir_ConsumesOnFirstGroundFrame()
    {
        // D69 落地消费语义：空中识别的指令不消费（本阶段无空中招式），
        // 落地帧本身也不消费（落地发生在状态机之后），落地后的第一个地面帧消费。
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();

        state = sim.Step(state, new FrameInput(0, InputBits.Up, InputBits.None)).State; // 请求跳跃（帧 3 起跳）
        Assert.Equal(FighterLogicState.PreJump, P1(state).LogicState);

        // 空中帧 14：按 A（5A 裸按键，边沿识别但空中不消费；识别帧 14 → 有效期到 22）。
        for (int f = 1; f <= 16; f++)
        {
            InputBits bits = f == 14 ? InputBits.LightPunch : InputBits.None;
            state = sim.Step(state, new FrameInput(f, bits, InputBits.None)).State;
            Assert.NotEqual(FighterLogicState.Startup, P1(state).LogicState); // 空中不发动
        }

        // 帧 17：落地帧——回站立，但本帧不消费。
        state = sim.Step(state, new FrameInput(17, InputBits.None, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Null(P1(state).MoveId);

        // 帧 18：第一个完整地面帧 → 消费 5A 缓冲（识别帧 14，有效期到 22，仍有效）。
        state = sim.Step(state, new FrameInput(18, InputBits.None, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Startup, P1(state).LogicState);
        Assert.Equal(DemoRoster.StandLightPunch, P1(state).MoveId);
    }

    [Fact]
    public void Airborne_UpEdge_DoesNotJumpAgain()
    {
        // 空中重复按「上」不重新起跳（B5）。帧 1/2 仍是 PreJump 倒计时，
        // 帧 3 起跳；空中每两帧制造一次新的「上」边沿也不影响。
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();
        state = sim.Step(state, new FrameInput(0, InputBits.Up, InputBits.None)).State;

        for (int f = 1; f <= 16; f++)
        {
            InputBits bits = f % 2 == 0 ? InputBits.Up : InputBits.None;
            state = sim.Step(state, new FrameInput(f, bits, InputBits.None)).State;
            if (f >= 3)
                Assert.Equal(FighterLogicState.Airborne, P1(state).LogicState);
            else
                Assert.Equal(FighterLogicState.PreJump, P1(state).LogicState);
        }
    }

    [Fact]
    public void Jump_FromCrouching_Works()
    {
        // 蹲下中按「上」边沿 → 直接进入起跳预备（B4.1）。
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();
        state = sim.Step(state, new FrameInput(0, InputBits.Down, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Crouching, P1(state).LogicState);

        state = sim.Step(state, new FrameInput(1, InputBits.Up | InputBits.Down, InputBits.None)).State;
        Assert.Equal(FighterLogicState.PreJump, P1(state).LogicState); // SOCD 上优先 → 跳跃请求
    }

    [Fact]
    public void LandingFrame_WithDownHeld_StandsFirstThenCrouches()
    {
        // 落地帧回站立（D69）；蹲下判定从下一个地面帧开始——明确语义，测试固定。
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();
        state = sim.Step(state, new FrameInput(0, InputBits.Up, InputBits.None)).State;

        for (int f = 1; f <= 17; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.Down, InputBits.None)).State;

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState); // 落地帧

        state = sim.Step(state, new FrameInput(18, InputBits.Down, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Crouching, P1(state).LogicState);
    }

    // ─────────────────────────────────────────────────────────────
    // Validate 新不变式（D68/D69，非法状态不能借新枚举绕过 D62）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AirborneInvariants()
    {
        // 空中必须 Y > 0 且携带合法跳跃类型。
        var air = new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 5,
            Facing: Facing.Right, LogicState: FighterLogicState.Airborne,
            JumpKind: JumpKind.ShortHop, VelocityY: FixedPoint.FromInt(3));
        air.Validate();

        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Airborne,
            JumpKind: JumpKind.ShortHop).Validate()); // 空中 Y=0 拒绝

        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 5,
            Facing: Facing.Right, LogicState: FighterLogicState.Airborne).Validate()); // 无跳跃类型拒绝
    }

    [Theory]
    [InlineData(FighterLogicState.Airborne, 999, "999")]
    [InlineData(FighterLogicState.Airborne, -1, "-1")]
    [InlineData(FighterLogicState.Standing, 999, "999")]
    [InlineData(FighterLogicState.Crouching, 999, "999")]
    [InlineData(FighterLogicState.PreJump, 999, "999")]
    public void Validate_RejectsUndefinedJumpKind(FighterLogicState state, int rawKind, string rawText)
    {
        // P2 专项补修：未定义的 JumpKind 一律拒绝（与 D62 对 LogicState 的处理一致）。
        // 修复前：(JumpKind)999 + Airborne + Y>0 能通过校验进入权威快照。
        var fighter = new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000,
            PositionX: -120, PositionY: state == FighterLogicState.Airborne ? 5 : 0,
            Facing: Facing.Right, LogicState: state,
            JumpKind: (JumpKind)rawKind,
            PhaseRemainingFrames: state == FighterLogicState.PreJump ? 3 : 0,
            VelocityY: state == FighterLogicState.Airborne ? FixedPoint.FromInt(3) : FixedPoint.Zero);

        var ex = Assert.Throws<InvalidOperationException>(fighter.Validate);
        Assert.Contains("未定义", ex.Message);
        Assert.Contains(rawText, ex.Message); // 错误原因包含具体非法值
    }

    [Fact]
    public void BattleState_Construction_RejectsUndefinedJumpKind()
    {
        // 第一道门：非法跳跃类型不能进入权威快照（BattleState 构造期拒绝）。
        Assert.Throws<InvalidOperationException>(() => new BattleState(0, new[]
        {
            new FighterState(Id: 0, MaxHealth: 1000, Health: 1000,
                PositionX: -120, PositionY: 5, Facing: Facing.Right,
                LogicState: FighterLogicState.Airborne, JumpKind: (JumpKind)999),
            FighterState.Create(1, 1000, 120, Facing.Left),
        }, BattleState.CreateDefault().StateVersion));
    }

    [Fact]
    public void Validate_GroundStatesMustBeGroundedAndStill()
    {
        // 地面状态：Y 必须为 0、速度必须为零、不得携带跳跃类型（新增字段不能绕过校验）。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 3,
            Facing: Facing.Right, LogicState: FighterLogicState.Standing).Validate());

        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Standing,
            VelocityX: FixedPoint.FromInt(1)).Validate());

        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Crouching,
            JumpKind: JumpKind.HighJump).Validate());

        // PreJump 携带预备窗口剩余帧合法；剩余 0 非法。
        new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.PreJump,
            PhaseRemainingFrames: 3).Validate();
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.PreJump,
            PhaseRemainingFrames: 0).Validate());
    }

    [Fact]
    public void MovementDefinition_JumpConfigValidation()
    {
        FixedPoint W(int v) => FixedPoint.FromInt(v);

        // 大跳初速必须 > 小跳初速（B2 可分辨性）。
        Assert.Throws<ArgumentException>(() => new MovementDefinition(
            W(2), W(1), W(3), W(5), W(5), FixedPoint.FromFraction(1, 2), W(3)));
        Assert.Throws<ArgumentException>(() => new MovementDefinition(
            W(2), W(1), W(3), W(6), W(5), FixedPoint.FromFraction(1, 2), W(3)));

        // 跳跃初速/重力上限与正负校验。
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementDefinition(
            W(2), W(1), W(3), FixedPoint.Zero, W(6), FixedPoint.FromFraction(1, 2), W(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementDefinition(
            W(2), W(1), W(3), W(4), W(6), FixedPoint.Zero, W(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementDefinition(
            W(2), W(1), W(3), W(4), W(6), FixedPoint.FromFraction(1, 2), W(65)));

        // 预备窗口：非整数帧与越界拒绝。
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementDefinition(
            W(2), W(1), FixedPoint.FromFraction(7, 2), W(4), W(6), FixedPoint.FromFraction(1, 2), W(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementDefinition(
            W(2), W(1), W(0), W(4), W(6), FixedPoint.FromFraction(1, 2), W(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementDefinition(
            W(2), W(1), W(31), W(4), W(6), FixedPoint.FromFraction(1, 2), W(3)));

        // 合法配置保持字段。
        var ok = new MovementDefinition(
            W(2), W(1), W(3), W(4), W(6), FixedPoint.FromFraction(1, 2), W(3));
        Assert.Equal(3, ok.PreJumpFrames);
        Assert.Equal(W(6), ok.HighJumpInitialVelocity);
    }
}
