namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一场对局的双方角色配置（1v1 固定两名）。纯数据、构造期校验、不可变。
///
/// <para><see cref="CreateEmpty"/> 提供双方「无指令、无招式」的空配置，
/// 保持阶段 01–03 的默认行为（只有输入与帧推进，没有动作）。</para>
/// </summary>
public sealed class FightRoster
{
    /// <param name="player1">P1 侧角色配置。</param>
    /// <param name="player2">P2 侧角色配置（镜像对战允许传同一实例）。</param>
    /// <exception cref="ArgumentNullException">任一配置为 null。</exception>
    public FightRoster(FighterDefinition player1, FighterDefinition player2)
    {
        Player1 = player1 ?? throw new ArgumentNullException(nameof(player1));
        Player2 = player2 ?? throw new ArgumentNullException(nameof(player2));
    }

    /// <summary>P1 侧角色配置。</summary>
    public FighterDefinition Player1 { get; }

    /// <summary>P2 侧角色配置。</summary>
    public FighterDefinition Player2 { get; }

    /// <summary>按索引取角色配置（0 = P1，1 = P2）。</summary>
    public FighterDefinition Fighter(int index) => index switch
    {
        0 => Player1,
        1 => Player2,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "仅支持 1v1（索引 0/1）。"),
    };

    /// <summary>空配置：双方都没有指令与招式（等价于阶段 01–03 的默认行为）。</summary>
    public static FightRoster CreateEmpty() => new(
        new FighterDefinition("EmptyP1", Array.Empty<Commands.CommandDefinition>(),
            Array.Empty<MoveDefinition>()),
        new FighterDefinition("EmptyP2", Array.Empty<Commands.CommandDefinition>(),
            Array.Empty<MoveDefinition>()));
}
