using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Commands;

/// <summary>
/// 拳皇式<b>指令识别器</b>（门面）：把逐逻辑帧的输入历史转换成「识别结果」。
/// 纯 C#、零 Godot 依赖，<b>不</b>负责招式发动、取消、硬直或伤害。
///
/// <para>
/// 阶段 04（D55）起，识别核心是纯函数式的 <see cref="RecognitionState"/>：
/// 本类只是它的<b>双玩家可变包装</b>，保留 03B 的公共 API 供既有测试与
/// 独立使用（识别演示、工具）场景调用。战斗内核不经过本类——它直接在
/// <c>BattleSimulation.Step</c> 内按角色推进各玩家的 <see cref="RecognitionState"/>，
/// 使识别状态进入权威快照、可随快照重放。</para>
///
/// <para>识别规则的完整文档见 <see cref="RecognitionState"/>（D39–D41、D43–D54）：
/// 方向按变化压缩、每条目按当帧朝向解析、序列严格连续、末方向允许按键容差内稍早完成、
/// 蓄力 = 释放挂起状态机（保持任意久、释放窗口 3 帧、尾段必须属于本次释放之后、
/// 一次性资源）、冲突裁决 = 优先级降序 → 长度降序 → ID 序数、一次边沿/一次手势
/// 只完成一条指令、结果按逻辑帧过期。</para>
///
/// <para><b>帧号契约</b>：第一次 <see cref="Observe"/> 允许从任意非负初始帧开始；
/// 此后必须严格逐帧连续，非法帧在修改任何状态前抛出（纯函数天然无半提交）。</para>
/// </summary>
public sealed class CommandRecognizer
{
    private readonly IReadOnlyList<CommandDefinition> _definitions;
    private readonly RecognitionState[] _players = { RecognitionState.Empty, RecognitionState.Empty };
    private int _lastObservedFrame = -1;

    /// <param name="definitions">指令定义集合；ID 必须唯一。</param>
    /// <exception cref="ArgumentNullException"><paramref name="definitions"/> 为 null。</exception>
    /// <exception cref="ArgumentException">ID 重复或为空。</exception>
    public CommandRecognizer(IEnumerable<CommandDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        CommandDefinition[] copy = definitions.ToArray();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CommandDefinition definition in copy)
        {
            if (!seen.Add(definition.CommandId))
                throw new ArgumentException($"指令 ID 重复：{definition.CommandId}。", nameof(definitions));
        }

        _definitions = Array.AsReadOnly(copy);
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
    /// 该契约与 <c>BattleSession</c> 的逐帧提交机制一致（每帧恰好调用一次）。
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

        _players[0] = _players[0].Advance(input.Player1, player1Facing, input.Frame, 0, _definitions);
        _players[1] = _players[1].Advance(input.Player2, player2Facing, input.Frame, 1, _definitions);
    }

    /// <summary>最近一次识别到的指令；从未识别到返回 null（供 HUD 显示）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">玩家索引不是 0/1。</exception>
    public CommandMatch? LastMatch(int player) => Player(player).Last;

    /// <summary>
    /// 当前仍在有效期内的识别结果（按识别顺序，只读视图）。
    /// 过期结果在下一帧 <see cref="Observe"/> 时被移除。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">玩家索引不是 0/1。</exception>
    public IReadOnlyList<CommandMatch> BufferedMatches(int player)
    {
        RecognitionState state = Player(player);
        var matches = new CommandMatch[state.Buffer.Count];
        for (int i = 0; i < state.Buffer.Count; i++)
            matches[i] = state.Buffer[i].Match;

        return matches;
    }

    private RecognitionState Player(int player) => player switch
    {
        0 => _players[0],
        1 => _players[1],
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "仅支持 1v1（玩家索引 0/1）。"),
    };
}
