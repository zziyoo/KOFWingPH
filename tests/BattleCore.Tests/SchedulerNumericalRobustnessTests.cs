using NewKOF.BattleCore.Simulation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 02 补修第二轮：数值健壮性回归测试。
///
/// 修复前实测到的两个真实缺陷（2026-11-07 本机 .NET 8 重新测量）：
/// <list type="number">
/// <item><b>超大 delta 溢出</b>：<c>(huge + eps) / step</c> 得到 ∞，
///       <c>(long)Math.Floor(∞)</c> 在 .NET 8 上饱和为 <c>long.MinValue</c>（负数），
///       于是 <c>if (pending &gt; 0)</c> 判定为假，<b>整个丢弃记账被跳过</b>：
///       <c>DroppedStepCount</c> 停在 0，<c>_accumulator</c> 永久停在 4.49e307 且不清零，
///       之后每次 <c>Advance</c> 都返回上限帧数（实测 8）——相当于永久满速追帧、且账目不可见。
///       （旧注释曾写作「溢出为负 + NaN」，实测不成立，已更正。）</item>
/// <item><b>单次巨大 delta 精度损失</b>：单次 3600 秒用「循环减步长」处理时，
///       连续减法累积浮点残差，最后残留 0.0166666484 &lt; 1/60，少算 1 帧
///       （实测 215999 而非 216000）。</item>
/// </list>
/// 修复方式：整除一次算出整帧数 + 余数（O(1)），并对转 long 做饱和保护。
/// </summary>
public class SchedulerNumericalRobustnessTests
{
    private const double Step = 1.0 / 60.0;

    [Fact]
    public void Advance_MaxValueDelta_DoesNotProduceNaNOrNegativeCounters()
    {
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 8);

        int steps = scheduler.Advance(double.MaxValue / 4.0);
        scheduler.CommitStep(steps);

        // 累加器绝不能是 NaN / 负数 / 天文数字（修复前停在 4.49e307 且丢弃计数为 0）
        Assert.False(double.IsNaN(scheduler.AccumulatedSeconds), "累加器不得为 NaN");
        Assert.True(scheduler.AccumulatedSeconds >= 0, "累加器不得为负");
        Assert.True(scheduler.AccumulatedSeconds < Step);

