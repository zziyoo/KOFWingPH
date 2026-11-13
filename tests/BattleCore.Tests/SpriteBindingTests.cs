using NewKOF.BattleCore.Presentation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class SpriteBindingTests
{
    [Fact]
    public void OptionalClipAppearanceDoesNotAffectLegacyClips()
    {
        var binding = SpriteBinding.Parse("""
            {"clips":{"idle":0,"projectile_Fireball":{"action":1070,"offsetX":16,
              "offsetY":48,"silhouetteColor":"#FFBC5B"}}}
            """, "x");
        Assert.Equal((16, 48, "#FFBC5B"), (binding.Clips["projectile_Fireball"].OffsetX,
            binding.Clips["projectile_Fireball"].OffsetY, binding.Clips["projectile_Fireball"].SilhouetteColor));
        Assert.Equal(0, binding.Clips["idle"].OffsetX);
        Assert.Null(binding.Clips["idle"].SilhouetteColor);
        Assert.Throws<ArgumentException>(() =>
            SpriteBinding.Parse("""{"clips":{"bad":{"action":1,"silhouetteColor":"orange"}}}""", "x"));
    }
    [Fact]
    public void ExplicitFrameScheduleUsesMoveTicksAndLeavesDefaultTimelineUnchanged()
    {
        var binding = SpriteBinding.Parse("""
            {"schemaVersion":2,"visualId":"x","clips":{
              "rising":{"action":1200,"clock":"move","loop":false,"frameSchedule":[0,0,1,2,3,4,5,6,7,8,9,10,10,11,11,12,12,12]},
              "normal":{"action":200,"loop":false}}}
            """, "x");
        var native = new SpriteTimeline(new[] {2,3,4,1,2,2,2,2,2,1,5,6,9}, 0, false);
        var clip = binding.Clips["rising"];
        Assert.Equal(new[] {0,0,1,2,3,4,5,6,7,8,9,10,10,11,11,12,12,12},
            Enumerable.Range(0,18).Select(t => clip.FrameAt(native,t,13)));
        Assert.Equal(12, clip.FrameAt(native,100,13));
        Assert.Equal(native.FrameAt(6), binding.Clips["normal"].FrameAt(native,6,13));
    }

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

    [Fact]
    public void CrouchingHitReactionSelectsItsOwnClipsAndSharesTheAuthoredClock()
    {
        var binding = SpriteBinding.Parse("""
            {"clips":{"standImpact":5002,"standSlide":5007,"crouchImpact":5022,"crouchSlide":5027},
             "hitReactions":{"Heavy":{"initial":"standImpact","followup":"standSlide","followupTick":4,
               "crouching":{"initial":"crouchImpact","followup":"crouchSlide","followupTick":4}}}}
            """, "x");
        Assert.Equal(("standImpact", 0), binding.ResolveHitReaction("Heavy", 1));
        Assert.Equal(("standSlide", 0), binding.ResolveHitReaction("Heavy", 4));
        Assert.Equal(("crouchImpact", 0), binding.ResolveHitReaction("Heavy", 1, crouching: true));
        Assert.Equal(("crouchImpact", 2), binding.ResolveHitReaction("Heavy", 3, crouching: true));
        Assert.Equal(("crouchSlide", 0), binding.ResolveHitReaction("Heavy", 4, crouching: true));
    }

    [Fact]
    public void HitReactionTracksTheCommittedMoveAndCountsOnlyAfterFreeze()
    {
        var binding = SpriteBinding.Parse("""
            {"schemaVersion":2,"visualId":"x","clips":{"heavy":5002,"falloff":5007},
             "hitReactions":{"Heavy":{"initial":"heavy","followup":"falloff","followupTick":4}}}
            """, "x");
        var pose = new HitReactionVisualPose();
        pose.Observe(hitstun: true, frozen: false, "Heavy");
        for (int i = 0; i < 13; i++)
            pose.Observe(hitstun: true, frozen: true, null);
        Assert.Equal(("heavy", 0), binding.ResolveHitReaction(pose.MoveId, pose.Elapsed));
        var initial = new SpriteTimeline(new[] { 1, 2 }, 0, false);
        for (int i = 1; i <= 4; i++)
        {
            pose.Observe(hitstun: true, frozen: false, null);
            Assert.Equal(i < 4 ? "heavy" : "falloff", binding.ResolveHitReaction(pose.MoveId, pose.Elapsed)!.Value.Clip);
            if (i < 4)
                Assert.Equal(i == 1 ? 0 : 1, initial.FrameAt(binding.ResolveHitReaction(pose.MoveId, pose.Elapsed)!.Value.Tick));
        }
        pose.Observe(hitstun: true, frozen: false, "Light");
        Assert.Null(binding.ResolveHitReaction(pose.MoveId, pose.Elapsed));
        pose.Observe(hitstun: true, frozen: false, "Heavy");
        pose.Observe(hitstun: false, frozen: false, null);
        Assert.Null(binding.ResolveHitReaction(pose.MoveId, pose.Elapsed));
    }

    [Fact]
    public void HitReactionRejectsUnknownClipAndMissingSwitchTick()
    {
        Assert.Throws<ArgumentException>(() => SpriteBinding.Parse("""
            {"schemaVersion":2,"visualId":"x","clips":{"initial":5002},
             "hitReactions":{"Heavy":{"initial":"initial","followup":"missing","followupTick":4}}}
            """, "x"));
        Assert.Throws<ArgumentException>(() => SpriteBinding.Parse("""
            {"schemaVersion":2,"visualId":"x","clips":{"initial":5002,"followup":5007},
             "hitReactions":{"Heavy":{"initial":"initial","followup":"followup"}}}
            """, "x"));
    }

    [Fact]
    public void StateBindingSelectsGuardEvenWithGenericHurtClip()
    {
        var binding = SpriteBinding.Parse("""
            {"schemaVersion":2,"visualId":"x","clips":{"guard":150,"hurt":5000,"walk":20},
             "states":{"Blockstun":"guard","Walking":"walk"}}
            """, "x");
        Assert.Equal("guard", binding.ResolveStateClip("hurt", "Blockstun"));
        Assert.Equal("walk", binding.ResolveStateClip("idle", "Walking"));
        Assert.Equal("hurt", binding.ResolveStateClip("hurt", "Hitstun"));
    }
}
