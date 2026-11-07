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
/// <item>首尾跨度超过 <see cref="CommandDefinition.MaxSequenceFrames"/> → 超时。</item>
/// <item>按键边沿与末方向进入帧的帧差必须在容差内（可提前或延后）。</item>
/// <item>纯方向指令（<see cref="CommandDefinition.RequiredButtons"/> 为
///       <see cref="InputBits.None"/>）只在其末方向<b>进入的那一帧</b>触发。</item>
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
    private readonly PlayerState[] _players = { new(), new() };
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

        _maxSequenceFrames = _definitions.Length == 0 ? 0 : _definitions.Max(d => d.MaxSequenceFrames);
        _maxButtonTolerance = _definitions.Length == 0 ? 0 : _definitions.Max(d => d.ButtonToleranceFrames);
    }

    /// <summary>
    /// 观察一个已提交的逻辑帧。必须<b>每帧调用一次、帧号严格递增</b>。
    /// </summary>
    /// <param name="input">该逻辑帧的双人输入。</param>
    /// <param name="player1Facing">P1 在该帧的朝向（来自当帧权威状态）。</param>
    /// <param name="player2Facing">P2 在该帧的朝向。</param>
    /// <exception cref="InvalidOperationException">帧号没有严格递增。</exception>
    public void Observe(FrameInput input, Facing player1Facing, Facing player2Facing)
    {
        if (input.Frame <= _lastObservedFrame)
            throw new InvalidOperationException(
                $"指令识别要求帧号严格递增：上一帧 {_lastObservedFrame}，收到 {input.Frame}。");

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

        // 1) 方向：只在「变化」时追加条目（按住不重复计数）。
        CommandDirection direction = ToCommandDirection(DirectionResolver.Resolve(bits, facing));
        if (player.Directions.Count == 0 || player.Directions[^1].Direction != direction)
            player.Directions.Add(new DirectionEntry(frame, direction));

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
        CommandMatch? best = null;
        ButtonPress? bestPress = null;
        foreach (CommandDefinition definition in _definitions)
        {
            if (!TryMatch(player, playerIndex, definition, frame, out CommandMatch? match,
                    out ButtonPress? press))
                continue;

            if (best is null || IsBetterThan(match, best))
            {
                best = match;
                bestPress = press;
            }
        }

        if (best is null)
            return;

        // 一次按键边沿只允许完成一条指令。
        if (bestPress is not null)
            bestPress.Consumed = true;

        player.Buffer.Add(best);
        player.Last = best;
    }

    /// <summary>
    /// 尝试匹配一条指令。返回的 <paramref name="press"/> 尚未标记消费，
    /// 由调用方在确定胜者后统一消费（同帧竞争时不能提前吃边沿）。
    /// </summary>
    private bool TryMatch(
        PlayerState player,
        int playerIndex,
        CommandDefinition definition,
        int frame,
        out CommandMatch match,
        out ButtonPress? press)
    {
        match = null!;
        press = null;

        IReadOnlyList<CommandDirection> sequence = definition.DirectionSequence;
        List<DirectionEntry> directions = player.Directions;
        if (directions.Count < sequence.Count)
            return false;

        // 从最近的方向条目向前尝试：末方向不必是「当前方向」——
        // 允许在容差窗口内稍早完成（例如 623 的 ↘ 结束后继续滚到 →）。
        for (int endIndex = directions.Count - 1; endIndex >= sequence.Count - 1; endIndex--)
        {
            int endFrame = directions[endIndex].Frame;

            // 帧号随索引递增：更早的条目只会离当前帧更远，可以直接停。
            if (frame - endFrame > definition.ButtonToleranceFrames)
                break;

            int startFrame = directions[endIndex - sequence.Count + 1].Frame;
            if (endFrame - startFrame > definition.MaxSequenceFrames)
                continue;

            if (!MatchesEndingAt(directions, endIndex, sequence))
                continue;

            // 纯方向指令：只在末方向进入的那一帧触发，避免按住重复触发。
            if (definition.RequiredButtons == InputBits.None)
            {
                if (endFrame != frame)
                    continue;

                match = CreateMatch(definition, playerIndex, frame, directions, endIndex, buttonFrame: -1);
                return true;
            }

            ButtonPress? candidate = FindPress(
                player.Presses, definition.RequiredButtons, endFrame, definition.ButtonToleranceFrames);
            if (candidate is null)
                continue;

            match = CreateMatch(definition, playerIndex, frame, directions, endIndex, candidate.Frame);
            press = candidate;
            return true;
        }

        return false;
    }

    /// <summary>检查压缩历史的 <paramref name="endIndex"/> 结尾是否恰好等于方向序列。</summary>
    private static bool MatchesEndingAt(
        List<DirectionEntry> directions,
        int endIndex,
        IReadOnlyList<CommandDirection> sequence)
    {
        for (int k = 0; k < sequence.Count; k++)
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
        List<DirectionEntry> directions,
        int endIndex,
        int buttonFrame)
    {
        int sequenceLength = definition.DirectionSequence.Count;
        int startIndex = endIndex - sequenceLength + 1;

        var directionFrames = new int[sequenceLength];
        for (int k = 0; k < sequenceLength; k++)
            directionFrames[k] = directions[startIndex + k].Frame;

        return new CommandMatch(
            definition.CommandId,
            playerIndex,
            recognizedFrame,
            sequenceStartFrame: directionFrames[0],
            sequenceEndFrame: directionFrames[^1],
            buttonFrame,
            definition.Priority,
            sequenceLength,
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

    /// <summary>压缩后的方向条目：只在方向变化时产生。</summary>
    private readonly record struct DirectionEntry(int Frame, CommandDirection Direction);

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
        public PlayerState()
        {
            BufferView = new ReadOnlyCollection<CommandMatch>(Buffer);
        }

        public List<DirectionEntry> Directions { get; } = new();

        public List<ButtonPress> Presses { get; } = new();

        public List<CommandMatch> Buffer { get; } = new();

        public ReadOnlyCollection<CommandMatch> BufferView { get; }

        public InputBits HeldAttacks { get; set; }

        public CommandMatch? Last { get; set; }
    }
}
