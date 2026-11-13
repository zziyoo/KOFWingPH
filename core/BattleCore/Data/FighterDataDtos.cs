using System.Text.Json.Serialization;

namespace NewKOF.BattleCore.Data;

/// <summary>
/// JSON 角色配置 v1 的<b>数据传输对象</b>（阶段 09D，D113）。
///
/// <para><b>职责</b>：只负责把 JSON 原样反序列化成可空字段，<b>不做任何战斗规则判断</b>；
/// 校验与转换全部在 <see cref="FighterDataLoader"/> 中完成，最终仍由既有
/// <c>FighterDefinition</c> / <c>MoveDefinition</c> / <c>AttackDefinition</c> 等构造器
/// 承担权威不变式（D114：加载器只会更严格，绝不放宽）。</para>
///
/// <para>所有字段可空是为了区分「缺失 / 显式 null / 合法值」；必填字段的缺失会立即失败，
/// 不会被默认值掩盖。未知字段由 <see cref="JsonUnmappedMemberHandling.Disallow"/> 拒绝。</para>
/// </summary>
public sealed class FighterDocumentDto
{
    [JsonPropertyName("schemaVersion")]
    public int? SchemaVersion { get; set; }

    [JsonPropertyName("fighterId")]
    public string? FighterId { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("movement")]
    public MovementDto? Movement { get; set; }

    [JsonPropertyName("commands")]
    public List<CommandDto>? Commands { get; set; }

    [JsonPropertyName("moves")]
    public List<MoveDto>? Moves { get; set; }
}

/// <summary>Q16.16 定点数的<b>整数分数</b>表示：numerator / denominator。</summary>
public sealed class FractionDto
{
    [JsonPropertyName("numerator")]
    public long? Numerator { get; set; }

    [JsonPropertyName("denominator")]
    public long? Denominator { get; set; }
}

/// <summary>运动配置（7 个定点分数，全部必填）。</summary>
public sealed class MovementDto
{
    [JsonPropertyName("walkForwardSpeed")]
    public FractionDto? WalkForwardSpeed { get; set; }

    [JsonPropertyName("walkBackwardSpeed")]
    public FractionDto? WalkBackwardSpeed { get; set; }

    [JsonPropertyName("preJumpFrames")]
    public FractionDto? PreJumpFrames { get; set; }

    [JsonPropertyName("shortHopInitialVelocity")]
    public FractionDto? ShortHopInitialVelocity { get; set; }

    [JsonPropertyName("highJumpInitialVelocity")]
    public FractionDto? HighJumpInitialVelocity { get; set; }

    [JsonPropertyName("jumpGravity")]
    public FractionDto? JumpGravity { get; set; }

    [JsonPropertyName("horizontalJumpSpeed")]
    public FractionDto? HorizontalJumpSpeed { get; set; }
}

public sealed class CommandDto
{
    [JsonPropertyName("commandId")]
    public string? CommandId { get; set; }

    /// <summary>相对朝向方向名序列（空数组 = 裸按键指令）。</summary>
    [JsonPropertyName("directions")]
    public List<string>? Directions { get; set; }

    /// <summary>攻击键名数组（同一帧全部按下；空数组 = 纯方向指令）。</summary>
    [JsonPropertyName("buttons")]
    public List<string>? Buttons { get; set; }

    [JsonPropertyName("maxSequenceFrames")]
    public int? MaxSequenceFrames { get; set; }

    [JsonPropertyName("buttonToleranceFrames")]
    public int? ButtonToleranceFrames { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("bufferLifetimeFrames")]
    public int? BufferLifetimeFrames { get; set; }

    [JsonPropertyName("chargeHoldFrames")]
    public int? ChargeHoldFrames { get; set; }

    [JsonPropertyName("chargeReleaseToleranceFrames")]
    public int? ChargeReleaseToleranceFrames { get; set; }
}

public sealed class MoveDto
{
    [JsonPropertyName("moveId")]
    public string? MoveId { get; set; }

    [JsonPropertyName("commandId")]
    public string? CommandId { get; set; }

    [JsonPropertyName("startupFrames")]
    public int? StartupFrames { get; set; }

    [JsonPropertyName("activeFrames")]
    public int? ActiveFrames { get; set; }

    [JsonPropertyName("recoveryFrames")]
    public int? RecoveryFrames { get; set; }

    [JsonPropertyName("allowedFrom")]
    public string? AllowedFrom { get; set; }

    [JsonPropertyName("attacks")]
    public List<AttackDto>? Attacks { get; set; }

    [JsonPropertyName("cancels")]
    public List<CancelDto>? Cancels { get; set; }

    [JsonPropertyName("throws")]
    public List<ThrowDto>? Throws { get; set; }

    [JsonPropertyName("projectiles")]
    public List<ProjectileDto>? Projectiles { get; set; }
}

public sealed class AttackDto
{
    [JsonPropertyName("offsetForward")]
    public FractionDto? OffsetForward { get; set; }

    [JsonPropertyName("offsetUp")]
    public FractionDto? OffsetUp { get; set; }

