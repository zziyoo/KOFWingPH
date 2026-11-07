using System.Globalization;

namespace NewKOF.BattleCore.Numerics;

/// <summary>
/// Q16.16 <b>定点数</b>（阶段 05A，D63）——BattleCore 权威运动数值的唯一表示。
///
/// <para><b>格式</b>：64 位有符号整数原始值 <see cref="RawValue"/>，低 16 位为小数部分，
/// 高 48 位为整数部分。<see cref="Scale"/> = 2^16 = 65536，即「1 世界单位」的原始值。
/// 整数部分范围约 ±2^47（±140 万亿世界单位），运动场景不可能触界。</para>
///
/// <para><b>单位约定</b>：1 世界单位 = 阶段 01–04 的 1 个旧整数坐标单位（旧 PositionX = -120
/// 即世界坐标 -120）。速度/加速度单位 = 世界单位/逻辑帧（60Hz）。<b>Y 轴向上</b>，
/// 地面高度为 0（重力为负方向，05B 引入）；Godot 显示时再转为屏幕坐标（Y 向下）。</para>
///
/// <para><b>算术规则（全部确定性，绝无浮点）</b>：</para>
/// <list type="bullet">
/// <item>加减：64 位 <c>checked</c> 运算——溢出抛 <see cref="OverflowException"/>，绝不静默回绕；</item>
/// <item>乘除：先用 <see cref="Int128"/> 保存 128 位中间结果，再除以 <see cref="Scale"/>；
///       中间结果不丢失精度，最终结果超出 64 位时抛 <see cref="OverflowException"/>；</item>
/// <item>取整：<b>向零截断</b>（与 C# 整数除法一致）——乘法、除法、<see cref="FromFraction"/>、
///       <see cref="ToIntTruncated"/> 四处规则统一，正负数行为对称、可重复；</item>
/// <item>除零：抛 <see cref="DivideByZeroException"/>（包括 <see cref="FromFraction"/> 分母为 0）；</item>
/// <item>无 NaN / 无 Infinity：本类型是整数，不存在这些状态。</item>
/// </list>
///
/// <para><b>与整数的边界</b>：<c>int → FixedPoint</c> 隐式转换表示「世界单位」——
/// <c>PositionX: -120</c> 语义是 -120 单位（内部乘 <see cref="Scale"/>），
/// <b>不是</b>原始定点值；读回整数用 <see cref="ToIntTruncated"/>（向零截断）。
/// 这样旧调用点（默认出生点 -120/120）无需改动且语义不变（迁移方案，D63）。</para>
///
/// <para><b>所有权</b>：与所有权威数值一样，定点坐标/速度只存在于不可变快照
/// （<see cref="State.FighterState"/>）中；<see langword="double"/> 转换
/// （<see cref="ToDouble"/>）<b>仅供 Godot 显示</b>，结果不得写回权威状态。</para>
/// </summary>
public readonly record struct FixedPoint : IComparable<FixedPoint>
{
    /// <summary>小数位数（Q16.16 的小数部分）。</summary>
    public const int FractionalBits = 16;

    /// <summary>1 世界单位的原始值 = 2^16 = 65536。</summary>
    public const long Scale = 1L << FractionalBits;

    /// <summary>原始定点值：实际值 = <see cref="RawValue"/> / 65536。</summary>
    public long RawValue { get; }

    private FixedPoint(long raw) => RawValue = raw;

    /// <summary>定点数 0。</summary>
    public static FixedPoint Zero => default;

    /// <summary>定点数 1（1 世界单位）。</summary>
    public static FixedPoint One => new(Scale);

    /// <summary>原始值上限（用于溢出测试）。</summary>
    public static FixedPoint MaxValue => new(long.MaxValue);

    /// <summary>原始值下限（用于溢出测试）。</summary>
    public static FixedPoint MinValue => new(long.MinValue);

    /// <summary>由原始定点值构造（测试与序列化用；业务代码应使用单位语义的构造）。</summary>
    public static FixedPoint FromRaw(long raw) => new(raw);

    /// <summary>由世界单位整数构造（无损）。</summary>
    public static FixedPoint FromInt(int value) => new((long)value * Scale);

    /// <summary>
    /// 由分数 <paramref name="numerator"/>/<paramref name="denominator"/> 构造
    /// （如 1/2、3/2、7/4），结果向零截断到 16 位小数精度。
    /// </summary>
    /// <exception cref="DivideByZeroException">分母为 0。</exception>
    public static FixedPoint FromFraction(long numerator, long denominator)
    {
        if (denominator == 0)
            throw new DivideByZeroException("FixedPoint.FromFraction 分母不能为 0。");

        // Int128 中间结果：numerator × 65536 最大约 2^79，不丢失精度。
        Int128 scaled = (Int128)numerator * Scale / denominator; // 向零截断
        return FromInt128(scaled, "FromFraction");
    }

    /// <summary>加法。溢出抛 <see cref="OverflowException"/>，不静默回绕。</summary>
    public static FixedPoint operator +(FixedPoint a, FixedPoint b)
    {
        checked
        {
            return new(a.RawValue + b.RawValue);
        }
    }

    /// <summary>减法。溢出抛 <see cref="OverflowException"/>，不静默回绕。</summary>
    public static FixedPoint operator -(FixedPoint a, FixedPoint b)
    {
        checked
        {
            return new(a.RawValue - b.RawValue);
        }
    }

    /// <summary>一元取负。对 <see cref="MinValue"/> 取负溢出，抛 <see cref="OverflowException"/>。</summary>
    public static FixedPoint operator -(FixedPoint a)
    {
        checked
        {
            return new(-a.RawValue);
        }
    }

    /// <summary>定点 × 定点：128 位中间结果，除以 Scale 后向零截断。</summary>
    public static FixedPoint operator *(FixedPoint a, FixedPoint b)
    {
        Int128 product = (Int128)a.RawValue * b.RawValue;
        return FromInt128(product / Scale, "乘法");
    }

    /// <summary>定点 ÷ 定点：先放大 Scale 再除，保留小数精度；除零显式失败。</summary>
    public static FixedPoint operator /(FixedPoint a, FixedPoint b)
    {
        if (b.RawValue == 0)
            throw new DivideByZeroException("定点数除数不能为 0。");

        Int128 numerator = (Int128)a.RawValue << FractionalBits;
        return FromInt128(numerator / b.RawValue, "除法");
    }

    /// <summary>
    /// 定点 × 整数标量：结果原始值 = <see cref="RawValue"/> × 标量（精确，无舍入），
    /// 超界抛 <see cref="OverflowException"/>。运动系统用 ±1 符号乘速度时零成本且无损。
    /// </summary>
    public static FixedPoint operator *(FixedPoint a, int scalar) =>
        FromInt128((Int128)a.RawValue * scalar, "整数乘法");

    /// <summary>整数标量 × 定点（见另一重载）。</summary>
    public static FixedPoint operator *(int scalar, FixedPoint a) => a * scalar;

    public static bool operator <(FixedPoint a, FixedPoint b) => a.RawValue < b.RawValue;

    public static bool operator >(FixedPoint a, FixedPoint b) => a.RawValue > b.RawValue;

    public static bool operator <=(FixedPoint a, FixedPoint b) => a.RawValue <= b.RawValue;

    public static bool operator >=(FixedPoint a, FixedPoint b) => a.RawValue >= b.RawValue;

    /// <summary>隐式转换：整数字面量 = 世界单位（PositionX: -120 表示 -120 单位，而非原始值）。</summary>
    public static implicit operator FixedPoint(int value) => FromInt(value);

    public int CompareTo(FixedPoint other) => RawValue.CompareTo(other.RawValue);

    /// <summary>
    /// 转为整数世界单位，<b>向零截断</b>（1.75 → 1，-1.75 → -1）。
    /// 这是旧 <c>PositionX</c> 整数坐标的只读视图语义。
    /// </summary>
    /// <exception cref="OverflowException">整数部分超出 int 范围。</exception>
    public int ToIntTruncated()
    {
        long whole = RawValue / Scale; // long 除法本身向零截断
        if (whole > int.MaxValue || whole < int.MinValue)
            throw new OverflowException($"定点数整数部分超出 int 范围：{whole}。");

        return (int)whole;
    }

    /// <summary>
    /// 转为 double。<b>仅供表现层（Godot）显示</b>，结果不得参与权威计算；
    /// 除数是 2 的幂，转换结果跨平台一致。
    /// </summary>
    public double ToDouble() => RawValue / (double)Scale;

    public override string ToString() =>
        ((double)RawValue / Scale).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>从 128 位中间结果收拢到 64 位原始值；超界显式溢出（不静默截断）。</summary>
    private static FixedPoint FromInt128(Int128 value, string operation)
    {
        if (value > long.MaxValue || value < long.MinValue)
            throw new OverflowException($"定点数{operation}结果超出 64 位原始值范围：{value}。");

        return new((long)value);
    }
}
