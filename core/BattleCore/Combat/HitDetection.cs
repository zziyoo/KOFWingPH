using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 三种判定框的<b>权威查询</b>（阶段 06A，D76）。
///
/// <para><b>职责边界</b>：</para>
/// <list type="table">
/// <item><term>Pushbox</term><description>角色实体占位——权威位置约束仍完全由 D73 最小
///       间距求解器负责（间距 60 = 双方各 30 半宽的实体相接）；本查询只提供与该配置
///       一致的几何供显示/调试，<b>绝不产生第二次推挤</b>。</description></item>
/// <item><term>Hurtbox</term><description>可被命中的区域——按姿态给出一至两个矩形
///       （站立系高 120、蹲下高 70；空中框跟随权威 PositionY）。</description></item>
/// <item><term>Hitbox</term><description>攻击框——只来自招式数据（<see cref="AttackDefinition"/>），
///       仅在 Active 阶段的配置帧范围内生效；Startup / Recovery / 无招式一律为空。</description></item>
/// </list>
///
/// <para>全部查询为纯函数：几何由<b>权威状态</b>（位置/朝向/动作阶段）与<b>配置</b>决定，
/// 与 Godot 无关；左右镜像只由权威 <c>Facing</c> 施加（D76），不读表现层状态。</para>
/// </summary>
public static class FighterBoxes
{
    /// <summary>Pushbox 半宽（与 D73 最小中心间距 60 一致：双方各 30，相接即 60）。</summary>
    public static readonly FixedPoint PushboxHalfWidth = FixedPoint.FromInt(30);

    /// <summary>Pushbox / 站立 Hurtbox 高度（与表现层色块 120 一致）。</summary>
    public static readonly FixedPoint StandHeight = FixedPoint.FromInt(120);

    /// <summary>蹲下 Hurtbox 高度。</summary>
    public static readonly FixedPoint CrouchHeight = FixedPoint.FromInt(70);

    /// <summary>Hurtbox 半宽（与 Pushbox 同宽；本阶段身体即受击面）。</summary>
    public static readonly FixedPoint HurtboxHalfWidth = FixedPoint.FromInt(30);

    /// <summary>
    /// 该角色当帧的<b>受击框</b>（姿态相关；脚底锚点 = PositionX/PositionY，Y 向上）。
    /// </summary>
    public static FixedRect GetHurtbox(FighterState fighter)
    {
        // 姿态相关（D76/D93）：蹲下与倒地贴地（70 高）；起身与其余状态用站立高度。
        FixedPoint height = fighter.LogicState
            is FighterLogicState.Crouching or FighterLogicState.Knockdown
            ? CrouchHeight
            : StandHeight;

        return new FixedRect(
            fighter.PositionX - HurtboxHalfWidth,
            fighter.PositionX + HurtboxHalfWidth,
            fighter.PositionY,
            fighter.PositionY + height);
    }

    /// <summary>
    /// 该角色当帧的 <b>Pushbox</b>（仅显示/调试查询；权威推挤仍由 D73 求解器负责）。
    /// </summary>
    public static FixedRect GetPushbox(FighterState fighter) =>
        new(
            fighter.PositionX - PushboxHalfWidth,
            fighter.PositionX + PushboxHalfWidth,
            fighter.PositionY,
            fighter.PositionY + StandHeight);

