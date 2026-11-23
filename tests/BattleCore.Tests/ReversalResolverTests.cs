using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class ReversalResolverTests
{
    private static readonly MoveReversal Reversal = new(2, 4, [new FixedRect(20, 51, 25, 72)], 11, 13);
    private static readonly ActiveHitbox Strike = new(new FixedRect(19, 58, 45, 84), 7);

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void InclusiveMoveTicksControlWhetherAReversalCanConnect(int elapsed, bool connects)
    {
        FighterState source = InMove(1, 70, Facing.Left, elapsed);
        FighterState target = InMove(0, 0, Facing.Right, 3);

        MoveReversalContact? contact = MoveReversalResolver.Detect(source, target, [Reversal], [], [], [Strike]);

        if (connects)
            Assert.Equal(new MoveReversalContact(1, 0, 7, 11, 13), contact);
        else
            Assert.Null(contact);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(32, 0, false)]
    [InlineData(0, 45, false)]
    public void ReversalAcceptsNativeEdgeContactUsingMirroredAndTranslatedClsn1(int offsetX, int offsetY, bool connects)
    {
        FighterState source = InMove(1, 70 + offsetX, Facing.Left, 2) with { PositionY = offsetY };
        FighterState target = InMove(0, 0, Facing.Right, 3);
        var edgeStrike = new ActiveHitbox(new FixedRect(5, 19, 28, 48), 3);

        MoveReversalContact? contact = MoveReversalResolver.Detect(source, target, [Reversal], [], [], [edgeStrike]);

        Assert.Equal(connects, contact.HasValue);
    }

    [Fact]
    public void OpposingReversalBoxesWithoutLiveStrikesCannotRevertEachOther()
    {
        FighterState first = InMove(0, 0, Facing.Right, 2);
        FighterState second = InMove(1, 60, Facing.Left, 2);

        Assert.Null(MoveReversalResolver.Detect(first, second, [Reversal], [Reversal], [], []));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReversalCanTargetAnOpponentWhoseReversalWindowHasAlreadyEnded(bool lowerIdFirst)
    {
        FighterState lower = InMove(0, 0, Facing.Right, 2);
        FighterState higher = InMove(1, 60, Facing.Left, 5);
        var higherStrike = new ActiveHitbox(new FixedRect(16, 55, 15, 40), 9);

        MoveReversalContact? contact = lowerIdFirst
            ? MoveReversalResolver.Detect(lower, higher, [Reversal], [Reversal], [], [higherStrike])
            : MoveReversalResolver.Detect(higher, lower, [Reversal], [Reversal], [higherStrike], []);

        Assert.Equal(new MoveReversalContact(0, 1, 9, 11, 13), contact);
    }

    [Theory]
    [InlineData(2, 40, true)]
    [InlineData(1, 40, false)]
    [InlineData(2, 19, false)]
    public void AHelperReversalReturnsTheEntityIdAndLocalHitPause(int elapsed, int edge, bool connects)
    {
        FighterState source = InMove(1, 0, Facing.Right, elapsed);
        var helper = new ProjectileReversalCandidate(72, new FixedRect(-23, edge, 1, 48));

        ProjectileReversalContact? contact = MoveReversalResolver.DetectProjectile(source, [Reversal], [helper]);

        if (connects)
            Assert.Equal(new ProjectileReversalContact(1, 72, 11, 13), contact);
        else
            Assert.Null(contact);
    }

    [Fact]
    public void AReversalWindowCopiesItsBoxesAndRejectsEmptyOrInvalidRanges()
    {
        FixedRect[] boxes = [new(20, 51, 25, 72)];
        var reversal = new MoveReversal(2, 4, boxes, 11, 13);
        boxes[0] = new FixedRect(1, 2, 3, 4);

        Assert.Equal(new FixedRect(20, 51, 25, 72), Assert.Single(reversal.Boxes));
        Assert.Throws<ArgumentException>(() => new MoveReversal(2, 4, [], 11, 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveReversal(4, 2, boxes, 11, 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveReversal(2, 4, boxes, 11, -1));
    }

    private static FighterState InMove(int id, int x, Facing facing, int elapsed) =>
        FighterState.Create(id, 1000, x, facing) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = "Move",
            MoveElapsedFrames = elapsed,
            PhaseRemainingFrames = 1,
            MoveStartedFrame = 0,
        };
}
