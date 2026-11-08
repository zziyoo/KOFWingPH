using System.Text;
using NewKOF.BattleCore.Input;

namespace NewKOF.BattleCore.Presentation;

/// <summary>
/// <b>输入链路诊断记录器</b>（阶段 10B，D124）：按顺序记录「键盘事件 → 采样掩码 →
/// Prepare 输入 → Commit 输入」，用于定位「双人同帧按键只有一方生效」这类问题。
///
/// <para>纯 C#、可在单元测试里验证顺序与容量；Godot 侧只在 <c>--inputdiag</c> 打开时写入，
/// 正常启动零开销。</para>
///
/// <para><b>不使用它做任何判定</b>：记录器只观察，不改变输入、不改写战斗状态。</para>
/// </summary>
public sealed class InputTrace
{
    private readonly Queue<string> _lines;
    private readonly int _capacity;
    private long _sequence;

    public InputTrace(int capacity = 512)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "容量必须为正。");
        _capacity = capacity;
        _lines = new Queue<string>(capacity);
    }

    /// <summary>是否启用（false 时 <see cref="Record"/> 直接返回，零分配）。</summary>
    public bool Enabled { get; set; }

    /// <summary>记录条数（受容量上限约束）。</summary>
    public int Count => _lines.Count;

    /// <summary>累计记录过的条数（含已被容量淘汰的）。</summary>
    public long TotalRecorded => _sequence;

    /// <summary>按时间顺序返回当前保留的记录（只读）。</summary>
    public IReadOnlyList<string> Lines => _lines.ToArray();

    /// <summary>记录一条诊断（自动加序号）；未启用时忽略。</summary>
    public void Record(string category, string detail)
    {
        if (!Enabled)
            return;

        _sequence++;
        if (_lines.Count == _capacity)
            _lines.Dequeue();
        _lines.Enqueue($"[{_sequence,5}] {category}: {detail}");
    }

    /// <summary>清空保留的记录（不动累计计数）。</summary>
    public void Clear() => _lines.Clear();

    /// <summary>把最近 <paramref name="max"/> 条拼成多行文本（调试输出/HUD 用）。</summary>
    public string Dump(int max = 200)
    {
        string[] lines = _lines.ToArray();
        int start = Math.Max(0, lines.Length - max);
        var text = new StringBuilder(lines.Length * 24);
        for (int i = start; i < lines.Length; i++)
            text.AppendLine(lines[i]);
        return text.ToString();
    }

    /// <summary>把输入位掩码转成短名（诊断用，不含空格）。</summary>
    public static string DescribeBits(InputBits bits)
    {
        if (bits == InputBits.None)
            return "-";

        var text = new StringBuilder(12);
        if ((bits & InputBits.Up) != 0) text.Append('U');
        if ((bits & InputBits.Down) != 0) text.Append('D');
        if ((bits & InputBits.Left) != 0) text.Append('L');
        if ((bits & InputBits.Right) != 0) text.Append('R');
        if ((bits & InputBits.LightPunch) != 0) text.Append('A');
        if ((bits & InputBits.HeavyPunch) != 0) text.Append('B');
        if ((bits & InputBits.LightKick) != 0) text.Append('C');
        if ((bits & InputBits.HeavyKick) != 0) text.Append('D');
        return text.ToString();
    }

    /// <summary>把一帧输入转成短文本（诊断用）。</summary>
    public static string DescribeInput(FrameInput input) =>
        $"f{input.Frame} P1={DescribeBits(input.Player1)} P2={DescribeBits(input.Player2)}";
}

/// <summary>
/// 给 <see cref="IInputSource"/> 套一层只读探针（阶段 10B，D124）：记录每次 Prepare/Commit 的
/// 帧号与双方输入位，用于确认「同一逻辑帧里两名玩家的按键是否都进了权威输入」。
/// 转发语义与目标源完全一致，不改变任何输入或时序。
/// </summary>
public sealed class TracingInputSource : IInputSource
{
    private readonly IInputSource _inner;
    private readonly InputTrace _trace;

    public TracingInputSource(IInputSource inner, InputTrace trace)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
    }

    /// <summary>被包装的输入源（便于表现层继续读 LastInput 等只读信息）。</summary>
    public IInputSource Inner => _inner;

    public FrameInput Prepare(int frame)
    {
        FrameInput input = _inner.Prepare(frame);
        _trace.Record("prepare", InputTrace.DescribeInput(input));
        return input;
    }

    public void Commit(FrameInput input)
    {
        _trace.Record("commit ", InputTrace.DescribeInput(input));
        _inner.Commit(input);
    }
}