    /// <summary>
    /// 两个攻击实例的攻击框是否在同一帧<b>正面积重叠</b>（阶段 08C，D98 的几何前提）。
    ///
    /// <para>只做几何：两侧都必须处于 Active 且各自有生效攻击框；任一侧没有攻击框
    /// （待机/收招/无招式）直接返回 false。判定用 <see cref="FixedRect.Overlaps"/>
    /// （D76 正面积规则）——<b>仅边缘接触不算相杀</b>。攻击序号（D84）不参与本判定。</para>
    /// </summary>
    public static bool HitboxesOverlap(
        FighterState first, FighterDefinition firstDefinition,
        FighterState second, FighterDefinition secondDefinition)
    {
        ArgumentNullException.ThrowIfNull(firstDefinition);
        ArgumentNullException.ThrowIfNull(secondDefinition);

        IReadOnlyList<ActiveHitbox> firstBoxes = GetActiveHitboxesWithIndex(first, firstDefinition);
        if (firstBoxes.Count == 0)
            return false;

        IReadOnlyList<ActiveHitbox> secondBoxes = GetActiveHitboxesWithIndex(second, secondDefinition);
        if (secondBoxes.Count == 0)
            return false;

        for (int i = 0; i < firstBoxes.Count; i++)
        {
            for (int j = 0; j < secondBoxes.Count; j++)
            {
                if (firstBoxes[i].Box.Overlaps(secondBoxes[j].Box))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 该角色当帧的<b>生效攻击框</b>列表（随角色位置与朝向镜像）。
    /// 无招式 / 非 Active 阶段 / 招式无攻击数据 / 当前帧不在攻击生效范围内 → 空列表。
    ///
    /// <para>本重载只给<b>几何消费者</b>（调试绘制）使用；命中结算必须使用
    /// <see cref="GetActiveHitboxesWithIndex"/>——过滤后的列表下标<b>不是</b>
    /// 攻击定义在 <see cref="MoveDefinition.Attacks"/> 中的原始序号。</para>
    /// </summary>
    /// <param name="fighter">权威角色状态（动作阶段与剩余帧决定 elapsed）。</param>
    /// <param name="definition">该角色的配置（按 MoveId 查招式数据）。</param>
    public static IReadOnlyList<FixedRect> GetActiveHitboxes(FighterState fighter, FighterDefinition definition)
    {
        IReadOnlyList<ActiveHitbox> active = GetActiveHitboxesWithIndex(fighter, definition);
        if (active.Count == 0)
            return Array.Empty<FixedRect>();

        var boxes = new FixedRect[active.Count];
        for (int i = 0; i < boxes.Length; i++)
            boxes[i] = active[i].Box;

        return Array.AsReadOnly(boxes);
    }

    /// <summary>
    /// 该角色当帧的<b>生效攻击框</b>列表，同时携带每块框对应的
    /// <see cref="AttackDefinition"/> 在 <see cref="MoveDefinition.Attacks"/> 中的<b>原始序号</b>。
    ///
    /// <para><b>为什么需要原始序号（阶段 06 专项补修 P1）</b>：过滤掉未生效的攻击定义后，
    /// 列表下标与配置下标不再一致（例如 Attack[0] 只在第 2 个 Active 帧生效，
    /// 第 1 帧的列表只有 Attack[1]，其下标是 0 而原始序号是 1）。结算必须按原始序号
    /// 取回同一份攻击数据，否则会取到错误的伤害/高度/硬直。</para>
    ///
    /// <para>顺序固定：按 <see cref="MoveDefinition.Attacks"/> 的原始序号升序，
    /// 同一攻击定义一帧内至多一块框。</para>
    /// </summary>
    /// <param name="fighter">权威角色状态（动作阶段与剩余帧决定 elapsed）。</param>
    /// <param name="definition">该角色的配置（按 MoveId 查招式数据）。</param>
    public static IReadOnlyList<ActiveHitbox> GetActiveHitboxesWithIndex(
        FighterState fighter, FighterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // 只有 Active 阶段产生攻击框：Startup 不命中、Recovery 不再命中（D76）。
        if (fighter.LogicState != FighterLogicState.Active || fighter.MoveId is null)
            return Array.Empty<ActiveHitbox>();

        MoveDefinition? move = definition.FindMoveById(fighter.MoveId);
        if (move is null || move.Attacks.Count == 0)
            return Array.Empty<ActiveHitbox>();

        // D58 时序：Active 阶段内已流逝帧号 = ActiveFrames − 剩余帧数（0 起始）。
        // Startup → Active 转换完成的那一逻辑帧即 elapsed 0（第 1 个生效帧）。
        int elapsed = move.ActiveFrames - fighter.PhaseRemainingFrames;

        // 朝向镜像（D76）：前向偏移按权威 Facing 施加，世界 X 恒向右为正。
        FixedPoint forwardSign = fighter.Facing == Facing.Right ? FixedPoint.One : -FixedPoint.One;

        List<ActiveHitbox>? boxes = null;
        for (int i = 0; i < move.Attacks.Count; i++)
        {
            AttackDefinition attack = move.Attacks[i];
            if (elapsed < attack.FirstActiveFrame || elapsed > attack.LastActiveFrame)
                continue;

            FixedPoint centerX = fighter.PositionX + forwardSign * attack.OffsetForward;
            FixedPoint centerY = fighter.PositionY + attack.OffsetUp;

            boxes ??= new List<ActiveHitbox>(move.Attacks.Count);
            boxes.Add(new ActiveHitbox(
                FixedRect.FromCenter(centerX, centerY, attack.HalfWidth, attack.HalfHeight), i));
        }

        // 只读包装（D61 约定）：空结果共享同一实例；非空结果不可强转为 List/数组，写操作抛异常。
        return boxes is null
            ? (IReadOnlyList<ActiveHitbox>)Array.Empty<ActiveHitbox>()
            : boxes.AsReadOnly();
    }
}

/// <summary>一个生效攻击框 + 它在 <see cref="MoveDefinition.Attacks"/> 中的原始序号（阶段 06 补修 P1）。</summary>
/// <param name="Box">世界坐标攻击框（已按权威朝向镜像）。</param>
/// <param name="AttackIndex">原始攻击定义序号（结算取数据的唯一凭据）。</param>
public readonly record struct ActiveHitbox(FixedRect Box, int AttackIndex);

/// <summary>一帧内的一组攻击接触候选（不可变；空列表共享同一实例）。</summary>
/// <param name="AttackerId">攻击者角色 ID。</param>
/// <param name="TargetId">被击者角色 ID。</param>
/// <param name="MoveId">攻击招式 ID。</param>
/// <param name="AttackIndex">命中的攻击数据在招式内的序号。</param>
public readonly record struct AttackContact(int AttackerId, int TargetId, string MoveId, int AttackIndex);

/// <summary>
/// 攻击框 × 受击框的确定性检测（阶段 06A，D76）。
///
/// <para>直接双角色 × 各自攻击框的 AABB 检测（数量极小，无需空间索引）。
/// 结果顺序固定：先攻击者 0 后攻击者 1，攻击者内部按攻击数据序号——
/// 与 P1/P2 计算顺序无关的确定性输出。本类只产生<b>接触候选</b>；
/// 命中/防御/伤害结算属于 06B，重复命中抑制属于 06C。</para>
/// </summary>
public static class HitDetection
{
    /// <summary>共享空列表（避免每帧分配；调用方不得修改）。</summary>
    private static readonly AttackContact[] EmptyContacts = Array.Empty<AttackContact>();

    /// <summary>
    /// 检测本帧全部「攻击框 ∩ 受击框」接触候选（几何层；不结算）。
    /// 攻击框不检测攻击者自己；空中角色的 Hurtbox 同样参与几何检测
    /// （是否结算由 06B 的规则决定）。
    /// </summary>
    public static IReadOnlyList<AttackContact> Detect(
        FighterState fighter0,
        FighterState fighter1,
        FighterDefinition definition0,
        FighterDefinition definition1)
    {
        ArgumentNullException.ThrowIfNull(definition0);
        ArgumentNullException.ThrowIfNull(definition1);

        List<AttackContact>? contacts = null;
        Collect(fighter0, fighter1, definition0, 0, ref contacts);
        Collect(fighter1, fighter0, definition1, 1, ref contacts);

        // 只读包装（D34/D61 同一先例）：快照附属数据不得暴露可变 List。
        return contacts is null
            ? (IReadOnlyList<AttackContact>)EmptyContacts
            : contacts.AsReadOnly();
    }

    private static void Collect(
        FighterState attacker,
        FighterState target,
        FighterDefinition attackerDefinition,
        int attackerId,
        ref List<AttackContact>? contacts)
    {
        IReadOnlyList<ActiveHitbox> hitboxes = FighterBoxes.GetActiveHitboxesWithIndex(attacker, attackerDefinition);
        if (hitboxes.Count == 0)
            return;

        FixedRect hurtbox = FighterBoxes.GetHurtbox(target);
        string? moveId = attacker.MoveId;
        if (moveId is null)
            return;

        for (int i = 0; i < hitboxes.Count; i++)
        {
            if (hitboxes[i].Box.Overlaps(hurtbox))
            {
                contacts ??= new List<AttackContact>(4);

                // 原始攻击定义序号（不是过滤后列表下标）：结算据此取回同一份攻击数据。
                contacts.Add(new AttackContact(attackerId, 1 - attackerId, moveId, hitboxes[i].AttackIndex));
            }
        }
    }
}
