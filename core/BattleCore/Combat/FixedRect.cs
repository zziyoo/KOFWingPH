using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 轴对齐包围盒（AABB，阶段 06A，D76）——攻击判定与受击判定的唯一几何表示。
///
/// <para><b>表示</b>：MinX/MaxX/MinY/MaxY（世界坐标，Q16.16 定点数，Y 轴向上、地面为 0）。
/// 构造期强制 <c>MinX &lt; MaxX</c> 且 <c>MinY &lt; MaxY</c>——<b>宽高严格为正</b>，
/// 不允许退化矩形或负宽高（非法几何构造期失败，不进入判定）。</para>
///
/// <para><b>相交规则（D76，专项测试固定）</b>：<see cref="Overlaps"/> 只在两矩形有
/// <b>正面积重叠</b>时为真——边缘恰好接触（MinX == MaxX 等）<b>不算命中</b>；
/// 不使用任何 epsilon 容差，全定点比较，跨平台逐位一致。</para>
///
/// <para>判定不依赖 Godot 的 Rect2/物理系统；表现层绘制用 <see cref="ToGodotCompatible"/>
/// 语义自行换算（仅显示，非权威）。</para>
/// </summary>
public readonly record struct FixedRect
{
    public FixedPoint MinX { get; }

    public FixedPoint MaxX { get; }

    public FixedPoint MinY { get; }

    public FixedPoint MaxY { get; }

    /// <param name="minX">左边界（含）。</param>
    /// <param name="maxX">右边界（不含语义上的接触边；必须 &gt; minX）。</param>
    /// <param name="minY">下边界（含）。</param>
    /// <param name="maxY">上边界（必须 &gt; minY）。</param>
    /// <exception cref="ArgumentException">MaxX ≤ MinX 或 MaxY ≤ MinY（零面积/负面积矩形）。</exception>
    public FixedRect(FixedPoint minX, FixedPoint maxX, FixedPoint minY, FixedPoint maxY)
    {
        if (maxX <= minX)
            throw new ArgumentException(
                $"矩形水平方向必须严格为正（MinX={minX}, MaxX={maxX}），不允许零面积或负宽。", nameof(maxX));
        if (maxY <= minY)
            throw new ArgumentException(
                $"矩形垂直方向必须严格为正（MinY={minY}, MaxY={maxY}），不允许零面积或负高。", nameof(maxY));

        MinX = minX;
        MaxX = maxX;
        MinY = minY;
        MaxY = maxY;
    }

    /// <summary>由中心与半宽半高构造（攻击框数据的人体工学入口；校验同主构造）。</summary>
    public static FixedRect FromCenter(FixedPoint centerX, FixedPoint centerY, FixedPoint halfWidth, FixedPoint halfHeight) =>
        new(centerX - halfWidth, centerX + halfWidth, centerY - halfHeight, centerY + halfHeight);

    /// <summary>
    /// 是否与另一矩形存在<b>正面积重叠</b>。边缘接触（一个矩形的 MaxX == 对方 MinX 等）
    /// 返回 false——接触不算命中（D76）。
    /// </summary>
    public bool Overlaps(FixedRect other) =>
        MinX < other.MaxX && other.MinX < MaxX &&
        MinY < other.MaxY && other.MinY < MaxY;
}
