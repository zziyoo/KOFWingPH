using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一名角色的<b>运动配置</b>（阶段 05A/05B，D64，纯数据）：地面步行与跳跃参数。
///
/// <para><b>单位</b>：速度 = 世界单位/逻辑帧（60Hz），重力 = 世界单位/帧²，
/// 全部为定点数（<see cref="Numerics.FixedPoint"/>）。速度/初速存<b>大小</b>（恒为正），
/// 方向由朝向与输入在运动函数里决定；重力存大小，积分时向 -Y（下）施加——
/// 世界坐标 Y 轴向上、地面为 0（D63）。</para>
///
/// <para><b>跳跃分类（D68）</b>：短按「上」→ <see cref="ShortHopInitialVelocity"/>（小跳），
/// 预备窗口末帧仍按住 → <see cref="HighJumpInitialVelocity"/>（大跳）；
/// 两者的差由构造期校验保证（大跳初速必须 > 小跳初速）。</para>
///
/// <para><b>构造期校验（A2.4）</b>：所有速度/初速为正且不超过每帧上限，重力为正且不超过上限，
/// <see cref="PreJumpFrames"/> 在 1..30 之间，大跳初速 &gt; 小跳初速——非法配置一律构造期失败。</para>
/// </summary>
public sealed class MovementDefinition
{
    /// <summary>单帧步行速度上限（配置健全性边界）。超过该值的配置视为错误拒绝。</summary>
    public const int MaxWalkSpeedPerFrameUnits = 32;

    /// <summary>单帧跳跃初速度上限（健全性边界）。</summary>
    public const int MaxJumpVelocityUnits = 64;

    /// <summary>单帧重力大小上限（健全性边界）。</summary>
    public const int MaxGravityUnits = 16;

    /// <summary>起跳预备窗口帧数上限（健全性边界）。</summary>
    public const int MaxPreJumpFrames = 30;

    /// <param name="walkForwardSpeed">前向步行速度（世界单位/帧，&gt; 0）。</param>
    /// <param name="walkBackwardSpeed">后向步行速度（世界单位/帧，&gt; 0；允许与前向不同）。</param>
    /// <param name="preJumpFrames">起跳预备窗口帧数（1..30；窗口内不消费指令、不移动）。</param>
    /// <param name="shortHopInitialVelocity">小跳垂直初速度（&gt; 0）。</param>
    /// <param name="highJumpInitialVelocity">大跳垂直初速度（必须 &gt; 小跳初速度）。</param>
    /// <param name="jumpGravity">重力大小（世界单位/帧²，&gt; 0；积分时向 -Y 施加）。</param>
    /// <param name="horizontalJumpSpeed">前/后跳水平初速度大小（&gt; 0；中立跳为 0）。</param>
    /// <exception cref="ArgumentOutOfRangeException">速度/初速/重力 ≤ 0 或超过上限；
    /// 预备窗口越界。</exception>
    /// <exception cref="ArgumentException">大跳初速度 ≤ 小跳初速度。</exception>
    public MovementDefinition(
        FixedPoint walkForwardSpeed,
        FixedPoint walkBackwardSpeed,
        FixedPoint preJumpFrames,
        FixedPoint shortHopInitialVelocity,
        FixedPoint highJumpInitialVelocity,
        FixedPoint jumpGravity,
        FixedPoint horizontalJumpSpeed)
    {
        ValidateSpeed(walkForwardSpeed, nameof(walkForwardSpeed), MaxWalkSpeedPerFrameUnits);
        ValidateSpeed(walkBackwardSpeed, nameof(walkBackwardSpeed), MaxWalkSpeedPerFrameUnits);
        ValidateSpeed(shortHopInitialVelocity, nameof(shortHopInitialVelocity), MaxJumpVelocityUnits);
        ValidateSpeed(highJumpInitialVelocity, nameof(highJumpInitialVelocity), MaxJumpVelocityUnits);
        ValidateSpeed(jumpGravity, nameof(jumpGravity), MaxGravityUnits);
        ValidateSpeed(horizontalJumpSpeed, nameof(horizontalJumpSpeed), MaxJumpVelocityUnits);

        long preJumpRaw = preJumpFrames.RawValue;
        if (preJumpRaw % FixedPoint.Scale != 0)
            throw new ArgumentOutOfRangeException(nameof(preJumpFrames), preJumpFrames,
                $"起跳预备窗口必须是整数帧，收到 {preJumpFrames}。");

        long preJump = preJumpRaw / FixedPoint.Scale;
        if (preJump < 1 || preJump > MaxPreJumpFrames)
            throw new ArgumentOutOfRangeException(nameof(preJumpFrames), preJumpFrames,
                $"起跳预备窗口必须是 1..{MaxPreJumpFrames} 帧，收到 {preJump}。");

        if (highJumpInitialVelocity <= shortHopInitialVelocity)
            throw new ArgumentException(
                $"大跳初速度（{highJumpInitialVelocity}）必须大于小跳初速度（{shortHopInitialVelocity}），" +
                "否则小跳/大跳没有可分辨的高度与腾空差（B2）。", nameof(highJumpInitialVelocity));

        WalkForwardSpeed = walkForwardSpeed;
        WalkBackwardSpeed = walkBackwardSpeed;
        PreJumpFrames = (int)preJump;
        ShortHopInitialVelocity = shortHopInitialVelocity;
        HighJumpInitialVelocity = highJumpInitialVelocity;
        JumpGravity = jumpGravity;
        HorizontalJumpSpeed = horizontalJumpSpeed;
    }

