using System.Numerics;

namespace NewKOF.BattleCore.Input;

/// <summary>
/// 把「渲染帧采样」转换为「逻辑帧输入」的缓冲器 —— 纯 C#，零 Godot 依赖。
///
/// <para>
/// <b>为什么需要它：</b>渲染帧率与 60Hz 逻辑帧率互质关系任意，一个渲染帧内可能推进
/// 0 个、1 个或多个逻辑帧；反过来，两次逻辑帧之间也可能发生多次按下/松开。
/// 直接把「一次渲染帧采样」当成「一帧输入」会错帧，把采样结果复制给整批逻辑帧
/// 会重复消费一次性边沿。本类把采样与消费解耦，并显式记录边沿。
/// </para>
///
/// <para><b>两条不变量：</b></para>
/// <list type="number">
/// <item><b>电平</b>（<see cref="InputBits"/>）：<see cref="Sample"/> 每次都整体替换
///       当前按住状态。按键仍按住时，后续逻辑帧自然继续带该位——这是「保持」，
///       不是「重复消费边沿」。</item>
/// <item><b>边沿</b>：采样中出现 0→1 时，该位进入 <c>pending</c>；
///       <see cref="Take"/> 只消费一次，因此「按下」必定出现在且仅出现在
///       紧随其后的那个逻辑帧上，即使采样发生在两次逻辑帧之间。</item>
/// </list>
///
/// <para><b>无法归属的边沿（明确策略，不静默丢失）：</b>
/// 若两次逻辑帧之间同一按键被按下多次（按-松-按），受 <see cref="InputBits"/> 的
/// 电平语义限制，只能合并成「下一逻辑帧按下」。合并次数累加到
/// <see cref="CoalescedPressCount"/>，可观测、不静默。真正的逐次事件流属于后续
/// 指令识别阶段的输入缓冲，不在本阶段范围。
/// </para>
/// </summary>
public sealed class InputBuffer
{
    private readonly List<FrameInput> _history = new();

    private ushort _held1;
    private ushort _held2;
    private ushort _pending1;
    private ushort _pending2;

    /// <summary>
    /// 被合并掉的按下边沿累计次数（两名玩家合计）。
    ///
    /// 非零说明「两次逻辑帧之间按了不止一次」——数据没有丢，但按压缩成了一个逻辑帧。
    /// 供调试 HUD 与测试观测，不参与任何权威判定。
    /// </summary>
    public int CoalescedPressCount { get; private set; }

    /// <summary>已产出的逻辑帧输入历史（按帧号严格递增、连续）。</summary>
    public IReadOnlyList<FrameInput> History => _history;

    /// <summary>最近一次产出的帧输入；尚未产出任何帧时为 null。</summary>
    public FrameInput? LastInput => _history.Count == 0 ? null : _history[^1];

    /// <summary>某名玩家当前的真实按住状态（未经边沿叠加）。</summary>
    public InputBits HeldBy(int player) => player switch
    {
        0 => (InputBits)_held1,
        1 => (InputBits)_held2,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "仅支持 1v1（玩家索引 0/1）。"),
    };

    /// <summary>
    /// 提交一次采样（一个渲染帧一次，或每个按键事件一次）。
    /// 调用时机越密集，边沿归属越精确——按事件调用可以捕获
    /// 「同一渲染帧内按下又松开」。
    /// </summary>
    public void Sample(InputBits player1, InputBits player2)
    {
        _pending1 = Latch(_pending1, _held1, (ushort)player1, out int coalesced1);
        _pending2 = Latch(_pending2, _held2, (ushort)player2, out int coalesced2);
        CoalescedPressCount += coalesced1 + coalesced2;

        _held1 = (ushort)player1;
        _held2 = (ushort)player2;
    }

    /// <summary>
    /// 取某个逻辑帧的输入，并消耗已闩锁的按下边沿。
    /// 由 <see cref="Simulation.BattleSession.Advance"/> 按实际推进的帧号逐帧调用。
    /// </summary>
    /// <param name="frame">即将模拟的逻辑帧号，必须与上一帧连续。</param>
    /// <exception cref="ArgumentOutOfRangeException">帧号为负。</exception>
    /// <exception cref="InvalidOperationException">帧号与上一帧不连续（会导致输入错帧）。</exception>
    public FrameInput Take(int frame)
    {
        if (frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "逻辑帧号不能为负。");

        if (_history.Count > 0)
        {
            int expected = _history[^1].Frame + 1;
            if (frame != expected)
                throw new InvalidOperationException(
                    $"输入帧号必须严格连续：期望 {expected}，实际 {frame}。");
        }

        var input = new FrameInput(
            frame,
            (InputBits)(_held1 | _pending1),
            (InputBits)(_held2 | _pending2));

        // 边沿只属于这一帧，取走即清零。
        _pending1 = 0;
        _pending2 = 0;

        _history.Add(input);
        return input;
    }

    /// <summary>计算 0→1 边沿并与待消费掩码合并，同时统计被合并的重复按下。</summary>
    private static ushort Latch(ushort pending, ushort held, ushort now, out int coalesced)
    {
        ushort pressed = (ushort)(now & ~held);
        coalesced = BitOperations.PopCount((uint)(pressed & pending));
        return (ushort)(pending | pressed);
    }
}
