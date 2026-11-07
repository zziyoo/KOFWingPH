using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 05A（D64/D65）：确定性地面运动。
///
/// <para>覆盖任务书 A3/A4 清单：前后步行、朝向镜像、原地停止无残余、方向反转、
/// SOCD 中立、蹲下限制、动作期间不被移动打断、双人独立、暂停无位移、
/// Advance 与 StepExact 一致、中间快照续跑一致、长跑无漂移、配置校验。
/// 全部断言基于演示配置的精确速度（Kai 前 2.0 / 后 1.5，Rei 前 1.75 / 后 1.25）。</para>
/// </summary>
public class GroundMovementTests
{
    private static BattleSimulation NewSim() => new(DemoRoster.CreateKaiVsRei());

    private static BattleState Run(BattleState state, Func<int, FrameInput> inputFor, int frames)
    {
        var sim = NewSim();
        for (int i = 0; i < frames; i++)
            state = sim.Step(state, inputFor(state.Frame)).State;
        return state;
    }

    /// <summary>恒定按住输入（每帧相同电平）。</summary>
    private static Func<int, FrameInput> Hold(InputBits p1, InputBits p2) =>
        frame => new FrameInput(frame, p1, p2);

    private static FixedPoint X(BattleState state, int player) => state.Fighter(player).PositionX;

    /// <summary>双方运动数据完全相同的对称配置（镜像对称性测试用）。</summary>
    private static FightRoster CreateSymmetricRoster()
    {
        var movement = new MovementDefinition(
            FixedPoint.FromInt(2), FixedPoint.FromInt(1));
        FighterDefinition kai = DemoRoster.CreateKai();
        FighterDefinition rei = DemoRoster.CreateRei();
        return new FightRoster(
            new FighterDefinition(kai.FighterId, kai.Commands, kai.Moves, movement),
            new FighterDefinition(rei.FighterId, rei.Commands, rei.Moves, movement));
    }

