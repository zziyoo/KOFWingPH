using System.Collections.ObjectModel;
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
/// <para><b>三条不变量：</b></para>
/// <list type="number">
/// <item><b>电平</b>（<see cref="InputBits"/>）：<see cref="Sample"/> 每次都整体替换
///       当前按住状态。按键仍按住时，后续逻辑帧自然继续带该位——这是「保持」，
///       不是「重复消费边沿」。</item>
/// <item><b>边沿</b>：采样中出现 0→1 时，该位进入 <c>pending</c>；
///       只有 <see cref="Commit"/> 才消费一次，因此「按下」必定出现在且仅出现在
///       紧随其后的那个已提交逻辑帧上。</item>
/// <item><b>所有权</b>：<see cref="History"/> 是<b>真正的只读视图</b>
///       （<see cref="ReadOnlyCollection{T}"/>），外部既不能强转成 <c>List&lt;T&gt;</c>，
///       也不能通过 <c>IList&lt;T&gt;</c> 增删改。</item>
/// </list>
///
/// <para><b>提交契约（阶段 03A 补修）：</b>生产路径使用两阶段
/// <see cref="Prepare"/> → 模拟 → <see cref="Commit"/>（见 <see cref="IInputSource"/>）。
/// 模拟失败时不会写入历史、不会清掉按下边沿，重试同一帧得到同一输入。
/// <see cref="Take"/> 保留为「准备 + 立即提交」的便捷方法，仅供测试与离线工具使用。
/// </para>
///
/// <para><b>历史容量：</b>默认只保留最近 <see cref="DefaultCapacity"/> 帧（600 帧 ≈ 10 秒），
/// 超出后淘汰最旧的帧并置 <see cref="IsHistoryTruncated"/>。这是<b>指令识别所需的短期历史</b>，
/// 不是完整输入录像——完整录像/回放属于后续独立系统，不在本类职责内。
/// </para>
///
/// <para><b>无法归属的边沿（明确策略，不静默丢失）：</b>
/// 若两次逻辑帧之间同一按键被按下多次（按-松-按），受 <see cref="InputBits"/> 的
/// 电平语义限制，只能合并成「下一逻辑帧按下」。合并次数累加到
/// <see cref="CoalescedPressCount"/>，可观测、不静默。真正的逐次事件流属于后续
/// 指令识别阶段的输入缓冲，不在本阶段范围。
/// </para>
/// </summary>
public sealed class InputBuffer : IInputSource
{
    /// <summary>
    /// 默认保留的历史帧数（600 帧 = 10 秒 @60Hz）。
    /// 覆盖常见指令识别窗口与调试回溯，同时给出确定的内存上界。
    /// </summary>
    public const int DefaultCapacity = 600;

    private readonly List<FrameInput> _history = new();
    private readonly ReadOnlyCollection<FrameInput> _historyView;

    private ushort _held1;
    private ushort _held2;
    private ushort _pending1;
    private ushort _pending2;

    /// <param name="historyCapacity">历史保留帧数上限，必须为正。</param>
    /// <exception cref="ArgumentOutOfRangeException">容量非正。</exception>
    public InputBuffer(int historyCapacity = DefaultCapacity)
    {
        if (historyCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(historyCapacity), historyCapacity,
                "历史容量必须为正。");

        Capacity = historyCapacity;
        _historyView = new ReadOnlyCollection<FrameInput>(_history);
    }

    /// <summary>历史保留帧数上限。</summary>
    public int Capacity { get; }

    /// <summary>
    /// 是否发生过历史淘汰。
    /// 为 true 说明更早的帧已经不在 <see cref="History"/> 里，不能再用于回放或长窗口指令识别。
    /// </summary>
    public bool IsHistoryTruncated { get; private set; }

    /// <summary>
    /// 被合并掉的按下边沿累计次数（两名玩家合计）。
    ///
    /// 非零说明「两次逻辑帧之间按了不止一次」——数据没有丢，但按压缩成了一个逻辑帧。
    /// 供调试 HUD 与测试观测，不参与任何权威判定。
    /// </summary>
    public int CoalescedPressCount { get; private set; }

    /// <summary>
    /// 已提交的逻辑帧输入历史（按帧号严格递增、连续）。
    /// <b>只读视图</b>：底层可变列表不对外暴露，且仅在容量超限时由本类淘汰最旧帧。
    /// </summary>
    public IReadOnlyList<FrameInput> History => _historyView;

    /// <summary>最近一次已提交的帧输入；尚未提交任何帧时为 null。</summary>
    public FrameInput? LastInput => _history.Count == 0 ? null : _history[^1];

