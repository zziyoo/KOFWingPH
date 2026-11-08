namespace NewKOF.BattleCore.Presentation;

/// <summary>
/// <b>逐逻辑帧表现队列</b>（阶段 10 最终补修，D127）：比赛层把每个已提交帧的
/// <see cref="PresentationFrame"/> 追加进来，表现层按渲染帧<b>整批取走并清空</b>。
///
/// <para><b>生命周期</b>：<c>Enqueue（每次逻辑帧提交） → Drain（每次渲染帧消费）</c>。
/// 队列不会自己增长：正常运行时一次 <c>Advance</c> 追加若干帧、同一次 <c>_Process</c> 内
/// 立即被取走。容量只是防御性上限——万一消费方停摆，超出时丢弃<b>最旧</b>的帧
/// （纯表现数据，丢失只影响当次特效，绝不影响权威状态与比赛进度）。</para>
///
/// <para><b>为什么不是「只留最后一帧」</b>：一次 <c>Advance</c> 可能提交多帧，
/// 命中可能发生在其中任意一帧；只保留最后一帧会丢掉较早的事件与当时的坐标。</para>
/// </summary>
public sealed class PresentationFrameBuffer
{
    /// <summary>默认容量：60Hz 下约 4 秒的帧数，足以覆盖任何一次渲染批次。</summary>
    public const int DefaultCapacity = 240;

    private readonly List<PresentationFrame> _pending = new();

    /// <param name="capacity">防御性容量上限，必须为正。</param>
    public PresentationFrameBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "容量必须为正。");
        Capacity = capacity;
    }

    /// <summary>容量上限（超出时丢弃最旧帧）。</summary>
    public int Capacity { get; }

    /// <summary>当前待消费帧数。</summary>
    public int PendingCount => _pending.Count;

    /// <summary>因超出容量被丢弃的帧数（诊断用；正常运行时恒为 0）。</summary>
    public long DroppedCount { get; private set; }

    /// <summary>追加一个已提交帧（由比赛层在帧提交后调用；不得由表现层调用）。</summary>
    public void Enqueue(PresentationFrame frame)
    {
        if (_pending.Count >= Capacity)
        {
            // 丢弃最旧的帧：表现层已停摆，保留最近的画面比重放陈旧特效更有意义。
            _pending.RemoveAt(0);
            DroppedCount++;
        }

        _pending.Add(frame);
    }

    /// <summary>
    /// 取走当前全部待消费帧并清空队列（按提交顺序返回）。无待消费帧时返回共享空列表。
    /// <b>返回值是快照</b>：调用方可以安全持有；队列本身已清空，不会重复消费。
    /// </summary>
    public IReadOnlyList<PresentationFrame> Drain()
    {
        if (_pending.Count == 0)
            return Array.Empty<PresentationFrame>();

        PresentationFrame[] drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }

    /// <summary>丢弃全部待消费帧（重开/换人/关闭特效时用；不影响权威状态）。</summary>
    public void Clear() => _pending.Clear();
}