    [JsonPropertyName("halfWidth")]
    public FractionDto? HalfWidth { get; set; }

    [JsonPropertyName("halfHeight")]
    public FractionDto? HalfHeight { get; set; }

    [JsonPropertyName("firstActiveFrame")]
    public int? FirstActiveFrame { get; set; }

    /// <summary>省略或 null = 哨兵「直到 Active 末帧」（int.MaxValue）。</summary>
    [JsonPropertyName("lastActiveFrame")]
    public int? LastActiveFrame { get; set; }

    [JsonPropertyName("height")]
    public string? Height { get; set; }

    [JsonPropertyName("damage")]
    public int? Damage { get; set; }

    [JsonPropertyName("hitstunFrames")]
    public int? HitstunFrames { get; set; }

    [JsonPropertyName("blockstunFrames")]
    public int? BlockstunFrames { get; set; }

    [JsonPropertyName("groundKnockback")]
    public FractionDto? GroundKnockback { get; set; }

    [JsonPropertyName("groundKnockbackFriction")]
    public FractionDto? GroundKnockbackFriction { get; set; }

    [JsonPropertyName("blockKnockback")]
    public FractionDto? BlockKnockback { get; set; }

    [JsonPropertyName("launchVelocityX")]
    public FractionDto? LaunchVelocityX { get; set; }

    [JsonPropertyName("launchVelocityY")]
    public FractionDto? LaunchVelocityY { get; set; }

    [JsonPropertyName("knockdownFrames")]
    public int? KnockdownFrames { get; set; }

    [JsonPropertyName("wakeupFrames")]
    public int? WakeupFrames { get; set; }

    [JsonPropertyName("hitstopFrames")]
    public int? HitstopFrames { get; set; }

    [JsonPropertyName("hitstopByOutcome")]
    public HitstopByOutcomeDto? HitstopByOutcome { get; set; }
}

public sealed class HitstopByOutcomeDto
{
    [JsonPropertyName("hit")]
    public HitstopPairDto? Hit { get; set; }

    [JsonPropertyName("block")]
    public HitstopPairDto? Block { get; set; }
}

public sealed class HitstopPairDto
{
    [JsonPropertyName("attackerFrames")]
    public int? AttackerFrames { get; set; }

    [JsonPropertyName("defenderFrames")]
    public int? DefenderFrames { get; set; }
}

public sealed class CancelDto
{
    [JsonPropertyName("targetMoveId")]
    public string? TargetMoveId { get; set; }

    [JsonPropertyName("windowFirstFrame")]
    public int? WindowFirstFrame { get; set; }

    [JsonPropertyName("windowLastFrame")]
    public int? WindowLastFrame { get; set; }

    /// <summary>逗号分隔的阶段名（如 "Active,Recovery"）。</summary>
    [JsonPropertyName("phases")]
    public string? Phases { get; set; }

    [JsonPropertyName("requiresHitConfirm")]
    public bool? RequiresHitConfirm { get; set; }

    [JsonPropertyName("requiresBlockConfirm")]
    public bool? RequiresBlockConfirm { get; set; }

    [JsonPropertyName("allowWhiff")]
    public bool? AllowWhiff { get; set; }
}

public sealed class ThrowDto
{
    [JsonPropertyName("techable")]
    public bool Techable { get; set; } = true;

    [JsonPropertyName("throwId")]
    public string? ThrowId { get; set; }

    [JsonPropertyName("range")]
    public FractionDto? Range { get; set; }

    [JsonPropertyName("firstActiveFrame")]
    public int? FirstActiveFrame { get; set; }

    [JsonPropertyName("lastActiveFrame")]
    public int? LastActiveFrame { get; set; }

    [JsonPropertyName("damage")]
    public int? Damage { get; set; }

    [JsonPropertyName("knockdownFrames")]
    public int? KnockdownFrames { get; set; }

    [JsonPropertyName("wakeupFrames")]
    public int? WakeupFrames { get; set; }

    [JsonPropertyName("pushDistance")]
    public FractionDto? PushDistance { get; set; }

    [JsonPropertyName("hitstopFrames")]
    public int? HitstopFrames { get; set; }

    /// <summary>目标状态类别（Flags 名，逗号分隔）。</summary>
    [JsonPropertyName("targetStates")]
    public string? TargetStates { get; set; }
}

public sealed class ProjectileDto
{
    [JsonPropertyName("projectileTypeId")]
    public string? ProjectileTypeId { get; set; }

    /// <summary>命中载荷（复用 AttackDto 的全部字段）。</summary>
    [JsonPropertyName("hit")]
    public AttackDto? Hit { get; set; }

    [JsonPropertyName("speedX")]
    public FractionDto? SpeedX { get; set; }

    [JsonPropertyName("speedY")]
    public FractionDto? SpeedY { get; set; }

    [JsonPropertyName("lifetimeFrames")]
    public int? LifetimeFrames { get; set; }

    [JsonPropertyName("maxAlive")]
    public int? MaxAlive { get; set; }

    [JsonPropertyName("spawnAtActiveFrame")]
    public int? SpawnAtActiveFrame { get; set; }
}
