using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 跳跃类型（阶段 05B，D68）：起跳帧由「预备窗口末帧的『上』是否仍按住」分类，
/// 类型随权威快照携带（<c>FighterState.JumpKind</c>），Godot 只读显示。
/// </summary>
public enum JumpKind
{
    /// <summary>不在空中（地面状态无跳跃类型）。</summary>
    None = 0,

    /// <summary>小跳：预备窗口末帧「上」已松开——较低的初始垂直速度、较短的腾空时间。</summary>
    ShortHop = 1,

    /// <summary>大跳：预备窗口末帧「上」仍按住——较高的初始垂直速度、较长的腾空时间。</summary>
    HighJump = 2,
}

/// <summary>
/// 一次起跳的初始运动参数（阶段 05B，D68）：状态机在预备窗口归零同帧产生，
/// 由运动阶段据此开始积分；起跳后速度保存在 <c>FighterState.VelocityX/VelocityY</c>。
/// </summary>
/// <param name="Kind">跳跃类型（小跳 / 大跳）。</param>
/// <param name="VelocityX">水平初速度（世界单位/帧；中立跳为 0；方向在起跳帧锁定）。</param>
/// <param name="VelocityY">垂直初速度（世界单位/帧，向上为正；来自运动配置的小跳/大跳初速）。</param>
public readonly record struct JumpLaunch(JumpKind Kind, FixedPoint VelocityX, FixedPoint VelocityY);
