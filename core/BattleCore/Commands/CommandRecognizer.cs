using System.Collections.ObjectModel;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Commands;

/// <summary>
/// 拳皇式<b>指令识别器</b>：把逐逻辑帧的输入历史转换成「识别结果」。
/// 纯 C#、零 Godot 依赖，<b>不</b>负责招式发动、取消、硬直或伤害。
///
/// <para><b>输入模型</b></para>
/// <list type="number">
/// <item>每帧调用一次 <see cref="Observe"/>，帧号必须严格递增（由
///       <see cref="Simulation.BattleSession"/> 的提交回调保证）。</item>
/// <item>方向按<b>变化</b>压缩：同一方向按住 N 帧只产生一个条目。
///       因此「↓ 按住 10 帧再 →」不会被误判成 ↓↘→，也不会因为按住而重复计数。</item>
/// <item>每个条目在<b>当帧朝向</b>下经 <see cref="DirectionResolver"/> 换算为
///       <see cref="CommandDirection"/> 后存入历史。换边只影响之后的条目，
///       历史条目保持当时的相对含义——不会用当前朝向重算全部历史。</item>
/// <item>按键只认<b>按下边沿</b>（本帧有、上帧无）；按住不会重复触发。</item>
/// </list>
///
/// <para><b>匹配规则</b></para>
/// <list type="bullet">
/// <item>方向序列必须在压缩历史中<b>连续且顺序一致</b>（不跳过中间方向）。</item>
/// <item>序列末元素允许「稍早完成」：只要它距当前帧不超过该指令的
///       <see cref="CommandDefinition.ButtonToleranceFrames"/>，仍可被本帧按键补完。
///       这是 623 与 236 能在同一帧竞争的原因（见测试）。</item>
/// <item>首尾跨度超过 <see cref="CommandDefinition.MaxSequenceFrames"/> → 超时。
///       <b>仅约束普通方向指令</b>；蓄力指令的保持时长不受它约束（D51）。</item>
/// <item>按键边沿与末方向进入帧的帧差必须在容差内（可提前或延后）。</item>
/// <item>纯方向指令（<see cref="CommandDefinition.RequiredButtons"/> 为
///       <see cref="InputBits.None"/>）只在其末方向<b>进入的那一帧</b>触发。</item>
/// </list>
///
/// <para><b>蓄力规则（D49/D51/D52/D53）</b>：<see cref="CommandDefinition.ChargeHoldFrames"/>
/// &gt; 0 的指令按「释放挂起」状态机匹配，与方向历史条目解耦：</para>
/// <list type="number">
/// <item><b>保持</b>：蓄力方向（序列第一个元素，只认纯方向）连续保持
///       ≥ ChargeHoldFrames 帧；保持多久都不失效，内存不随时长增长。</item>
/// <item><b>释放</b>：离开蓄力方向的瞬间，从历史条目快照蓄力量与释放开始帧，
///       进入「释放挂起」；直接进入无关方向则蓄力立即失效。</item>
/// <item><b>释放窗口</b>：挂起期间容忍<b>中立</b>（含 SOCD 中立）与序列尾部方向；
///       其他任何方向立即失效。序列末方向须在「释放帧 +
///       <see cref="CommandDefinition.ChargeReleaseToleranceFrames"/>」内进入。</item>
/// <item><b>不足不补</b>：蓄力量不足 ChargeHoldFrames 时释放窗口不能补足。</item>
/// <item><b>重新计数</b>：挂起期间重新按回蓄力方向 → 挂起作废、从 0 重新计数。</item>
/// <item><b>一次性</b>：产生过候选的蓄力指令挂起立即作废——同一蓄力不得借
///       第二次按键边沿反复触发；重新蓄满后才可再次识别（D53）。</item>
/// <item><b>超时分离</b>：蓄力匹配不检查 MaxSequenceFrames；释放窗口与按键容差
///       分别约束方向完成与按键时机。普通方向指令超时规则不变。</item>
/// </list>
///
/// <para><b>冲突优先级（稳定、与遍历顺序无关）</b></para>
/// <list type="number">
/// <item><see cref="CommandDefinition.Priority"/> 大者优先；</item>
/// <item>方向序列长者优先（更复杂的指令压过更简单的）；</item>
/// <item><see cref="CommandDefinition.CommandId"/> 按 <see cref="string.CompareOrdinal"/>
///       升序，作为最终确定性兜底。</item>
/// </list>
///
/// <para><b>消费语义</b>：一次按键边沿最多完成一条指令。胜出者会消费该边沿，
/// 同一边沿不会在后续帧重复触发同一条或另一条指令。</para>
///
/// <para><b>手势消费（阶段 03B-1 补修，抑制幽灵指令）</b>：当一条指令在某一帧胜出，
/// 其方向序列的<b>末方向帧号</b>被标记为「已消费」。此后任何指令都不能再借该
/// 手势来匹配——即使落败指令的按键边沿仍在容差窗口内，也不会在后续帧幽灵触发。
/// 两个独立手势（重新走一遍方向）的末方向帧号不同，因此互不影响，后续合法输入
/// 仍能正常识别。这区分了「同一手势的冲突落败」与「两个独立手势先后完成」。</para>
///
/// <para><b>帧号契约</b>：第一次 <see cref="Observe"/> 允许从任意非负初始帧开始
/// （快照恢复可能从中途帧起）；此后必须<b>严格逐帧连续</b>，重复/回退/跳过都拒绝，
/// 且非法帧在修改内部状态前即抛出。</para>
///
/// <para><b>内存上界</b>：方向历史、按键历史与识别结果缓冲都有显式上限/过期规则，
/// 不随会话时长无限增长。</para>
/// </summary>
public sealed class CommandRecognizer
{
    /// <summary>
    /// 方向条目数量上限（兜底）。按帧窗口裁剪后，正常游玩远达不到该值；
    /// 保留它是为了在极端抖动输入下仍有确定的内存上界。
    /// </summary>
    private const int MaxDirectionEntries = 64;

