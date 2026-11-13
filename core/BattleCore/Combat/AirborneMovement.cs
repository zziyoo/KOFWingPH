using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一帧空中运动积分结果（不可变）。
/// </summary>
/// <param name="PositionX">积分后的 X 坐标。</param>
/// <param name="PositionY">积分后的 Y 坐标；落地时被修正为 0。</param>
/// <param name="VelocityX">积分后的水平速度（起跳时锁定，空中恒定）。</param>
/// <param name="VelocityY">积分后的垂直速度；落地时归零。</param>
/// <param name="Landed">本帧是否触地（Y 积分后 ≤ 0）。</param>
public readonly record struct AirborneTick(
    FixedPoint PositionX,
    FixedPoint PositionY,
    FixedPoint VelocityX,
    FixedPoint VelocityY,
    bool Landed);

/// <summary>
/// 空中运动（阶段 05B，D69）：纯函数定点积分。
///
/// <para><b>默认积分顺序（任务书 B4 推荐序列）：</b></para>
/// <list type="number">
/// <item>按当帧逻辑状态决定加速度——空中恒为重力（<b>-Y</b> 方向，大小来自配置）；</item>
/// <item>更新垂直速度：<c>vy' = vy - g</c>（起跳帧同样施加重力，无例外）；</item>
/// <item>按速度更新坐标：<c>x' = x + vx</c>（起跳时锁定，无空中控制）、<c>y' = y + vy'</c>；</item>
/// <item>判断地面接触：<c>y' ≤ 0</c> 即落地；</item>
/// <item>落地修正：y 归 0、速度归零——位置/速度修正与「回站立」状态边由
///       <c>BattleSimulation</c> 统一执行（唯一由运动阶段写入的状态边，测试固定）。</item>
/// </list>
///
/// <para><b>性质</b>：不会无限悬空（重力恒在）；不会穿地（触地同帧钳制到 0）；
/// 不会在地面弹跳（落地帧速度归零、下一帧已是地面状态）；顶点由速度自然过零；
/// 落地高度精确为 0（定点整数积分无累计误差）。全部运算为定点整数，
/// 同初态同配置必得同轨迹，测试可逐帧预测位置。</para>
/// </summary>
public static class AirborneMovement
{
    /// <summary>
    /// 对一名空中角色积分一帧。调用方保证当前为空中状态（起跳帧传入
    /// <paramref name="velocityY"/> = 起跳初速，其余帧传入快照中的速度）。
    /// </summary>
    /// <param name="positionX">帧开始 X 坐标。</param>
    /// <param name="positionY">帧开始 Y 坐标（起跳帧为 0）。</param>
    /// <param name="velocityX">帧开始水平速度（起跳帧为锁定后的初速）。</param>
    /// <param name="velocityY">帧开始垂直速度（起跳帧为锁定后的初速）。</param>
    /// <param name="gravity">重力大小（世界单位/帧²，&gt; 0；本函数向 -Y 施加）。</param>
    /// <param name="velocityBeforeGravity">受击轨迹可使用本帧施加重力前的速度更新坐标。</param>
    public static AirborneTick Integrate(
        FixedPoint positionX,
        FixedPoint positionY,
        FixedPoint velocityX,
        FixedPoint velocityY,
        FixedPoint gravity,
        bool velocityBeforeGravity = false)
    {
        if (gravity <= FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(gravity), gravity, "重力大小必须为正。");

        // ① 加速度（重力向下）→ ② 垂直速度 → ③ 坐标 → ④ 触地判断。
        FixedPoint nextVy = velocityY - gravity;
        FixedPoint nextX = positionX + velocityX;
        FixedPoint nextY = positionY + (velocityBeforeGravity ? velocityY : nextVy);

        if (nextY <= FixedPoint.Zero)
        {
            // ⑤ 落地修正：贴地、速度归零（防弹跳；状态边由 BattleSimulation 写入）。
            return new AirborneTick(nextX, FixedPoint.Zero, FixedPoint.Zero, FixedPoint.Zero, Landed: true);
        }

        return new AirborneTick(nextX, nextY, velocityX, nextVy, Landed: false);
    }
}
