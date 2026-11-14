using System.Collections.ObjectModel;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Commands;

/// <summary>压缩后的方向条目：只在方向变化时产生；<see cref="EndFrame"/> 记录该方向持续到的最后一帧。</summary>
public readonly record struct DirectionEntry(int Frame, CommandDirection Direction, int EndFrame);

/// <summary>一次按键按下边沿记录；<see cref="Consumed"/> 保证同一次按下不重复触发（不可变值，消费 = 替换）。</summary>
public readonly record struct PressRecord(int Frame, InputBits Bits, bool Consumed);

/// <summary>
/// 缓冲中的一条识别结果及其消费状态（不可变值类型）。<see cref="Match"/> 本身不可变；
/// <see cref="Consumed"/> 表示该结果已被招式系统取走（每条结果最多消费一次，D60）。
/// 相等包括可改变后续选招的时钟。启用 CMD 候选时，同一识别帧可有多个互斥候选。
/// </summary>
public readonly struct BufferedResult
{
    public BufferedResult(CommandMatch match, bool consumed)
        : this(match, consumed, null, 0, 0, InputBits.None)
    {
    }

    private BufferedResult(CommandMatch match, bool consumed, CommandBufferPolicy? policy,
        int rootTicks, int helperTicks, InputBits buttons, bool helperActive = false)
    {
        Match = match;
        Consumed = consumed;
        Policy = policy;
        RootTicks = rootTicks;
        HelperTicks = helperTicks;
        Buttons = buttons;
        HelperActive = helperActive;
    }

    /// <summary>识别结果（不可变）。</summary>
    public CommandMatch Match { get; }

    /// <summary>是否已被消费（每条结果最多一次）。</summary>
    public bool Consumed { get; }

    private CommandBufferPolicy? Policy { get; }
    private int RootTicks { get; }
    private int HelperTicks { get; }
    private InputBits Buttons { get; }
    private bool HelperActive { get; }

    internal static BufferedResult Recognized(CommandMatch match, CommandDefinition definition, bool inHitPause, bool globalPaused)
    {
        if (definition.BufferPolicy is not { } policy)
            return new BufferedResult(match, false);

        // A root command completed during HitPause receives one extra tick on thaw.
        int root = checked(definition.BufferLifetimeFrames + 1 + (inHitPause && policy.PreserveDuringHitPause ? 1 : 0));
        int helper = globalPaused ? 0 : Math.Max(0, policy.HelperTicks - policy.HelperHeldDecay);
        return new BufferedResult(match, false, policy, root, helper, definition.RequiredButtons);
    }

    internal BufferedResult AdvanceClock(InputBits held, bool inHitPause, bool globalPaused)
    {
        if (Policy is not { } policy)
            return this;

        int root = inHitPause && policy.PreserveDuringHitPause ? RootTicks : Math.Max(0, RootTicks - 1);
        int decay = (held & Buttons) == Buttons ? policy.HelperHeldDecay : policy.HelperReleasedDecay;
        int helper = globalPaused ? HelperTicks : Math.Max(0, HelperTicks - decay);
        return new BufferedResult(Match, Consumed, policy, root, helper, Buttons, HelperTicks > 0);
    }

    internal bool IsExpiredAt(int frame) => Policy is null ? Match.IsExpiredAt(frame) : RootTicks == 0 && !HelperActive;

    internal BufferedResult WithoutRootClock() => new(Match, Consumed, Policy, 0, HelperTicks, Buttons, HelperActive);
    internal BufferedResult Consume() => new(Match, true, Policy, RootTicks, HelperTicks, Buttons, HelperActive);

    public bool Equals(BufferedResult other) =>
        Consumed == other.Consumed &&
        Match.RecognizedFrame == other.Match.RecognizedFrame &&
        string.Equals(Match.CommandId, other.Match.CommandId, StringComparison.Ordinal) &&
        Policy == other.Policy && RootTicks == other.RootTicks && HelperTicks == other.HelperTicks &&
        Buttons == other.Buttons && HelperActive == other.HelperActive;

    public override bool Equals(object? obj) => obj is BufferedResult other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Match.CommandId, Match.RecognizedFrame, Consumed, Policy, RootTicks, HelperTicks, Buttons, HelperActive);

    public static bool operator ==(BufferedResult left, BufferedResult right) => left.Equals(right);

    public static bool operator !=(BufferedResult left, BufferedResult right) => !left.Equals(right);
}

