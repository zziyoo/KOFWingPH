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

    [JsonPropertyName("useCommandPriority")]
    public bool UseCommandPriority { get; set; }

    [JsonPropertyName("allowStrikeClash")]
    public bool AllowStrikeClash { get; set; } = true;

    [JsonPropertyName("forwardGuardDistance")]
    public FractionDto? ForwardGuardDistance { get; set; }

    [JsonPropertyName("retainProjectileGuard")]
    public bool RetainProjectileGuard { get; set; }

    [JsonPropertyName("preserveGuardEntryMomentum")]
    public bool PreserveGuardEntryMomentum { get; set; }

    [JsonPropertyName("projectileGuardRecoveryFrames")]
    public int ProjectileGuardRecoveryFrames { get; set; }

    [JsonPropertyName("protectGroundHitstunExit")]
    public bool ProtectGroundHitstunExit { get; set; }

    [JsonPropertyName("moves")]
    public List<MoveDto>? Moves { get; set; }

    [JsonPropertyName("groundHurtboxes")]
    public GroundHurtboxesDto? GroundHurtboxes { get; set; }

    [JsonPropertyName("groundBodyWidths")]
    public GroundBodyWidthsDto? GroundBodyWidths { get; set; }

    [JsonPropertyName("airBodyWidths")]
    public GroundBodyWidthsDto? AirBodyWidths { get; set; }

    [JsonPropertyName("neutralAirHurtboxes")]
    public List<HurtboxDto>? NeutralAirHurtboxes { get; set; }

    [JsonPropertyName("damageRules")]
    public DamageRulesDto? DamageRules { get; set; }
}

public sealed class DamageRulesDto
{
    [JsonPropertyName("baseScale")]
    public FractionDto? BaseScale { get; set; }

    [JsonPropertyName("attackMultiplier")]
    public FractionDto? AttackMultiplier { get; set; }

    [JsonPropertyName("defenseMultiplier")]
    public FractionDto? DefenseMultiplier { get; set; }

    [JsonPropertyName("hitstunDefenseMultiplier")]
    public FractionDto? HitstunDefenseMultiplier { get; set; }

    [JsonPropertyName("varianceMaximum")]
    public int? VarianceMaximum { get; set; }

    [JsonPropertyName("counterGraceTicks")]
    public int? CounterGraceTicks { get; set; }
}

public sealed class DynamicAttackDamageDto
{
    [JsonPropertyName("normalBase")]
    public int? NormalBase { get; set; }

    [JsonPropertyName("counterBase")]
    public int? CounterBase { get; set; }

    [JsonPropertyName("useContactDivisor")]
    public bool UseContactDivisor { get; set; }
}

public sealed class GroundBodyWidthsDto
{
    [JsonPropertyName("front")]
    public FractionDto? Front { get; set; }

    [JsonPropertyName("back")]
    public FractionDto? Back { get; set; }
}

public sealed class GroundHurtboxesDto
{
    [JsonPropertyName("tripFall")]
    public List<MoveHurtboxSpanDto>? TripFall { get; set; }

    [JsonPropertyName("standingOnCrouchEntry")]
    public bool StandingOnCrouchEntry { get; set; }

    [JsonPropertyName("standing")]
    public List<HurtboxDto>? Standing { get; set; }

    [JsonPropertyName("crouching")]
    public List<HurtboxDto>? Crouching { get; set; }

    [JsonPropertyName("hitstun")]
    public List<HurtboxDto>? Hitstun { get; set; }

    [JsonPropertyName("hitstunRecoveryFrames")]
    public int HitstunRecoveryFrames { get; set; }

    [JsonPropertyName("crouchRise")]
    public List<MoveHurtboxSpanDto>? CrouchRise { get; set; }

    [JsonPropertyName("lightHitstun")]
    public List<MoveHurtboxSpanDto>? LightHitstun { get; set; }

    [JsonPropertyName("heavyHitstun")]
    public List<MoveHurtboxSpanDto>? HeavyHitstun { get; set; }

    [JsonPropertyName("crouchingHeavyHitstun")]
    public List<MoveHurtboxSpanDto>? CrouchingHeavyHitstun { get; set; }

