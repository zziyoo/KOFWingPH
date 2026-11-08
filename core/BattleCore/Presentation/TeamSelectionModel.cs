using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Team;

namespace NewKOF.BattleCore.Presentation;

/// <summary>
/// <b>队伍选择模型</b>（阶段 10B，D123）：纯 C#、无 Godot 依赖，负责「双方各选三人 + 出战顺序」
/// 的界面状态。表现层只把按键转成本类的方法调用，再把结果交给 <c>FighterCatalog</c> 建队。
///
/// <para><b>为什么放在 BattleCore</b>：选队规则（槽位数、游标回绕、改阵容即取消确认、
/// 非法选择拒绝）是确定性逻辑，放进 Godot 节点里就无法用单元测试守住。</para>
///
/// <para><b>不负责</b>：绘制、按键映射、比赛胜负——那些属于表现层与比赛层。</para>
/// </summary>
public sealed class TeamSelectionModel
{
    /// <summary>玩家索引：0 = P1，1 = P2。</summary>
    public const int Player1 = 0;

    /// <summary>玩家索引：1 = P2。</summary>
    public const int Player2 = 1;

    private readonly int[][] _slotCharacterIndex;
    private readonly int[] _cursor = new int[2];
    private readonly bool[] _ready = new bool[2];

    public TeamSelectionModel(FighterCatalog catalog, int teamSize = TeamDefinition.TeamSize)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (teamSize < 1)
            throw new ArgumentOutOfRangeException(nameof(teamSize), teamSize, "队伍人数必须 ≥ 1。");
        if (catalog.Entries.Count == 0)
            throw new ArgumentException("角色目录不能为空。", nameof(catalog));

        Catalog = catalog;
        TeamSize = teamSize;
        _slotCharacterIndex = new[]
        {
            new int[teamSize],
            new int[teamSize],
        };

        // 默认：P1 全选第一个角色，P2 全选第二个（目录只有一个角色时用第一个）。
        int p2Default = catalog.Entries.Count > 1 ? 1 : 0;
        for (int i = 0; i < teamSize; i++)
        {
            _slotCharacterIndex[Player1][i] = 0;
            _slotCharacterIndex[Player2][i] = p2Default;
        }
    }

    /// <summary>角色目录（只读，用于显示与建队）。</summary>
    public FighterCatalog Catalog { get; }

    /// <summary>每队人数。</summary>
    public int TeamSize { get; }

    /// <summary>P1 当前编辑的槽位游标。</summary>
    public int P1Cursor => _cursor[Player1];

    /// <summary>P2 当前编辑的槽位游标。</summary>
    public int P2Cursor => _cursor[Player2];

    /// <summary>P1 是否已确认阵容。</summary>
    public bool P1Ready => _ready[Player1];

    /// <summary>P2 是否已确认阵容。</summary>
    public bool P2Ready => _ready[Player2];

    /// <summary>双方是否都已确认（可以开赛）。</summary>
    public bool BothReady => P1Ready && P2Ready;

    /// <summary>取某玩家某槽位当前选中的角色条目。</summary>
    public FighterCatalogEntry CharacterAt(int player, int slot)
    {
        ValidatePlayer(player);
        if (slot < 0 || slot >= TeamSize)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"槽位越界（0..{TeamSize - 1}）。");
        return Catalog.Entries[_slotCharacterIndex[player][slot]];
    }

    /// <summary>某玩家已选的角色 ID 序列（按出战顺序）。</summary>
    public IReadOnlyList<string> SelectedIds(int player)
    {
        ValidatePlayer(player);
        var ids = new string[TeamSize];
        for (int i = 0; i < TeamSize; i++)
            ids[i] = CharacterAt(player, i).FighterId;
        return Array.AsReadOnly(ids);
    }

    /// <summary>移动槽位游标（回绕）。</summary>
    public void MoveCursor(int player, int delta)
    {
        ValidatePlayer(player);
        _cursor[player] = Wrap(_cursor[player] + delta, TeamSize);
    }

    /// <summary>切换当前槽位的角色（回绕）。<b>改阵容会取消该玩家的确认状态</b>。</summary>
    public void CycleCharacter(int player, int delta)
    {
        ValidatePlayer(player);
        int slot = _cursor[player];
        _slotCharacterIndex[player][slot] =
            Wrap(_slotCharacterIndex[player][slot] + delta, Catalog.Entries.Count);
        _ready[player] = false;
    }

    /// <summary>切换确认状态。</summary>
    public void ToggleReady(int player)
    {
        ValidatePlayer(player);
        _ready[player] = !_ready[player];
    }

    /// <summary>取消双方确认（进入/离开界面时用，不清空已选阵容）。</summary>
    public void ClearReady()
    {
        _ready[Player1] = false;
        _ready[Player2] = false;
    }

    /// <summary>回到默认阵容与未确认状态。</summary>
    public void Reset()
    {
        int p2Default = Catalog.Entries.Count > 1 ? 1 : 0;
        for (int i = 0; i < TeamSize; i++)
        {
            _slotCharacterIndex[Player1][i] = 0;
            _slotCharacterIndex[Player2][i] = p2Default;
        }
        _cursor[Player1] = 0;
        _cursor[Player2] = 0;
        ClearReady();
    }

    /// <summary>按当前选择构建队伍定义（满血值由目录提供）。</summary>
    public TeamDefinition BuildTeam(int player, string teamId) =>
        Catalog.CreateTeam(teamId, SelectedIds(player));

    /// <summary>供 HUD 的单行阵容文本：<c>&lt; Kai * Rei ... &gt;</c>，<c>*</c> 标出当前编辑槽位。</summary>
    public string DescribeLine(int player)
    {
        ValidatePlayer(player);
        var parts = new List<string>(TeamSize);
        for (int i = 0; i < TeamSize; i++)
        {
            string name = CharacterAt(player, i).DisplayName;
            parts.Add(i == _cursor[player] ? $"[{name}]" : name);
        }
        return string.Join(" > ", parts);
    }

    private static int Wrap(int value, int count)
    {
        int wrapped = value % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    private static void ValidatePlayer(int player)
    {
        if (player is not (Player1 or Player2))
            throw new ArgumentOutOfRangeException(nameof(player), player, "玩家索引只能是 0 或 1。");
    }
}
