namespace NewKOF.BattleCore.Commands;

/// <summary>
/// 指令序列中的「相对朝向方向」——以角色面朝方向为基准的九宫格。
///
/// <para>
/// <b>为什么不直接存 <see cref="Input.InputBits"/>：</b>原始输入记录的是<b>绝对方向</b>
/// （见 <see cref="Input.DirectionResolver"/>）。同一串绝对输入在不同朝向下含义相反，
/// 而指令（↓↘→ 等）永远是「相对角色前方」的。因此指令定义使用本枚举，
/// 由识别器在<b>每个历史帧自己的朝向</b>下换算，而不是用当前朝向重算全部历史。
/// </para>
/// </summary>
public enum CommandDirection
{
    /// <summary>九宫格中心（无方向 / SOCD 抵消后的水平中立）。</summary>
    Neutral,

    /// <summary>朝向正前方（对手方向）。</summary>
    Forward,

    /// <summary>朝向身后（后退方向）。</summary>
    Back,

    /// <summary>正上。</summary>
    Up,

    /// <summary>正下。</summary>
    Down,

    /// <summary>前上。</summary>
    UpForward,

    /// <summary>后上。</summary>
    UpBack,

    /// <summary>前下（蹲前，↓↘ 中的 ↘）。</summary>
    DownForward,

    /// <summary>后下（蹲后，↓↙ 中的 ↙）。</summary>
    DownBack,
}
