using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 05B：运动系统的确定性验收（任务书 E 组）。
///
/// <para>输入脚本混合步行 / 跳跃 / 蓄力方向 / 攻击键——验证运动数值进入权威快照后，
/// 「同初态同输入必得同结果」的既有契约不因运动而退化：</para>
/// <list type="bullet">
/// <item>同脚本连续两次运行逐位一致；</item>
/// <item>从中间快照续跑与完整运行一致（含速度/跳跃类型/朝向全部字段）；</item>
/// <item>Advance 与 StepExact 一致；不同渲染帧率下相同逻辑输入一致；</item>
/// <item>暂停不改变坐标/速度/跳跃状态；10000 帧长跑确定性且帧同步不变量成立。</item>
/// </list>
/// </summary>
public class MovementDeterminismTests
{
    /// <summary>混合脚本：步行 → 跳跃 → 空中方向变化 → 落地 → 招式 → 相向推挤。</summary>
    private static FrameInput Script(int f)
    {
        InputBits p1 = f switch
        {
            < 20 => InputBits.Right,
            20 => InputBits.Up | InputBits.Right,
            < 50 => InputBits.Right,           // 空中（水平速度已锁定）
            50 => InputBits.Left | InputBits.LightPunch,
            < 80 => InputBits.Left,
            < 120 => InputBits.Right,          // 向 P2 推进
            120 => InputBits.Up | InputBits.Left,
            < 160 => InputBits.Left,
            _ => InputBits.None,
        };
        InputBits p2 = f switch
        {
            < 30 => InputBits.None,
            30 => InputBits.Up | InputBits.Left,
            < 60 => InputBits.Left,
            < 100 => InputBits.Right,
            100 => InputBits.Up | InputBits.Down,   // SOCD：上优先 → 跳跃
            < 140 => InputBits.Down,
            _ => InputBits.None,
        };
        return new FrameInput(f, p1, p2);
    }

    private static BattleState RunAll(BattleState initial, int frames, out BattleState[] snapshots)
    {
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        snapshots = new BattleState[frames + 1];
        snapshots[0] = initial;
        BattleState state = initial;
        for (int f = 0; f < frames; f++)
        {
            state = sim.Step(state, Script(state.Frame)).State;
            snapshots[f + 1] = state;
        }
        return state;
    }

    private static void AssertFightersEqual(BattleState expected, BattleState actual)
    {
        Assert.Equal(expected.Frame, actual.Frame);
        for (int i = 0; i < 2; i++)
            Assert.Equal(expected.Fighter(i), actual.Fighter(i));
    }

    [Fact]
    public void SameInputs_Twice_ProduceIdenticalResults()
    {
        BattleState first = RunAll(BattleState.CreateDefault(), 600, out _);
        BattleState second = RunAll(BattleState.CreateDefault(), 600, out _);

        AssertFightersEqual(first, second);
    }

    [Fact]
    public void MidSnapshotContinuation_MatchesFullRun_IncludingVelocityAndJump()
    {
        BattleState full = RunAll(BattleState.CreateDefault(), 600, out BattleState[] snapshots);

        // 从第 300 帧快照（可能正处于空中/动作中）续跑。
        BattleState mid = snapshots[300];
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        BattleState state = mid;
        for (int f = 300; f < 600; f++)
            state = sim.Step(state, Script(state.Frame)).State;

        AssertFightersEqual(full, state);

        // 中间快照本身不可被续跑污染（不可变契约）。
        AssertFightersEqual(snapshots[300], mid);
    }