/// <summary>
/// 单条蓄力指令的「释放挂起」快照（D51/D52/D54）：离开蓄力方向瞬间记录的蓄力量与
/// 释放开始帧。每条蓄力指令一个槽位（按定义索引对齐）；挂起不按时间自行过期——
/// 匹配时用「末方向进入帧 ∈ [ReleasedAtFrame, ReleasedAtFrame + 释放窗口]」判定。
/// </summary>
public readonly record struct ChargePending(bool HasPending, int AmountHeld, int ReleasedAtFrame)
{
    public static readonly ChargePending Cleared = default;
}

/// <summary>
/// 单名玩家的指令识别<b>状态快照</b>（不可变）+ 纯函数推进。
///
/// <para>
/// 阶段 04（D55）把识别从「提交后观察者里的可变对象」改为<b>权威状态的一部分</b>：
/// 状态随 <c>FighterState</c> 进入快照，<see cref="Advance"/> 输入旧状态与本帧输入、
/// 返回全新状态——同初态同输入必得同结果，旧快照 + 后续输入可完整重放。
/// 识别规则（方向压缩、蓄力 D49/D51/D52/D54、按键边沿匹配、冲突裁决 D40、
/// 手势消费 D43/D47、蓄力一次性 D53）与 03B 完全一致，只是数据结构由可变列表
/// 换成不可变数组（每帧小数组的写时复制，成本可忽略）。</para>
///
/// <para><b>帧号契约</b>：首次允许任意非负帧（快照恢复），此后严格逐帧连续；
/// 非法帧在返回任何新状态前抛出（纯函数天然无半提交）。</para>
///
/// <para><b>内存上界</b>：方向条目 ≤ <see cref="MaxDirectionEntries"/>，按键/缓冲/消费标记
/// 按时间窗裁剪；蓄力槽位 = 指令数，不随时长增长。</para>
///
/// <para><b>集合所有权（D61）</b>：五个公开集合（Directions/Presses/Buffer/
/// ConsumedEndFrames/ChargePendings）在构造点一律包装为单实例
/// <see cref="ReadOnlyCollection{T}"/>——强转 <c>List{T}</c>/<c>T[]</c> 得 null，
/// <c>IList{T}</c>/非泛型 <c>IList</c> 写操作抛 <see cref="NotSupportedException"/>
/// 且不生效。内部构造仍用可变 List（高效流程保留），只在快照落定点包装一次；
/// 新旧状态各自持有独立包装（不共享可写底层），只读包装之间共享是安全的。</para>
///
/// <para><b>结构化相等</b>：重写了 <c>Equals</c>——两个内容相同的状态相等，
/// 因此携带识别状态的 <c>FighterState</c> 在跨实例确定性比较中仍然成立。</para>
/// </summary>
public sealed class RecognitionState
{
    /// <summary>方向条目数量上限（兜底，防极端抖动输入下无界增长）。</summary>
    private const int MaxDirectionEntries = 64;

    /// <summary>未观察过的初始状态（无任何历史；蓄力槽位在首次推进时按指令数补齐）。
    /// 共享实例被多角色/多对局使用，因此与常规状态一样是只读包装（D61）。</summary>
    public static RecognitionState Empty { get; } = new(
        lastObservedFrame: -1,
        heldAttacks: InputBits.None,
        directions: new ReadOnlyCollection<DirectionEntry>(Array.Empty<DirectionEntry>()),
        presses: new ReadOnlyCollection<PressRecord>(Array.Empty<PressRecord>()),
        buffer: new ReadOnlyCollection<BufferedResult>(Array.Empty<BufferedResult>()),
        consumedEndFrames: new ReadOnlyCollection<int>(Array.Empty<int>()),
        chargePendings: new ReadOnlyCollection<ChargePending>(Array.Empty<ChargePending>()),
        last: null);

    private RecognitionState(
        int lastObservedFrame,
        InputBits heldAttacks,
        IReadOnlyList<DirectionEntry> directions,
        IReadOnlyList<PressRecord> presses,
        IReadOnlyList<BufferedResult> buffer,
        IReadOnlyList<int> consumedEndFrames,
        IReadOnlyList<ChargePending> chargePendings,
        CommandMatch? last)
    {
        LastObservedFrame = lastObservedFrame;
        HeldAttacks = heldAttacks;
        Directions = directions;
        Presses = presses;
        Buffer = buffer;
        ConsumedEndFrames = consumedEndFrames;
        ChargePendings = chargePendings;
        Last = last;
    }

