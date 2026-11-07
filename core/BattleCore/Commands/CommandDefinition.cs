using NewKOF.BattleCore.Input;

namespace NewKOF.BattleCore.Commands;

/// <summary>
/// 一条可识别的指令定义 —— <b>纯数据</b>，不含任何识别流程。
///
/// <para>
/// 新增指令只需往 <see cref="CommandRecognizer"/> 传入新的定义，
/// <b>不需要修改识别器核心逻辑</b>（无 per-move 的 if/else 硬编码）。
/// </para>
///
/// <para><b>字段语义</b></para>
/// <list type="table">
/// <item><term><see cref="DirectionSequence"/></term>
///       <description>按时间顺序的<b>相对朝向</b>方向序列。相邻元素必须不同；
///       同一方向按住多帧只算一个元素（识别器按「方向变化」压缩历史）。</description></item>
/// <item><term><see cref="RequiredButtons"/></term>
///       <description>完成指令所需的攻击键（可多选，必须<b>同一帧</b>全部按下）。
///       <see cref="InputBits.None"/> 表示纯方向指令（如 →→）。</description></item>
/// <item><term><see cref="MaxSequenceFrames"/></term>
///       <description>从<b>第一个方向进入帧</b>到<b>最后一个方向进入帧</b>的最大跨度。
///       超过即视为超时，不再匹配。</description></item>
/// <item><term><see cref="ButtonToleranceFrames"/></term>
///       <description>按键边沿与「最后一个方向进入帧」之间允许的帧差（提前或延后均可）。
///       0 表示必须同帧。</description></item>
/// <item><term><see cref="Priority"/></term>
///       <description>同一帧多条指令都匹配时的<b>第一排序键</b>（大者优先）。</description></item>
/// <item><term><see cref="BufferLifetimeFrames"/></term>
///       <description>识别结果在 <see cref="CommandRecognizer.BufferedMatches"/> 中保留的
///       逻辑帧数（供招式系统消费，本阶段只做展示/测试）。</description></item>
/// </list>
///
/// <para><b>为什么是 class 而不是 record：</b>方向序列是可变数组的只读包装，
/// record 的自动值相等会退化成引用比较，容易让人误以为两条定义「值相等」。</para>
/// </summary>
public sealed class CommandDefinition
{
    /// <summary>构造并校验一条指令定义。</summary>
    /// <param name="commandId">唯一标识（如 <c>236A</c>）；同一个识别器内不得重复。</param>
    /// <param name="directionSequence">相对朝向方向序列，不能为空。</param>
    /// <param name="requiredButtons">所需攻击键；<see cref="InputBits.None"/> = 纯方向指令。</param>
    /// <param name="maxSequenceFrames">方向序列首尾最大跨度（逻辑帧），不能为负。</param>
    /// <param name="buttonToleranceFrames">按键与末方向的最大帧差，不能为负。</param>
    /// <param name="priority">冲突时的优先键，大者优先。</param>
    /// <param name="bufferLifetimeFrames">识别结果保留帧数，不能为负。</param>
    /// <param name="chargeHoldFrames">
    /// 蓄力保持帧数：方向序列<b>第一个元素</b>必须被连续保持的最少逻辑帧数。
    /// 0 表示普通指令（无蓄力）。例如 [4]6A（后蓄前）设 &gt; 0 表示「后」需保持该帧数。
    /// </param>
    /// <exception cref="ArgumentException">ID 为空 / 序列为空 / 含非攻击键位。</exception>
    /// <exception cref="ArgumentOutOfRangeException">帧数参数为负。</exception>
    public CommandDefinition(
        string commandId,
        IEnumerable<CommandDirection> directionSequence,
        InputBits requiredButtons = InputBits.None,
        int maxSequenceFrames = 20,
        int buttonToleranceFrames = 3,
        int priority = 0,
        int bufferLifetimeFrames = 8,
        int chargeHoldFrames = 0)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            throw new ArgumentException("指令 ID 不能为空。", nameof(commandId));
        ArgumentNullException.ThrowIfNull(directionSequence);

        CommandDirection[] sequence = directionSequence.ToArray();
        if (sequence.Length == 0)
            throw new ArgumentException("方向序列不能为空。", nameof(directionSequence));

        // 每个元素必须是合法枚举值；相邻元素不得相同（识别器按「方向变化」
        // 压缩历史，相同相邻方向在定义里是冗余/错误，直接拒绝）。
        for (int i = 0; i < sequence.Length; i++)
        {
            if (!Enum.IsDefined(sequence[i]))
                throw new ArgumentOutOfRangeException(
                    nameof(directionSequence), sequence[i], $"第 {i} 个方向不是合法的 {nameof(CommandDirection)} 值。");

            if (i > 0 && sequence[i] == sequence[i - 1])
                throw new ArgumentException(
                    $"方向序列相邻元素不能相同（第 {i - 1} 与第 {i} 个都是 {sequence[i]}）。",
                    nameof(directionSequence));
        }

        InputBits nonAttack = requiredButtons & ~DirectionResolver.AttackButtons;
        if (nonAttack != InputBits.None)
            throw new ArgumentException(
                $"RequiredButtons 只能包含攻击键，收到非攻击位：{nonAttack}。", nameof(requiredButtons));

        if (maxSequenceFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(maxSequenceFrames), maxSequenceFrames,
                "最大序列跨度不能为负。");
        if (buttonToleranceFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(buttonToleranceFrames), buttonToleranceFrames,
                "按键容差不能为负。");
        if (bufferLifetimeFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferLifetimeFrames), bufferLifetimeFrames,
                "缓冲有效期不能为负。");
        if (chargeHoldFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(chargeHoldFrames), chargeHoldFrames,
                "蓄力保持帧数不能为负。");

        CommandId = commandId;
        DirectionSequence = Array.AsReadOnly(sequence);
        RequiredButtons = requiredButtons;
        MaxSequenceFrames = maxSequenceFrames;
        ButtonToleranceFrames = buttonToleranceFrames;
        Priority = priority;
        BufferLifetimeFrames = bufferLifetimeFrames;
        ChargeHoldFrames = chargeHoldFrames;
    }

    /// <summary>唯一标识，如 <c>236A</c>。</summary>
    public string CommandId { get; }

    /// <summary>按时间顺序的相对朝向方向序列（只读）。</summary>
    public IReadOnlyList<CommandDirection> DirectionSequence { get; }

    /// <summary>需要同一帧按下的攻击键；<see cref="InputBits.None"/> 表示纯方向指令。</summary>
    public InputBits RequiredButtons { get; }

    /// <summary>方向序列首尾最大跨度（逻辑帧）。</summary>
    public int MaxSequenceFrames { get; }

    /// <summary>按键边沿相对末方向进入帧的最大提前/延后帧数。</summary>
    public int ButtonToleranceFrames { get; }

    /// <summary>冲突排序第一键：大者优先。</summary>
    public int Priority { get; }

    /// <summary>识别结果在缓冲中的保留帧数。</summary>
    public int BufferLifetimeFrames { get; }

    /// <summary>
    /// 蓄力保持帧数：方向序列第一个元素必须被连续保持的最少逻辑帧数。
    /// 0 表示普通指令（无蓄力）。例如 [4]6A 的「后」需保持该帧数。
    /// 蓄力只认<b>纯方向</b>（斜方向不计入），见 <see cref="CommandRecognizer"/> 的匹配规则。
    /// </summary>
    public int ChargeHoldFrames { get; }
}
