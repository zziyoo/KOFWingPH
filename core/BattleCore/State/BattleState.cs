namespace NewKOF.BattleCore.State;

/// <summary>
/// 权威战斗状态快照。**不可变**：所有字段为 readonly，构造后不得修改。
///
/// 所有权规则（阶段 01 已确定）：
/// - <see cref="BattleState"/> 由 <c>BattleSimulation</c> 唯一创建与持有。
/// - <c>Step()</c> 永不修改传入的状态，而是返回一个全新的实例。
/// - 表现层（Godot）只读，不得写回。
/// - 后续阶段的快照/回滚/重放直接依赖这一约定。
/// </summary>
public sealed record BattleState
{
    /// <summary>下一个待模拟的逻辑帧号。初始为 0。</summary>
    public int Frame { get; }

    /// <summary>1v1 固定两名角色。数组本身在构造时复制，对外只读。</summary>
    public IReadOnlyList<FighterState> Fighters { get; }

    /// <summary>状态版本号，用于未来快照格式兼容判断。</summary>
    public int StateVersion { get; }

    public BattleState(int frame, IReadOnlyList<FighterState> fighters, int stateVersion)
    {
        if (frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "逻辑帧号不能为负。");
        ArgumentNullException.ThrowIfNull(fighters);
        if (fighters.Count != 2)
            throw new ArgumentException($"阶段 01 仅支持 2 名角色，实际 {fighters.Count}。", nameof(fighters));

        Frame = frame;
        Fighters = Array.AsReadOnly(fighters.ToArray());
        StateVersion = stateVersion;

        foreach (FighterState fighter in Fighters)
            fighter.Validate();
    }

    /// <summary>按索引取角色（0 = P1，1 = P2）。</summary>
    public FighterState Fighter(int index) =>
        index is 0 or 1
            ? Fighters[index]
            : throw new ArgumentOutOfRangeException(nameof(index), index, "阶段 01 仅支持 1v1（索引 0/1）。");

    /// <summary>创建阶段 01 的默认对局状态：两名满血角色面对面站立。</summary>
    public static BattleState CreateDefault(int stateVersion = 1) => new(
        frame: 0,
        fighters: new[]
        {
            FighterState.Create(id: 0, maxHealth: 1000, positionX: -120, facing: Facing.Right),
            FighterState.Create(id: 1, maxHealth: 1000, positionX: 120, facing: Facing.Left),
        },
        stateVersion: stateVersion);
}