    [JsonPropertyName("crouchingLightHitstun")]
    public List<MoveHurtboxSpanDto>? CrouchingLightHitstun { get; set; }

    [JsonPropertyName("lowHeavyHitstun")]
    public List<MoveHurtboxSpanDto>? LowHeavyHitstun { get; set; }

    [JsonPropertyName("standingGuard")]
    public List<HurtboxDto>? StandingGuard { get; set; }

    [JsonPropertyName("crouchingGuard")]
    public List<HurtboxDto>? CrouchingGuard { get; set; }

    [JsonPropertyName("standingGuardReady")]
    public List<HurtboxDto>? StandingGuardReady { get; set; }

    [JsonPropertyName("crouchingGuardReady")]
    public List<HurtboxDto>? CrouchingGuardReady { get; set; }
}

public sealed class HurtboxDto
{
    [JsonPropertyName("minX")]
    public FractionDto? MinX { get; set; }

    [JsonPropertyName("minY")]
    public FractionDto? MinY { get; set; }

    [JsonPropertyName("maxX")]
    public FractionDto? MaxX { get; set; }

    [JsonPropertyName("maxY")]
    public FractionDto? MaxY { get; set; }
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

    [JsonPropertyName("groundReaction")]
    public GroundReactionMovementDto? GroundReaction { get; set; }
}

public sealed class GroundReactionMovementDto
{
    [JsonPropertyName("velocityRetention")]
    public FractionDto? VelocityRetention { get; set; }

    [JsonPropertyName("lastHitStepScale")]
    public FractionDto? LastHitStepScale { get; set; }

    [JsonPropertyName("crouchGuardVelocityRetention")]
    public FractionDto? CrouchGuardVelocityRetention { get; set; }

    [JsonPropertyName("crouchHitVelocityRetention")]
    public FractionDto? CrouchHitVelocityRetention { get; set; }
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

    [JsonPropertyName("matchHeldMotion")]
    public bool MatchHeldMotion { get; set; }

    [JsonPropertyName("bufferPolicy")]
    public CommandBufferPolicyDto? BufferPolicy { get; set; }

    [JsonPropertyName("reserveGroundInput")]
    public bool ReserveGroundInput { get; set; }
}

public sealed class CommandBufferPolicyDto
{
    [JsonPropertyName("preserveDuringHitPause")]
    public bool PreserveDuringHitPause { get; set; }

    [JsonPropertyName("helperTicks")]
    public int? HelperTicks { get; set; }

    [JsonPropertyName("helperHeldDecay")]
    public int? HelperHeldDecay { get; set; }

    [JsonPropertyName("helperReleasedDecay")]
    public int? HelperReleasedDecay { get; set; }
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

    [JsonPropertyName("minimumStartDistance")]
    public FractionDto? MinimumStartDistance { get; set; }

    [JsonPropertyName("maximumStartBodyDistance")]
    public FractionDto? MaximumStartBodyDistance { get; set; }

    [JsonPropertyName("guardThreatFrames")]
    public int? GuardThreatFrames { get; set; }

    [JsonPropertyName("hurtboxTimeline")]
    public List<MoveHurtboxSpanDto>? HurtboxTimeline { get; set; }

    [JsonPropertyName("returnsToCrouch")]
    public bool ReturnsToCrouch { get; set; }


    [JsonPropertyName("attacks")]
    public List<AttackDto>? Attacks { get; set; }

    [JsonPropertyName("cancels")]
    public List<CancelDto>? Cancels { get; set; }

    [JsonPropertyName("throws")]
    public List<ThrowDto>? Throws { get; set; }

    [JsonPropertyName("projectiles")]
    public List<ProjectileDto>? Projectiles { get; set; }
}

public sealed class MoveHurtboxSpanDto
{
    [JsonPropertyName("frames")]
    public int? Frames { get; set; }

    [JsonPropertyName("boxes")]
    public List<HurtboxDto>? Boxes { get; set; }
}

public sealed class AttackDto
{
    [JsonPropertyName("launchGravity")]
    public FractionDto? LaunchGravity { get; set; }

    [JsonPropertyName("launchReaction")]
    public LaunchReactionDto? LaunchReaction { get; set; }

    [JsonPropertyName("lightGroundReaction")]
    public bool LightGroundReaction { get; set; }

