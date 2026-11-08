namespace NewKOF.BattleCore.Team;

/// <summary>
/// 队伍中的一名队员（阶段 10A，D120）。<b>身份是「队伍 + 槽位」，不是 FighterId</b>——
/// 当前角色目录只有 Kai / Rei，必须允许同一角色占据多个槽位（Kai / Rei / Kai），
/// 而每个槽位拥有独立的出战历史、剩余体力与淘汰记录。
/// </summary>
public sealed class TeamMemberDefinition : IEquatable<TeamMemberDefinition>
{
    public TeamMemberDefinition(string fighterId, int maxHealth)
    {
        if (string.IsNullOrWhiteSpace(fighterId))
            throw new ArgumentException("队员的 FighterId 不能为空。", nameof(fighterId));
        if (maxHealth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHealth), maxHealth,
                "队员的最大体力必须为正。");

        FighterId = fighterId;
        MaxHealth = maxHealth;
    }

    /// <summary>角色 ID（可以与其他槽位重复）。</summary>
    public string FighterId { get; }

    /// <summary>该队员的满血值（来自权威 <c>FighterDefinition</c>，不假定恒为 1000）。</summary>
    public int MaxHealth { get; }

    public bool Equals(TeamMemberDefinition? other) =>
        other is not null && FighterId == other.FighterId && MaxHealth == other.MaxHealth;

    public override bool Equals(object? obj) => obj is TeamMemberDefinition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FighterId, MaxHealth);

    public override string ToString() => $"{FighterId}({MaxHealth})";
}

/// <summary>
/// <b>队伍定义</b>（阶段 10A，D120）：一个有固定出战顺序的队员序列。
///
/// <para>构造期拒绝：空队伍、人数不符（须与 <see cref="TeamSize"/> 一致）、
/// 空 FighterId、非法体力、重复槽位。队伍本身<b>不可变</b>，可安全共享给重开/回放。</para>
///
/// <para>默认 <see cref="TeamSize"/> = 3（第一版 3v3），但所有算法都通过
/// <see cref="Count"/> 取实际人数，不在别处散落常量 3。</para>
/// </summary>
public sealed class TeamDefinition : IEquatable<TeamDefinition>
{
    /// <summary>第一版队伍人数（3v3）。算法请一律读 <see cref="Count"/>。</summary>
    public const int TeamSize = 3;

    public TeamDefinition(string teamId, IEnumerable<TeamMemberDefinition> members)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            throw new ArgumentException("队伍 ID 不能为空。", nameof(teamId));
        ArgumentNullException.ThrowIfNull(members);

        TeamMemberDefinition[] list = members.ToArray();
        if (list.Length != TeamSize)
            throw new ArgumentException(
                $"队伍必须恰好 {TeamSize} 名队员，实际 {list.Length} 名。", nameof(members));
        if (list.Any(m => m is null))
            throw new ArgumentException("队伍包含 null 队员。", nameof(members));

        TeamId = teamId;
        Members = Array.AsReadOnly(list);
    }

    /// <summary>队伍 ID（仅用于显示/诊断；权威身份仍由队伍对象 + 槽位决定）。</summary>
    public string TeamId { get; }

    /// <summary>按出战顺序排列的队员（只读）。</summary>
    public IReadOnlyList<TeamMemberDefinition> Members { get; }

    /// <summary>队伍人数（= <see cref="Members"/> 的长度）。</summary>
    public int Count => Members.Count;

    /// <summary>按出战顺序排列的角色 ID（只读便捷视图）。</summary>
    public IReadOnlyList<string> FighterIds => _fighterIds ??= Array.AsReadOnly(
        Members.Select(m => m.FighterId).ToArray());

    private IReadOnlyList<string>? _fighterIds;

    /// <summary>取指定槽位的队员；越界立即拒绝。</summary>
    public TeamMemberDefinition MemberAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= Members.Count)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex,
                $"槽位索引越界（0..{Members.Count - 1}）。");
        return Members[slotIndex];
    }

    public bool Equals(TeamDefinition? other) =>
        other is not null && TeamId == other.TeamId && Members.SequenceEqual(other.Members);

    public override bool Equals(object? obj) => obj is TeamDefinition other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TeamId);
        foreach (TeamMemberDefinition member in Members)
            hash.Add(member);
        return hash.ToHashCode();
    }

    public override string ToString() => $"{TeamId}[{string.Join("/", Members)}]";
}

/// <summary>
/// 队内槽位身份（阶段 10A，D120）：<c>(TeamId, SlotIndex)</c>。
/// 用它区分「同名角色的不同队员」，绝不能用 FighterId 当队员身份。
/// </summary>
public readonly record struct TeamSlot(string TeamId, int SlotIndex)
{
    public override string ToString() => $"{TeamId}#{SlotIndex}";
}
