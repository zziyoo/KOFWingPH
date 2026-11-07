using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Numerics;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 06A（D76）：<see cref="FixedRect"/> AABB 几何契约。
/// 相交 = 正面积重叠；边缘接触不算命中；无 epsilon 容差，全定点比较。
/// </summary>
public class FixedRectTests
{
    private static FixedRect Rect(int minX, int maxX, int minY, int maxY) =>
        new(FixedPoint.FromInt(minX), FixedPoint.FromInt(maxX),
            FixedPoint.FromInt(minY), FixedPoint.FromInt(maxY));

    [Fact]
    public void Construction_RequiresStrictlyPositiveExtent()
    {
        // 零面积与负宽高一律拒绝。
        Assert.Throws<ArgumentException>(() => Rect(0, 0, 0, 10));
        Assert.Throws<ArgumentException>(() => Rect(0, -5, 0, 10));
        Assert.Throws<ArgumentException>(() => Rect(0, 10, 3, 3));
        Assert.Throws<ArgumentException>(() => Rect(0, 10, 5, 2));

        // 合法矩形字段保持。
        var r = Rect(-10, 30, 0, 120);
        Assert.Equal(FixedPoint.FromInt(-10), r.MinX);
        Assert.Equal(FixedPoint.FromInt(30), r.MaxX);
        Assert.Equal(FixedPoint.FromInt(0), r.MinY);
        Assert.Equal(FixedPoint.FromInt(120), r.MaxY);
    }

    [Fact]
    public void FromCenter_MatchesManualRect()
    {
        var centered = FixedRect.FromCenter(
            FixedPoint.FromInt(10), FixedPoint.FromInt(50),
            FixedPoint.FromInt(20), FixedPoint.FromInt(30));
        var manual = Rect(-10, 30, 20, 80);
        Assert.Equal(manual, centered);
    }

    [Fact]
    public void Overlaps_FullAndPartialOverlap()
    {
        Assert.True(Rect(0, 10, 0, 10).Overlaps(Rect(0, 10, 0, 10)));   // 完全重叠
        Assert.True(Rect(0, 10, 0, 10).Overlaps(Rect(5, 15, 5, 15)));   // 部分重叠
        Assert.True(Rect(-10, 0, 0, 10).Overlaps(Rect(-5, 5, 0, 10)));  // 负坐标部分重叠
    }

    [Fact]
    public void OverlapsEdgeContact_DoesNotCount()
    {
        // 仅边缘接触（X 或 Y 恰好相接）不算命中——D76 明确规则，无 epsilon。
        Assert.False(Rect(0, 10, 0, 10).Overlaps(Rect(10, 20, 0, 10)));  // X 边相接
        Assert.False(Rect(0, 10, 0, 10).Overlaps(Rect(-10, 0, 0, 10)));  // X 边相接（左）
        Assert.False(Rect(0, 10, 0, 10).Overlaps(Rect(0, 10, 10, 20)));  // Y 边相接
        Assert.False(Rect(0, 10, 0, 10).Overlaps(Rect(0, 10, -10, 0)));  // Y 边相接（下）
    }

    [Fact]
    public void Overlaps_OneRawUnitApart_DoesNotCount_OneRawUnitOverlap_Counts()
    {
        // 相距 1 raw：不命中。
        var a = new FixedRect(FixedPoint.Zero, FixedPoint.FromRaw(100), FixedPoint.Zero, FixedPoint.FromRaw(100));
        var b = new FixedRect(FixedPoint.FromRaw(101), FixedPoint.FromRaw(200), FixedPoint.Zero, FixedPoint.FromRaw(100));
        Assert.False(a.Overlaps(b));

        // 重叠 1 raw：命中。
        var c = new FixedRect(FixedPoint.FromRaw(99), FixedPoint.FromRaw(200), FixedPoint.Zero, FixedPoint.FromRaw(100));
        Assert.True(a.Overlaps(c));
    }

    [Fact]
    public void Overlaps_NegativeCoordinates_AndSeparated()
    {
        Assert.False(Rect(-50, -40, -30, -20).Overlaps(Rect(40, 50, 20, 30)));  // 异号远隔
        Assert.True(Rect(-50, -40, -30, -20).Overlaps(Rect(-45, -35, -25, -15))); // 负坐标重叠
    }
}
