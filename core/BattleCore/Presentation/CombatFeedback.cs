using NewKOF.BattleCore.Simulation;

namespace NewKOF.BattleCore.Presentation;

/// <summary>打击反馈种类（阶段 10D，D126）。只由权威 <see cref="BattleEventType"/> 映射而来。</summary>
public enum FeedbackKind
{
    /// <summary>无反馈（事件不产生瞬时特效，如 MoveStarted / AttackActive）。</summary>
    None = 0,

    /// <summary>普通命中：火花 + 受击闪白 + 轻微抖动。</summary>
    Hit = 1,

    /// <summary>防御成功：冷色火花、无掉血数字。</summary>
    Blocked = 2,

    /// <summary>投技成立：更大的冲击火花。</summary>
    ThrowImpact = 3,

    /// <summary>双方投技相杀：中点火花。</summary>
    ThrowClash = 4,

    /// <summary>攻击相杀：中点火花、无伤害。</summary>
    StrikeClash = 5,

    /// <summary>飞行道具命中：火花。</summary>
    ProjectileImpact = 6,

    /// <summary>飞行道具被防御：冷色火花。</summary>
    ProjectileBlocked = 7,

    /// <summary>飞行道具对撞：中点爆炸。</summary>
    ProjectileClash = 8,

    /// <summary>单方 KO：红色闪光 + 强抖动。</summary>
    Knockout = 9,

    /// <summary>双方同帧 KO：红色闪光 + 强抖动。</summary>
    DoubleKnockout = 10,

    /// <summary>时间到：仅横幅，无火花。</summary>
    TimeOver = 11,
}

/// <summary>
/// 一条瞬时反馈指令（阶段 10D，D126）：由权威事件产生，携带<b>去重身份</b>与
/// 「发生在谁身上」，位置由表现层用权威 <c>BattleState</c> 解析。
/// </summary>
public readonly record struct FeedbackCue(
    FeedbackKind Kind,
    int SourceFighterId,
    int TargetFighterId,
    int Amount,
    long Identity,
    BattleEventType SourceType);

/// <summary>
/// <b>打击反馈导演</b>（阶段 10D，D126）：把权威战斗事件翻译成<b>至多一次</b>的瞬时反馈。
///
/// <para><b>去重</b>：身份是 <c>(scope, EventId)</c> 压成 <c>long</c>（scope = 回合号或交锋号）。
/// 同一条事件被重复读取（每个渲染帧都会读 <c>LastEvents</c>）也只产生一次火花；
/// 不同回合/交锋的同号事件因 scope 不同而各自生效。</para>
///
/// <para><b>绝不推断</b>：只有真实的 <see cref="BattleEvent"/> 才产生反馈——
/// 不根据「两人靠得近」猜命中，也不根据 HP 变化补特效。</para>
/// </summary>
public sealed class FeedbackDirector
{
    private readonly HashSet<long> _seen = new();

    /// <summary>已经产生过反馈的事件数（诊断用）。</summary>
    public int EmittedCount { get; private set; }

    /// <summary>被去重跳过的事件数（诊断用）。</summary>
    public int SuppressedCount { get; private set; }

    /// <summary>
    /// 尝试消费一条事件。返回 false 表示「无反馈」或「已消费过」。
    /// </summary>
    /// <param name="scope">回合号（1v1）或交锋号（3v3），与 EventId 组成唯一身份。</param>
    /// <param name="e">权威战斗事件。</param>
    /// <param name="cue">产生的反馈（<paramref name="kind"/> 为 None 时不写出）。</param>
    public bool TryConsume(int scope, BattleEvent e, out FeedbackCue cue)
    {
        cue = default;
        FeedbackKind kind = Map(e.Type);
        if (kind == FeedbackKind.None)
            return false;

        // scope 与 EventId 都非负；压成 long 后全局唯一。
        long identity = ((long)scope << 32) | (uint)e.EventId;
        if (!_seen.Add(identity))
        {
            SuppressedCount++;
            return false;
        }

        cue = new FeedbackCue(kind, e.SourceFighterId, e.TargetFighterId, e.Amount,
            identity, e.Type);
        EmittedCount++;
        return true;
    }

    /// <summary>清空去重记录（重开比赛 / 返回选人时调用）。</summary>
    public void Reset()
    {
        _seen.Clear();
        EmittedCount = 0;
        SuppressedCount = 0;
    }

    /// <summary>权威事件 → 反馈种类（唯一映射表；未列出的事件不产生瞬时特效）。</summary>
    public static FeedbackKind Map(BattleEventType type) => type switch
    {
        BattleEventType.Hit => FeedbackKind.Hit,
        BattleEventType.Blocked => FeedbackKind.Blocked,
        BattleEventType.ThrowHit => FeedbackKind.ThrowImpact,
        BattleEventType.ThrowClash => FeedbackKind.ThrowClash,
        BattleEventType.StrikeClash => FeedbackKind.StrikeClash,
        BattleEventType.ProjectileHit => FeedbackKind.ProjectileImpact,
        BattleEventType.ProjectileBlocked => FeedbackKind.ProjectileBlocked,
        BattleEventType.ProjectileClash => FeedbackKind.ProjectileClash,
        BattleEventType.Knockout => FeedbackKind.Knockout,
        BattleEventType.DoubleKnockout => FeedbackKind.DoubleKnockout,
        BattleEventType.TimeOver => FeedbackKind.TimeOver,
        _ => FeedbackKind.None,
    };
}

/// <summary>
/// <b>命中抖动</b>（阶段 10D，D126）：只作用于表现层节点偏移，<b>不改变权威坐标</b>。
///
/// <para>用固定偏移序列而非随机数，保证同一输入序列在截图/回放里完全可复现；
/// 幅度随帧线性衰减到 0，绝不允许持续抖动。</para>
/// </summary>
public sealed class HitShake
{
    /// <summary>固定抖动序列（世界单位，按幅度缩放）。</summary>
    private static readonly (int X, int Y)[] Pattern =
    {
        (1, 0), (-1, -1), (0, 1), (-1, 0), (1, 1), (0, -1),
    };

    private int _magnitude;
    private int _elapsed;

    /// <summary>当前是否在抖动。</summary>
    public bool IsActive => _magnitude > 0;

    /// <summary>当前幅度上限（诊断用）。</summary>
    public int Magnitude => _magnitude;

    /// <summary>叠加一次抖动（取较大值，不累加，避免连续命中把画面晃散）。</summary>
    /// <param name="magnitude">0..8；超出上限被夹紧。</param>
    public void Trigger(int magnitude)
    {
        if (magnitude <= 0)
            return;
        _magnitude = Math.Max(_magnitude, Math.Clamp(magnitude, 0, 8));
        _elapsed = 0;
    }

    /// <summary>推进一个渲染帧并返回本帧偏移（无抖动时返回 (0,0)）。</summary>
    public (int X, int Y) Tick()
    {
        if (_magnitude <= 0)
            return (0, 0);

        (int x, int y) = Pattern[_elapsed % Pattern.Length];
        _elapsed++;
        if (_elapsed >= Pattern.Length)
        {
            _magnitude--;
            _elapsed = 0;
        }

        int scale = Math.Max(1, _magnitude);
        return (x * scale, y * scale);
    }

    /// <summary>立即停止（新回合/换人/关特效）。</summary>
    public void Reset()
    {
        _magnitude = 0;
        _elapsed = 0;
    }
}
