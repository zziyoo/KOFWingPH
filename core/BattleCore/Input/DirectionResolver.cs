using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Input;

/// <summary>
/// 相对角色朝向解析后的方向。**仅供后续运动系统使用**，不参与帧输入录制。
/// </summary>
/// <param name="Forward">朝向正前方（对手方向）。</param>
/// <param name="Back">朝向身后（后退方向）。</param>
/// <param name="Up">上方（跳跃方向）。</param>
/// <param name="Down">下方（蹲下方向）。</param>
/// <param name="Attacks">A/B/C/D 四个攻击键的原始位。</param>
public readonly record struct RelativeInput(
    bool Forward,
    bool Back,
    bool Up,
    bool Down,
    InputBits Attacks);

/// <summary>
/// 把「原始绝对方向」解析为「相对朝向方向」，并执行 SOCD 清理。
///
/// <para>
/// <b>分层原因：</b><see cref="InputBits"/> 记录的是<b>物理按键的绝对方向</b>，
/// 这正是被逐帧录制与重放的数据；朝向是权威状态字段（<see cref="Facing"/>），
/// 会随换边变化。<b>相对方向只在解析时计算</b>，因此同一份输入日志在换边后
/// 依然可以正确重放，也不需要为两名玩家准备两套录法。
/// </para>
///
/// <para><b>SOCD 规则（阶段 03A 决策 D27）：</b></para>
/// <list type="bullet">
/// <item><b>左右同时 / 都不按 → 水平中立。</b>两条相反方向互相抵消。
///       选它是因为当前 <see cref="FrameInput"/> 只保存电平位，没有事件顺序，
///       「后输入优先」无法从位掩码唯一还原；而要改成后者必须先扩展录制格式，
///       属于后续阶段。该规则对称、确定、可无歧义测试。</item>
/// <item><b>上下同时 → 上优先。</b>跳跃优先是 2D 格斗的通行做法，
///       避免蹲防输入被跳跃意图污染。</item>
/// </list>
/// </summary>
public static class DirectionResolver
{
    /// <summary>
    /// 四个攻击键的掩码。指令识别需要用它区分「方向位」与「按键位」，
    /// 因此公开为唯一事实来源，避免各处重复定义。
    /// </summary>
    public const InputBits AttackButtons =
        InputBits.LightPunch | InputBits.HeavyPunch | InputBits.LightKick | InputBits.HeavyKick;

    /// <summary>按当前朝向解析一帧原始输入。</summary>
    /// <param name="raw">逐帧录制的原始输入位（绝对方向）。</param>
    /// <param name="facing">该角色当前朝向，来自权威状态。</param>
    public static RelativeInput Resolve(InputBits raw, Facing facing)
    {
        bool left = (raw & InputBits.Left) != 0;
        bool right = (raw & InputBits.Right) != 0;
        bool up = (raw & InputBits.Up) != 0;
        bool down = (raw & InputBits.Down) != 0;

        // SOCD-H：只有恰好一个水平方向按下时才有前后；同时按下视为中立。
        bool forward = false;
        bool back = false;
        if (left != right)
        {
            bool pressedRight = right;
            forward = facing == Facing.Right ? pressedRight : !pressedRight;
            back = !forward;
        }

        // SOCD-V：上优先。
        bool resolvedUp = up;
        bool resolvedDown = down && !up;

        return new RelativeInput(forward, back, resolvedUp, resolvedDown, raw & AttackButtons);
    }
}
