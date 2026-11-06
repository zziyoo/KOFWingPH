using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 02 补修：帧同步缺陷回归测试。
///
/// 本文件的两个缺陷类：
/// <list type="bullet">
/// <item><b>问题 1</b>：<c>Advance</c> 先增加 <c>Scheduler.TotalSteps</c>，
///       后执行输入提供者与 <c>Step()</c>。中途抛异常 → 调度器已前进、
///       状态未跟上 → 半提交状态。</item>
/// <item><b>问题 2</b>：<c>StepExact</c> 只改 <c>CurrentState.Frame</c>，
///       不改 <c>Scheduler.TotalSteps</c> → 与 <c>Advance</c> 混用时计数错位。</item>
/// </list>
///
/// 修复前这些测试应当失败；修复后必须全部通过。
/// </summary>
public class FrameSyncRegressionTests
{
    // ─────────────────────────────────────────────────────────────
    // 问题 1：异常导致帧计数不同步
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Advance_InputProviderThrows_SchedulerDoesNotCountUncommittedStep()
    {
        var session = new BattleSession();
        int frameBefore = session.CurrentState.Frame;
        long stepsBefore = session.Scheduler.TotalSteps;

        // 第 0 帧的输入提供者直接抛异常
        Assert.Throws<InvalidOperationException>(() =>
            session.Advance(FixedStepScheduler.FixedStepSeconds,
                frame => throw new InvalidOperationException("模拟输入故障")));

        // 权威状态没有推进
        Assert.Equal(frameBefore, session.CurrentState.Frame);

        // 调度器也不得声称推进过（这是修复前会失败的关键断言）
        Assert.Equal(stepsBefore, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_MultiStepBatch_ThrowsMidway_CountsOnlyCommittedSteps()
    {
        // 一次补 3 帧，第 2 帧（frame == 1）抛异常。
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 3));
        long stepsBefore = session.Scheduler.TotalSteps;

        Assert.Throws<InvalidOperationException>(() =>
            session.Advance(3 * FixedStepScheduler.FixedStepSeconds, frame =>
                frame == 1
                    ? throw new InvalidOperationException("第二帧故障")
                    : FrameInput.Empty(frame)));

        // 只有第 0 帧成功提交
        Assert.Equal(1, session.CurrentState.Frame);

        // 调度器计数必须与已提交帧数一致，而不是声称推进了 3 帧
        Assert.Equal(stepsBefore + 1, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_AfterFailure_ResumesFromConsistentFrame()
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 3));
        bool shouldThrow = true;

        Assert.Throws<InvalidOperationException>(() =>
            session.Advance(3 * FixedStepScheduler.FixedStepSeconds, frame =>
                frame == 1 && shouldThrow
                    ? throw new InvalidOperationException("故意故障")
                    : FrameInput.Empty(frame)));

        int frameAfterFailure = session.CurrentState.Frame;
        long stepsAfterFailure = session.Scheduler.TotalSteps;
        Assert.Equal(frameAfterFailure, stepsAfterFailure);

        // 关闭故障后继续推进，帧号必须从一致点接着走，不跳号不重复。
        shouldThrow = false;
        session.Advance(FixedStepScheduler.FixedStepSeconds);
        Assert.Equal(frameAfterFailure + 1, session.CurrentState.Frame);
        Assert.Equal(stepsAfterFailure + 1, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_InputFrameMismatch_DoesNotLeaveSchedulerAheadOfState()
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 2));

        Assert.Throws<InvalidOperationException>(() =>
            session.Advance(2 * FixedStepScheduler.FixedStepSeconds, frame => FrameInput.Empty(frame + 5)));

        // 第一帧就因帧号错误失败，状态与计数都必须停在 0
        Assert.Equal(0, session.CurrentState.Frame);
        Assert.Equal(0, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_SimulationThrows_SchedulerStaysConsistent()
    {
        // 用非法初态构造会话，使 Step() 在内部抛异常（输入帧号为负无法触发，
        // 这里改为让 inputProvider 返回非法帧号触发 Step 的校验异常）。
        var session = new BattleSession();
        long stepsBefore = session.Scheduler.TotalSteps;

        Assert.ThrowsAny<Exception>(() =>
            session.Advance(FixedStepScheduler.FixedStepSeconds,
                frame => new FrameInput(-1, InputBits.None, InputBits.None)));

        Assert.Equal(0, session.CurrentState.Frame);
        Assert.Equal(stepsBefore, session.Scheduler.TotalSteps);
    }

    // ─────────────────────────────────────────────────────────────
    // 问题 2：StepExact 与调度器计数不一致
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StepExact_UpdatesSchedulerTotalSteps()
    {
        var session = new BattleSession();
        session.StepExact(10);

        Assert.Equal(10, session.CurrentState.Frame);
        // 修复前 TotalSteps 保持 0，此处是核心回归断言
        Assert.Equal(10, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void StepExact_ThenAdvance_KeepsFrameAndStepCountAligned()
    {
        var session = new BattleSession();
        session.StepExact(5);

        Assert.Equal(5, session.CurrentState.Frame);
        Assert.Equal(5, session.Scheduler.TotalSteps);

        session.Advance(FixedStepScheduler.FixedStepSeconds);

        Assert.Equal(6, session.CurrentState.Frame);
        Assert.Equal(6, session.Scheduler.TotalSteps);
        Assert.Equal(session.CurrentState.Frame, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_ThenStepExact_KeepsFrameAndStepCountAligned()
    {
        var session = new BattleSession();
        session.Advance(FixedStepScheduler.FixedStepSeconds);

        Assert.Equal(1, session.CurrentState.Frame);
        Assert.Equal(1, session.Scheduler.TotalSteps);

        session.StepExact(4);

        Assert.Equal(5, session.CurrentState.Frame);
        Assert.Equal(5, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void AlternatingStepExactAndAdvance_NeverDesynchronizes()
    {
        var session = new BattleSession();

        for (int i = 0; i < 200; i++)
        {
            if (i % 3 == 0)
                session.StepExact(2);
            else
                session.Advance(FixedStepScheduler.FixedStepSeconds);

            // 核心不变量：状态帧号恒等于调度器累计步数（初始帧号为 0）
            Assert.Equal(session.CurrentState.Frame, session.Scheduler.TotalSteps);
        }

        Assert.Equal(session.CurrentState.Frame, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void StepExact_WhilePaused_StillAdvancesFrameAndCounter()
    {
        var session = new BattleSession();
        session.Scheduler.Pause();

        session.StepExact(3);

        // 暂停只阻止「时间驱动」推进；显式单步是调试/工具用途，必须生效。
        Assert.Equal(3, session.CurrentState.Frame);
        Assert.Equal(3, session.Scheduler.TotalSteps);

        // 但时间驱动仍被暂停阻挡
        session.Advance(FixedStepScheduler.FixedStepSeconds);
        Assert.Equal(3, session.CurrentState.Frame);
    }

    [Fact]
    public void StepExact_Zero_DoesNotChangeAnything()
    {
        var session = new BattleSession();
        session.StepExact(0);

        Assert.Equal(0, session.CurrentState.Frame);
        Assert.Equal(0, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void StepExact_RejectsNegativeCount()
    {
        var session = new BattleSession();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.StepExact(-1));
    }
}