        // 丢弃计数必须真实记账（修复前因 (long) 饱和为负数而被整段跳过，停在 0）
        Assert.True(scheduler.DroppedStepCount > 0, "超大 delta 应记录为正的丢弃预算");
        Assert.True(scheduler.TotalSteps > 0);
        Assert.Equal(8, scheduler.TotalSteps);
    }

    [Fact]
    public void Advance_AfterHugeDelta_SchedulerStillWorksNormally()
    {
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 8);
        scheduler.Advance(double.MaxValue / 4.0);
        scheduler.CommitStep(8);

        // 修复前累加器被 NaN 污染，之后所有 Advance 都返回 0（永久卡死）
        int after = scheduler.Advance(Step);
        scheduler.CommitStep(after);

        Assert.Equal(1, after);
        Assert.Equal(9, scheduler.TotalSteps);
        Assert.False(double.IsNaN(scheduler.AccumulatedSeconds));
    }

    [Fact]
    public void Advance_VariousHugeFiniteDeltas_NeverProduceNaN()
    {
        foreach (double delta in new[] { 1e6, 1e12, 1e100, 1e300, double.MaxValue })
        {
            var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 4);
            int steps = scheduler.Advance(delta);
            scheduler.CommitStep(steps);

            Assert.False(double.IsNaN(scheduler.AccumulatedSeconds), $"delta={delta:E1} 产生 NaN");
            Assert.True(scheduler.AccumulatedSeconds >= 0, $"delta={delta:E1} 累加器为负");
            Assert.True(scheduler.DroppedStepCount > 0, $"delta={delta:E1} 未记录丢弃");
            Assert.True(scheduler.TotalSteps >= 0);
        }
    }

    [Fact]
    public void Advance_OneHourSingleDelta_ProducesExactly216000Frames()
    {
        // 修复前：215999（少 1 帧，连续减法累积浮点误差）
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 300_000);
        int steps = scheduler.Advance(3600.0);
        scheduler.CommitStep(steps);

        Assert.Equal(216_000, steps);
        Assert.Equal(216_000, scheduler.TotalSteps);
        Assert.Equal(0, scheduler.DroppedStepCount);
    }

    [Theory]
    [InlineData(1.0, 60)]
    [InlineData(10.0, 600)]
    [InlineData(60.0, 3600)]
    [InlineData(600.0, 36000)]
    [InlineData(3600.0, 216000)]
    public void Advance_SingleLargeDelta_MatchesExpectedFrameCount(double seconds, int expectedFrames)
    {
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: expectedFrames + 10);
        int steps = scheduler.Advance(seconds);
        scheduler.CommitStep(steps);

        Assert.Equal(expectedFrames, steps);
    }

    [Fact]
    public void Advance_SingleLargeDelta_EqualsChunkedAccumulation()
    {
        // 关键等价性：一次性投入 60 秒，与分 3600 次每帧 1/60 秒，结果必须相同。
        var single = new FixedStepScheduler(maxStepsPerAdvance: 10_000);
        int singleSteps = single.Advance(60.0);
        single.CommitStep(singleSteps);

        var chunked = new FixedStepScheduler(maxStepsPerAdvance: 10_000);
        long chunkedTotal = 0;
        for (int i = 0; i < 3600; i++)
        {
            int s = chunked.Advance(Step);
            chunked.CommitStep(s);
            chunkedTotal += s;
        }

        Assert.Equal(3600, singleSteps);
        Assert.Equal(singleSteps, chunkedTotal);
        Assert.Equal(single.TotalSteps, chunked.TotalSteps);
    }

    [Fact]
    public void Advance_AccumulatorAlwaysStaysInRange_UnderRandomDeltas()
    {
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 10);
        var rng = new Random(20261107);

        for (int i = 0; i < 5000; i++)
        {
            // 混合正常与极端 delta
            double delta = i % 500 == 0
                ? 1e9 * rng.NextDouble()
                : rng.NextDouble() * 0.05;

            int steps = scheduler.Advance(delta);
            scheduler.CommitStep(steps);

            Assert.False(double.IsNaN(scheduler.AccumulatedSeconds));
            Assert.True(scheduler.AccumulatedSeconds >= 0);
            Assert.True(scheduler.AccumulatedSeconds < Step,
                $"累加器越界：{scheduler.AccumulatedSeconds:R} >= {Step:R}");
            Assert.True(scheduler.DroppedStepCount >= 0);
        }
    }

    [Fact]
    public void Advance_DroppedCountSaturatesInsteadOfWrappingNegative()
    {
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 1);

        // 反复投入天文数字 delta：丢弃计数既要保持非负（饱和而非回绕），
        // 也必须真的记账为正数——修复前该计数恒为 0（整段丢弃记账被跳过）。
        for (int i = 0; i < 50; i++)
        {
            int steps = scheduler.Advance(double.MaxValue / 8.0);
            scheduler.CommitStep(steps);
            Assert.True(scheduler.DroppedStepCount > 0,
                $"第 {i} 次后丢弃计数未记账：{scheduler.DroppedStepCount}");
            Assert.True(scheduler.DroppedStepCount >= 0,
                $"第 {i} 次后丢弃计数溢出为负：{scheduler.DroppedStepCount}");
            Assert.True(scheduler.AccumulatedSeconds < Step,
                $"第 {i} 次后累加器未收敛：{scheduler.AccumulatedSeconds:R}");
        }

        // 饱和到 long.MaxValue，而不是回绕成负数
        Assert.Equal(long.MaxValue, scheduler.DroppedStepCount);
    }

    [Fact]
    public void Advance_ZeroAndTinyDeltas_NeverAdvance()
    {
        var scheduler = new FixedStepScheduler();
        for (int i = 0; i < 1000; i++)
        {
            int steps = scheduler.Advance(1e-9);
            scheduler.CommitStep(steps);
        }
        Assert.Equal(0, scheduler.TotalSteps);
        Assert.False(double.IsNaN(scheduler.AccumulatedSeconds));
    }

    [Fact]
    public void Advance_NormalFrameRate_StillZeroDriftAfterFixes()
    {
        // 确认数值健壮性改动没有破坏原有零漂移特性
        foreach (double fps in new[] { 30.0, 60.0, 144.0, 240.0 })
        {
            var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 1000);
            int renders = (int)Math.Round(fps * 600.0);
            double delta = 1.0 / fps;

            for (int i = 0; i < renders; i++)
            {
                int s = scheduler.Advance(delta);
                scheduler.CommitStep(s);
            }

            Assert.Equal(36000, scheduler.TotalSteps);
            Assert.Equal(0, scheduler.DroppedStepCount);
        }
    }
}
