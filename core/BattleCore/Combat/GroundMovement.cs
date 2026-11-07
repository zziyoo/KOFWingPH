using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 地面运动（阶段 05A，D65）：纯函数，把「解析输入 + 朝向 + 运动配置」推进为新一帧位置。
///
/// <para><b>移动规则（A3）</b>——只有站立待机可以步行：</para>
/// <list type="bullet">
/// <item>按「前」（相对当帧朝向）→ 世界坐标沿朝向正方向移动 <see cref="MovementDefinition.WalkForwardSpeed"/>；</item>
/// <item>按「后」→ 沿朝向负方向移动 <see cref="MovementDefinition.WalkBackwardSpeed"/>；</item>
/// <item>无水平输入 / 左右同时按下（SOCD 水平中立）→ 原地停止，无残余速度
///       （步行速度不进入快照，停止即静止，不存在需要衰减的动量）。</item>
/// </list>
///
/// <para><b>朝向语义（A3 焦点）</b>：输入已经由 <see cref="DirectionResolver"/> 按
/// 「当帧开始朝向」（D46）解析成相对方向，本函数只做「前/后 → 世界 ±」的换算——
/// 朝左时按前，世界坐标<b>减小</b>，绝不因方向名叫 Forward 就永远增加 X。</para>
///
/// <para><b>状态门槛（A3 表）</b>：Crouching / Startup / Active / Recovery 不移动。
/// 门槛按「本帧状态机推进<b>之后</b>」的状态判定：收招归零同帧回站立即可恢复步行
/// （与 D58 的同帧回待机语义一致）。移动攻击与招式自带位移属于后续阶段
/// （招式帧数据扩展），本函数不硬编码任何特殊位移。</para>
///
/// <para><b>确定性</b>：全部运算为定点整数，同初态同输入必得同坐标；
/// 不读取 Godot、不使用浮点、不依赖调用顺序。垂直坐标（重力/跳跃）属阶段 05B。</para>
/// </summary>
public static class GroundMovement
{
    /// <summary>
    /// 计算一帧地面步行后的 X 坐标。
    /// </summary>
    /// <param name="nextState">本帧状态机推进<b>之后</b>的逻辑状态（决定是否允许步行）。</param>
    /// <param name="positionX">帧开始 X 坐标（世界单位定点数）。</param>
    /// <param name="facing">当帧开始朝向（与输入解析所用朝向一致，D46）。</param>
    /// <param name="input">本帧解析输入（SOCD 已处理；Forward/Back 为相对朝向）。</param>
    /// <param name="movement">该角色的运动配置。</param>
    public static FixedPoint StepX(
        FighterLogicState nextState,
        FixedPoint positionX,
        Facing facing,
        RelativeInput input,
        MovementDefinition movement)
    {
        ArgumentNullException.ThrowIfNull(movement);

        // A3 表：仅站立待机允许步行；蹲下与动作三阶段一律原地。
        if (nextState != FighterLogicState.Standing)
            return positionX;

        // 前后互斥（SOCD 已保证左右同按 → 双 false；此处再防御前后同置的非法解析结果）。
        if (input.Forward == input.Back)
            return positionX;

        // 朝向换算：朝右前 = +，朝左前 = -（世界 X 正方向恒向右）。
        int forwardSign = facing == Facing.Right ? 1 : -1;

        return input.Forward
            ? positionX + forwardSign * movement.WalkForwardSpeed
            : positionX - forwardSign * movement.WalkBackwardSpeed;
    }
}
