using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.AI;

public enum AiDifficulty { Easy, Normal, Hard, Expert }
public enum AiTactic { Balanced, Offensive, Defensive }

/// <summary>
/// Immutable policy tuning. Chances use integer thousandths; distances use world units.
/// These values influence generated input only and never alter combat rules.
/// </summary>
public sealed record AiDifficultyProfile
{
    public int ObservationDelayFrames { get; }
    public int ReactionDelayFrames { get; }
    public int DecisionIntervalFrames { get; }
    public int GuardChance { get; }
    public int AttackChance { get; }
    public int ThrowChance { get; }
    public int TechChance { get; }
    public int MistakeChance { get; }
    public int ActionCooldownFrames { get; }
    public FixedPoint PreferredDistance { get; }
    public int CounterChance { get; }
    /// <summary>
    /// Latency for a visible pending grab, independent of normal attack observation/reaction.
    /// TechChance still determines whether the AI attempts a tech.
    /// </summary>
    public int TechReactionFrames { get; }

    public AiDifficultyProfile(int observationDelayFrames, int reactionDelayFrames,
        int decisionIntervalFrames, int guardChance, int attackChance, int throwChance,
        int techChance, int mistakeChance, int actionCooldownFrames,
        FixedPoint preferredDistance, int counterChance, int techReactionFrames = 2)
    {
        if(observationDelayFrames is < 0 or > 121) throw new ArgumentOutOfRangeException(nameof(observationDelayFrames));
        ArgumentOutOfRangeException.ThrowIfNegative(reactionDelayFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decisionIntervalFrames);
        ArgumentOutOfRangeException.ThrowIfNegative(actionCooldownFrames);
        ArgumentOutOfRangeException.ThrowIfNegative(techReactionFrames);
        if (preferredDistance < FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(preferredDistance));
        ValidateChance(guardChance, nameof(guardChance));
        ValidateChance(attackChance, nameof(attackChance));
        ValidateChance(throwChance, nameof(throwChance));
        ValidateChance(techChance, nameof(techChance));
        ValidateChance(mistakeChance, nameof(mistakeChance));
        ValidateChance(counterChance, nameof(counterChance));

        ObservationDelayFrames = observationDelayFrames;
        ReactionDelayFrames = reactionDelayFrames;
        DecisionIntervalFrames = decisionIntervalFrames;
        GuardChance = guardChance;
        AttackChance = attackChance;
        ThrowChance = throwChance;
        TechChance = techChance;
        MistakeChance = mistakeChance;
        ActionCooldownFrames = actionCooldownFrames;
        PreferredDistance = preferredDistance;
        CounterChance = counterChance;
        TechReactionFrames = techReactionFrames;
    }

    public static AiDifficultyProfile For(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy   => new(4, 18, 8, 350, 450,  80, 100, 220, 18, 105, 100, 4),
        AiDifficulty.Normal => new(3, 12, 6, 550, 580, 160, 300, 130, 12,  95, 250, 3),
        AiDifficulty.Hard   => new(2,  7, 4, 740, 700, 240, 550,  65,  8,  85, 450, 2),
        AiDifficulty.Expert => new(1,  4, 2, 850, 800, 320, 800,  25,  5,  75, 650, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    private static void ValidateChance(int value, string name)
    {
        if (value is < 0 or > 1000) throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>Pure integer random transitions. Callers own and snapshot the returned state.</summary>
public static class AiRandom
{
    /// <summary>Xorshift32 (13,17,5), with a fixed substitute for the absorbing zero seed.</summary>
    public static uint Next(uint state)
    {
        if (state == 0) state = 0x9E3779B9u;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    /// <summary>Derives reproducible streams for player 0/1, match/round scope, and team slot.</summary>
    public static uint DeriveSeed(uint baseSeed, int player, int scope, int slot)
    {
        if (player is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(player));
        ArgumentOutOfRangeException.ThrowIfNegative(scope);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        unchecked
        {
            uint seed = Mix(baseSeed ^ 0xA511E9B3u);
            seed = Mix(seed ^ ((uint)player + 0x9E3779B9u));
            seed = Mix(seed ^ ((uint)scope + 0x85EBCA6Bu));
            seed = Mix(seed ^ ((uint)slot + 0xC2B2AE35u));
            return seed == 0 ? 0x9E3779B9u : seed;
        }
    }

    private static uint Mix(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }
    }
}
