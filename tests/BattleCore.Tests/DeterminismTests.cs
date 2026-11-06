using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 确定性与长跑测试（任务 2）。
///
/// 覆盖：
/// - 连续推进 60 / 600 / 36000 帧的帧编号正确性。
/// - 相同初态 + 相同输入序列 → 相同结果。
/// - 不同渲染频率下，相同模拟时段对应相同逻辑结果。
/// - 中间保存的不可变状态不受后续 Step 影响。
/// - 暂停不推进、积压不静默丢帧。
/// </summary>
public class DeterminismTests
{
    private const int LongRunFrames = 36000; // 600 秒 @ 60Hz

    [Theory]
    [InlineData(60)]
    [InlineData(600)]
    [InlineData(LongRunFrames)]
    public void StepExact_ReachesExactFrameNumber(int frames)
    {
        var sim = new BattleSimulation();
        BattleState state = BattleState.CreateDefault();

        for (int i = 0; i < frames; i++)
            state = sim.Step(state, FrameInput.Empty(state.Frame)).State;

        Assert.Equal(frames, state.Frame);
    }

    [Fact]
    public void LongRun_36000Frames_HasNoAccumulatedDrift()
    {
        // 阶段 02 补修后：StepExact 与 Advance 共用同一记账路径，
        // 因此 TotalSteps 也会同步增加（不再是 0）。
        var session = new BattleSession();
        session.StepExact(LongRunFrames);

        Assert.Equal(LongRunFrames, session.CurrentState.Frame);

        // 再验证调度器路径同样无漂移：按 60FPS 驱动 600 秒。
        var scheduled = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1000));
        for (int i = 0; i < LongRunFrames; i++)
            scheduled.Advance(1.0 / 60.0);

        Assert.Equal(LongRunFrames, scheduled.CurrentState.Frame);
        Assert.Equal(LongRunFrames, scheduled.Scheduler.TotalSteps);
        Assert.Equal(0, scheduled.Scheduler.DroppedStepCount);
    }

    /// <summary>
    /// 关键测试：同一段模拟时间，在不同渲染频率下应产生相同的逻辑帧序列。
    /// 这里比较的是"推进次数与每帧状态"，而不是渲染次数。
    /// </summary>
    [Theory]
    [InlineData(30.0)]
    [InlineData(60.0)]
    [InlineData(144.0)]
    [InlineData(240.0)]
    public void DifferentRenderRates_ProduceSameLogicSequenceForSameDuration(double fps)
    {
        // 基准：直接按逻辑帧推进 600 帧（= 10 秒）
        var reference = new BattleSession();
        reference.StepExact(600);
        BattleState expected = reference.CurrentState;

        // 对照：按给定渲染帧率驱动 10 秒
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1000));
        int renderCount = (int)Math.Round(fps * 10.0);
        double delta = 1.0 / fps;

        for (int i = 0; i < renderCount; i++)
            session.Advance(delta);

        Assert.Equal(600, session.CurrentState.Frame);
        Assert.Equal(expected.Frame, session.CurrentState.Frame);
        for (int p = 0; p < 2; p++)
            Assert.Equal(expected.Fighter(p), session.CurrentState.Fighter(p));
    }

    [Fact]
    public void SameInitialStateAndInputs_ProduceByteIdenticalResults()
    {
        var inputs = new List<FrameInput>
        {
            FrameInput.Empty(0),
            new(1, InputBits.Right, InputBits.Left),
            new(2, InputBits.Right | InputBits.LightPunch, InputBits.Down),
            new(3, InputBits.Up, InputBits.HeavyKick),
            FrameInput.Empty(4),
        };

        BattleState RunA()
        {
            var sim = new BattleSimulation();
            BattleState s = BattleState.CreateDefault();
            foreach (FrameInput input in inputs)
                s = sim.Step(s, input).State;
            return s;
        }

        BattleState a = RunA();
        BattleState b = RunA();

        Assert.Equal(a.Frame, b.Frame);
        Assert.Equal(a.StateVersion, b.StateVersion);
        Assert.Equal(a.Fighters.Count, b.Fighters.Count);
        for (int i = 0; i < a.Fighters.Count; i++)
            Assert.Equal(a.Fighter(i), b.Fighter(i));
    }

    [Fact]
    public void DifferentSimulationInstances_ProduceSameResult()
    {
        var simA = new BattleSimulation();
        var simB = new BattleSimulation();

        BattleState a = BattleState.CreateDefault();
        BattleState b = BattleState.CreateDefault();

        for (int i = 0; i < 1000; i++)
        {
            a = simA.Step(a, FrameInput.Empty(a.Frame)).State;
            b = simB.Step(b, FrameInput.Empty(b.Frame)).State;
        }

        Assert.Equal(a.Frame, b.Frame);
        for (int i = 0; i < 2; i++)
            Assert.Equal(a.Fighter(i), b.Fighter(i));
    }

    [Fact]
    public void IntermediateSnapshot_IsNotAffectedByLaterSteps()
    {
        var sim = new BattleSimulation();
        BattleState start = BattleState.CreateDefault();

        BattleState mid = sim.Step(start, FrameInput.Empty(0)).State;
        BattleState midCopy = mid;

        // 继续推进很多帧
        BattleState current = mid;
        for (int i = 0; i < 500; i++)
            current = sim.Step(current, FrameInput.Empty(current.Frame)).State;

        // 中间快照必须完全未变（mid 帧号为 1，推进 500 次后 current 应为 501）
        Assert.Equal(1, midCopy.Frame);
        Assert.Equal(1, mid.Frame);
        Assert.Equal(501, current.Frame);
        for (int i = 0; i < 2; i++)
            Assert.Equal(midCopy.Fighter(i), mid.Fighter(i));
    }

    [Fact]
    public void OldState_CanReExecuteSameInput_AndReproduceResult()
    {
        var sim = new BattleSimulation();
        BattleState start = BattleState.CreateDefault();
        var input = FrameInput.Empty(0);

        BattleState firstRun = sim.Step(start, input).State;

        // 用同一个旧状态重新执行同一输入（这正是回滚重放的需求）
        BattleState secondRun = sim.Step(start, input).State;

        Assert.NotSame(firstRun, secondRun);
        Assert.Equal(firstRun.Frame, secondRun.Frame);
        for (int i = 0; i < 2; i++)
            Assert.Equal(firstRun.Fighter(i), secondRun.Fighter(i));
    }

    [Fact]
    public void PausedSession_DoesNotAdvance()
    {
        var session = new BattleSession();
        session.StepExact(10);
        session.Scheduler.Pause();

        for (int i = 0; i < 300; i++)
            session.Advance(1.0 / 60.0);

        Assert.Equal(10, session.CurrentState.Frame);
    }

    [Fact]
    public void InputProvider_ReceivesExactlyTheFrameBeingSimulated()
    {
        var session = new BattleSession();
        var seenFrames = new List<int>();

        for (int i = 0; i < 120; i++)
        {
            session.Advance(1.0 / 60.0, frame =>
            {
                seenFrames.Add(frame);
                return FrameInput.Empty(frame);
            });
        }

        // 输入帧号必须严格连续且从 0 开始，与逻辑帧一一对应。
        Assert.Equal(120, seenFrames.Count);
        for (int i = 0; i < seenFrames.Count; i++)
            Assert.Equal(i, seenFrames[i]);
    }

    [Fact]
    public void InputProvider_ReturningWrongFrame_IsRejected()
    {
        var session = new BattleSession();

        // 故意返回错误帧号
        var ex = Assert.Throws<InvalidOperationException>(
            () => session.Advance(FixedStepScheduler.FixedStepSeconds,
                frame => FrameInput.Empty(frame + 7)));

        Assert.Contains("不一致", ex.Message);
    }

    [Fact]
    public void BattleState_ExposesNoMutableReference()
    {
        var fighters = new[]
        {
            FighterState.Create(0, 1000, -120, Facing.Right),
            FighterState.Create(1, 1000, 120, Facing.Left),
        };
        var state = new BattleState(0, fighters, 1);

        // 修改源数组不影响状态
        fighters[0] = FighterState.Create(0, 1, 999, Facing.Left);
        Assert.Equal(1000, state.Fighter(0).MaxHealth);

        // Fighters 暴露为 IReadOnlyList，无法 Add/索引赋值
        Assert.IsAssignableFrom<IReadOnlyList<FighterState>>(state.Fighters);

        // 底层类型是 ReadOnlyCollection，不是可写的 FighterState[]
        Assert.False(state.Fighters is FighterState[],
            "Fighters 不得直接暴露可写数组，否则外部可绕过错路修改权威状态。");
    }
}
