using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一条<b>飞行道具定义</b>（阶段 08D，D99）：挂在 <see cref="MoveDefinition.Projectiles"/> 上，
/// 由招式的 Active 阶段在指定帧生成一个<b>权威实体</b>（不是附着在角色身上的判定框）。
///
/// <para><b>命中载荷复用 <see cref="AttackDefinition"/></b>（避免第二套伤害/硬直/击退规则）：</para>
/// <list type="bullet">
/// <item><c>OffsetForward</c> / <c>OffsetUp</c> → 出生点相对发射者脚底锚点的偏移（按朝向镜像）；</item>
/// <item><c>HalfWidth</c> / <c>HalfHeight</c> → 投掷物自身的碰撞盒半尺寸；</item>
/// <item><c>Height</c> → 防御高度表（High/Low/Mid，与近战同一张表 D77）；</item>
/// <item><c>Damage</c> / <c>HitstunFrames</c> / <c>BlockstunFrames</c> / 击退 / 击飞 / <c>HitstopFrames</c>
///       → 命中结算载荷（与近战完全同源）；</item>
/// <item><c>FirstActiveFrame</c> / <c>LastActiveFrame</c> <b>不适用</b>：投掷物在存活期间恒为生效。</item>
/// </list>
///
/// <para><b>生成时机</b>：发射者在该招式的 Active 阶段、经过帧 == <see cref="SpawnAtActiveFrame"/> 的那一帧
/// 生成实体。普通投掷物从下一帧移动与命中；配置 <see cref="Timeline"/> 的实体从生成帧移动，
/// 并按实体年龄选择判定。</para>
/// </summary>
public sealed class ProjectileDefinition
{
    /// <param name="projectileTypeId">投掷物类型 ID（同一招式内唯一；也写进事件 MoveId）。</param>
    /// <param name="hit">命中载荷 + 出生偏移 + 碰撞盒尺寸（见类型说明）。</param>
    /// <param name="speedX">沿发射者朝向的水平速度（世界单位/帧，&gt; 0；朝向在生成时镜像）。</param>
    /// <param name="speedY">垂直速度（世界单位/帧，默认 0 = 水平飞行）。</param>
    /// <param name="lifetimeFrames">存活帧数（≥ 1；每帧 −1，归零即消失）。</param>
    /// <param name="maxAlive">同一发射者、同一类型同时存在的上限（≥ 1）。</param>
    /// <param name="spawnAtActiveFrame">在 Active 阶段的哪一经过帧生成（0 起始，≥ 0）。</param>
    /// <exception cref="ArgumentException">ID 为空 / 出生帧为负。</exception>
    /// <exception cref="ArgumentOutOfRangeException">速度、存活、上限越界。</exception>
    public ProjectileDefinition(
        string projectileTypeId,
        AttackDefinition hit,
        FixedPoint speedX,
        FixedPoint speedY = default,
        int lifetimeFrames = 120,
        int maxAlive = 1,
        int spawnAtActiveFrame = 0,
        ProjectileTimeline? timeline = null)
    {
        ArgumentNullException.ThrowIfNull(hit);

        if (string.IsNullOrWhiteSpace(projectileTypeId))
            throw new ArgumentException("投掷物类型 ID 不能为空。", nameof(projectileTypeId));
        if (hit.HitstopByOutcome is not null)
            throw new ArgumentException("投掷物使用 hitstopFrames；hitstopByOutcome 仅用于近身攻击。", nameof(hit));
        if (hit.ContactOnEdge)
            throw new ArgumentException("contactOnEdge 仅用于近身攻击。", nameof(hit));
        if (hit.CounterDamage is not null)
            throw new ArgumentException("counterDamage 仅用于近身攻击。", nameof(hit));
        if (hit.ContactGroup != 0)
            throw new ArgumentException("contactGroup 仅用于近身攻击。", nameof(hit));
        if (hit.DynamicDamage is not null)
            throw new ArgumentException("dynamicDamage 仅用于近身攻击。", nameof(hit));
        if (speedX <= FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(speedX), speedX,
                "投掷物水平速度必须为正（方向由发射者朝向决定）。");
        if (speedX > FixedPoint.FromInt(MaxSpeedUnits))
            throw new ArgumentOutOfRangeException(nameof(speedX), speedX,
                $"投掷物水平速度超过上限 {MaxSpeedUnits} 世界单位/帧。");
        if (speedY < -FixedPoint.FromInt(MaxSpeedUnits) || speedY > FixedPoint.FromInt(MaxSpeedUnits))
            throw new ArgumentOutOfRangeException(nameof(speedY), speedY,
                $"投掷物垂直速度超出 ±{MaxSpeedUnits} 世界单位/帧。");
        if (lifetimeFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(lifetimeFrames), lifetimeFrames,
                "投掷物存活帧数必须 ≥ 1。");
        if (lifetimeFrames > MaxLifetimeFrames)
            throw new ArgumentOutOfRangeException(nameof(lifetimeFrames), lifetimeFrames,
                $"投掷物存活帧数超过上限 {MaxLifetimeFrames}。");
        if (maxAlive < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAlive), maxAlive,
                "同类型同时存在数量上限必须 ≥ 1。");
        if (maxAlive > MaxAlivePerType)
            throw new ArgumentOutOfRangeException(nameof(maxAlive), maxAlive,
                $"同类型同时存在数量上限超过 {MaxAlivePerType}。");
        if (spawnAtActiveFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(spawnAtActiveFrame), spawnAtActiveFrame,
                "投掷物出生帧不能为负。");

        if (timeline is not null && (timeline.MovementFrames > lifetimeFrames ||
            timeline.HitSpans[^1].LastFrame >= lifetimeFrames || timeline.GuardThreatFrames > lifetimeFrames))
            throw new ArgumentException("Entity movement, hit spans and guard threat must fit its lifetime.", nameof(timeline));

        ProjectileTypeId = projectileTypeId;
        Hit = hit;
        SpeedX = speedX;
        SpeedY = speedY;
        LifetimeFrames = lifetimeFrames;
        MaxAlive = maxAlive;
        SpawnAtActiveFrame = spawnAtActiveFrame;
        Timeline = timeline;
    }

    /// <summary>投掷物类型 ID（同一招式内唯一；事件 MoveId 用它）。</summary>
    public string ProjectileTypeId { get; }

    /// <summary>命中载荷（伤害/硬直/防御高度/击退/击飞/停顿）+ 出生偏移 + 碰撞盒尺寸。</summary>
    public AttackDefinition Hit { get; }

    /// <summary>沿发射者朝向的水平速度（世界单位/帧，&gt; 0）。</summary>
    public FixedPoint SpeedX { get; }

    /// <summary>垂直速度（世界单位/帧）。</summary>
    public FixedPoint SpeedY { get; }

    /// <summary>存活帧数（≥ 1）。</summary>
    public int LifetimeFrames { get; }

    /// <summary>同一发射者、同一类型同时存在的上限。</summary>
    public int MaxAlive { get; }

    /// <summary>在 Active 阶段的哪一经过帧生成（0 起始）。</summary>
    public int SpawnAtActiveFrame { get; }

    public ProjectileTimeline? Timeline { get; }

    /// <summary>单帧速度上限（健全性边界）。</summary>
    public const int MaxSpeedUnits = 64;

    /// <summary>存活帧数上限（20 秒）。</summary>
    public const int MaxLifetimeFrames = 1200;

    /// <summary>同类型同时存在数量上限。</summary>
    public const int MaxAlivePerType = 8;
}
