using NewKOF.BattleCore.Commands;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一名角色的<b>配置</b>（阶段 04，纯数据）：角色 ID + 专属指令表 + 专属招式表。
///
/// <para><b>角色专属指令表（B4）</b>：不同角色可拥有不同指令集合；两名角色可以拥有
/// 相同的 <see cref="CommandDefinition.CommandId"/> 但映射到不同
/// <see cref="MoveDefinition.MoveId"/>。识别器不认识角色，只按本配置给出的指令表匹配；
/// 指令到招式的映射只在本配置中。</para>
///
/// <para><b>不可变性</b>：构造时对传入集合做防御性复制，外部修改源数组不影响本配置；
/// 配置对象在模拟构造后被视为只读事实。</para>
/// </summary>
public sealed class FighterDefinition
{
    /// <param name="fighterId">角色唯一标识（如 <c>Kai</c>）。</param>
    /// <param name="commands">该角色的指令表（可为空 = 该角色没有可识别指令）。</param>
    /// <param name="moves">该角色的招式表（可为空）。</param>
    /// <exception cref="ArgumentException">ID 为空 / 指令 ID 重复 / 招式 ID 重复 /
    /// 招式引用了指令表中不存在的 CommandId。</exception>
    public FighterDefinition(
        string fighterId,
        IReadOnlyList<CommandDefinition> commands,
        IReadOnlyList<MoveDefinition> moves)
    {
        if (string.IsNullOrWhiteSpace(fighterId))
            throw new ArgumentException("角色 ID 不能为空。", nameof(fighterId));
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(moves);

        FighterId = fighterId;

        // 防御性复制：外部改动源集合不影响配置（B4.6）。
        var commandCopy = commands.ToArray();
        var moveCopy = moves.ToArray();

        var commandIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (CommandDefinition command in commandCopy)
        {
            if (!commandIds.Add(command.CommandId))
                throw new ArgumentException(
                    $"角色 {fighterId} 的指令 ID 重复：{command.CommandId}（同一角色内同一指令" +
                    "映射到多个招式会产生歧义）。", nameof(commands));
        }

        var moveIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (MoveDefinition move in moveCopy)
        {
            if (!moveIds.Add(move.MoveId))
                throw new ArgumentException(
                    $"角色 {fighterId} 的招式 ID 重复：{move.MoveId}。", nameof(moves));

            if (!commandIds.Contains(move.CommandId))
                throw new ArgumentException(
                    $"角色 {fighterId} 的招式 {move.MoveId} 引用了指令表中不存在的 " +
                    $"CommandId：{move.CommandId}。", nameof(moves));
        }

        Commands = Array.AsReadOnly(commandCopy);
        Moves = Array.AsReadOnly(moveCopy);

        // 指令 ID → 招式 的只读查找表（构造期建好，识别外不扫描）。
        var byCommand = new Dictionary<string, MoveDefinition>(StringComparer.Ordinal);
        var byId = new Dictionary<string, MoveDefinition>(StringComparer.Ordinal);
        foreach (MoveDefinition move in moveCopy)
        {
            byCommand.Add(move.CommandId, move);
            byId.Add(move.MoveId, move);
        }

        _moveByCommandId = byCommand;
        _moveById = byId;
    }

    /// <summary>角色唯一标识。</summary>
    public string FighterId { get; }

    /// <summary>该角色的专属指令表（只读，识别器按此匹配）。</summary>
    public IReadOnlyList<CommandDefinition> Commands { get; }

    /// <summary>该角色的专属招式表（只读）。</summary>
    public IReadOnlyList<MoveDefinition> Moves { get; }

    private readonly Dictionary<string, MoveDefinition> _moveByCommandId;
    private readonly Dictionary<string, MoveDefinition> _moveById;

    /// <summary>按指令 ID 查招式；该指令不触发任何招式时返回 null（指令保留在缓冲中）。</summary>
    public MoveDefinition? FindMove(string commandId) =>
        _moveByCommandId.TryGetValue(commandId, out MoveDefinition? move) ? move : null;

    /// <summary>按招式 ID 查招式；未配置时抛出异常由调用方处理（状态携带的 MoveId 必须已配置）。</summary>
    public MoveDefinition? FindMoveById(string moveId) =>
        _moveById.TryGetValue(moveId, out MoveDefinition? move) ? move : null;
}
