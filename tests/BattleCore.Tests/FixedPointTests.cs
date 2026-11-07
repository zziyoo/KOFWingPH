using NewKOF.BattleCore.Numerics;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 05A（D63）：<see cref="FixedPoint"/> Q16.16 定点数数值契约。
///
/// <para>覆盖任务书 A1「必测」清单：正负转换、常用小数、正负取整、
/// 连续微小位移累计、计算顺序舍入边界、溢出防护、整数坐标兼容。
/// 全部断言 pinned 到具体原始值——取整规则（向零截断）一旦被改变即失败。</para>
/// </summary>
public class FixedPointTests
{
    // ─────────────────────────────────────────────────────────────
    // 正负转换与 Scale 语义
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 65536)]
    [InlineData(-1, -65536)]
    [InlineData(120, 7864320)]
    [InlineData(-120, -7864320)]
    [InlineData(int.MaxValue, 140737488289792)]
    [InlineData(int.MinValue, -140737488355328)]
    public void FromInt_RawValueIsValueTimesScale(int value, long expectedRaw) =>
        Assert.Equal(expectedRaw, FixedPoint.FromInt(value).RawValue);

    [Fact]
    public void ImplicitIntConversion_MeansWorldUnits_NotRaw()
    {
        // 迁移契约（D63）：旧调用点 PositionX: -120 的语义是 -120 世界单位，
        // 绝不能被解释成原始定点值 -120（那是 0.00183 单位）。
        FixedPoint value = -120;
        Assert.Equal(-120L * FixedPoint.Scale, value.RawValue);
        Assert.Equal(FixedPoint.FromInt(-120), value);
    }

    [Fact]
    public void FromRaw_RoundTrips() =>
        Assert.Equal(-7864320L, FixedPoint.FromRaw(-7864320L).RawValue);

    // ─────────────────────────────────────────────────────────────
    // 常用小数精确表示
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 2, 32768)]     // 0.5
    [InlineData(1, 4, 16384)]     // 0.25
    [InlineData(3, 4, 49152)]     // 0.75
    [InlineData(3, 2, 98304)]     // 1.5
    [InlineData(7, 4, 114688)]    // 1.75
    [InlineData(2, 1, 131072)]    // 2.0
    [InlineData(-1, 2, -32768)]   // -0.5
    [InlineData(-3, 2, -98304)]   // -1.5
    public void FromFraction_ExactFractionsAreExact(long n, long d, long expectedRaw) =>
        Assert.Equal(expectedRaw, FixedPoint.FromFraction(n, d).RawValue);

    [Fact]
    public void FromFraction_RepeatingFraction_TruncatesTowardZero()
    {
        // 1/3 = 0.333328...：65536/3 = 21845.33… → 21845（向零）。
        Assert.Equal(21845L, FixedPoint.FromFraction(1, 3).RawValue);
        // -1/3：-21845.33… → -21845（向零，不是向负无穷的 -21846）。
        Assert.Equal(-21845L, FixedPoint.FromFraction(-1, 3).RawValue);
        Assert.Equal(-21845L, FixedPoint.FromFraction(1, -3).RawValue);
    }

    // ─────────────────────────────────────────────────────────────
    // 取整（ToIntTruncated：向零截断）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(114688, 1)]    // 1.75 → 1
    [InlineData(-114688, -1)]  // -1.75 → -1（向零，不是 floor 的 -2）
    [InlineData(32768, 0)]     // 0.5 → 0
    [InlineData(-32768, 0)]    // -0.5 → 0（向零）
    [InlineData(131072, 2)]    // 2.0 → 2
    [InlineData(-131072, -2)]
    [InlineData(0, 0)]
    public void ToIntTruncated_RoundsTowardZero(long raw, int expected) =>
        Assert.Equal(expected, FixedPoint.FromRaw(raw).ToIntTruncated());

    [Fact]
    public void ToIntTruncated_OverflowsExplicitly() =>
        Assert.Throws<OverflowException>(() => FixedPoint.MaxValue.ToIntTruncated());

    // ─────────────────────────────────────────────────────────────
    // 加减：精确、无舍入、checked 溢出
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddSubtract_Exact()
    {
        Assert.Equal(FixedPoint.FromFraction(15, 4),
            FixedPoint.FromFraction(3, 2) + FixedPoint.FromFraction(9, 4)); // 1.5 + 2.25 = 3.75
        Assert.Equal(FixedPoint.FromFraction(-3, 4),
            FixedPoint.FromFraction(3, 2) - FixedPoint.FromFraction(9, 4)); // 1.5 - 2.25 = -0.75
    }

    [Fact]
    public void Addition_IsExactSoAssociative_NoRoundingBoundary()
    {
        // 加法不产生舍入：任意结合顺序结果逐位一致（区别于乘除的截断边界）。
        FixedPoint x = FixedPoint.FromFraction(1, 3);
        Assert.Equal((x + x) + x, x + (x + x));
        Assert.Equal(65535L, (x + x + x).RawValue);
    }

    [Fact]
    public void AddSubtract_OverflowThrows_NeverWrapsSilently()
    {
        Assert.Throws<OverflowException>(() => FixedPoint.MaxValue + FixedPoint.One);
        Assert.Throws<OverflowException>(() => FixedPoint.MinValue - FixedPoint.One);
    }

    [Fact]
    public void Negate_MinValueOverflowsExplicitly() =>
        Assert.Throws<OverflowException>(() => -FixedPoint.MinValue);

    // ─────────────────────────────────────────────────────────────
    // 乘除：128 位中间结果、向零截断、边界
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Multiply_ExactCases()
    {
        Assert.Equal(FixedPoint.FromInt(3), FixedPoint.FromFraction(3, 2) * FixedPoint.FromInt(2));
        Assert.Equal(FixedPoint.FromInt(-3), FixedPoint.FromFraction(-3, 2) * FixedPoint.FromInt(2));
        Assert.Equal(FixedPoint.FromFraction(1, 4), FixedPoint.FromFraction(1, 2) * FixedPoint.FromFraction(1, 2));
    }

    [Fact]
    public void Multiply_TruncatesTowardZero_ForBothSigns()
    {
        FixedPoint third = FixedPoint.FromFraction(1, 3);        // raw 21845
        FixedPoint minusThird = FixedPoint.FromFraction(-1, 3);  // raw -21845

        // 21845 × 21845 / 65536 = 7281.26… → 7281（向零）。
        Assert.Equal(7281L, (third * third).RawValue);
        // -477204025 / 65536 = -7281.26… → -7281（向零；floor 会得 -7282）。
        Assert.Equal(-7281L, (minusThird * third).RawValue);
    }

    [Fact]
    public void Multiply_ComputationOrderBoundary_IsDeterministic()
    {
        // 先乘后除与先除后乘可能不同（截断发生点不同）——两者都必须可重复。
        FixedPoint a = FixedPoint.FromInt(7);
        FixedPoint b = FixedPoint.FromInt(3);
        FixedPoint c = FixedPoint.FromInt(2);

        FixedPoint multiplyFirst = a * b / c;   // (7×3)/2 = 10.5
        FixedPoint divideFirst = a / b * c;     // (7/3)×2 = 4.666…：先除截断 raw 152917，×2 → 305834

        Assert.Equal(FixedPoint.FromFraction(21, 2), multiplyFirst);
        Assert.Equal(305834L, divideFirst.RawValue); // 7/3 raw = 152917（152917.33 向零），×2 = 305834

        // 同一表达式重算一万次结果不变（确定性，而非数值正确性）。
        FixedPoint repeat = FixedPoint.Zero;
        for (int i = 0; i < 10000; i++)
            repeat = a / b * c;
        Assert.Equal(divideFirst, repeat);
    }

    [Fact]
    public void Multiply_LargeOperands_NoIntermediateLoss()
    {
        // 128 位中间结果：65536（raw）× 65536（raw）= 2^32，/65536 = 65536 = 1.0 × 1.0。
        // 大数：raw 2^40（=65536.0 单位）× raw 2^40 → 中间 2^80 不丢失 → /2^16 = 2^64? 超界拒绝。
        FixedPoint huge = FixedPoint.FromRaw(1L << 40);
        Assert.Throws<OverflowException>(() => huge * huge);

        // 恰好可表示的边界：MaxValue × One == MaxValue（中间 2^79 → /2^16 回到 2^63-1）。
        Assert.Equal(FixedPoint.MaxValue.RawValue, (FixedPoint.MaxValue * FixedPoint.One).RawValue);
    }

    [Fact]
    public void Divide_ExactAndTruncated()
    {
        Assert.Equal(FixedPoint.FromFraction(3, 2), FixedPoint.FromInt(3) / FixedPoint.FromInt(2));
        Assert.Equal(FixedPoint.FromFraction(-3, 2), FixedPoint.FromInt(-3) / FixedPoint.FromInt(2));

        // 1/3：原始值 21845（21845.33… 向零截断）。
        Assert.Equal(21845L, (FixedPoint.FromInt(1) / FixedPoint.FromInt(3)).RawValue);
        // -1/3 → -21845（向零；floor 会得 -21846）。
        Assert.Equal(-21845L, (FixedPoint.FromInt(-1) / FixedPoint.FromInt(3)).RawValue);
    }

    [Fact]
    public void Divide_ByZeroThrows_Always()
    {
        Assert.Throws<DivideByZeroException>(() => FixedPoint.One / FixedPoint.Zero);
        Assert.Throws<DivideByZeroException>(() => FixedPoint.FromFraction(1, 0));
    }

    [Fact]
    public void IntegerScalarMultiply_IsExact()
    {
        Assert.Equal(FixedPoint.FromInt(-5), FixedPoint.FromFraction(5, 2) * -2);
        Assert.Equal(FixedPoint.FromInt(-5), -2 * FixedPoint.FromFraction(5, 2));
        Assert.Equal(FixedPoint.FromFraction(5, 2), FixedPoint.FromFraction(5, 2) * 1);

        // 标量乘法精确：raw × scalar 无舍入；超界显式溢出。
        Assert.Throws<OverflowException>(() => FixedPoint.MaxValue * 2);
        Assert.Equal(FixedPoint.MaxValue.RawValue, (FixedPoint.MaxValue * 1).RawValue);
    }

    // ─────────────────────────────────────────────────────────────
    // 比较 / 相等 / 哈希
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Equality_AndOrdering()
    {
        FixedPoint half = FixedPoint.FromFraction(1, 2);
        FixedPoint sameHalf = FixedPoint.FromRaw(32768);

        Assert.True(half == sameHalf);
        Assert.False(half != sameHalf);
        Assert.Equal(half.GetHashCode(), sameHalf.GetHashCode());
        Assert.True(half.CompareTo(sameHalf) == 0);

        Assert.True(FixedPoint.Zero < half);
        Assert.True(half <= sameHalf);
        Assert.True(half > FixedPoint.FromFraction(1, 3));
        Assert.True(half >= sameHalf);
        Assert.False(half < FixedPoint.MinValue);
    }

    // ─────────────────────────────────────────────────────────────
    // 连续微小位移累计：无漂移
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Accumulation_ExactIncrements_NeverDrift()
    {
        // 1000 次 += 0.25：精确 250.0（若用浮点累计会出现二进制误差）。
        FixedPoint position = FixedPoint.Zero;
        FixedPoint step = FixedPoint.FromFraction(1, 4);
        for (int i = 0; i < 1000; i++)
            position += step;

        Assert.Equal(FixedPoint.FromInt(250), position);
    }

    [Fact]
    public void Accumulation_InexactIncrement_IsDeterministic()
    {
        // 1/3 不可精确表示，但每次累加的都是同一个固定 raw 值：
        // 60 帧 = 60 × 21845 = 1310700 raw（19.9999…），结果逐位可预测、无累计漂移。
        FixedPoint position = FixedPoint.Zero;
        FixedPoint step = FixedPoint.FromFraction(1, 3);
        for (int i = 0; i < 60; i++)
            position += step;

        Assert.Equal(60L * 21845, position.RawValue);
        Assert.Equal(19, position.ToIntTruncated());
    }

    [Fact]
    public void Accumulation_NegativeDirection_SymmetricAndDeterministic()
    {
        FixedPoint position = FixedPoint.Zero;
        FixedPoint step = -FixedPoint.FromFraction(3, 2);
        for (int i = 0; i < 3600; i++) // 1 分钟 @60Hz
            position += step;

        Assert.Equal(FixedPoint.FromInt(-5400), position);
    }

    // ─────────────────────────────────────────────────────────────
    // 显示转换（非权威）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(163840, 2.5)]
    [InlineData(-98304, -1.5)]
    [InlineData(0, 0.0)]
    public void ToDouble_ForDisplayOnly(long raw, double expected) =>
        Assert.Equal(expected, FixedPoint.FromRaw(raw).ToDouble());
}
