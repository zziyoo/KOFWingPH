using NewKOF.BattleCore.Combat;

namespace NewKOF.BattleCore.Match;

/// <summary>角色目录中的一条（阶段 09C，D110）。配置对象本身不可变，可安全共享给镜像对战。</summary>
public sealed class FighterCatalogEntry
{
    public FighterCatalogEntry(
        string fighterId, string displayName, string source, FighterDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(fighterId))
            throw new ArgumentException("角色 ID 不能为空。", nameof(fighterId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("角色显示名称不能为空。", nameof(displayName));

        FighterId = fighterId;
        DisplayName = displayName;
        Source = source ?? string.Empty;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    /// <summary>唯一角色 ID（用于选人与权威状态标识）。</summary>
    public string FighterId { get; }

    /// <summary>供 UI 显示的名称。</summary>
    public string DisplayName { get; }

    /// <summary>配置来源说明（09C 为 C# 演示配置，09D 起为 JSON 资源路径）。</summary>
    public string Source { get; }

    /// <summary>权威角色配置（不可变，构造期校验过）。</summary>
    public FighterDefinition Definition { get; }
}

/// <summary>
/// <b>数据驱动的角色目录</b>（阶段 09C，D110）：选人界面只读它，按双方 ID 生成
/// <see cref="FightRoster"/>；<c>BattleSimulation</c> 完全不知道角色名称与目录的存在。
///
/// <para>ID 唯一、非法选择立即拒绝；允许镜像角色（P1/P2 选同一个 ID）。
/// 配置对象不可变且不含任何可变战斗状态，因此镜像对战共用同一
/// <see cref="FighterDefinition"/> 实例不会共享受击/取消/飞行道具状态——
/// 那些都在每回合独立的 <c>BattleState</c> 里。</para>
/// </summary>
public sealed class FighterCatalog
{
    private readonly Dictionary<string, FighterCatalogEntry> _byId;

    public FighterCatalog(IEnumerable<FighterCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToArray();
        if (list.Length == 0)
            throw new ArgumentException("角色目录不能为空。", nameof(entries));

        _byId = new Dictionary<string, FighterCatalogEntry>(StringComparer.Ordinal);
        foreach (FighterCatalogEntry entry in list)
        {
            if (entry is null)
                throw new ArgumentException("角色目录包含 null 条目。", nameof(entries));
            if (!_byId.TryAdd(entry.FighterId, entry))
                throw new ArgumentException(
                    $"角色 ID 重复：{entry.FighterId}（目录内必须唯一）。", nameof(entries));
        }

        Entries = Array.AsReadOnly(list);
    }

    /// <summary>可选角色列表（顺序固定，供选人界面按索引切换）。</summary>
    public IReadOnlyList<FighterCatalogEntry> Entries { get; }

    /// <summary>按 ID 取条目；未知 ID 立即拒绝。</summary>
    public FighterCatalogEntry Get(string fighterId)
    {
        if (string.IsNullOrWhiteSpace(fighterId) || !_byId.TryGetValue(fighterId, out var entry))
            throw new ArgumentException(
                $"未知角色 ID：{fighterId ?? "null"}。可选：{string.Join(", ", _byId.Keys)}。",
                nameof(fighterId));
        return entry;
    }

    /// <summary>显示名称（供 HUD 只读展示）。</summary>
    public string DisplayName(string fighterId) => Get(fighterId).DisplayName;

    /// <summary>按双方选人生成对局配置；镜像角色允许。</summary>
    public FightRoster CreateRoster(string p1FighterId, string p2FighterId) =>
        new(Get(p1FighterId).Definition, Get(p2FighterId).Definition);

    /// <summary>
    /// 09C 的演示目录：Kai / Rei 来自既有 <see cref="DemoRoster"/>（测试夹具与明确回退路径）。
    /// 09D 起将由 JSON 加载器构建同构目录，正常游戏优先使用外部配置。
    /// </summary>
    public static FighterCatalog CreateDemo() => new(new[]
    {
        new FighterCatalogEntry("Kai", "Kai", "DemoRoster.CreateKai()", DemoRoster.CreateKai()),
        new FighterCatalogEntry("Rei", "Rei", "DemoRoster.CreateRei()", DemoRoster.CreateRei()),
    });
}
