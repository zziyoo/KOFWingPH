namespace NewKOF.BattleCore.Combat;

/// <summary>精确伤害倍率，保留离线源数据中的十进制分数。</summary>
public readonly record struct DamageRatio
{
    public DamageRatio(int numerator, int denominator)
    {
        if (numerator is < 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(numerator), "伤害倍率分子必须在 0..1000000 内。");
        if (denominator is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(denominator), "伤害倍率分母必须在 1..1000000 内。");
        Numerator = numerator;
        Denominator = denominator;
    }

    public int Numerator { get; }
    public int Denominator { get; }

    public static DamageRatio One { get; } = new(1, 1);
}

/// <summary>可选的近战 HitDef 伤害参数，按招式和接触组配置。</summary>
public sealed class DynamicAttackDamage
{
    public DynamicAttackDamage(int normalBase, int counterBase, bool useContactDivisor)
    {
        if (normalBase is < 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(normalBase), "基础伤害必须在 0..1000000 内。");
        if (counterBase is < 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(counterBase), "反击基础伤害必须在 0..1000000 内。");
        NormalBase = normalBase;
        CounterBase = counterBase;
        UseContactDivisor = useContactDivisor;
    }

    public int NormalBase { get; }
    public int CounterBase { get; }
    public bool UseContactDivisor { get; }
}

/// <summary>普通技脚本伤害的角色级参数与运算顺序。</summary>
public sealed class DynamicDamageRules
{
    public DynamicDamageRules(DamageRatio baseScale, DamageRatio attackMultiplier, DamageRatio defenseMultiplier,
        DamageRatio hitstunDefenseMultiplier, int varianceMaximum, int counterGraceTicks)
    {
        if (baseScale.Numerator == 0 || attackMultiplier.Denominator == 0 ||
            defenseMultiplier.Numerator == 0 || hitstunDefenseMultiplier.Numerator == 0)
            throw new ArgumentException("伤害换算与防御倍率必须为正，攻击倍率必须为有效分数。");
        if (varianceMaximum is < 0 or > DamageScriptState.MaxVariance)
            throw new ArgumentOutOfRangeException(nameof(varianceMaximum), "伤害波动越界。");
        if (counterGraceTicks is < 0 or > DamageScriptState.MaxCounterGraceTicks)
            throw new ArgumentOutOfRangeException(nameof(counterGraceTicks), "反击延续越界。");
        BaseScale = baseScale;
        AttackMultiplier = attackMultiplier;
        DefenseMultiplier = defenseMultiplier;
        HitstunDefenseMultiplier = hitstunDefenseMultiplier;
        VarianceMaximum = varianceMaximum;
        CounterGraceTicks = counterGraceTicks;
    }

    public DamageRatio BaseScale { get; }
    public DamageRatio AttackMultiplier { get; }
    public DamageRatio DefenseMultiplier { get; }
    public DamageRatio HitstunDefenseMultiplier { get; }
    public int VarianceMaximum { get; }
    public int CounterGraceTicks { get; }

    internal int Compute(DynamicAttackDamage attack, DamageScriptState script,
        DynamicDamageRules? defenderRules, bool defenderInHitReaction)
    {
        int basis = script.CounterAge > 0 ? attack.CounterBase : attack.NormalBase;
        int divisor = attack.UseContactDivisor ? script.ContactDivisor : 1;
        int quotient = (basis - script.Variance) / divisor;
        Int128 raw = (Int128)quotient * BaseScale.Numerator / BaseScale.Denominator;
        if (raw == 0 || AttackMultiplier.Numerator == 0)
            return 0;

        DamageRatio defense = defenderRules?.DefenseMultiplier ?? DamageRatio.One;
        DamageRatio reaction = defenderInHitReaction
            ? defenderRules?.HitstunDefenseMultiplier ?? DamageRatio.One : DamageRatio.One;
        Int128 numerator = raw * AttackMultiplier.Numerator * defense.Numerator * reaction.Numerator;
        Int128 denominator = (Int128)AttackMultiplier.Denominator * defense.Denominator * reaction.Denominator;
        Int128 rounded = (2 * numerator + denominator) / (2 * denominator);
        return (int)Int128.Clamp(rounded, 1, int.MaxValue);
    }
}

/// <summary>每席普通技伤害脚本的权威快照，默认值用于旧角色和惰性初始化。</summary>
public readonly record struct DamageScriptState(uint RandomState, int Variance = 0, int CounterAge = 0,
    int Pause = 0, int ContactDivisor = 1, bool ContactPending = false)
{
    public const uint DefaultSeed = 1337;
    private const uint Modulus = 2147483647;
    internal const int MaxVariance = 1024;
    internal const int MaxCounterGraceTicks = 120;

    public static DamageScriptState FromSeed(uint seed, int seat)
    {
        if (seat is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(seat));
        uint first = seed == 0 ? 1 : 1 + (seed - 1) % (Modulus - 1);
        return new DamageScriptState(1 + (first - 1 + (seat == 0 ? 0u : 104729u)) % (Modulus - 1));
    }

    internal bool IsValid(bool inMove) => RandomState == 0 ? this == default
        : RandomState < Modulus && Variance is >= 0 and <= MaxVariance && CounterAge is >= 0 and <= MaxCounterGraceTicks + 1 &&
            Pause is >= 0 and <= AttackDefinition.MaxHitstopFrames && ContactDivisor is >= 1 and <= 32 &&
            (inMove || Pause == 0 && ContactDivisor == 1 && !ContactPending);

    internal DamageScriptState Advance(DynamicDamageRules rules, bool targetAttacking, bool targetInHitReaction,
        bool sameMove, bool paused)
    {
        if (paused)
            return this;
        if (Pause > 0)
            return this with { Pause = Pause - 1 };

        uint random = (uint)((ulong)RandomState * 16807 % Modulus);
        int age = targetAttacking ? 1 : CounterAge > 0 && CounterAge <= rules.CounterGraceTicks ? CounterAge + 1 : 0;
        int divisor = sameMove ? ContactDivisor : 1;
        if (sameMove && ContactPending && age == 0 && targetInHitReaction)
            divisor++;
        return new DamageScriptState(random, (int)(random / (Modulus / (uint)(rules.VarianceMaximum + 1) + 1)),
            age, ContactDivisor: divisor);
    }
}
