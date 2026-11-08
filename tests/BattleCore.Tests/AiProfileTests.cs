using NewKOF.BattleCore.AI;
using NewKOF.BattleCore.Numerics;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class AiProfileTests
{
    [Fact]
    public void IncreasingDifficultyReducesLatencyAndMistakesWithoutPerfectDefense()
    {
        AiDifficultyProfile? previous = null;
        foreach (var difficulty in Enum.GetValues<AiDifficulty>())
        {
            var profile = AiDifficultyProfile.For(difficulty);
            Assert.True(profile.ObservationDelayFrames > 0);
            Assert.True(profile.ReactionDelayFrames > 0);
            Assert.True(profile.TechReactionFrames > 0);
            Assert.InRange(profile.GuardChance, 1, 999);
            Assert.InRange(profile.TechChance, 1, 999);
            Assert.InRange(profile.MistakeChance, 1, 999);
            if (previous is not null)
            {
                Assert.True(profile.ReactionDelayFrames < previous.ReactionDelayFrames);
                Assert.True(profile.DecisionIntervalFrames < previous.DecisionIntervalFrames);
                Assert.True(profile.TechReactionFrames < previous.TechReactionFrames);
                Assert.True(profile.MistakeChance < previous.MistakeChance);
                Assert.True(profile.GuardChance > previous.GuardChance);
                Assert.True(profile.TechChance > previous.TechChance);
                Assert.True(profile.CounterChance > previous.CounterChance);
            }
            previous = profile;
        }
        Assert.True(AiDifficultyProfile.For(AiDifficulty.Expert).TechReactionFrames <= 2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void UndefinedDifficultyIsRejected(int difficulty) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AiDifficultyProfile.For((AiDifficulty)difficulty));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void ConstructorRejectsNegativeValues(int index)
    {
        var values = ValidValues();
        values[index] = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(values));
    }

    [Theory]
    [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(10)]
    public void ProbabilitiesAcceptEndpointsAndRejectAboveOneThousand(int index)
    {
        var values = ValidValues();
        values[index] = 0;
        _ = Create(values);
        values[index] = 1000;
        _ = Create(values);
        values[index] = 1001;
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(values));
    }

    [Fact]
    public void DecisionIntervalMustAdvanceFrames()
    {
        var values = ValidValues();
        values[2] = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(values));
    }

    [Fact]
    public void RandomSequenceIsStableAndDoesNotAbsorbZero()
    {
        uint state = 1;
        Assert.Equal(270369u, state = AiRandom.Next(state));
        Assert.Equal(67634689u, state = AiRandom.Next(state));
        Assert.Equal(2647435461u, AiRandom.Next(state));
        Assert.NotEqual(0u, AiRandom.Next(0));
        var states = new HashSet<uint>();
        state = 0;
        for (int i = 0; i < 10000; i++)
        {
            state = AiRandom.Next(state);
            Assert.NotEqual(0u, state);
            Assert.True(states.Add(state));
        }
    }

    [Fact]
    public void SeedDerivationIsStableAndSeparatesPlayersScopesAndSlots()
    {
        var seed = AiRandom.DeriveSeed(42, 0, 0, 0);
        Assert.Equal(seed, AiRandom.DeriveSeed(42, 0, 0, 0));
        var seeds = new HashSet<uint>();
        for (int player = 0; player < 2; player++)
        for (int scope = 0; scope < 4; scope++)
        for (int slot = 0; slot < 3; slot++)
        {
            var derived = AiRandom.DeriveSeed(42, player, scope, slot);
            Assert.NotEqual(0u, derived);
            Assert.True(seeds.Add(derived));
        }
        Assert.NotEqual(seed, AiRandom.DeriveSeed(43, 0, 0, 0));
    }

    [Theory]
    [InlineData(-1, 0, 0)] [InlineData(2, 0, 0)]
    [InlineData(0, -1, 0)] [InlineData(0, 0, -1)]
    public void SeedDerivationRejectsInvalidIdentity(int player, int scope, int slot) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRandom.DeriveSeed(1, player, scope, slot));

    private static int[] ValidValues() => new[] { 1, 4, 2, 800, 700, 200, 700, 20, 5, 80, 600, 1 };

    private static AiDifficultyProfile Create(int[] values) => new(
        values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7],
        values[8], FixedPoint.FromInt(values[9]), values[10], values[11]);
}
