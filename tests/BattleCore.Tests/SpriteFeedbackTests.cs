using NewKOF.BattleCore.Presentation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class SpriteFeedbackTests
{
    [Theory]
    [InlineData(FeedbackKind.Hit)]
    [InlineData(FeedbackKind.ProjectileImpact)]
    [InlineData(FeedbackKind.ThrowImpact)]
    public void ImpactKindsTriggerFlash(FeedbackKind kind) => Assert.True(VisualFlash.IsImpact(kind));

    [Fact]
    public void FlashEndsAtExclusiveFrameAndRepeatedHitsExtendIt()
    {
        var flash = new VisualFlash();
        Assert.False(flash.ActiveAt(5));
        flash.Until(10);
        flash.Until(8);
        Assert.True(flash.ActiveAt(9));
        flash.Until(12);
        Assert.True(flash.ActiveAt(11));
        Assert.False(flash.ActiveAt(12));
        flash.Reset();
        Assert.False(flash.ActiveAt(11));
    }

    [Fact]
    public void TerminalFlashExpiresWithoutAdvancingCombatClock()
    {
        var flash = new VisualFlash();
        flash.Until(105);
        Assert.False(flash.AdvanceTerminal(100, 4d / 60));
        Assert.True(flash.ActiveAt(100));
        Assert.True(flash.AdvanceTerminal(100, 1.01d / 60));
        Assert.False(flash.ActiveAt(100));
    }
}