    /// <summary>前向步行速度（大小，世界单位/帧）。</summary>
    public FixedPoint WalkForwardSpeed { get; }

    /// <summary>后向步行速度（大小，世界单位/帧）。</summary>
    public FixedPoint WalkBackwardSpeed { get; }

    /// <summary>起跳预备窗口帧数（整数，1..30；D67）。</summary>
    public int PreJumpFrames { get; }

    /// <summary>小跳垂直初速度（世界单位/帧，向上）。</summary>
    public FixedPoint ShortHopInitialVelocity { get; }

    /// <summary>大跳垂直初速度（世界单位/帧，向上；构造期保证 &gt; 小跳初速度）。</summary>
    public FixedPoint HighJumpInitialVelocity { get; }

    /// <summary>重力大小（世界单位/帧²；积分时向 -Y 施加）。</summary>
    public FixedPoint JumpGravity { get; }

    /// <summary>前/后跳水平初速度大小（世界单位/帧；起跳帧锁定，空中无控制，D68）。</summary>
    public FixedPoint HorizontalJumpSpeed { get; }

    /// <summary>
    /// 默认运动配置（阶段 05 演示基线）：前 2.0 / 后 1.5 单位/帧；预备窗口 3 帧；
    /// 小跳初速 4.0（腾空 16 帧、最高 14）、大跳初速 6.5（腾空 26 帧、最高 39）、
    /// 重力 0.5、水平跳速 3.0。未显式配置运动数据的角色使用该值。
    /// </summary>
    public static MovementDefinition CreateDefault() => new(
        walkForwardSpeed: FixedPoint.FromFraction(2, 1),
        walkBackwardSpeed: FixedPoint.FromFraction(3, 2),
        preJumpFrames: FixedPoint.FromInt(3),
        shortHopInitialVelocity: FixedPoint.FromFraction(4, 1),
        highJumpInitialVelocity: FixedPoint.FromFraction(13, 2),
        jumpGravity: FixedPoint.FromFraction(1, 2),
        horizontalJumpSpeed: FixedPoint.FromInt(3));

    private static void ValidateSpeed(FixedPoint speed, string paramName, int maxUnits)
    {
        if (speed <= FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(paramName, speed,
                $"运动速度/重力必须为正（大小），收到 {speed}。方向由朝向与输入决定，配置不携带符号。");

        if (speed > FixedPoint.FromInt(maxUnits))
            throw new ArgumentOutOfRangeException(paramName, speed,
                $"运动数值超过每帧上限 {maxUnits} 单位：{speed}。超过该值的配置视为错误。");
    }
}
