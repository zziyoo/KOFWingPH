using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>地面角色原点到朝向两侧身体边缘的距离。</summary>
public sealed class GroundBodyWidths
{
    public GroundBodyWidths(FixedPoint front, FixedPoint back)
    {
        FixedPoint max = FixedPoint.FromInt(640);
        if (front <= FixedPoint.Zero || front > max || back <= FixedPoint.Zero || back > max)
            throw new ArgumentOutOfRangeException(nameof(front), "地面身体宽度必须在 (0, 640] 内。");
        Front = front;
        Back = back;
    }

    public FixedPoint Front { get; }
    public FixedPoint Back { get; }
}

/// <summary>站立和蹲伏的局部受击矩形，X 沿面向，Y 向上。</summary>
public sealed class GroundHurtboxes
{
    public const int MaxLocalCoordinate = 8192;

    public GroundHurtboxes(IReadOnlyList<FixedRect> standing, IReadOnlyList<FixedRect> crouching,
        bool standingOnCrouchEntry = false)
    {
        Standing = Copy(standing, nameof(standing));
        Crouching = Copy(crouching, nameof(crouching));
        StandingOnCrouchEntry = standingOnCrouchEntry;
    }

    public IReadOnlyList<FixedRect> Standing { get; }
    public IReadOnlyList<FixedRect> Crouching { get; }
    public bool StandingOnCrouchEntry { get; }

    private static IReadOnlyList<FixedRect> Copy(IReadOnlyList<FixedRect> boxes, string name)
    {
        if (boxes is { Count: 0 })
            throw new ArgumentException("需要至少一个具有正面积的受击矩形。", name);
        return CopyLocalBoxes(boxes, name);
    }

    internal static IReadOnlyList<FixedRect> CopyLocalBoxes(IReadOnlyList<FixedRect> boxes, string name)
    {
        ArgumentNullException.ThrowIfNull(boxes, name);
        if (boxes.Any(box => box.MinX >= box.MaxX || box.MinY >= box.MaxY))
            throw new ArgumentException("受击矩形必须具有正面积。", name);
        FixedPoint limit = FixedPoint.FromInt(MaxLocalCoordinate);
        if (boxes.Any(box => box.MinX < -limit || box.MaxX > limit || box.MinY < -limit || box.MaxY > limit))
            throw new ArgumentOutOfRangeException(name, $"受击框局部坐标必须在 ±{MaxLocalCoordinate} 内。");
        return Array.AsReadOnly(boxes.ToArray());
    }
}

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
    /// <param name="movement">该角色的运动配置（阶段 05A，D64）；null 使用
    /// <see cref="MovementDefinition.CreateDefault"/>，保证 P1/P2 可携带不同运动数据。</param>
    /// <param name="groundHurtboxes">可选地面待机受击框；其他状态沿用通用受击框。</param>
    /// <exception cref="ArgumentException">ID 为空 / 指令 ID 重复 / 招式 ID 重复 /
    /// 招式引用了指令表中不存在的 CommandId。</exception>
    public FighterDefinition(
        string fighterId,
        IReadOnlyList<CommandDefinition> commands,
        IReadOnlyList<MoveDefinition> moves,
        MovementDefinition? movement = null,
        GroundHurtboxes? groundHurtboxes = null,
        GroundBodyWidths? groundBodyWidths = null,
        GroundBodyWidths? airBodyWidths = null)
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

        // 取消目标校验（阶段 07B，D91）：目标必须存在于<b>本角色</b>的招式表，且不能是自身
        // （自取消会形成无进展的循环，且没有实际意义）。必须在 moveIds 完整后再校验。
        foreach (MoveDefinition move in moveCopy)
        {
            foreach (CancelDefinition cancel in move.Cancels)
            {
                if (string.Equals(cancel.TargetMoveId, move.MoveId, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"角色 {fighterId} 的招式 {move.MoveId} 配置了取消到自身的规则。", nameof(moves));
                if (!moveIds.Contains(cancel.TargetMoveId))
                    throw new ArgumentException(
                        $"角色 {fighterId} 的招式 {move.MoveId} 取消了不存在的招式 " +
                        $"{cancel.TargetMoveId}。", nameof(moves));
            }
        }

        Commands = Array.AsReadOnly(commandCopy);
        Moves = Array.AsReadOnly(moveCopy);

        // 运动配置（D64）：null → 默认配置；非 null 直接持有（MovementDefinition 自身构造期不可变）。
        Movement = movement ?? MovementDefinition.CreateDefault();
        GroundHurtboxes = groundHurtboxes;
        GroundBodyWidths = groundBodyWidths;
        AirBodyWidths = airBodyWidths;
        if (airBodyWidths is not null && groundBodyWidths is null)
            throw new ArgumentException("airBodyWidths 需要配置 groundBodyWidths。", nameof(airBodyWidths));

        _movesByCommandId = new(StringComparer.Ordinal);
        foreach (var group in moveCopy.GroupBy(move => move.CommandId, StringComparer.Ordinal))
        {
            MoveDefinition[] variants = group.ToArray();
            if (variants.Any(move => move.MaximumStartBodyDistance.HasValue) && groundBodyWidths is null)
                throw new ArgumentException($"指令 {group.Key} 的身体距离条件需要 groundBodyWidths。", nameof(moves));
            for (int i = 0; i < variants.Length - 1; i++)
            {
                if (variants[i].MaximumStartBodyDistance is not { } threshold ||
                    variants[i + 1].MaximumStartBodyDistance is { } next && threshold >= next)
                    throw new ArgumentException($"指令 {group.Key} 的距离变体必须按递增阈值排列。", nameof(moves));
            }
            _movesByCommandId.Add(group.Key, Array.AsReadOnly(variants));
        }
        _moveById = moveCopy.ToDictionary(move => move.MoveId, StringComparer.Ordinal);
    }

    /// <summary>角色唯一标识。</summary>
    public string FighterId { get; }

    /// <summary>该角色的专属指令表（只读，识别器按此匹配）。</summary>
    public IReadOnlyList<CommandDefinition> Commands { get; }

    /// <summary>该角色的专属招式表（只读）。</summary>
    public IReadOnlyList<MoveDefinition> Moves { get; }

    /// <summary>该角色的运动配置（阶段 05A，D64；恒非 null）。</summary>
    public MovementDefinition Movement { get; }

    public GroundHurtboxes? GroundHurtboxes { get; }
    public GroundBodyWidths? GroundBodyWidths { get; }
    public GroundBodyWidths? AirBodyWidths { get; }

    private readonly Dictionary<string, IReadOnlyList<MoveDefinition>> _movesByCommandId;
    private readonly Dictionary<string, MoveDefinition> _moveById;

    /// <summary>按指令 ID 查第一个配置的招式。</summary>
    public MoveDefinition? FindMove(string commandId) => FindMoves(commandId).FirstOrDefault();

    /// <summary>按配置优先级返回该指令的招式候选。</summary>
    public IReadOnlyList<MoveDefinition> FindMoves(string commandId) =>
        _movesByCommandId.GetValueOrDefault(commandId) ?? Array.Empty<MoveDefinition>();

    /// <summary>按招式 ID 查招式；未配置时抛出异常由调用方处理（状态携带的 MoveId 必须已配置）。</summary>
    public MoveDefinition? FindMoveById(string moveId) =>
        _moveById.TryGetValue(moveId, out MoveDefinition? move) ? move : null;
}