    [JsonPropertyName("groundHitReaction")]
    public string? GroundHitReaction { get; set; }

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

    [JsonPropertyName("contactOnEdge")]
    public bool ContactOnEdge { get; set; }

    [JsonPropertyName("counterDamage")]
    public int? CounterDamage { get; set; }

    [JsonPropertyName("contactGroup")]
    public int ContactGroup { get; set; }

    [JsonPropertyName("damageAfterPriorContact")]
    public int? DamageAfterPriorContact { get; set; }

    [JsonPropertyName("dynamicDamage")]
    public DynamicAttackDamageDto? DynamicDamage { get; set; }
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

    [JsonPropertyName("allowSameMove")]
    public bool AllowSameMove { get; set; }

    [JsonPropertyName("standingInputOnly")]
    public bool StandingInputOnly { get; set; }

    [JsonPropertyName("maximumInputAge")]
    public int? MaximumInputAge { get; set; }
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

    [JsonPropertyName("timeline")]
    public ProjectileTimelineDto? Timeline { get; set; }
}

public sealed class ProjectileTimelineDto
{
    [JsonPropertyName("movementFrames")]
    public int? MovementFrames { get; set; }

    [JsonPropertyName("hitSpans")]
    public List<ProjectileHitSpanDto>? HitSpans { get; set; }

    [JsonPropertyName("requestPauseOnHit")]
    public bool RequestPauseOnHit { get; set; }

    [JsonPropertyName("guardThreatFrames")]
    public int? GuardThreatFrames { get; set; }
}

public sealed class ProjectileHitSpanDto
{
    [JsonPropertyName("firstFrame")]
    public int? FirstFrame { get; set; }

    [JsonPropertyName("lastFrame")]
    public int? LastFrame { get; set; }

    [JsonPropertyName("localHitbox")]
    public HurtboxDto? LocalHitbox { get; set; }

    [JsonPropertyName("hit")]
    public AttackDto? Hit { get; set; }

    [JsonPropertyName("guardDamage")]
    public int GuardDamage { get; set; }

    [JsonPropertyName("dynamicDamage")]
    public DynamicAttackDamageDto? DynamicDamage { get; set; }

    [JsonPropertyName("contactOnEdge")]
    public bool ContactOnEdge { get; set; } = true;

    [JsonPropertyName("launchGravity")]
    public FractionDto? LaunchGravity { get; set; }

    [JsonPropertyName("launchReaction")]
    public LaunchReactionDto? LaunchReaction { get; set; }
}

public sealed class LaunchReactionDto
{
    [JsonPropertyName("startInFall")]
    public bool StartInFall { get; set; }

    [JsonPropertyName("groundTech")]
    public GroundTechDto? GroundTech { get; set; }

    [JsonPropertyName("impactFrames")]
    public int? ImpactFrames { get; set; }

    [JsonPropertyName("bounceHorizontalRetention")]
    public FractionDto? BounceHorizontalRetention { get; set; }

    [JsonPropertyName("bounceVerticalVelocity")]
    public FractionDto? BounceVerticalVelocity { get; set; }

    [JsonPropertyName("bounceGravity")]
    public FractionDto? BounceGravity { get; set; }

    [JsonPropertyName("lieDownFrames")]
    public int? LieDownFrames { get; set; }

    [JsonPropertyName("wakeupFrames")]
    public int? WakeupFrames { get; set; }

    [JsonPropertyName("riseFrames")]
    public int? RiseFrames { get; set; }

    [JsonPropertyName("lieImpactFrames")]
    public int? LieImpactFrames { get; set; }

    [JsonPropertyName("transitionGravitySteps")]
    public int? TransitionGravitySteps { get; set; }
}

public sealed class GroundTechDto
{
    [JsonPropertyName("inputBufferFrames")]
    public int? InputBufferFrames { get; set; }

    [JsonPropertyName("durationFrames")]
    public int? DurationFrames { get; set; }

    [JsonPropertyName("slideFrames")]
    public int? SlideFrames { get; set; }

    [JsonPropertyName("initialSpeed")]
    public FractionDto? InitialSpeed { get; set; }

    [JsonPropertyName("velocityRetention")]
    public FractionDto? VelocityRetention { get; set; }
}