    private readonly CommandDefinition[] _definitions;
    private readonly int _maxSequenceFrames;
    private readonly int _maxButtonTolerance;
    private readonly PlayerState[] _players;
    private int _lastObservedFrame = -1;

    /// <param name="definitions">指令定义集合；ID 必须唯一。</param>
    /// <exception cref="ArgumentNullException"><paramref name="definitions"/> 为 null。</exception>
    /// <exception cref="ArgumentException">ID 重复或为空。</exception>
    public CommandRecognizer(IEnumerable<CommandDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _definitions = definitions.ToArray();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CommandDefinition definition in _definitions)
        {
            if (!seen.Add(definition.CommandId))
                throw new ArgumentException($"指令 ID 重复：{definition.CommandId}。", nameof(definitions));
        }

        _players = new[] { new PlayerState(_definitions.Length), new PlayerState(_definitions.Length) };

        _maxSequenceFrames = _definitions.Length == 0 ? 0 : _definitions.Max(d => d.MaxSequenceFrames);
        _maxButtonTolerance = _definitions.Length == 0 ? 0 : _definitions.Max(d => d.ButtonToleranceFrames);
    }

    /// <summary>
    /// 观察一个已提交的逻辑帧。
    ///
    /// <para><b>帧号契约：</b></para>
    /// <list type="number">
    /// <item>第一次调用允许从任意<b>非负</b>初始帧开始（快照恢复可能从中间帧起）。</item>
    /// <item>后续必须<b>严格逐帧连续</b>：重复帧、回退帧、跳过帧都拒绝。</item>
    /// <item>非法帧在<b>修改任何内部状态之前</b>即抛出，识别器保持原状。</item>
    /// </list>
    /// 该契约与 <see cref="Simulation.BattleSession"/> 的逐帧提交机制一致
    /// （每帧恰好调用一次 <c>frameCommitted</c>）。
    /// </summary>
    /// <param name="input">该逻辑帧的双人输入。</param>
    /// <param name="player1Facing">P1 在该帧的朝向（来自当帧权威状态）。</param>
    /// <param name="player2Facing">P2 在该帧的朝向。</param>
    /// <exception cref="ArgumentOutOfRangeException">帧号为负。</exception>
    /// <exception cref="InvalidOperationException">帧号与上一帧不连续。</exception>
    public void Observe(FrameInput input, Facing player1Facing, Facing player2Facing)
    {
        if (input.Frame < 0)
            throw new ArgumentOutOfRangeException(nameof(input), input.Frame, "逻辑帧号不能为负。");

        // 首次允许任意非负起始帧；此后必须严格 +1 连续。
        if (_lastObservedFrame >= 0 && input.Frame != _lastObservedFrame + 1)
        {
            throw new InvalidOperationException(
                $"指令识别要求帧号严格逐帧连续：上一帧 {_lastObservedFrame}，收到 {input.Frame}（期望 {_lastObservedFrame + 1}）。");
        }

        _lastObservedFrame = input.Frame;

        ObservePlayer(0, input.Frame, input.Player1, player1Facing);
        ObservePlayer(1, input.Frame, input.Player2, player2Facing);
    }

    /// <summary>最近一次识别到的指令；从未识别到返回 null（供 HUD 显示）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">玩家索引不是 0/1。</exception>
    public CommandMatch? LastMatch(int player) => Player(player).Last;

    /// <summary>
    /// 当前仍在有效期内的识别结果（按识别顺序，只读视图）。
    /// 过期结果在下一帧 <see cref="Observe"/> 时被移除。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">玩家索引不是 0/1。</exception>
    public IReadOnlyList<CommandMatch> BufferedMatches(int player) => Player(player).BufferView;

    private PlayerState Player(int player) => player switch
    {
        0 => _players[0],
        1 => _players[1],
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "仅支持 1v1（玩家索引 0/1）。"),
    };

    private void ObservePlayer(int playerIndex, int frame, InputBits bits, Facing facing)
    {
        PlayerState player = _players[playerIndex];

        // 1) 方向：只在「变化」时追加条目（按住不重复计数），
        //    否则实时更新最后一个条目的持续结束帧。
        CommandDirection direction = ToCommandDirection(DirectionResolver.Resolve(bits, facing));
        bool directionChanged = player.Directions.Count == 0 || player.Directions[^1].Direction != direction;
        if (directionChanged)
        {
            player.Directions.Add(new DirectionEntry(frame, direction, frame));
        }
        else
        {
            player.Directions[^1] = player.Directions[^1] with { EndFrame = frame };
        }

        // 1b) 蓄力进度（D51/D52）：在方向历史更新后、裁剪前更新。
        //     释放瞬间从历史条目快照蓄力量与释放帧，此后蓄力信息不再依赖历史条目，
        //     长时间蓄力不会被 Prune() 提前清除，内存也不随蓄力时长增长。
        UpdateChargeStates(player, frame, direction, directionChanged);

        // 2) 按键：只认按下边沿（本帧有、上帧无）。
        InputBits attacks = bits & DirectionResolver.AttackButtons;
        InputBits pressed = attacks & ~player.HeldAttacks;
        player.HeldAttacks = attacks;
        if (pressed != InputBits.None)
            player.Presses.Add(new ButtonPress(frame, pressed));

        Prune(player, frame);

        // 3) 过期结果出队（逻辑帧计时，暂停时不流逝）。
        player.Buffer.RemoveAll(match => match.IsExpiredAt(frame));

        // 4) 同一帧所有指令一起评估，再按稳定优先级取唯一胜者。
        //    同时收集本次裁决中**所有合法候选的按键边沿**：胜者与落败候选
        //    都属于「同一次冲突」，它们的按键边沿都要一次性消费，否则落败候选
        //    （即使末方向帧号与胜者不同）仍能用残留边沿在后续帧幽灵触发。
        CommandMatch? best = null;
        int bestEndFrame = -1;
        var candidatePresses = new List<ButtonPress>();
        var candidateChargeIndices = new List<int>();
        for (int definitionIndex = 0; definitionIndex < _definitions.Length; definitionIndex++)
        {
            CommandDefinition definition = _definitions[definitionIndex];
            if (!TryMatch(player, playerIndex, definitionIndex, definition, frame, out CommandMatch? match,
                    out ButtonPress? press, out int endFrame))
                continue;

            if (press is not null)
                candidatePresses.Add(press);
            if (definition.ChargeHoldFrames > 0)
                candidateChargeIndices.Add(definitionIndex);

            if (best is null || IsBetterThan(match, best))
            {
                best = match;
                bestEndFrame = endFrame;
            }
        }

        if (best is null)
            return;

        // 一次按键边沿只允许完成一条指令：胜者与落败候选的边沿都消费，
        // 落败候选不会在后续帧借残留边沿补完（幽灵指令）。
        foreach (ButtonPress press in candidatePresses)
            press.Consumed = true;

        // 蓄力是一次性资源（D53）：产生过候选的蓄力指令其释放挂起立即作废。
        // 同一蓄力不得在释放窗口内借第二次按键边沿反复触发；重新蓄满后才可再次识别。
        foreach (int definitionIndex in candidateChargeIndices)
            player.ChargePendings[definitionIndex].Clear();

        // 抑制「同一手势」被其他指令在后续帧补完（幽灵指令）：
        // 该手势的方向序列末方向帧号标记为已消费，之后任何指令都不能
        // 再借它匹配。两个独立手势的末方向帧号不同，互不影响。
        player.ConsumedEndFrames.Add(bestEndFrame);

        player.Buffer.Add(best);
        player.Last = best;
    }

    /// <summary>
    /// 更新每条蓄力指令的「释放挂起」状态（D51/D52）。
    ///
    /// <para>状态规则（每帧、每条蓄力指令）：</para>
    /// <list type="number">
    /// <item>当前方向 == 蓄力方向：挂起作废，重新计数（保持时长从历史条目现算）——
    ///       「释放后重新按回蓄力方向」即重新计数。</item>
    /// <item>本帧刚离开蓄力方向（方向变化且前一末条目是蓄力方向）：从该条目快照
    ///       蓄力量与释放开始帧（= 本帧，第一个未保持蓄力方向的帧），进入释放挂起。</item>
    /// <item>挂起期间：中立（含 SOCD 中立）容忍；序列尾部方向容忍（正在走尾段）；
    ///       其他任何方向 → 挂起立即作废（蓄力完全失效，须重新蓄满）。</item>
    /// </list>
    /// 挂起不按时间自行过期——匹配时用「末方向进入帧 − 释放帧 ≤ 释放窗口」判定，
    /// 因此超时的挂起自然无法完成匹配，也无需随时间清理（状态为每指令一个固定槽位）。
    /// </summary>
    private void UpdateChargeStates(
        PlayerState player, int frame, CommandDirection current, bool directionChanged)
    {
        for (int definitionIndex = 0; definitionIndex < _definitions.Length; definitionIndex++)
        {
            CommandDefinition definition = _definitions[definitionIndex];
            if (definition.ChargeHoldFrames <= 0)
                continue; // 非蓄力指令没有蓄力进度。

            ChargePendingState state = player.ChargePendings[definitionIndex];
            CommandDirection chargeDirection = definition.DirectionSequence[0];

            if (current == chargeDirection)
            {
                // 正在（重新）保持蓄力方向：挂起作废，重新计数。
                state.Clear();
                continue;
            }

            if (directionChanged && player.Directions.Count >= 2 &&
                player.Directions[^2].Direction == chargeDirection)
            {
                // 本帧刚从蓄力方向离开：进入中立（含 SOCD 中立）或尾段方向 → 释放挂起；
                // 直接进入无关方向 → 蓄力立即失效（不产生挂起，明确规则）。
                if (current == CommandDirection.Neutral || IsTailDirection(definition, current))
                {
                    // 快照蓄力量（该条目持续帧数）与释放开始帧（= 本帧，第一个未保持的帧）。
                    DirectionEntry held = player.Directions[^2];
                    state.MarkReleased(held.EndFrame - held.Frame + 1, frame);
                }
                else
                {
                    state.Clear();
                }

                continue;
            }

            if (state.HasPending && current != CommandDirection.Neutral && !IsTailDirection(definition, current))
            {
                // 释放窗口内出现无关方向 → 立即失效（明确规则，不允许蒙混）。
                state.Clear();
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
    /// 尝试匹配一条指令。返回的 <paramref name="press"/> 尚未标记消费，
    /// 由调用方在确定胜者后统一消费（同帧竞争时不能提前吃边沿）。
    /// <paramref name="matchedEndFrame"/> 是本次匹配所用方向序列末元素的帧号，
    /// 供调用方标记「该手势已消费」以抑制幽灵指令。
    /// </summary>
    private bool TryMatch(
        PlayerState player,
        int playerIndex,
        int definitionIndex,
        CommandDefinition definition,
        int frame,
        out CommandMatch match,
        out ButtonPress? press,
        out int matchedEndFrame)
    {
        match = null!;
        press = null;
        matchedEndFrame = -1;

        IReadOnlyList<CommandDirection> sequence = definition.DirectionSequence;
        List<DirectionEntry> directions = player.Directions;

        // 蓄力指令（D51）：蓄力方向来自释放挂起状态（与历史条目解耦，长蓄力条目
        // 被裁剪也能匹配），历史只需容纳「尾段」（第 2 个及以后元素）；
        // 普通指令需要完整序列都在历史中。
        bool isCharge = definition.ChargeHoldFrames > 0;
        int matchElementCount = isCharge ? sequence.Count - 1 : sequence.Count;
        if (directions.Count < matchElementCount)
            return false;

        // 从最近的方向条目向前尝试：末方向不必是「当前方向」——
        // 允许在容差窗口内稍早完成（例如 623 的 ↘ 结束后继续滚到 →）。
        for (int endIndex = directions.Count - 1; endIndex >= matchElementCount - 1; endIndex--)
        {
            int endFrame = directions[endIndex].Frame;

            // 帧号随索引递增：更早的条目只会离当前帧更远，可以直接停。
            if (frame - endFrame > definition.ButtonToleranceFrames)
                break;

            // 已被其他指令消费过的方向手势，不能再被本指令借来匹配。
            if (player.ConsumedEndFrames.Contains(endFrame))
                continue;

            int startFrame;
            if (isCharge)
            {
                // 蓄力检查（D51/D52）：蓄力量与释放窗口来自释放挂起状态。
                // 保持时长不受 MaxSequenceFrames 约束（否则长蓄力反而失效）；
                // 蓄力不足不能靠释放窗口补足；释放窗口已过则不得匹配。
                ChargePendingState pending = player.ChargePendings[definitionIndex];
                if (!pending.HasPending)
                    continue;
                if (pending.AmountHeld < definition.ChargeHoldFrames)
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

            ButtonPress? candidate = FindPress(
                player.Presses, definition.RequiredButtons, endFrame, definition.ButtonToleranceFrames);
            if (candidate is null)
                continue;

            match = CreateMatch(definition, playerIndex, frame, startFrame, directions,
                endIndex, matchElementCount, candidate.Frame);
            press = candidate;
            matchedEndFrame = endFrame;
            return true;
        }

        return false;
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
    /// 并列则取更晚的一帧（确定性规则）。
    /// </summary>
    private static ButtonPress? FindPress(
        List<ButtonPress> presses,
        InputBits requiredButtons,
        int endFrame,
        int tolerance)
    {
        ButtonPress? best = null;
        int bestDistance = int.MaxValue;

        foreach (ButtonPress press in presses)
        {
            if (press.Consumed)
                continue;
            if ((press.Bits & requiredButtons) != requiredButtons)
                continue;

            int distance = Math.Abs(press.Frame - endFrame);
            if (distance > tolerance)
                continue;

            if (distance < bestDistance || (distance == bestDistance && best is not null && press.Frame > best.Frame))
            {
                best = press;
                bestDistance = distance;
            }
        }

        return best;
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
        // 普通：逐元素进入帧。
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
            sequenceEndFrame: directionFrames[^1],
            buttonFrame,
            definition.Priority,
            sequenceLength: definition.DirectionSequence.Count,
            expiresAtFrame: recognizedFrame + definition.BufferLifetimeFrames,
            directionFrames);
    }

    /// <summary>稳定冲突裁决：优先级降序 → 序列长度降序 → 指令 ID 序数升序。</summary>
    private static bool IsBetterThan(CommandMatch candidate, CommandMatch current)
    {
        if (candidate.Priority != current.Priority)
            return candidate.Priority > current.Priority;

        if (candidate.SequenceLength != current.SequenceLength)
            return candidate.SequenceLength > current.SequenceLength;

        return string.CompareOrdinal(candidate.CommandId, current.CommandId) < 0;
    }

    /// <summary>裁剪过旧历史，保证内存上界；被裁掉的条目已不可能参与任何匹配。</summary>
    private void Prune(PlayerState player, int frame)
    {
        int oldestUsefulFrame = frame - _maxSequenceFrames - _maxButtonTolerance;

        // 至少保留最后一个条目：它是「当前方向」，也是下一次变化判断的基准。
        while (player.Directions.Count > 1 && player.Directions[0].Frame < oldestUsefulFrame)
            player.Directions.RemoveAt(0);

        while (player.Directions.Count > MaxDirectionEntries)
            player.Directions.RemoveAt(0);

        while (player.Presses.Count > 0 && frame - player.Presses[0].Frame > _maxButtonTolerance)
            player.Presses.RemoveAt(0);

        // 已消费手势的帧号随方向条目一并淘汰，避免无限增长。
        player.ConsumedEndFrames.RemoveWhere(endFrame => endFrame < oldestUsefulFrame);
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

    /// <summary>压缩后的方向条目：只在方向变化时产生；<see cref="EndFrame"/> 记录该方向持续到的最后一帧（供蓄力保持时长计算）。</summary>
    private readonly record struct DirectionEntry(int Frame, CommandDirection Direction, int EndFrame);

    /// <summary>一次按键按下边沿；<see cref="Consumed"/> 保证同一次按下不重复触发。</summary>
    private sealed class ButtonPress
    {
        public ButtonPress(int frame, InputBits bits)
        {
            Frame = frame;
            Bits = bits;
        }

        public int Frame { get; }

        public InputBits Bits { get; }

        public bool Consumed { get; set; }
    }

    /// <summary>单名玩家的识别状态。</summary>
    private sealed class PlayerState
    {
        public PlayerState(int definitionCount)
        {
            BufferView = new ReadOnlyCollection<CommandMatch>(Buffer);

            // 每条指令一个固定蓄力槽位（非蓄力指令的槽位永不使用）。
            // 状态总量 = 蓄力指令数 × 常数，不随蓄力时长、历史或会话时长增长（D51）。
            ChargePendings = new ChargePendingState[definitionCount];
            for (int i = 0; i < definitionCount; i++)
                ChargePendings[i] = new ChargePendingState();
        }

        public List<DirectionEntry> Directions { get; } = new();

        public List<ButtonPress> Presses { get; } = new();

        public List<CommandMatch> Buffer { get; } = new();

        public ReadOnlyCollection<CommandMatch> BufferView { get; }

        /// <summary>
        /// 每条指令的蓄力「释放挂起」槽位（按定义索引对齐）。
        /// 蓄力量与释放帧在离开蓄力方向的瞬间从历史条目快照，此后不再依赖
        /// 方向历史——长蓄力的起始条目被裁剪也不影响识别（D51）。
        /// </summary>
        public ChargePendingState[] ChargePendings { get; }

        /// <summary>
        /// 已被某条指令消费的方向手势末方向帧号。用于抑制「同一手势」被
        /// 其他指令在后续容差窗口内补完（幽灵指令）。帧号与方向条目一一对应，
        /// 随方向条目淘汰时一并清理。
        /// </summary>
        public HashSet<int> ConsumedEndFrames { get; } = new();

        public InputBits HeldAttacks { get; set; }

        public CommandMatch? Last { get; set; }
    }

    /// <summary>
    /// 单条蓄力指令的「释放挂起」状态（D51/D52）：一条固定槽位，
    /// 记录释放瞬间的蓄力量与释放开始帧。挂起不按时间自行过期——匹配时用
    /// 「末方向进入帧 − 释放帧 ≤ 释放窗口」判定，超时挂起自然无法完成匹配。
    /// </summary>
    private sealed class ChargePendingState
    {
        public bool HasPending { get; private set; }

        /// <summary>释放瞬间的蓄力量（蓄力方向已连续保持的帧数，从历史条目快照）。</summary>
        public int AmountHeld { get; private set; }

        /// <summary>释放开始帧（第一个未保持蓄力方向的逻辑帧）。</summary>
        public int ReleasedAtFrame { get; private set; }

        public void MarkReleased(int amountHeld, int releasedAtFrame)
        {
            HasPending = true;
            AmountHeld = amountHeld;
            ReleasedAtFrame = releasedAtFrame;
        }

        public void Clear() => HasPending = false;
    }
}
