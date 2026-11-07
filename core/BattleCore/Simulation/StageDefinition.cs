using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// 场地配置（阶段 05B，D70，纯数据）：水平边界、地面高度与双人最小中心间距。
///
/// <para><b>世界坐标（D63）</b>：X 向右为正，Y 向上为正，地面高度为
/// <see cref="GroundY"/>（演示配置恒为 0）。角色中心 X 必须始终位于
/// [<see cref="MinX"/>, <see cref="MaxX"/>] 内（含边界）。</para>
///
/// <para><b>构造期校验</b>：MinX &lt; MaxX；最小间距 ≥ 0 且 ≤ 场地宽度——
/// 非法配置构造期失败，不进入模拟。</para>
/// </summary>
public sealed class StageDefinition
{
    /// <param name="minX">左边界（世界单位，含）。</param>
    /// <param name="maxX">右边界（世界单位，含）。</param>
    /// <param name="groundY">地面高度（世界单位；演示配置为 0）。</param>
    /// <param name="minimumFighterSeparation">双人最小中心间距（世界单位，≥ 0）。</param>
    /// <exception cref="ArgumentOutOfRangeException">边界倒置或间距为负。</exception>
    /// <exception cref="ArgumentException">间距超过场地宽度。</exception>
    public StageDefinition(FixedPoint minX, FixedPoint maxX, FixedPoint groundY, FixedPoint minimumFighterSeparation)
    {
        if (minX >= maxX)
            throw new ArgumentOutOfRangeException(nameof(minX), minX,
                $"左边界（{minX}）必须小于右边界（{maxX}）。");
        if (minimumFighterSeparation < FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumFighterSeparation), minimumFighterSeparation,
                "双人最小中心间距不能为负。");
        if (minimumFighterSeparation > maxX - minX)
            throw new ArgumentException(
                $"双人最小中心间距（{minimumFighterSeparation}）不能超过场地宽度（{maxX - minX}）。");

        MinX = minX;
        MaxX = maxX;
        GroundY = groundY;
        MinimumFighterSeparation = minimumFighterSeparation;
    }

    /// <summary>左边界（世界单位，含）。</summary>
    public FixedPoint MinX { get; }

    /// <summary>右边界（世界单位，含）。</summary>
    public FixedPoint MaxX { get; }

    /// <summary>地面高度（世界单位）。</summary>
    public FixedPoint GroundY { get; }

    /// <summary>双人最小中心间距（世界单位；仅双方都在地面时强制，D71）。</summary>
    public FixedPoint MinimumFighterSeparation { get; }

    /// <summary>
    /// 默认演示场地：X ∈ [-640, 640]（与 1280 宽的 Godot 视口对齐）、地面 0、
    /// 最小中心间距 60（色块宽 60，相接即止）。
    /// </summary>
    public static StageDefinition CreateDefault() => new(
        minX: -640,
        maxX: 640,
        groundY: FixedPoint.Zero,
        minimumFighterSeparation: FixedPoint.FromInt(60));

    /// <summary>把 X 钳制到场地边界内（含边界；持续顶墙输入不会穿墙、不反弹）。</summary>
    public FixedPoint ClampX(FixedPoint x) =>
        x < MinX ? MinX : (x > MaxX ? MaxX : x);
}