    /// <summary>某名玩家当前的真实按住状态（未经边沿叠加）。</summary>
    public InputBits HeldBy(int player) => player switch
    {
        0 => (InputBits)_held1,
        1 => (InputBits)_held2,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "仅支持 1v1（玩家索引 0/1）。"),
    };

    /// <summary>
    /// 校验「已提交输入历史」与战斗状态是否对齐：
    /// 历史为空（还没提交过）或最后一帧 == <paramref name="currentFrame"/> - 1。
    ///
    /// <para>
    /// 不用 <c>History.Count == Frame</c> 判断：历史有容量上限，且未来可能截断，
    /// 只有「最后一帧号」是稳定可比对的。
    /// </para>
    /// </summary>
    public bool IsAlignedWith(int currentFrame) =>
        _history.Count == 0 || _history[^1].Frame == currentFrame - 1;

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
    /// 计算某个逻辑帧的输入，<b>不</b>写历史、<b>不</b>清边沿。
    /// 由 <see cref="Simulation.BattleSession.Advance"/> 在模拟前调用。
    /// </summary>
    /// <param name="frame">即将模拟的逻辑帧号，必须与已提交的最后一帧连续。</param>
    /// <exception cref="ArgumentOutOfRangeException">帧号为负。</exception>
    /// <exception cref="InvalidOperationException">帧号与上一帧不连续，或帧号已到 int 上限。</exception>
    public FrameInput Prepare(int frame)
    {
        if (frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "逻辑帧号不能为负。");

        int expected = NextFrameAfterHistory();
        if (expected >= 0 && frame != expected)
            throw new InvalidOperationException(
                $"输入帧号必须严格连续：期望 {expected}，实际 {frame}。");

        return new FrameInput(
            frame,
            (InputBits)(_held1 | _pending1),
            (InputBits)(_held2 | _pending2));
    }

    /// <summary>
    /// 模拟成功后提交该帧输入：写入历史、消费按下边沿、按容量淘汰最旧帧。
    /// 调用方必须传入同一帧 <see cref="Prepare"/> 的返回值。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 帧号与历史不连续，或提交的输入与当前采样状态不一致（Prepare 之后又被采样改动）。
    /// </exception>
    public void Commit(FrameInput input)
    {
        int expected = NextFrameAfterHistory();
        if (expected >= 0 && input.Frame != expected)
            throw new InvalidOperationException(
                $"提交的输入帧号({input.Frame})与期望({expected})不一致。");

        var current = new FrameInput(
            input.Frame,
            (InputBits)(_held1 | _pending1),
            (InputBits)(_held2 | _pending2));

        if (input != current)
            throw new InvalidOperationException(
                "提交的输入与当前采样状态不一致：Prepare 之后采样被改动，或提交了别的帧的输入。");

        _history.Add(input);
        TrimToCapacity();

        // 边沿只属于这一帧，真正提交后才清零。
        _pending1 = 0;
        _pending2 = 0;
    }

    /// <summary>
    /// 「准备 + 立即提交」的便捷方法，<b>仅供测试与本程序集内部</b>。
    ///
    /// <para>
    /// 它不提供事务语义：先写历史再交给 <c>Step()</c>，一旦模拟失败就会留下幻影帧。
    /// 生产路径（<see cref="Simulation.BattleSession"/>）必须使用
    /// <see cref="Prepare"/>/<see cref="Commit"/> 两阶段提交，因此本方法为 <c>internal</c>，
    /// 防止表现层把它当作 <c>Func&lt;int, FrameInput&gt;</c> 传给会话。
    /// </para>
    /// </summary>
    internal FrameInput Take(int frame)
    {
        FrameInput input = Prepare(frame);
        Commit(input);
        return input;
    }

    /// <summary>下一帧应有的帧号；历史为空返回 -1（不限制起始帧）。</summary>
    private int NextFrameAfterHistory()
    {
        if (_history.Count == 0)
            return -1;

        long next = (long)_history[^1].Frame + 1;
        if (next > int.MaxValue)
            throw new InvalidOperationException(
                $"输入帧号已达 int 上限（{_history[^1].Frame}），无法继续。");

        return (int)next;
    }

    private void TrimToCapacity()
    {
        int excess = _history.Count - Capacity;
        if (excess <= 0)
            return;

        _history.RemoveRange(0, excess);
        IsHistoryTruncated = true;
    }

    /// <summary>计算 0→1 边沿并与待消费掩码合并，同时统计被合并的重复按下。</summary>
    private static ushort Latch(ushort pending, ushort held, ushort now, out int coalesced)
    {
        ushort pressed = (ushort)(now & ~held);
        coalesced = BitOperations.PopCount((uint)(pressed & pending));
        return (ushort)(pending | pressed);
    }
}
