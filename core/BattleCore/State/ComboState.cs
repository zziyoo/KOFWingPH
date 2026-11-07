namespace NewKOF.BattleCore.State;

/// <summary>
/// 一名角色作为<b>被击方</b>的连段记录（阶段 07B，D90）。不可变，随 <see cref="FighterState"/> 进快照。
///
/// <para><b>连段定义（D90）</b>：目标在「尚未恢复自由行动」的连续受击过程中，再次受到
/// <b>另一个独立攻击实例</b>的有效命中，计入同一连段。第一次命中 = 1 Hit，
/// 之后每次满足连续条件的命中 +1。</para>
///
/// <para><b>连续性判定</b>：命中结算时读<b>结算前</b>的帧内状态——
/// 若它属于「受击延续状态」（Hitstun / AirHitstun / Knockdown / Wakeup，见
/// <see cref="Combat.HitReaction"/>）且连段记录的攻击者相同 → Count + 1；
/// 否则重新开始为 1 Hit。<b>恢复自由行动的那一帧</b>（硬直归零 → 待机）不算延续：
/// 该帧再被命中按新连段 1 Hit 计，并且该帧结束时旧连段被清空。</para>
///
/// <para>成功防御不增加命中数，并立即清空连段（防御方重新获得行动框架）。
/// 被中断的攻击实例不会重复计数（D81 去重在前）。</para>
/// </summary>
public sealed record ComboState
{
    private ComboState(int count, int attackerId)
    {
        Count = count;
        AttackerId = attackerId;
    }

    /// <summary>无连段（Count 0，AttackerId -1）。</summary>
    public static ComboState None { get; } = new(0, -1);

    /// <summary>当前连段命中数（0 = 无连段）。</summary>
    public int Count { get; }

    /// <summary>造成该连段的攻击者 ID（0/1）；无连段时为 -1。</summary>
    public int AttackerId { get; }

    /// <summary>是否处于连段中。</summary>
    public bool IsActive => Count > 0;

    /// <summary>
    /// 计算一次有效命中之后的连段记录（纯函数，不修改入参）。
    /// </summary>
    /// <param name="current">命中的连段记录。</param>
    /// <param name="attackerId">本次命中的攻击者 ID。</param>
    /// <param name="targetInHitReaction">命中前目标是否处于受击延续状态（不能自由行动）。</param>
    public static ComboState AfterHit(ComboState current, int attackerId, bool targetInHitReaction)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (targetInHitReaction && current.Count > 0 && current.AttackerId == attackerId)
            return new ComboState(current.Count + 1, attackerId);

        return new ComboState(1, attackerId);
    }

    /// <summary>校验不变式（在 <see cref="FighterState.Validate"/> 中调用）。</summary>
    /// <param name="defenderId">该记录所属角色（不能是攻击者自己）。</param>
    /// <exception cref="InvalidOperationException">计数/攻击者不自洽。</exception>
    public void Validate(int defenderId)
    {
        if (Count < 0)
            throw new InvalidOperationException($"连段命中数不能为负：{Count}。");

        if (Count == 0)
        {
            if (AttackerId != -1)
                throw new InvalidOperationException($"无连段时必须 AttackerId=-1，实际 {AttackerId}。");
            return;
        }

        if (AttackerId is not (0 or 1))
            throw new InvalidOperationException($"连段攻击者必须是 0/1，实际 {AttackerId}。");
        if (AttackerId == defenderId)
            throw new InvalidOperationException($"角色 {defenderId} 的连段不能由自己造成。");
    }

    /// <summary>调试文本（如 <c>3 by P1</c>）。</summary>
    public override string ToString() => IsActive ? $"{Count} by P{AttackerId + 1}" : "-";
}