    [Fact]
    public void Advance_And_StepExact_ProduceIdenticalResults()
    {
        // 路径 A：时间驱动，1/60 秒逐次推进至 600 帧。
        var sessionA = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));
        int guard = 0;
        while (sessionA.CurrentState.Frame < 600 && guard++ < 2000)
            sessionA.Advance(1.0 / 60.0, inputProvider: Script);

        // 路径 B：显式逐帧。
        var sessionB = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));
        sessionB.StepExact(600, inputProvider: Script);

        Assert.Equal(600, sessionA.CurrentState.Frame);
        AssertFightersEqual(sessionB.CurrentState, sessionA.CurrentState);
        Assert.True(sessionA.IsFrameSyncIntact);
        Assert.True(sessionB.IsFrameSyncIntact);
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(144.0)]
    [InlineData(240.0)]
    public void DifferentRenderFps_SameLogicInputs_SameResult(double fps)
    {
        // 相同的逻辑帧输入，以不同渲染帧率的 delta 推进——逻辑结果必须逐位一致。
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1000),
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));

        int renders = (int)(fps * 10); // 10 秒
        double delta = 1.0 / fps;
        for (int i = 0; i < renders; i++)
            session.Advance(delta, inputProvider: Script);

        Assert.True(session.CurrentState.Frame >= 590, $"帧数不足：{session.CurrentState.Frame}");

        // 与显式逐帧的同帧数结果比较（取 Advance 实际到达的帧）。
        BattleState reference = BattleState.CreateDefault();
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        for (int f = 0; f < session.CurrentState.Frame; f++)
            reference = sim.Step(reference, Script(f)).State;

        AssertFightersEqual(reference, session.CurrentState);
        Assert.Equal(0, session.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void PausedSession_FreezesPositionVelocityAndJumpState()
    {
        var session = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));

        // 推进到 P1 跳跃空中（帧 20 请求跳跃 → 帧 23 起跳）。
        session.StepExact(30, inputProvider: Script);
        Assert.Equal(FighterLogicState.Airborne, session.CurrentState.Fighter(0).LogicState);

        BattleState before = session.CurrentState;
        session.Scheduler.Pause();
        int steps = session.Advance(5.0, inputProvider: Script);

        Assert.Equal(0, steps);
        Assert.Equal(before.Frame, session.CurrentState.Frame);
        for (int i = 0; i < 2; i++)
            Assert.Equal(before.Fighter(i), session.CurrentState.Fighter(i)); // 坐标/速度/跳跃状态全部不变
    }

    [Fact]
    public void LongRun_10000Frames_Deterministic_AndFrameSyncIntact()
    {
        BattleState first = RunAll(BattleState.CreateDefault(), 10000, out _);
        BattleState second = RunAll(BattleState.CreateDefault(), 10000, out _);

        AssertFightersEqual(first, second);

        // 会话级长跑：帧同步不变量在运动系统介入后仍成立。
        var session = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()));
        session.StepExact(10000, inputProvider: Script);
        Assert.True(session.IsFrameSyncIntact);
        Assert.Equal(10000, session.CurrentState.Frame);
    }

    [Fact]
    public void BothCandidatesConstrainedTogether_NoOrderAdvantage()
    {
        // 双人推挤的顺序公平性：P1 先动 / P2 先动的两种「计算顺序」在实现中
        // 由同一候选数组统一处理——用镜像场景证明结果互为镜像（无 P1 先手优势）。
        var wide = StageDefinition.CreateDefault();
        var simA = new BattleSimulation(DemoRoster.CreateKaiVsRei(), wide);
        var simB = new BattleSimulation(DemoRoster.CreateKaiVsRei(), wide);

        BattleState a = BattleState.CreateDefault();
        BattleState b = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, 120, Facing.Left),
            FighterState.Create(1, 1000, -120, Facing.Right),
        }, BattleState.CreateDefault().StateVersion);

        for (int f = 0; f < 120; f++)
        {
            a = simA.Step(a, new FrameInput(f, InputBits.Right, InputBits.Right)).State;
            b = simB.Step(b, new FrameInput(f, InputBits.Left, InputBits.Left)).State;
            // A：P1 向右推进 P2；B 是 A 的完全镜像（P1 在右侧向左推进 P2）。
            Assert.Equal(-a.Fighter(0).PositionX, b.Fighter(0).PositionX);
            Assert.Equal(-a.Fighter(1).PositionX, b.Fighter(1).PositionX);
        }
    }
}
