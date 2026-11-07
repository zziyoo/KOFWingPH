using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一名角色的<b>运动配置</b>（阶段 05A，D64，纯数据）：地面步行速度。
///
/// <para><b>单位</b>：世界单位/逻辑帧（60Hz），定点数（<see cref="Numerics.FixedPoint"/>）。
/// 数值是<b>大小</b>（恒为正），方向由朝向与前后输入在运动函数里决定——
/// 这样「朝左时按前 → 世界坐标减小」不会因为配置里存了带符号速度而出错。</para>
///
/// <para><b>职责分离</b>：运动配置只回答「多快」，不参与指令识别与状态机；
/// 状态机不散落速度常量（A2.5），Godot 不计算权威速度（A2.6）。
/// 阶段 05B 将按需扩展跳跃/重力字段，本阶段不预留空配置。</para>
///
/// <para><b>构造期校验（A2.4）</b>：速度必须为正且不超过
/// <see cref="MaxWalkSpeedPerFrame"/>（每帧 32 单位 = 每秒 1920 单位，
/// 超过视为配置错误），杜绝非法/荒谬配置进入模拟。</para>
/// </summary>
public sealed class MovementDefinition
{
    /// <summary>单帧步行速度上限（配置健全性边界）。超过该值的配置视为错误拒绝。</summary>
    public const int MaxWalkSpeedPerFrameUnits = 32;

    /// <param name="walkForwardSpeed">前向步行速度（世界单位/帧，&gt; 0）。</param>
    /// <param name="walkBackwardSpeed">后向步行速度（世界单位/帧，&gt; 0；允许与前向不同）。</param>
    /// <exception cref="ArgumentOutOfRangeException">速度 ≤ 0 或超过每帧上限。</exception>
    public MovementDefinition(FixedPoint walkForwardSpeed, FixedPoint walkBackwardSpeed)
    {
        ValidateSpeed(walkForwardSpeed, nameof(walkForwardSpeed));
        ValidateSpeed(walkBackwardSpeed, nameof(walkBackwardSpeed));

        WalkForwardSpeed = walkForwardSpeed;
        WalkBackwardSpeed = walkBackwardSpeed;
    }

    /// <summary>前向步行速度（大小，世界单位/帧）。</summary>
    public FixedPoint WalkForwardSpeed { get; }

    /// <summary>后向步行速度（大小，世界单位/帧）。</summary>
    public FixedPoint WalkBackwardSpeed { get; }

    /// <summary>
    /// 默认运动配置（阶段 05A 演示基线）：前 2.0、后 1.5 单位/帧
    /// （120 / 90 单位每秒）。未显式配置运动数据的角色使用该值。
    /// </summary>
    public static MovementDefinition CreateDefault() =>
        new(walkForwardSpeed: FixedPoint.FromFraction(2, 1),
            walkBackwardSpeed: FixedPoint.FromFraction(3, 2));

    private static void ValidateSpeed(FixedPoint speed, string paramName)
    {
        if (speed <= FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(paramName, speed,
                $"步行速度必须为正（大小），收到 {speed}。方向由朝向与输入决定，配置不携带符号。");

        if (speed > FixedPoint.FromInt(MaxWalkSpeedPerFrameUnits))
            throw new ArgumentOutOfRangeException(paramName, speed,
                $"步行速度超过每帧上限 {MaxWalkSpeedPerFrameUnits} 单位：{speed}。超过该值的配置视为错误。");
    }
}
