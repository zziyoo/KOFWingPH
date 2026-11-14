using NewKOF.BattleCore.Input;

namespace NewKOF.BattleCore.Commands;

/// <summary>Optional root and helper clocks for a recognized command.</summary>
public sealed record CommandBufferPolicy
{
    public CommandBufferPolicy(bool preserveDuringHitPause, int helperTicks, int helperHeldDecay, int helperReleasedDecay)
    {
        if (helperTicks is < 0 or > 120)
            throw new ArgumentOutOfRangeException(nameof(helperTicks));
        if (helperHeldDecay is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(helperHeldDecay));
        if (helperReleasedDecay is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(helperReleasedDecay));
        PreserveDuringHitPause = preserveDuringHitPause;
        HelperTicks = helperTicks;
        HelperHeldDecay = helperHeldDecay;
        HelperReleasedDecay = helperReleasedDecay;
    }

    public bool PreserveDuringHitPause { get; }
    public int HelperTicks { get; }
    public int HelperHeldDecay { get; }
    public int HelperReleasedDecay { get; }
}

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
///       超过即视为超时，不再匹配。<b>仅约束普通方向指令</b>；蓄力指令的保持时长
///       不受它约束（否则长蓄力反而失效），其超时由释放窗口与按键容差承担（D51）。</description></item>
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
    /// 蓄力指令的保持时长<b>不受</b> <see cref="maxSequenceFrames"/> 约束（超时语义分离，见 D51）。
    /// </param>
    /// <param name="chargeReleaseToleranceFrames">
    /// 蓄力释放窗口（逻辑帧）：离开蓄力方向后，序列<b>最后一个方向</b>允许进入的最晚帧差。
    /// 期间只容忍<b>中立</b>与序列尾部方向；出现无关方向立即失效（D52）。
    /// 仅 <paramref name="chargeHoldFrames"/> &gt; 0 时有意义，默认
    /// <see cref="CommandCatalog.DefaultChargeReleaseToleranceFrames"/>。
    /// </param>
    /// <param name="matchHeldMotion">Match the button edge on the held final direction, with neutral releases between motion steps.</param>
    /// <param name="bufferPolicy">Optional independent root and helper clocks.</param>
    /// <exception cref="ArgumentException">ID 为空 / 序列为空 / 含非攻击键位 /
    /// 蓄力指令序列不足 2 个元素。</exception>
    /// <exception cref="ArgumentOutOfRangeException">帧数参数为负。</exception>
    public CommandDefinition(
        string commandId,
        IEnumerable<CommandDirection> directionSequence,
        InputBits requiredButtons = InputBits.None,
        int maxSequenceFrames = 20,
        int buttonToleranceFrames = 3,
        int priority = 0,
        int bufferLifetimeFrames = 8,
        int chargeHoldFrames = 0,
        int chargeReleaseToleranceFrames = CommandCatalog.DefaultChargeReleaseToleranceFrames,
        bool matchHeldMotion = false,
        CommandBufferPolicy? bufferPolicy = null,
        bool reserveGroundInput = false,
        bool matchCompletedMotion = false,
        bool reserveOnEdgeDirection = false)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            throw new ArgumentException("指令 ID 不能为空。", nameof(commandId));
        ArgumentNullException.ThrowIfNull(directionSequence);

        CommandDirection[] sequence = directionSequence.ToArray();

        // 空序列 = 裸按键指令（D56）：无方向要求，仅按键边沿帧触发——
        // 必须带攻击键，否则无法触发。非空序列维持 03B 的相邻不同约束。
        if (sequence.Length == 0 && requiredButtons == InputBits.None)
            throw new ArgumentException(
                "方向序列不能为空；不带方向的裸按键指令必须声明攻击键。", nameof(directionSequence));

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
        if (chargeReleaseToleranceFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(chargeReleaseToleranceFrames), chargeReleaseToleranceFrames,
                "蓄力释放窗口不能为负。");

        // 蓄力语义 = 「保持蓄力方向 → 释放 → 释放方向进入」；序列只有 1 个元素时
        // 不存在「释放方向」，蓄力匹配无定义。直接在构造期拒绝（D52）。
        if (chargeHoldFrames > 0 && sequence.Length < 2)
            throw new ArgumentException(
                "蓄力指令的方向序列至少需要 2 个元素（蓄力方向 + 释放方向）。",
                nameof(directionSequence));

        if (matchHeldMotion && (sequence.Length < 2 || requiredButtons == InputBits.None ||
            chargeHoldFrames != 0 || buttonToleranceFrames != 0 || sequence.Contains(CommandDirection.Neutral)))
            throw new ArgumentException("Held motions require a button, distinct non-neutral directions, and no charge or button tolerance.",
                nameof(matchHeldMotion));
        if (matchCompletedMotion && !matchHeldMotion)
            throw new ArgumentException("A completed motion uses the held-motion direction parser.", nameof(matchCompletedMotion));
        if (bufferPolicy is { HelperTicks: > 0 } && requiredButtons == InputBits.None)
            throw new ArgumentException("A helper command buffer requires attack buttons.", nameof(bufferPolicy));
        if (reserveOnEdgeDirection && (!reserveGroundInput || matchHeldMotion || bufferPolicy is not { HelperTicks: > 0 }))
            throw new ArgumentException("An edge-direction reservation requires a directional ground reservation with a helper clock.",
                nameof(reserveOnEdgeDirection));
        if (reserveGroundInput && !matchHeldMotion &&
            (sequence.Length == 0 || sequence.Distinct().Count() != sequence.Length || requiredButtons == InputBits.None ||
             buttonToleranceFrames != 0 || chargeHoldFrames != 0 || bufferPolicy is not null && !reserveOnEdgeDirection))
            throw new ArgumentException("保留的地面指令需要一组不重复的相对方向和当帧按键边沿。", nameof(reserveGroundInput));

        CommandId = commandId;
        DirectionSequence = Array.AsReadOnly(sequence);
        RequiredButtons = requiredButtons;
        MaxSequenceFrames = maxSequenceFrames;
        ButtonToleranceFrames = buttonToleranceFrames;
        Priority = priority;
        BufferLifetimeFrames = bufferLifetimeFrames;
        ChargeHoldFrames = chargeHoldFrames;
        ChargeReleaseToleranceFrames = chargeReleaseToleranceFrames;
        MatchHeldMotion = matchHeldMotion;
        BufferPolicy = bufferPolicy;
        ReserveGroundInput = reserveGroundInput;
        MatchCompletedMotion = matchCompletedMotion;
        ReserveOnEdgeDirection = reserveOnEdgeDirection;
    }

    /// <summary>唯一标识，如 <c>236A</c>。</summary>
    public string CommandId { get; }

    /// <summary>按时间排序的方向序列。保留指令将其解释为消费时允许的相对方向集合。</summary>
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

    /// <summary>
    /// 蓄力释放窗口（逻辑帧）：离开蓄力方向后（含中立帧），序列<b>最后一个方向</b>
    /// 进入帧与「释放开始帧」的最大帧差。窗口内只容忍中立与序列尾部方向，
    /// 出现无关方向立即失效；蓄力不足 <see cref="ChargeHoldFrames"/> 不能靠窗口补足（D52）。
    /// </summary>
    public int ChargeReleaseToleranceFrames { get; }

    public bool MatchHeldMotion { get; }
    public CommandBufferPolicy? BufferPolicy { get; }
    public bool ReserveGroundInput { get; }
    public bool MatchCompletedMotion { get; }
    /// <summary>Latch directions and button edges separately for the root and qualify the helper on the physical edge.</summary>
    public bool ReserveOnEdgeDirection { get; }
}