    // ─────────────────────────────────────────────────────────────
    // 前进 / 后退（A4.1 / A4.2）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, -118)]   // 1 帧 × 2.0
    [InlineData(10, -100)]  // 10 帧 × 2.0
    [InlineData(60, 0)]     // 60 帧 × 2.0：-120 + 120
    public void HoldForward_MovesTowardOpponent_AtConfiguredSpeed(int frames, int expectedX)
    {
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.Right, InputBits.None), frames);

        Assert.Equal(FixedPoint.FromInt(expectedX), X(state, 0));
        Assert.Equal(FighterLogicState.Standing, state.Fighter(0).LogicState);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    public void HoldBackward_MovesAwayFromOpponent_AtConfiguredSpeed(int frames)
    {
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.Left, InputBits.None), frames);

        // Kai 后撤 1.5 单位/帧。
        Assert.Equal(FixedPoint.FromInt(-120) - FixedPoint.FromFraction(3, 2) * frames, X(state, 0));
    }

    [Fact]
    public void FacingLeft_Forward_DecreasesWorldX()
    {
        // A3 焦点：P2 朝左，按「前」（绝对 Left 键）→ 世界坐标必须减小。
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.None, InputBits.Left), 10);

        Assert.Equal(FixedPoint.FromFraction(205, 2), X(state, 1)); // 120 - 1.75×10 = 102.5
        Assert.True(X(state, 1) < FixedPoint.FromInt(120));
    }

    [Fact]
    public void FacingLeft_Back_IncreasesWorldX()
    {
        // P2 朝左按「后」（绝对 Right 键）→ 世界坐标增大。
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.None, InputBits.Right), 10);

        Assert.Equal(FixedPoint.FromInt(120) + FixedPoint.FromFraction(5, 4) * 10, X(state, 1));
    }

    [Fact]
    public void MirrorSymmetry_WithEqualSpeeds_AntiSymmetricEveryFrame()
    {
        // A4.3：同一份「前」输入在镜像朝向下逐帧反对称：P2.X == -P1.X。
        var sim = new BattleSimulation(CreateSymmetricRoster());
        BattleState state = BattleState.CreateDefault();

        for (int f = 0; f < 60; f++)
        {
            state = sim.Step(state, new FrameInput(f, InputBits.Right, InputBits.Left)).State;
            Assert.Equal(-X(state, 0), X(state, 1));
        }

        Assert.Equal(FixedPoint.FromInt(0), X(state, 0)); // -120 + 2×60
    }

    // ─────────────────────────────────────────────────────────────
    // 停止 / 反转 / SOCD（A4.4 / A4.5 / A4.6）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StopAfterWalking_NoResidualMovement()
    {
        var script = Composite(
            Hold(InputBits.Right, InputBits.None), 30,
            Hold(InputBits.None, InputBits.None));

        BattleState state = Run(BattleState.CreateDefault(), script, 90);

        Assert.Equal(FixedPoint.FromInt(-60), X(state, 0)); // -120 + 30×2
        // 后续 60 帧无输入：坐标纹丝不动（步行速度不进快照，停止即静止）。
        Assert.Equal(FixedPoint.FromInt(-60), X(state, 0));
    }

    [Fact]
    public void DirectionReversal_NetDisplacementIsSumOfBothPhases()
    {
        var script = Composite(
            Hold(InputBits.Right, InputBits.None), 10,
            Hold(InputBits.Left, InputBits.None));

        BattleState state = Run(BattleState.CreateDefault(), script, 20);

        // 前 10 帧 +2.0、后 10 帧 -1.5 → 净 -120 + 20 - 15 = -115。
        Assert.Equal(FixedPoint.FromInt(-115), X(state, 0));
    }

    [Fact]
    public void SocdLeftPlusRight_NoHorizontalMovement()
    {
        // D27 SOCD：左右同时 → 水平中立；运动系统复用同一解析，不另起炉灶。
        BattleState state = Run(
            BattleState.CreateDefault(),
            Hold(InputBits.Left | InputBits.Right, InputBits.None), 60);

        Assert.Equal(FixedPoint.FromInt(-120), X(state, 0));
        Assert.Equal(FighterLogicState.Standing, state.Fighter(0).LogicState);
    }

    // ─────────────────────────────────────────────────────────────
    // 蹲下与动作阶段的移动限制（A4.7 / A4.8）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Crouching_BlocksWalking_ResumesWhenStanding()
    {
        var script = Composite(
            Hold(InputBits.Down | InputBits.Right, InputBits.None), 30,
            Hold(InputBits.Right, InputBits.None));

        BattleState state = Run(BattleState.CreateDefault(), script, 40);

        // 蹲下 30 帧原地（A3 表：Crouching 不允许移动），站起后 10 帧 × 2.0：
        // -120 + 20 = -100（状态断言见 Crouching_BlocksWalking_StateCheck）。
        Assert.Equal(FixedPoint.FromInt(-100), X(state, 0));
    }

    [Fact]
    public void Crouching_BlocksWalking_StateCheck()
    {
        BattleState state = Run(
            BattleState.CreateDefault(),
            Hold(InputBits.Down | InputBits.Right, InputBits.None), 30);

        Assert.Equal(FighterLogicState.Crouching, state.Fighter(0).LogicState);
        Assert.Equal(FixedPoint.FromInt(-120), X(state, 0));
    }

    [Fact]
    public void MovePhases_BlockWalking_ResumesExactlyWhenRecoveryCompletes()
    {
        // Kai 5A：Startup 3 + Active 2 + Recovery 6 = 11 个阶段帧（帧 0..10）。
        // 持住 Right + 第 0 帧按 A：帧 0..10 坐标冻结；帧 11 收招归零回站立 → 同帧恢复步行。
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();

        for (int f = 0; f <= 10; f++)
        {
            state = sim.Step(state, new FrameInput(f, InputBits.Right | InputBits.LightPunch, InputBits.None)).State;
            Assert.Equal(FixedPoint.FromInt(-120), X(state, 0));
        }

        // 帧 10 结束时仍处于 Recovery（剩余 1 帧）。
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(0).LogicState);
        Assert.Equal(1, state.Fighter(0).PhaseRemainingFrames);

        state = sim.Step(state, new FrameInput(11, InputBits.Right, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Standing, state.Fighter(0).LogicState);
        Assert.Equal(FixedPoint.FromInt(-118), X(state, 0)); // 帧 11 走了 1 帧 × 2.0
    }

    // ─────────────────────────────────────────────────────────────
    // 双人独立与差异化配置（A4.9 / A2.1）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Players_AreIndependent()
    {
        BattleState state = Run(
            BattleState.CreateDefault(),
            Hold(InputBits.Right, InputBits.None), 30);

        Assert.Equal(FixedPoint.FromInt(-60), X(state, 0));
        Assert.Equal(FixedPoint.FromInt(120), X(state, 1)); // P2 无输入：纹丝不动
    }

    [Fact]
    public void PerCharacterMovementData_AreIndependent()
    {
        // A2.1：P1/P2 使用不同运动数据（Kai 前 2.0，Rei 前 1.75）。
        BattleState state = Run(
            BattleState.CreateDefault(),
            Hold(InputBits.Right, InputBits.Left), 10);

        Assert.Equal(FixedPoint.FromInt(-100), X(state, 0));
        Assert.Equal(FixedPoint.FromFraction(205, 2), X(state, 1));
    }

    // ─────────────────────────────────────────────────────────────
    // 暂停与丢弃预算（A4.10）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PausedSession_ProducesNoDisplacement()
    {
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(),
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));
        Func<int, FrameInput> holdRight = Hold(InputBits.Right, InputBits.None);

        session.Scheduler.Pause();
        int steps = session.Advance(1.0, inputProvider: holdRight);

        Assert.Equal(0, steps);
        Assert.Equal(0, session.CurrentState.Frame);
        Assert.Equal(FixedPoint.FromInt(-120), session.CurrentState.Fighter(0).PositionX);

        session.Scheduler.Resume();
        session.Advance(1.0, inputProvider: holdRight);
        Assert.Equal(FixedPoint.FromInt(0), session.CurrentState.Fighter(0).PositionX); // 60 帧 × 2.0
    }

    [Fact]
    public void DroppedBudget_ProducesNoMovement()
    {
        // 补帧上限 5：一次 1 秒的 Advance 只提交 5 帧，其余 55 帧记为丢弃
        // ——丢弃的逻辑时间没有被模拟，绝不产生位移。
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 5),
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));

        int steps = session.Advance(1.0, inputProvider: Hold(InputBits.Right, InputBits.None));

        Assert.Equal(5, steps);
        Assert.Equal(55, session.Scheduler.DroppedStepCount);
        Assert.Equal(FixedPoint.FromInt(-110), session.CurrentState.Fighter(0).PositionX); // 5 帧 × 2.0
    }

    // ─────────────────────────────────────────────────────────────
    // 确定性（A4.11 / A4.12）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Advance_And_StepExact_ProduceIdenticalTrajectory()
    {
        Func<int, FrameInput> holdRight = Hold(InputBits.Right, InputBits.None);

        // 路径 A：时间驱动（1/60 秒逐次 Advance，直到提交满 60 帧）。
        var sessionA = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));
        int guard = 0;
        while (sessionA.CurrentState.Frame < 60 && guard++ < 300)
            sessionA.Advance(1.0 / 60.0, inputProvider: holdRight);

        // 路径 B：显式逐帧（StepExact 60 帧）。
        var sessionB = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));
        sessionB.StepExact(60, inputProvider: holdRight);

        Assert.Equal(60, sessionA.CurrentState.Frame);
        Assert.Equal(sessionB.CurrentState.Frame, sessionA.CurrentState.Frame);
        for (int i = 0; i < 2; i++)
            Assert.Equal(sessionB.CurrentState.Fighter(i), sessionA.CurrentState.Fighter(i)); // 含识别快照逐位一致
    }

    [Fact]
    public void MidSnapshotContinuation_MatchesFullRun()
    {
        // 前 60 帧按前，后 60 帧按后；从第 60 帧快照续跑必须与完整模拟逐位一致。
        Func<int, FrameInput> full = f => f < 60
            ? new FrameInput(f, InputBits.Right, InputBits.None)
            : new FrameInput(f, InputBits.Left, InputBits.None);

        BattleState complete = Run(BattleState.CreateDefault(), full, 120);

        BattleState half = Run(BattleState.CreateDefault(), full, 60);
        BattleState continued = Run(half, full, 60); // 输入函数按帧号取值，续跑自然衔接

        Assert.Equal(complete.Frame, continued.Frame);
        for (int i = 0; i < 2; i++)
            Assert.Equal(complete.Fighter(i), continued.Fighter(i));
    }

    [Fact]
    public void SameInputs_Twice_ProduceIdenticalResults()
    {
        Func<int, FrameInput> inputs = f => new FrameInput(
            f,
            f < 20 ? InputBits.Right : InputBits.Left | InputBits.LightPunch,
            f < 10 ? InputBits.None : InputBits.Down);

        BattleState a = Run(BattleState.CreateDefault(), inputs, 90);
        BattleState b = Run(BattleState.CreateDefault(), inputs, 90);

        Assert.Equal(a.Frame, b.Frame);
        for (int i = 0; i < 2; i++)
            Assert.Equal(a.Fighter(i), b.Fighter(i));
    }

    // ─────────────────────────────────────────────────────────────
    // 长跑与默认状态（A4 扩展：无累计漂移异常）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LongRun_3600Frames_NoDrift()
    {
        BattleState state = Run(
            BattleState.CreateDefault(),
            Hold(InputBits.Right, InputBits.None), 3600);

        // -120 + 2.0×3600 = 7080，逐位精确（05A 无场地边界，越界属 05B 范围）。
        Assert.Equal(FixedPoint.FromInt(7080), X(state, 0));
        Assert.Equal(FixedPoint.Zero, state.Fighter(0).PositionY);
    }

    [Fact]
    public void NoInput_DefaultPositionsAndHeightsUnchanged()
    {
        BattleState state = Run(BattleState.CreateDefault(), Hold(InputBits.None, InputBits.None), 100);

        Assert.Equal(FixedPoint.FromInt(-120), X(state, 0));
        Assert.Equal(FixedPoint.FromInt(120), X(state, 1));
        Assert.Equal(FixedPoint.Zero, state.Fighter(0).PositionY);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).PositionY);
        Assert.Equal(FighterLogicState.Standing, state.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
    }

    // ─────────────────────────────────────────────────────────────
    // 配置校验（A2.4 / A2.5）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void MovementDefinition_RejectsNonPositiveSpeeds(int forward, int backward)
    {
        // 0 与负数一律拒绝（速度是大小，方向由朝向与输入决定）。
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementDefinition(
            FixedPoint.FromInt(forward), FixedPoint.FromInt(backward)));
    }

    [Fact]
    public void MovementDefinition_RejectsAbsurdSpeed()
    {
        // 每帧 32 单位上限：超过视为配置错误（健全性边界）。
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementDefinition(
            FixedPoint.FromInt(33), FixedPoint.FromInt(1)));
        // 恰好等于上限：合法。
        var ok = new MovementDefinition(FixedPoint.FromInt(32), FixedPoint.FromInt(32));
        Assert.Equal(FixedPoint.FromInt(32), ok.WalkForwardSpeed);
    }

    [Fact]
    public void FighterDefinition_WithoutMovement_UsesDefault()
    {
        FighterDefinition kai = DemoRoster.CreateKai();
        var noMovement = new FighterDefinition("Test", kai.Commands, kai.Moves);

        Assert.Equal(MovementDefinition.CreateDefault().WalkForwardSpeed, noMovement.Movement.WalkForwardSpeed);
        Assert.Equal(MovementDefinition.CreateDefault().WalkBackwardSpeed, noMovement.Movement.WalkBackwardSpeed);
    }

    // ─────────────────────────────────────────────────────────────
    // GroundMovement 纯函数直测（不经过模拟）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StepX_DirectRules()
    {
        var movement = MovementDefinition.CreateDefault();
        var forward = new RelativeInput(Forward: true, Back: false, Up: false, Down: false, Attacks: InputBits.None);
        var back = new RelativeInput(Forward: false, Back: true, Up: false, Down: false, Attacks: InputBits.None);

        // 朝右：前 +、后 -。
        Assert.Equal(FixedPoint.FromInt(-118), GroundMovement.StepX(
            FighterLogicState.Standing, FixedPoint.FromInt(-120), Facing.Right, forward, movement));
        Assert.Equal(FixedPoint.FromInt(-120) - FixedPoint.FromFraction(3, 2), GroundMovement.StepX(
            FighterLogicState.Standing, FixedPoint.FromInt(-120), Facing.Right, back, movement));

        // 朝左：前 -、后 +（世界坐标语义，与方向名无关）。
        Assert.Equal(FixedPoint.FromInt(-122), GroundMovement.StepX(
            FighterLogicState.Standing, FixedPoint.FromInt(-120), Facing.Left, forward, movement));
        Assert.Equal(FixedPoint.FromInt(-120) + FixedPoint.FromFraction(3, 2), GroundMovement.StepX(
            FighterLogicState.Standing, FixedPoint.FromInt(-120), Facing.Left, back, movement));

        // 非站立状态：一律原地。
        foreach (FighterLogicState blocked in new[]
                 {
                     FighterLogicState.Crouching,
                     FighterLogicState.Startup,
                     FighterLogicState.Active,
                     FighterLogicState.Recovery,
                 })
        {
            Assert.Equal(FixedPoint.FromInt(-120), GroundMovement.StepX(
                blocked, FixedPoint.FromInt(-120), Facing.Right, forward, movement));
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助：拼接两段输入脚本
    // ─────────────────────────────────────────────────────────────

    private static Func<int, FrameInput> Composite(
        Func<int, FrameInput> first, int firstFrames,
        Func<int, FrameInput> second)
    {
        return frame => frame < firstFrames ? first(frame) : second(frame);
    }
}
