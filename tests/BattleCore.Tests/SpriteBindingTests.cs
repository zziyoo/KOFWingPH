using NewKOF.BattleCore.Presentation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class SpriteBindingTests
{
    [Fact]
    public void OverrideIsPerSeatAndCanBeLimitedToOneFighter()
    {
        Assert.Equal("Kai", SpriteBinding.SelectVisualId("Kai", null, null));
        Assert.Equal("terry98", SpriteBinding.SelectVisualId("Kai", "terry98", "Kai"));
        Assert.Equal("Rei", SpriteBinding.SelectVisualId("Rei", "terry98", "Kai"));
        Assert.Equal("terry98", SpriteBinding.SelectVisualId("Rei", "terry98", null));
    }

    [Fact]
    public void ParsesLegacyAndV2WithIndependentVisualIdentity()
    {
        var legacy = SpriteBinding.Parse("{\"clips\":{\"idle\":0}}", "Kai");
        Assert.Equal("Kai", legacy.VisualId);
        Assert.Equal(0, legacy.Clips["idle"].Action);
        var binding = SpriteBinding.Parse("""
            {"schemaVersion":2,"visualId":"terry98","scale":1.25,"offsetX":3,"offsetY":-2,
             "clips":{"attack":{"action":200,"clock":"move","startTick":2,"firstFrame":1,"lastFrame":2,"loop":false}}}
            """, "terry98");
        Assert.Equal("terry98", binding.VisualId);
        Assert.Equal(1.25, binding.Scale);
        Assert.Equal(3, binding.OffsetX);
        var clip = binding.Clips["attack"];
        Assert.Equal(200, clip.Action);
        Assert.True(clip.MoveClock);
        Assert.Equal(2, clip.StartTick);
        Assert.False(clip.Loop);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"visualId\":\"../x\",\"clips\":{}}")]
    [InlineData("{\"schemaVersion\":9,\"clips\":{}}")]
    [InlineData("{\"schemaVersion\":2,\"visualId\":\"x\",\"scale\":0,\"clips\":{}}")]
    public void InvalidBindingIsRejected(string json) => Assert.ThrowsAny<Exception>(() => SpriteBinding.Parse(json, "x"));

    [Fact]
    public void NonLoopingTimelineHoldsFinalFrameAndCanPlaySegment()
    {
        var clip = new SpriteTimeline(new[] { 2, 3, 4 }, 0, loop: false, firstFrame: 1, lastFrame: 2);
        Assert.Equal(1, clip.FrameAt(0));
        Assert.Equal(1, clip.FrameAt(2));
        Assert.Equal(2, clip.FrameAt(3));
        Assert.Equal(2, clip.FrameAt(100));
    }

    [Fact]
    public void MovePhaseCanOverrideGenericClip()
    {
        var binding = SpriteBinding.Parse("""
            {"schemaVersion":2,"visualId":"x","clips":{"idle":{"action":0},"sweep":{"action":400}},
             "moves":{"SweepPrototype":{"Active":"sweep"}}}
            """, "x");
        Assert.Equal("sweep", binding.ResolveClip("idle", "SweepPrototype", "Active"));
        Assert.Equal("idle", binding.ResolveClip("idle", "SweepPrototype", "Recovery"));
        Assert.Equal("idle", binding.ResolveClip("idle", "other", "Active"));
    }
}
