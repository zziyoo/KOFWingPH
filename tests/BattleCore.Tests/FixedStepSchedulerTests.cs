using NewKOF.BattleCore.Simulation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 固定步长调度器测试（任务 2）。
///
/// 这些测试直接覆盖"渲染帧率 → 逻辑帧数"的映射关系，
/// 不依赖 Godot 图形窗口，是阶段 02 调度行为的主要证据。
///
/// 注意：本文件验证的是**调度器**（纯 C#）的行为。
/// Godot 层 <c>BattleScene</c> 只是把 delta 转交给本调度器，
/// 其集成行为另见 <see cref="GodotIntegrationContractTests"/> 的说明。
/// </summary>
public class FixedStepSchedulerTests
{
    /// <summary>按给定帧率与时长驱动调度器，返回累计<b>已提交</b>逻辑帧数。</summary>
    private static long RunAtFps(double fps, double seconds, int maxStepsPerAdvance = 1000)
    {
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance);
        int renderCount = (int)Math.Round(fps * seconds);
        double delta = 1.0 / fps;

        for (int i = 0; i < renderCount; i++)
            Tick(scheduler, delta);

        return scheduler.TotalSteps;
    }

    /// <summary>
    /// 模拟「正确调用方」的一次时间推进：取得预算，然后为每一帧提交记账。
    /// 阶段 02 补修后 <see cref="FixedStepScheduler.Advance"/> 只返回预算、
    /// 不再自行增加 <c>TotalSteps</c>，因此测试必须显式提交。
    /// </summary>
    private static int Tick(FixedStepScheduler scheduler, double delta)
    {
        int budget = scheduler.Advance(delta);
        scheduler.CommitStep(budget);
        return budget;
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(60.0)]
    [InlineData(144.0)]
    [InlineData(240.0)]
    [InlineData(59.94)]
    [InlineData(165.0)]
    [InlineData(25.0)]
    [InlineData(360.0)]
    public void Advance_600Seconds_ProducesExactly36000LogicFrames_RegardlessOfRenderRate(double fps)
    {
        // 600 秒 × 60Hz = 36000 逻辑帧，与渲染帧率无关。
        long frames = RunAtFps(fps, 600.0, maxStepsPerAdvance: 1000);
        Assert.Equal(36000, frames);
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(60.0)]
    [InlineData(144.0)]
    [InlineData(240.0)]
    public void Advance_10Seconds_ProducesExactly600LogicFrames(double fps)
    {
        long frames = RunAtFps(fps, 10.0, maxStepsPerAdvance: 1000);
        Assert.Equal(600, frames);
    }

    [Fact]
    public void Advance_SingleCallEqualToStep_ProducesOneFrame()
    {
        var scheduler = new FixedStepScheduler();
        int steps = Tick(scheduler, FixedStepScheduler.FixedStepSeconds);

        Assert.Equal(1, steps);
        Assert.Equal(1, scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_AccumulatesSubStepDeltas_WithoutLosingOrDuplicatingFrames()
    {
        var scheduler = new FixedStepScheduler();

        // 1 秒拆成 100 个 10ms 小块（远小于 1/60 秒）。
        for (int i = 0; i < 100; i++)
            Tick(scheduler, 0.01);

        Assert.Equal(60, scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_LargeDelta_IsCappedAndDroppedFramesAreAccountedFor()
    {
        // 单次 1 秒 delta，上限 5 帧：应推进 5 帧，其余记为丢弃。
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 5);
        int steps = Tick(scheduler, 1.0);

        Assert.Equal(5, steps);
        Assert.Equal(5, scheduler.TotalSteps);
        Assert.True(scheduler.DroppedStepCount >= 54,
            $"应记录约 55 个欠帧，实际 {scheduler.DroppedStepCount}");

        // 关键：丢弃是显式的，不是静默的。
        Assert.True(scheduler.DroppedStepCount + scheduler.TotalSteps >= 59);
    }

    [Fact]
    public void Advance_BacklogIsCapped_NoUnboundedCatchUp()
    {
        // 模拟一次长卡顿后连续渲染：不允许出现单帧推进几百帧的情况。
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 5);

        int stepsAfterStall = Tick(scheduler, 10.0); // 卡顿 10 秒
        Assert.Equal(5, stepsAfterStall);

        // 后续每帧最多补 5 帧，不会无限追帧。
        for (int i = 0; i < 10; i++)
            Assert.True(Tick(scheduler, 1.0 / 60.0) <= 5);
    }

    [Fact]
    public void Advance_WhenPaused_DoesNotAdvance()
    {
        var scheduler = new FixedStepScheduler();
        scheduler.Pause();

        for (int i = 0; i < 600; i++)
            Assert.Equal(0, Tick(scheduler, 1.0 / 60.0));

        Assert.Equal(0, scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_AfterResume_DoesNotBackfillPausedTime()
    {
        var scheduler = new FixedStepScheduler();

        Tick(scheduler, 1.0);                      // 1 秒 -> 60 帧
        Assert.Equal(60, scheduler.TotalSteps);

        scheduler.Pause();
        for (int i = 0; i < 120; i++)
            Tick(scheduler, 1.0 / 60.0);           // 暂停 2 秒，不推进
        Assert.Equal(60, scheduler.TotalSteps);

        scheduler.Resume();

        // 恢复后第一帧只推进 1 帧（或很少），不得补上暂停期间的 120 帧。
        int firstAfterResume = Tick(scheduler, 1.0 / 60.0);
        Assert.InRange(firstAfterResume, 0, 2);
        Assert.True(scheduler.TotalSteps <= 62,
            $"恢复后不得回补暂停时间，实际总数 {scheduler.TotalSteps}");
    }

    [Fact]
    public void Advance_RejectsNegativeDelta()
    {
        var scheduler = new FixedStepScheduler();
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(-0.016));
    }

    [Fact]
    public void Advance_RejectsNaNAndInfinity()
    {
        var scheduler = new FixedStepScheduler();
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(double.NegativeInfinity));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveMaxSteps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepScheduler(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepScheduler(-1));
    }

    [Fact]
    public void Advance_AccumulatorStaysBelowOneStep()
    {
        var scheduler = new FixedStepScheduler();

        for (int i = 0; i < 1000; i++)
        {
            Tick(scheduler, 0.0037);
            Assert.True(scheduler.AccumulatedSeconds >= 0);
            Assert.True(scheduler.AccumulatedSeconds < FixedStepScheduler.FixedStepSeconds);
        }
    }

    [Fact]
    public void Advance_ZeroDelta_ProducesNoSteps()
    {
        var scheduler = new FixedStepScheduler();
        Assert.Equal(0, scheduler.Advance(0.0));
        Assert.Equal(0, scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_TotalStepsIsMonotonic()
    {
        var scheduler = new FixedStepScheduler();
        long previous = 0;

        for (int i = 0; i < 500; i++)
        {
            Tick(scheduler, 0.017);
            Assert.True(scheduler.TotalSteps >= previous);
            previous = scheduler.TotalSteps;
        }
    }
}
