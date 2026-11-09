using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class SpriteTimelineTests
{
    [Fact]
    public void TimelineHonorsDurationsAndLoopStart()
    {
        var clip = new SpriteTimeline(new[] { 2, 3, 1 }, 1);
        Assert.Equal(new[] { 0, 0, 1, 1, 1, 2, 1, 1, 1, 2 },
            Enumerable.Range(0, 10).Select(clip.FrameAt));
    }

    [Fact]
    public void InfiniteFrameHoldsAndZeroDurationIsSkipped()
    {
        var clip = new SpriteTimeline(new[] { 0, 2, -1, 2 }, 0);
        Assert.Equal(1, clip.FrameAt(0));
        Assert.Equal(2, clip.FrameAt(2));
        Assert.Equal(2, clip.FrameAt(10000));
    }

    [Fact]
    public void CommittedFrameAnimationIsIdenticalAcrossRenderRatesAndHitstop()
    {
        static int[] Drive(int fps)
        {
            var scheduler = new FixedStepScheduler();
            var clock = new FighterVisualClock();
            var clip = new SpriteTimeline(new[] { 2, 3, 4 }, 1);
            var result = new List<int>();
            int logical = 0;
            for (int render = 0; render < fps; render++)
            {
                int steps = scheduler.Advance(1d / fps);
                for (int i = 0; i < steps; i++, logical++)
                {
                    int visual = clock.Observe(logical, FighterLogicState.Standing, null,
                        logical is >= 10 and < 15);
                    result.Add(clip.FrameAt(visual));
                }
            }
            return result.ToArray();
        }
        var baseline = Drive(60);
        Assert.Equal(60, baseline.Length);
        Assert.Equal(baseline, Drive(30));
        Assert.Equal(baseline, Drive(144));
        Assert.All(baseline[10..15], frame => Assert.Equal(baseline[9], frame));
    }
}
