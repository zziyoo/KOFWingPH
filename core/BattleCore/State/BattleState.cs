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

    /// <summary>
    /// 权威回合状态（阶段 07A，D89）。不可变；进行中为 <see cref="RoundState.InProgress"/>。
    /// 终局后本状态不再变化（战斗冻结），胜负只能由最终体力判定一次。
    /// </summary>
    public RoundState Round { get; }

    /// <summary>飞行道具集合（阶段 08D，D99）；构造时复制并只读，顺序 = 生成顺序（ID 升序）。</summary>
    public IReadOnlyList<ProjectileState> Projectiles { get; }

    /// <summary>
    /// 下一个可分配的投掷物 ID（阶段 08D，D99）：单调递增、随快照携带，
    /// 保证「同一快照重放 → 同一串 ID」，且同帧多弹不会撞号。
    /// </summary>
    public int NextProjectileId { get; }

    /// <summary>
    /// 当前回合剩余逻辑帧（阶段 09B，D107）：<b>权威计时器</b>，只由
    /// <c>BattleSimulation.Step</c> 在成功推进一个逻辑帧后递减；
    /// <c>null</c> = 未启用计时（旧单回合 API 的默认路径，逐位兼容阶段 01–08）。
    /// 终局冻结时原样保留，过渡/暂停/未提交帧都不改变它。
    /// </summary>
    public int? RoundTimerRemainingFrames { get; }

    /// <param name="round">回合状态；null 表示进行中（旧构造路径语义不变）。</param>
    /// <param name="projectiles">飞行道具集合；null/空 = 场上没有实体。</param>
    /// <param name="nextProjectileId">下一个投掷物 ID（≥ 1，且必须大于集合内所有 ID）。</param>
    /// <param name="roundTimerRemainingFrames">
    /// 回合剩余逻辑帧；null = 不启用计时。必须落在 0..<see cref="BattleCoreInfo.MaxRoundTimerFrames"/>。
    /// </param>
    public BattleState(int frame, IReadOnlyList<FighterState> fighters, int stateVersion,
        RoundState? round = null,
        IReadOnlyList<ProjectileState>? projectiles = null,
        int nextProjectileId = 1,
        int? roundTimerRemainingFrames = null)
    {
        if (frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "逻辑帧号不能为负。");
        ArgumentNullException.ThrowIfNull(fighters);
        if (fighters.Count != 2)
            throw new ArgumentException($"阶段 01 仅支持 2 名角色，实际 {fighters.Count}。", nameof(fighters));

        Frame = frame;
        Fighters = Array.AsReadOnly(fighters.ToArray());
        StateVersion = stateVersion;
        Round = round ?? RoundState.InProgress;
        Round.Validate();

        ProjectileState[] projectileCopy = (projectiles ?? Array.Empty<ProjectileState>()).ToArray();
        var seenIds = new HashSet<int>();
        foreach (ProjectileState projectile in projectileCopy)
        {
            projectile.Validate();
            if (!seenIds.Add(projectile.ProjectileId))
                throw new ArgumentException(
                    $"投掷物 ID 重复：{projectile.ProjectileId}（同一快照内必须唯一）。", nameof(projectiles));
            if (projectile.ProjectileId >= nextProjectileId)
                throw new ArgumentException(
                    $"投掷物 {projectile.ProjectileId} 的 ID 不小于 NextProjectileId={nextProjectileId}" +
                    "（分配器必须已经越过已存在的实体）。", nameof(nextProjectileId));
        }

        if (nextProjectileId < 1)
            throw new ArgumentOutOfRangeException(nameof(nextProjectileId), nextProjectileId,
                "下一个投掷物 ID 必须 ≥ 1。");

        Projectiles = Array.AsReadOnly(projectileCopy);
        NextProjectileId = nextProjectileId;

        if (roundTimerRemainingFrames is int timer
            && (timer < 0 || timer > BattleCoreInfo.MaxRoundTimerFrames))
        {
            throw new ArgumentOutOfRangeException(nameof(roundTimerRemainingFrames), timer,
                $"回合剩余逻辑帧必须在 0..{BattleCoreInfo.MaxRoundTimerFrames} 内。");
        }
        RoundTimerRemainingFrames = roundTimerRemainingFrames;

        foreach (FighterState fighter in Fighters)
            fighter.Validate();
    }

    /// <summary>按索引取角色（0 = P1，1 = P2）。</summary>
    public FighterState Fighter(int index) =>
        index is 0 or 1
            ? Fighters[index]
            : throw new ArgumentOutOfRangeException(nameof(index), index, "阶段 01 仅支持 1v1（索引 0/1）。");

    /// <summary>
    /// 创建默认对局状态：两名满血角色面对面站立（动作待机，无识别历史）。
    /// 状态格式版本 13 = 阶段 09B（新增权威回合计时器 RoundTimerRemainingFrames；
    /// 08D 的 12 版新增了 Projectiles 实体集合与 NextProjectileId）。
    /// 项目尚无序列化快照，无迁移负担，版本号只标记格式边界。
    /// <b>默认不启用计时</b>（null），保持旧单回合 API 逐位兼容。
    /// </summary>
    public static BattleState CreateDefault(int stateVersion = 14) => new(
        frame: 0,
        fighters: new[]
        {
            FighterState.Create(id: 0, maxHealth: 1000, positionX: -120, facing: Facing.Right),
            FighterState.Create(id: 1, maxHealth: 1000, positionX: 120, facing: Facing.Left),
        },
        stateVersion: stateVersion);
}