    /// <summary>最后一次观察的逻辑帧号（-1 = 尚未观察）。</summary>
    public int LastObservedFrame { get; }

    /// <summary>上一帧的攻击键按住状态（用于按下边沿判定）。</summary>
    public InputBits HeldAttacks { get; }

    /// <summary>按「变化」压缩的方向历史（只读）。</summary>
    public IReadOnlyList<DirectionEntry> Directions { get; }

    /// <summary>最近的按键按下边沿（只读）。</summary>
    public IReadOnlyList<PressRecord> Presses { get; }

    /// <summary>识别结果缓冲（含消费标记；过期结果在下一帧 Advance 时移除）。</summary>
    public IReadOnlyList<BufferedResult> Buffer { get; }

    /// <summary>已被胜者占用的方向手势末方向帧号（D43/D47）。</summary>
    public IReadOnlyList<int> ConsumedEndFrames { get; }

    /// <summary>每条指令的蓄力释放挂起槽位（按指令定义索引对齐）。</summary>
    public IReadOnlyList<ChargePending> ChargePendings { get; }

    /// <summary>最近一次识别到的指令（HUD 显示用；不参与消费语义）。</summary>
    public CommandMatch? Last { get; }

    /// <summary>
    /// 推进一帧：解析方向、更新蓄力挂起、记录按键边沿、裁剪历史、匹配并裁决，
    /// 返回全新状态；传入状态不受影响。
    /// </summary>
    /// <param name="bits">本帧原始输入位。</param>
    /// <param name="facing">本帧开始时的朝向（D46：输入按当帧开始朝向解析）。</param>
    /// <param name="frame">本逻辑帧号（必须严格逐帧连续）。</param>
    /// <param name="playerIndex">玩家索引（0/1，进入识别结果的 PlayerIndex）。</param>
    /// <param name="definitions">该角色的指令表（来自角色配置，不属于状态）。</param>
    /// <param name="useCommandPriority">Retain concurrent candidates for state-dependent CMD selection.</param>
    /// <param name="inHitPause">Keep root clocks for commands whose policy permits it.</param>
    /// <param name="globalPaused">Suspend helper controllers while root input remains observable.</param>
    public RecognitionState Advance(
        InputBits bits,
        Facing facing,
        int frame,
        int playerIndex,
        IReadOnlyList<CommandDefinition> definitions,
        bool useCommandPriority = false,
        bool inHitPause = false,
        bool globalPaused = false)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "逻辑帧号不能为负。");
        if (LastObservedFrame >= 0 && frame != LastObservedFrame + 1)
        {
            throw new InvalidOperationException(
                $"指令识别要求帧号严格逐帧连续：上一帧 {LastObservedFrame}，收到 {frame}（期望 {LastObservedFrame + 1}）。");
        }

        // 工作副本：逻辑与 03B 的可变实现一致，帧末转为不可变快照。
        var directions = new List<DirectionEntry>(Directions);
        var presses = new List<PressRecord>(Presses);
        var buffer = new List<BufferedResult>(Buffer);
        var consumedEndFrames = new List<int>(ConsumedEndFrames);
        var pendings = ChargePendings.Count == definitions.Count
            ? new List<ChargePending>(ChargePendings)
            : new List<ChargePending>(Enumerable.Repeat(ChargePending.Cleared, definitions.Count));

        // 1) 方向：只在「变化」时追加条目（按住不重复计数），
        //    否则实时更新最后一个条目的持续结束帧。
        CommandDirection direction = ToCommandDirection(DirectionResolver.Resolve(bits, facing));
        bool directionChanged = directions.Count == 0 || directions[^1].Direction != direction;
        if (directionChanged)
            directions.Add(new DirectionEntry(frame, direction, frame));
        else
            directions[^1] = directions[^1] with { EndFrame = frame };

        // 1b) 蓄力释放挂起（D51/D52/D54）：在方向历史更新后、裁剪前更新。
        UpdateChargeStates(pendings, definitions, directions, frame, direction, directionChanged);

        // 2) 按键：只认按下边沿（本帧有、上帧无）。
        InputBits attacks = bits & DirectionResolver.AttackButtons;
        InputBits pressed = attacks & ~HeldAttacks;
        if (pressed != InputBits.None)
            presses.Add(new PressRecord(frame, pressed, false));

        // 3) 裁剪过旧历史（内存上界；被裁条目已不可能参与任何匹配）。
        Prune(directions, presses, consumedEndFrames, definitions, frame);

        // Legacy results keep their absolute expiry. Scoped root and helper clocks advance independently.
        if (useCommandPriority)
            for (int i = 0; i < buffer.Count; i++)
                buffer[i] = buffer[i].AdvanceClock(attacks, inHitPause, globalPaused);
        buffer.RemoveAll(result => result.IsExpiredAt(frame));

        // 5) 同帧所有指令一起评估，再按稳定优先级取唯一胜者（D40/D47/D53）。
        CommandMatch? best = null;
        int bestEndFrame = -1;
        var candidatePressIndexes = new List<int>();
        var candidateChargeIndexes = new List<int>();
        List<(CommandMatch Match, CommandDefinition Definition, int EndFrame)>? candidates =
            useCommandPriority ? new() : null;
        for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            CommandDefinition definition = definitions[definitionIndex];
            if (!TryMatch(pendings, consumedEndFrames, definitionIndex, definition, directions, presses,
                    frame, playerIndex, out CommandMatch? match, out int pressIndex, out int endFrame))
                continue;

            if (pressIndex >= 0)
                candidatePressIndexes.Add(pressIndex);
            if (definition.ChargeHoldFrames > 0)
                candidateChargeIndexes.Add(definitionIndex);
            candidates?.Add((match, definition, endFrame));

            if (best is null || IsBetterThan(match, best))
            {
                best = match;
                bestEndFrame = endFrame;
            }
        }

        if (best is null)
        {
            return new RecognitionState(frame, attacks,
                new ReadOnlyCollection<DirectionEntry>(directions),
                new ReadOnlyCollection<PressRecord>(presses),
                new ReadOnlyCollection<BufferedResult>(buffer),
                new ReadOnlyCollection<int>(consumedEndFrames),
                new ReadOnlyCollection<ChargePending>(pendings),
                Last);
        }

        // 一次按键边沿只允许完成一条指令：胜者与落败候选的边沿都消费（D47）。
        foreach (int pressIndex in candidatePressIndexes)
            presses[pressIndex] = presses[pressIndex] with { Consumed = true };

        // 蓄力是一次性资源（D53）：产生过候选的蓄力指令其释放挂起立即作废。
        foreach (int definitionIndex in candidateChargeIndexes)
            pendings[definitionIndex] = ChargePending.Cleared;

        if (candidates is null)
        {
            // Legacy D43/D47 consumes a gesture immediately and retains its one winning command.
            if (bestEndFrame >= 0)
                consumedEndFrames.Add(bestEndFrame);
            buffer.Add(new BufferedResult(best, false));
        }
        else
        {
            foreach (var candidate in candidates)
            {
                // During global Pause only the root can refresh this command. Keep the helper's original edge.
                if (candidate.Definition.BufferPolicy is not null)
                {
                    for (int i = buffer.Count - 1; i >= 0; i--)
                    {
                        if (!string.Equals(buffer[i].Match.CommandId, candidate.Match.CommandId, StringComparison.Ordinal))
                            continue;
                        BufferedResult helper = buffer[i].WithoutRootClock();
                        if (globalPaused && !helper.IsExpiredAt(frame))
                            buffer[i] = helper;
                        else
                            buffer.RemoveAt(i);
                    }
                }
                if (candidate.EndFrame >= 0 && !consumedEndFrames.Contains(candidate.EndFrame))
                    consumedEndFrames.Add(candidate.EndFrame);
                buffer.Add(BufferedResult.Recognized(candidate.Match, candidate.Definition, inHitPause, globalPaused));
            }
        }

        return new RecognitionState(frame, attacks,
            new ReadOnlyCollection<DirectionEntry>(directions),
            new ReadOnlyCollection<PressRecord>(presses),
            new ReadOnlyCollection<BufferedResult>(buffer),
            new ReadOnlyCollection<int>(consumedEndFrames),
            new ReadOnlyCollection<ChargePending>(pendings),
            best);
    }

    /// <summary>
    /// 把缓冲中指定识别帧的所有<b>未消费</b>候选标记为已消费，返回全新状态（D60）。
    /// 结果不存在或已被消费时抛出——消费请求必须显式失败，不得静默重复。
    /// </summary>
    public RecognitionState WithResultConsumed(int recognizedFrame)
    {
        int index = -1;
        for (int i = 0; i < Buffer.Count; i++)
        {
            if (!Buffer[i].Consumed && Buffer[i].Match.RecognizedFrame == recognizedFrame)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"识别帧 {recognizedFrame} 的结果不存在或已被消费，不能再次消费。");
        }

        var buffer = new BufferedResult[Buffer.Count];
        for (int i = 0; i < Buffer.Count; i++)
            buffer[i] = Buffer[i];
        for (int i = index; i < buffer.Length; i++)
            if (!buffer[i].Consumed && buffer[i].Match.RecognizedFrame == recognizedFrame)
                buffer[i] = buffer[i].Consume();

        // 其余四个集合复用旧状态的只读包装（只读实例可安全共享）；Buffer 换新包装。
        return new RecognitionState(LastObservedFrame, HeldAttacks, Directions, Presses,
            new ReadOnlyCollection<BufferedResult>(buffer), ConsumedEndFrames, ChargePendings, Last);
    }

    /// <summary>
    /// 更新每条蓄力指令的「释放挂起」状态（D51/D52/D54，规则与 03B 一致）：
    /// 1) 当前方向 == 蓄力方向 → 挂起作废、重新计数；
    /// 2) 本帧刚离开蓄力方向 → 进入中立或尾段方向则快照挂起，进入无关方向则立即失效；
    /// 3) 挂起期间出现无关方向 → 立即失效。
    /// </summary>
    private static void UpdateChargeStates(
        List<ChargePending> pendings,
        IReadOnlyList<CommandDefinition> definitions,
        List<DirectionEntry> directions,
        int frame,
        CommandDirection current,
        bool directionChanged)
    {
        for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            CommandDefinition definition = definitions[definitionIndex];
            if (definition.ChargeHoldFrames <= 0)
                continue; // 非蓄力指令没有蓄力进度。

            ChargePending pending = pendings[definitionIndex];
            CommandDirection chargeDirection = definition.DirectionSequence[0];

            if (current == chargeDirection)
            {
                // 正在（重新）保持蓄力方向：挂起作废，重新计数。
                pendings[definitionIndex] = ChargePending.Cleared;
                continue;
            }

            if (directionChanged && directions.Count >= 2 &&
                directions[^2].Direction == chargeDirection)
            {
                // 本帧刚从蓄力方向离开：进入中立（含 SOCD 中立）或尾段方向 → 释放挂起；
                // 直接进入无关方向 → 蓄力立即失效（不产生挂起，明确规则）。
                if (current == CommandDirection.Neutral || IsTailDirection(definition, current))
                {
                    DirectionEntry held = directions[^2];
                    pendings[definitionIndex] = new ChargePending(
                        HasPending: true,
                        AmountHeld: held.EndFrame - held.Frame + 1,
                        ReleasedAtFrame: frame);
                }
                else
                {
                    pendings[definitionIndex] = ChargePending.Cleared;
                }

                continue;
            }

            if (pending.HasPending && current != CommandDirection.Neutral &&
                !IsTailDirection(definition, current))
            {
                // 释放窗口内出现无关方向 → 立即失效（明确规则，不允许蒙混）。
                pendings[definitionIndex] = ChargePending.Cleared;
            }
        }
    }

    /// <summary><paramref name="direction"/> 是否在该指令方向序列的尾部（第 2 个及以后元素）中。</summary>
    private static bool IsTailDirection(CommandDefinition definition, CommandDirection direction)
    {
        IReadOnlyList<CommandDirection> sequence = definition.DirectionSequence;
        for (int i = 1; i < sequence.Count; i++)
        {
            if (sequence[i] == direction)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 尝试匹配一条指令（规则与 03B 一致，含 D54 下界）。
    /// 返回匹配结果、所用按键边沿的工作副本索引（-1 = 纯方向指令）与末方向帧号。
    /// </summary>
    private static bool TryMatch(
        IReadOnlyList<ChargePending> pendings,
        IReadOnlyList<int> consumedEndFrames,
        int definitionIndex,
        CommandDefinition definition,
        List<DirectionEntry> directions,
        List<PressRecord> presses,
        int frame,
        int playerIndex,
        out CommandMatch match,
        out int pressIndex,
        out int matchedEndFrame)
    {
        match = null!;
        pressIndex = -1;
        matchedEndFrame = -1;

        IReadOnlyList<CommandDirection> sequence = definition.DirectionSequence;

        if (definition.ReserveGroundInput)
        {
            if (!sequence.Contains(directions[^1].Direction))
                return false;
            pressIndex = FindPressIndex(presses, definition.RequiredButtons, frame, tolerance: 0);
            if (pressIndex < 0)
                return false;
            // A held direction qualifies on each new edge without consuming the motion history.
            match = CreateMatch(definition, playerIndex, frame, frame, directions, 0, 0, presses[pressIndex].Frame);
            return true;
        }

        // 裸按键指令（D56）：空方向序列 + 攻击键——不依赖方向历史，
        // 仅当帧的未消费按键边沿可完成（旧边沿已被消费或按容差裁剪）。
        if (sequence.Count == 0)
        {
            int barePressIndex = FindPressIndex(presses, definition.RequiredButtons, frame, tolerance: 0);
            if (barePressIndex < 0)
                return false;

            match = CreateMatch(definition, playerIndex, frame, sequenceStartFrame: frame,
                directions, endIndex: 0, matchElementCount: 0, presses[barePressIndex].Frame);
            pressIndex = barePressIndex;
            matchedEndFrame = -1; // 裸按键没有方向手势；按键边沿消费已防止重复触发。
            return true;
        }

        if (definition.MatchHeldMotion)
            return TryMatchHeldMotion(definition, directions, presses, consumedEndFrames, frame, playerIndex,
                out match, out pressIndex, out matchedEndFrame);

        // 蓄力指令（D51）：蓄力方向来自释放挂起状态（与历史条目解耦），
        // 历史只需容纳「尾段」（第 2 个及以后元素）；普通指令需要完整序列。
        bool isCharge = definition.ChargeHoldFrames > 0;
        int matchElementCount = isCharge ? sequence.Count - 1 : sequence.Count;
        if (directions.Count < matchElementCount)
            return false;

        // 从最近的方向条目向前尝试：末方向不必是「当前方向」——
        // 允许在容差窗口内稍早完成（D39）。
        for (int endIndex = directions.Count - 1; endIndex >= matchElementCount - 1; endIndex--)
        {
            int endFrame = directions[endIndex].Frame;

            // 帧号随索引递增：更早的条目只会离当前帧更远，可以直接停。
            if (frame - endFrame > definition.ButtonToleranceFrames)
                break;

            // 已被其他指令消费过的方向手势，不能再被本指令借来匹配（D43/D47）。
            if (consumedEndFrames.Contains(endFrame))
                continue;

            int startFrame;
            if (isCharge)
            {
                // 蓄力检查（D51/D52/D54）：蓄力量与释放窗口来自释放挂起状态。
                // 保持时长不受 MaxSequenceFrames 约束；蓄力不足不能靠释放窗口补足；
                // 尾段必须属于本次释放之后（下界），窗口已过不得匹配（上界）。
                ChargePending pending = pendings[definitionIndex];
                if (!pending.HasPending)
                    continue;
                if (pending.AmountHeld < definition.ChargeHoldFrames)
                    continue;
                if (endFrame < pending.ReleasedAtFrame)
                    continue;
                if (endFrame - pending.ReleasedAtFrame > definition.ChargeReleaseToleranceFrames)
                    continue;

                // 蓄力起始帧 = 释放帧 − 蓄力量（即使起始条目已被裁剪也可还原）。
                startFrame = pending.ReleasedAtFrame - pending.AmountHeld;
            }
            else
            {
                startFrame = directions[endIndex - matchElementCount + 1].Frame;

                // 首尾跨度超时——仅普通方向指令（D51：蓄力不适用）。
                if (endFrame - startFrame > definition.MaxSequenceFrames)
                    continue;
            }

            // 蓄力 = 尾段、普通 = 完整序列：必须在压缩历史中连续且顺序一致。
            if (!MatchesEndingAt(directions, endIndex, sequence, matchElementCount))
                continue;

            // 纯方向指令：只在末方向进入的那一帧触发，避免按住重复触发。
            if (definition.RequiredButtons == InputBits.None)
            {
                if (endFrame != frame)
                    continue;

                match = CreateMatch(definition, playerIndex, frame, startFrame, directions,
                    endIndex, matchElementCount, buttonFrame: -1);
                matchedEndFrame = endFrame;
                return true;
            }

            int foundPressIndex = FindPressIndex(presses, definition.RequiredButtons, endFrame,
                definition.ButtonToleranceFrames);
            if (foundPressIndex < 0)
                continue;

            match = CreateMatch(definition, playerIndex, frame, startFrame, directions,
                endIndex, matchElementCount, presses[foundPressIndex].Frame);
            pressIndex = foundPressIndex;
            matchedEndFrame = endFrame;
            return true;
        }

        return false;
    }

    private static bool TryMatchHeldMotion(CommandDefinition definition, List<DirectionEntry> directions,
        List<PressRecord> presses, IReadOnlyList<int> consumedEndFrames, int frame, int playerIndex,
        out CommandMatch match, out int pressIndex, out int matchedEndFrame)
    {
        match = null!;
        matchedEndFrame = -1;
        pressIndex = FindPressIndex(presses, definition.RequiredButtons, frame, tolerance: 0);
        IReadOnlyList<CommandDirection> sequence = definition.DirectionSequence;
        if (pressIndex < 0 || directions[^1].Direction != sequence[^1] || consumedEndFrames.Contains(directions[^1].Frame))
            return false;

        var matched = new int[sequence.Count];
        int cursor = directions.Count - 1;
        for (int element = sequence.Count - 1; element >= 0; element--)
        {
            while (cursor >= 0 && directions[cursor].Direction == CommandDirection.Neutral)
                cursor--;
            if (cursor < 0 || directions[cursor].Direction != sequence[element])
                return false;
            DirectionEntry direction = directions[cursor--];
            // The first source step is a release. Its timer starts when the held interval ends.
            matched[element] = element == 0 ? direction.EndFrame + 1 : direction.Frame;
        }

        if (frame - matched[0] > definition.MaxSequenceFrames)
            return false;
        matchedEndFrame = matched[^1];
        match = new CommandMatch(definition.CommandId, playerIndex, frame, matched[0], matchedEndFrame, frame,
            definition.Priority, matched.Length, checked(frame + definition.BufferLifetimeFrames), matched);
        return true;
    }

    /// <summary>检查压缩历史的 <paramref name="endIndex"/> 结尾是否恰好等于序列的最后
    /// <paramref name="elementCount"/> 个元素（蓄力 = 尾段，普通 = 完整序列）。</summary>
    private static bool MatchesEndingAt(
        List<DirectionEntry> directions,
        int endIndex,
        IReadOnlyList<CommandDirection> sequence,
        int elementCount)
    {
        for (int k = 0; k < elementCount; k++)
        {
            if (directions[endIndex - k].Direction != sequence[sequence.Count - 1 - k])
                return false;
        }

        return true;
    }

    /// <summary>
    /// 在未消费的按键边沿中找一条能完成指令的：必须<b>同一帧</b>包含全部所需按键，
    /// 且与末方向帧的差值在容差内。多个候选时取<b>离末方向最近</b>的；
    /// 并列则取更晚的一帧（确定性规则）。返回工作副本索引（-1 = 无候选）。
    /// </summary>
    private static int FindPressIndex(
        List<PressRecord> presses,
        InputBits requiredButtons,
        int endFrame,
        int tolerance)
    {
        int bestIndex = -1;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < presses.Count; i++)
        {
            PressRecord press = presses[i];
            if (press.Consumed)
                continue;
            if ((press.Bits & requiredButtons) != requiredButtons)
                continue;

            int distance = Math.Abs(press.Frame - endFrame);
            if (distance > tolerance)
                continue;

            if (distance < bestDistance ||
                (distance == bestDistance && bestIndex >= 0 && press.Frame > presses[bestIndex].Frame))
            {
                bestIndex = i;
                bestDistance = distance;
            }
        }

        return bestIndex;
    }

    private static CommandMatch CreateMatch(
        CommandDefinition definition,
        int playerIndex,
        int recognizedFrame,
        int sequenceStartFrame,
        List<DirectionEntry> directions,
        int endIndex,
        int matchElementCount,
        int buttonFrame)
    {
        bool isCharge = definition.ChargeHoldFrames > 0;
        int directionFrameCount = isCharge ? matchElementCount + 1 : matchElementCount;

        // 蓄力：首帧 = 蓄力方向进入帧（从挂起状态还原），其后是尾段各元素进入帧；
        // 普通：逐元素进入帧；裸按键（D56）：无方向元素。
        var directionFrames = new int[directionFrameCount];
        for (int k = 0; k < matchElementCount; k++)
            directionFrames[directionFrameCount - 1 - k] = directions[endIndex - k].Frame;
        if (isCharge)
            directionFrames[0] = sequenceStartFrame;

        return new CommandMatch(
            definition.CommandId,
            playerIndex,
            recognizedFrame,
            sequenceStartFrame,
            sequenceEndFrame: directionFrameCount > 0 ? directionFrames[^1] : buttonFrame,
            buttonFrame,
            definition.Priority,
            sequenceLength: definition.DirectionSequence.Count,
            expiresAtFrame: recognizedFrame + definition.BufferLifetimeFrames,
            directionFrames);
    }

    /// <summary>稳定冲突裁决：优先级降序 → 序列长度降序 → 指令 ID 序数升序（D40）。</summary>
    private static bool IsBetterThan(CommandMatch candidate, CommandMatch current)
    {
        if (candidate.Priority != current.Priority)
            return candidate.Priority > current.Priority;

        if (candidate.SequenceLength != current.SequenceLength)
            return candidate.SequenceLength > current.SequenceLength;

        return string.CompareOrdinal(candidate.CommandId, current.CommandId) < 0;
    }

    /// <summary>裁剪过旧历史，保证内存上界；被裁掉的条目已不可能参与任何匹配。</summary>
    private static void Prune(
        List<DirectionEntry> directions,
        List<PressRecord> presses,
        List<int> consumedEndFrames,
        IReadOnlyList<CommandDefinition> definitions,
        int frame)
    {
        int maxSequenceFrames = definitions.Count == 0
            ? 0
            : definitions.Max(d => d.MaxSequenceFrames);
        int maxButtonTolerance = definitions.Count == 0
            ? 0
            : definitions.Max(d => d.ButtonToleranceFrames);
        int oldestUsefulFrame = frame - maxSequenceFrames - maxButtonTolerance;
        bool includesHeldMotion = definitions.Any(definition => definition.MatchHeldMotion);

        // 至少保留最后一个条目：它是「当前方向」，也是下一次变化判断的基准。
        while (directions.Count > 1 &&
            (includesHeldMotion ? (long)directions[0].EndFrame + 1 : directions[0].Frame) < oldestUsefulFrame)
            directions.RemoveAt(0);

        while (directions.Count > MaxDirectionEntries)
            directions.RemoveAt(0);

        while (presses.Count > 0 && frame - presses[0].Frame > maxButtonTolerance)
            presses.RemoveAt(0);

        // 已消费手势的帧号随方向条目一并淘汰，避免无限增长。
        consumedEndFrames.RemoveAll(endFrame => endFrame < oldestUsefulFrame);
    }

    /// <summary>把 SOCD 解析后的相对输入映射到九宫格方向。</summary>
    private static CommandDirection ToCommandDirection(RelativeInput input)
    {
        if (input.Up)
            return input.Forward ? CommandDirection.UpForward
                : input.Back ? CommandDirection.UpBack
                : CommandDirection.Up;

        if (input.Down)
            return input.Forward ? CommandDirection.DownForward
                : input.Back ? CommandDirection.DownBack
                : CommandDirection.Down;

        return input.Forward ? CommandDirection.Forward
            : input.Back ? CommandDirection.Back
            : CommandDirection.Neutral;
    }

    /// <summary>结构化相等：同内容的状态相等（跨实例确定性比较依赖这一点）。</summary>
    public bool Equals(RecognitionState? other) =>
        other is not null &&
        LastObservedFrame == other.LastObservedFrame &&
        HeldAttacks == other.HeldAttacks &&
        Directions.SequenceEqual(other.Directions) &&
        Presses.SequenceEqual(other.Presses) &&
        Buffer.SequenceEqual(other.Buffer) &&
        ConsumedEndFrames.SequenceEqual(other.ConsumedEndFrames) &&
        ChargePendings.SequenceEqual(other.ChargePendings) &&
        SameKey(Last, other.Last);

    public override bool Equals(object? obj) => Equals(obj as RecognitionState);

    public override int GetHashCode() =>
        HashCode.Combine(LastObservedFrame, HeldAttacks, Directions.Count,
            Buffer.Count, ConsumedEndFrames.Count);

    /// <summary>识别结果按语义键比较（CommandId + 识别帧）；缓冲内识别帧唯一。</summary>
    private static bool SameKey(CommandMatch? a, CommandMatch? b) =>
        (a is null && b is null) ||
        (a is not null && b is not null &&
         a.RecognizedFrame == b.RecognizedFrame &&
         string.Equals(a.CommandId, b.CommandId, StringComparison.Ordinal));
}
