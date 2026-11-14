using System.Text.Json;
using System.Text.Json.Serialization;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Data;

/// <summary>一份待加载的角色 JSON（来源名用于错误信息，不参与战斗确定性）。</summary>
public readonly record struct FighterDataDocument(string SourceName, string Json);

/// <summary>单份文档的加载结果（含 UI 显示名与权威配置）。</summary>
public readonly record struct FighterDataResult(
    string FighterId, string DisplayName, FighterDefinition Definition);

/// <summary>
/// <b>角色 JSON → 权威 FighterDefinition 加载器</b>（阶段 09D，D113/D114）。
///
/// <para><b>职责分离</b>：JSON 只存数据；本类负责反序列化、字段校验与转换；
/// 最终的 <c>FighterDefinition</c> 仍由既有构造器承担全部战斗不变式
/// （指令/招式唯一性、取消目标存在、生效帧范围、D95 摩擦、击飞倒地自洽、Hitstop 上限等）。
/// <c>BattleSimulation</c> 完全不知道 JSON 存在；加载只在开赛前做一次。</para>
///
/// <para><b>Schema v1 约定</b>：</para>
/// <list type="bullet">
/// <item>必填字段：schemaVersion / fighterId / displayName / movement / commands / moves，
///       以及各层所有字段；<b>缺失或显式 null 立即失败</b>，不用默认值掩盖。</item>
/// <item>唯一例外：<c>lastActiveFrame</c> 允许 null，表示哨兵「直到 Active 末帧」(int.MaxValue)。</item>
/// <item>定点数 = <c>{"numerator": long, "denominator": long}</c>；分母必须为正，
///       负号只能写在分子；由 <see cref="FixedPoint.FromFraction"/> 做 Q16.16 范围校验；
///       不使用 double、不读 NaN/Infinity、不受文化区域影响。</item>
/// <item>枚举用精确名称（区分大小写）：方向 / 攻击键 / 高度 / 允许状态 / 取消阶段 / 投技目标状态；
///       Flags 用逗号分隔组合。未知枚举值立即失败。</item>
/// <item>数组顺序即配置顺序（与 C# 定义一致）；<b>未知字段一律拒绝</b>
///       （<see cref="JsonUnmappedMemberHandling.Disallow"/>）。</item>
/// <item>版本兼容：只支持 schemaVersion = 1；其他值显式失败，不做猜测式迁移。</item>
/// </list>
/// </summary>
public static class FighterDataLoader
{
    /// <summary>当前支持的 JSON 格式版本。</summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
    };

    /// <summary>从字符串加载单个角色配置。</summary>
    /// <exception cref="FighterDataException">格式/字段/构造期校验失败。</exception>
    public static FighterDefinition Load(string json, string sourceName = "fighters/<inline>.json") =>
        LoadDocument(json, sourceName).Definition;

    /// <summary>从流加载单个角色配置（Godot 层可传 FileAccess 的字节流）。</summary>
    public static FighterDefinition Load(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd(), sourceName);
    }

    /// <summary>加载单份文档，同时返回显示名（供角色目录使用）。</summary>
    public static FighterDataResult LoadDocument(string json, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw Fail(sourceName, "<json>", "内容为空或 null。");

        FighterDocumentDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<FighterDocumentDto>(json, Options)
                ?? throw Fail(sourceName, "<json>", "JSON 根节点为 null。");
        }
        catch (FighterDataException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new FighterDataException(sourceName, "<json>",
                $"JSON 格式错误（含未知字段、类型不符或多余逗号）：{ex.Message}", ex);
        }

        int schema = RequiredInt(dto.SchemaVersion, sourceName, "schemaVersion");
        if (schema != SupportedSchemaVersion)
            throw Fail(sourceName, "schemaVersion",
                $"不支持的格式版本 {schema}（当前仅支持 v{SupportedSchemaVersion}）。");

        string fighterId = RequiredString(dto.FighterId, sourceName, "fighterId");
        string displayName = RequiredString(dto.DisplayName, sourceName, "displayName");

        if (dto.Movement is null)
            throw Fail(sourceName, "movement", "必填对象缺失或为 null。");
        MovementDefinition movement = BuildMovement(dto.Movement, sourceName);

        CommandDefinition[] commands = BuildArray(
            dto.Commands, sourceName, "commands", BuildCommand);
        MoveDefinition[] moves = BuildArray(
            dto.Moves, sourceName, "moves", BuildMove);
        GroundHurtboxes? groundHurtboxes = dto.GroundHurtboxes is { } boxes
            ? Guard(() => new GroundHurtboxes(
                BuildArray(boxes.Standing, sourceName, "groundHurtboxes.standing", BuildHurtbox),
                BuildArray(boxes.Crouching, sourceName, "groundHurtboxes.crouching", BuildHurtbox),
                boxes.StandingOnCrouchEntry,
                boxes.Hitstun is null ? null : BuildArray(boxes.Hitstun, sourceName, "groundHurtboxes.hitstun", BuildHurtbox),
                boxes.HitstunRecoveryFrames,
                boxes.CrouchRise is null ? null : BuildArray(boxes.CrouchRise, sourceName, "groundHurtboxes.crouchRise", BuildMoveHurtboxSpan),
                boxes.LightHitstun is null ? null : BuildArray(boxes.LightHitstun, sourceName, "groundHurtboxes.lightHitstun", BuildMoveHurtboxSpan),
                boxes.HeavyHitstun is null ? null : BuildArray(boxes.HeavyHitstun, sourceName, "groundHurtboxes.heavyHitstun", BuildMoveHurtboxSpan),
                boxes.CrouchingHeavyHitstun is null ? null : BuildArray(boxes.CrouchingHeavyHitstun, sourceName,
                    "groundHurtboxes.crouchingHeavyHitstun", BuildMoveHurtboxSpan),
                boxes.CrouchingLightHitstun is null ? null : BuildArray(boxes.CrouchingLightHitstun, sourceName,
                    "groundHurtboxes.crouchingLightHitstun", BuildMoveHurtboxSpan),
                boxes.LowHeavyHitstun is null ? null : BuildArray(boxes.LowHeavyHitstun, sourceName,
                    "groundHurtboxes.lowHeavyHitstun", BuildMoveHurtboxSpan),
                GuardBoxes(boxes.StandingGuard, "standingGuard"), GuardBoxes(boxes.CrouchingGuard, "crouchingGuard"),
                GuardBoxes(boxes.StandingGuardReady, "standingGuardReady"), GuardBoxes(boxes.CrouchingGuardReady, "crouchingGuardReady"),
                boxes.TripFall is null ? null : BuildArray(boxes.TripFall, sourceName, "groundHurtboxes.tripFall", BuildMoveHurtboxSpan)),
                sourceName, "groundHurtboxes")
            : null;

        IReadOnlyList<FixedRect>? GuardBoxes(List<HurtboxDto>? list, string key) =>
            list is null ? null : BuildArray(list, sourceName, $"groundHurtboxes.{key}", BuildHurtbox);

        GroundBodyWidths? groundBodyWidths = dto.GroundBodyWidths is { } widths
            ? BuildBodyWidths(widths, sourceName, "groundBodyWidths") : null;
        GroundBodyWidths? airBodyWidths = dto.AirBodyWidths is { } airWidths
            ? BuildBodyWidths(airWidths, sourceName, "airBodyWidths") : null;
        IReadOnlyList<FixedRect>? neutralAirHurtboxes = dto.NeutralAirHurtboxes is { } airBoxes
            ? BuildArray(airBoxes, sourceName, "neutralAirHurtboxes", BuildHurtbox) : null;
        DynamicDamageRules? damageRules = dto.DamageRules is { } rules
            ? BuildDamageRules(rules, sourceName) : null;

        FighterDefinition definition = Guard(
            () => new FighterDefinition(fighterId, commands, moves, movement, groundHurtboxes, groundBodyWidths,
                airBodyWidths, neutralAirHurtboxes, damageRules, dto.UseCommandPriority, dto.AllowStrikeClash,
                dto.ForwardGuardDistance is { } guard ? Fraction(guard, sourceName, "forwardGuardDistance") : null,
                dto.RetainProjectileGuard, dto.PreserveGuardEntryMomentum, dto.ProjectileGuardRecoveryFrames,
                dto.ProtectGroundHitstunExit, dto.UseOrderedGroundHitstunExit, dto.ProtectGroundBlockstunExit,
                dto.HoldGroundBlockstunExitPosition),
            sourceName, "<definition>");
        return new FighterDataResult(fighterId, displayName, definition);
    }

    private static GroundBodyWidths BuildBodyWidths(GroundBodyWidthsDto dto, string source, string path) =>
        Guard(() => new GroundBodyWidths(Fraction(dto.Front, source, path + ".front"),
            Fraction(dto.Back, source, path + ".back")), source, path);

    private static DynamicDamageRules BuildDamageRules(DamageRulesDto dto, string source)
    {
        const string path = "damageRules";
        return Guard(() => new DynamicDamageRules(
            DamageFraction(dto.BaseScale, source, path + ".baseScale"),
            DamageFraction(dto.AttackMultiplier, source, path + ".attackMultiplier"),
            DamageFraction(dto.DefenseMultiplier, source, path + ".defenseMultiplier"),
            DamageFraction(dto.HitstunDefenseMultiplier, source, path + ".hitstunDefenseMultiplier"),
            RequiredInt(dto.VarianceMaximum, source, path + ".varianceMaximum"),
            RequiredInt(dto.CounterGraceTicks, source, path + ".counterGraceTicks")), source, path);
    }

    private static DamageRatio DamageFraction(FractionDto? dto, string source, string path)
    {
        long numerator = RequiredLong(dto?.Numerator, source, path + ".numerator");
        long denominator = RequiredLong(dto?.Denominator, source, path + ".denominator");
        if (numerator is < 0 or > 1_000_000 || denominator is < 1 or > 1_000_000)
            throw Fail(source, path, "伤害倍率要求分子 0..1000000、分母 1..1000000。");
        return new DamageRatio((int)numerator, (int)denominator);
    }

    /// <summary>
    /// 从多份文档构建 <see cref="FighterCatalog"/>（角色目录 → 选人 → FightRoster 的入口）。
    /// 重复 ID 由 <c>FighterCatalog</c> 构造期拒绝。
    /// </summary>
    public static FighterCatalog LoadCatalog(IEnumerable<FighterDataDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var entries = new List<FighterCatalogEntry>();
        int index = 0;
        foreach (FighterDataDocument document in documents)
        {
            string source = string.IsNullOrWhiteSpace(document.SourceName)
                ? $"fighters/[{index}].json"
                : document.SourceName;
            FighterDataResult result = LoadDocument(document.Json, source);
            entries.Add(new FighterCatalogEntry(
                result.FighterId, result.DisplayName, source, result.Definition));
            index++;
        }
        return new FighterCatalog(entries);
    }

    // ── 顶层构造 ────────────────────────────────────────────────────

    private static FixedRect BuildHurtbox(HurtboxDto dto, string source, string path) => Guard(
        () => new FixedRect(
            Fraction(dto.MinX, source, path + ".minX"), Fraction(dto.MaxX, source, path + ".maxX"),
            Fraction(dto.MinY, source, path + ".minY"), Fraction(dto.MaxY, source, path + ".maxY")),
        source, path);

    private static MovementDefinition BuildMovement(MovementDto dto, string source) => Guard(
        () => new MovementDefinition(
            walkForwardSpeed: Fraction(dto.WalkForwardSpeed, source, "movement.walkForwardSpeed"),
            walkBackwardSpeed: Fraction(dto.WalkBackwardSpeed, source, "movement.walkBackwardSpeed"),
            preJumpFrames: Fraction(dto.PreJumpFrames, source, "movement.preJumpFrames"),
            shortHopInitialVelocity: Fraction(dto.ShortHopInitialVelocity, source,
                "movement.shortHopInitialVelocity"),
            highJumpInitialVelocity: Fraction(dto.HighJumpInitialVelocity, source,
                "movement.highJumpInitialVelocity"),
            jumpGravity: Fraction(dto.JumpGravity, source, "movement.jumpGravity"),
            horizontalJumpSpeed: Fraction(dto.HorizontalJumpSpeed, source,
                "movement.horizontalJumpSpeed"),
            groundReaction: dto.GroundReaction is { } reaction
                ? Guard(() => new GroundReactionMovement(
                    Fraction(reaction.VelocityRetention, source, "movement.groundReaction.velocityRetention"),
                    Fraction(reaction.LastHitStepScale, source, "movement.groundReaction.lastHitStepScale"),
                    reaction.CrouchGuardVelocityRetention is { } crouch
                        ? Fraction(crouch, source, "movement.groundReaction.crouchGuardVelocityRetention") : null,
                    reaction.CrouchHitVelocityRetention is { } crouchHit
                        ? Fraction(crouchHit, source, "movement.groundReaction.crouchHitVelocityRetention") : null),
                    source, "movement.groundReaction")
                : null),
        source, "movement");

    private static CommandDefinition BuildCommand(CommandDto dto, string source, string path) => Guard(
        () => new CommandDefinition(
            commandId: RequiredString(dto.CommandId, source, path + ".commandId"),
            directionSequence: BuildDirections(dto.Directions, source, path + ".directions"),
            requiredButtons: BuildButtons(dto.Buttons, source, path + ".buttons"),
            maxSequenceFrames: RequiredInt(dto.MaxSequenceFrames, source, path + ".maxSequenceFrames"),
            buttonToleranceFrames: RequiredInt(dto.ButtonToleranceFrames, source,
                path + ".buttonToleranceFrames"),
            priority: RequiredInt(dto.Priority, source, path + ".priority"),
            bufferLifetimeFrames: RequiredInt(dto.BufferLifetimeFrames, source,
                path + ".bufferLifetimeFrames"),
            chargeHoldFrames: RequiredInt(dto.ChargeHoldFrames, source, path + ".chargeHoldFrames"),
            chargeReleaseToleranceFrames: RequiredInt(dto.ChargeReleaseToleranceFrames, source,
                path + ".chargeReleaseToleranceFrames"),
            bufferPolicy: dto.BufferPolicy is { } buffer
                ? new CommandBufferPolicy(buffer.PreserveDuringHitPause,
                    RequiredInt(buffer.HelperTicks, source, path + ".bufferPolicy.helperTicks"),
                    RequiredInt(buffer.HelperHeldDecay, source, path + ".bufferPolicy.helperHeldDecay"),
                    RequiredInt(buffer.HelperReleasedDecay, source, path + ".bufferPolicy.helperReleasedDecay"),
                    buffer.HelperRunsDuringGlobalPause)
                : null,
            matchHeldMotion: dto.MatchHeldMotion, reserveGroundInput: dto.ReserveGroundInput,
            matchCompletedMotion: dto.MatchCompletedMotion, reserveOnEdgeDirection: dto.ReserveOnEdgeDirection,
            groundReservationTarget: dto.GroundReservationTarget is { } target
                ? new GroundReservationTarget(Fraction(target.MaximumBodyDistance, source,
                    path + ".groundReservationTarget.maximumBodyDistance"), target.RequiresGroundedNonHitstun) : null),
        source, path);

    private static MoveDefinition BuildMove(MoveDto dto, string source, string path) => Guard(
        () => new MoveDefinition(
            moveId: RequiredString(dto.MoveId, source, path + ".moveId"),
            commandId: RequiredString(dto.CommandId, source, path + ".commandId"),
            startupFrames: RequiredInt(dto.StartupFrames, source, path + ".startupFrames"),
            activeFrames: RequiredInt(dto.ActiveFrames, source, path + ".activeFrames"),
            recoveryFrames: RequiredInt(dto.RecoveryFrames, source, path + ".recoveryFrames"),
            allowedFrom: ParseFlags(
                RequiredString(dto.AllowedFrom, source, path + ".allowedFrom"),
                source, path + ".allowedFrom", MoveAllowedStates.GroundIdle),
            attacks: BuildArray(dto.Attacks, source, path + ".attacks", BuildAttack),
            cancels: BuildArray(dto.Cancels, source, path + ".cancels", BuildCancel),
            throws: BuildArray(dto.Throws, source, path + ".throws", BuildThrow),
            projectiles: BuildArray(dto.Projectiles, source, path + ".projectiles", BuildProjectile),
            minimumStartDistance: dto.MinimumStartDistance is { } distance
                ? Fraction(distance, source, path + ".minimumStartDistance") : FixedPoint.Zero,
            hurtboxTimeline: dto.HurtboxTimeline is { } timeline
                ? BuildArray(timeline, source, path + ".hurtboxTimeline", BuildMoveHurtboxSpan) : null,
            maximumStartBodyDistance: dto.MaximumStartBodyDistance is { } bodyDistance
                ? Fraction(bodyDistance, source, path + ".maximumStartBodyDistance") : null,
            guardThreatFrames: dto.GuardThreatFrames, returnsToCrouch: dto.ReturnsToCrouch,
            reservedCancels: dto.ReservedCancels is { } reserved
                ? BuildArray(reserved, source, path + ".reservedCancels", BuildReservedCancel) : null,
            walkOnCrouchRecovery: dto.WalkOnCrouchRecovery,
            positionSteps: dto.PositionSteps is { } steps
                ? BuildArray(steps, source, path + ".positionSteps", BuildMovePositionStep) : null),
        source, path);

    private static MovePositionStep BuildMovePositionStep(MovePositionStepDto dto, string source, string path) =>
        new(RequiredInt(dto.Frame, source, path + ".frame"), Fraction(dto.X, source, path + ".x"));

    private static MoveHurtboxSpan BuildMoveHurtboxSpan(MoveHurtboxSpanDto dto, string source, string path) => Guard(
        () => new MoveHurtboxSpan(RequiredInt(dto.Frames, source, path + ".frames"),
            BuildArray(dto.Boxes, source, path + ".boxes", BuildHurtbox)), source, path);

    private static AttackDefinition BuildAttack(AttackDto dto, string source, string path) => Guard(
        () => new AttackDefinition(
            offsetForward: Fraction(dto.OffsetForward, source, path + ".offsetForward"),
            offsetUp: Fraction(dto.OffsetUp, source, path + ".offsetUp"),
            halfWidth: Fraction(dto.HalfWidth, source, path + ".halfWidth"),
            halfHeight: Fraction(dto.HalfHeight, source, path + ".halfHeight"),
            firstActiveFrame: RequiredInt(dto.FirstActiveFrame, source, path + ".firstActiveFrame"),
            lastActiveFrame: SentinelOrValue(dto.LastActiveFrame),
            height: ParseEnum<AttackHeight>(
                RequiredString(dto.Height, source, path + ".height"), source, path + ".height"),
            damage: RequiredInt(dto.Damage, source, path + ".damage"),
            hitstunFrames: RequiredInt(dto.HitstunFrames, source, path + ".hitstunFrames"),
            blockstunFrames: RequiredInt(dto.BlockstunFrames, source, path + ".blockstunFrames"),
            groundKnockback: Fraction(dto.GroundKnockback, source, path + ".groundKnockback"),
            groundKnockbackFriction: Fraction(dto.GroundKnockbackFriction, source,
                path + ".groundKnockbackFriction"),
            blockKnockback: Fraction(dto.BlockKnockback, source, path + ".blockKnockback"),
            launchVelocityX: Fraction(dto.LaunchVelocityX, source, path + ".launchVelocityX"),
            launchVelocityY: Fraction(dto.LaunchVelocityY, source, path + ".launchVelocityY"),
            knockdownFrames: RequiredInt(dto.KnockdownFrames, source, path + ".knockdownFrames"),
            wakeupFrames: RequiredInt(dto.WakeupFrames, source, path + ".wakeupFrames"),
            hitstopFrames: RequiredInt(dto.HitstopFrames, source, path + ".hitstopFrames"),
            hitstopByOutcome: dto.HitstopByOutcome is { } outcome
                ? new HitstopByOutcome(
                    BuildHitstopPair(outcome.Hit, source, path + ".hitstopByOutcome.hit"),
                    BuildHitstopPair(outcome.Block, source, path + ".hitstopByOutcome.block"))
                : null,
            contactOnEdge: dto.ContactOnEdge,
            counterDamage: dto.CounterDamage,
            contactGroup: dto.ContactGroup,
            damageAfterPriorContact: dto.DamageAfterPriorContact,
            dynamicDamage: BuildDynamicDamage(dto.DynamicDamage, source, path + ".dynamicDamage"),
            lightGroundReaction: dto.LightGroundReaction,
            groundHitReaction: dto.GroundHitReaction is { } reaction
                ? ParseEnum<GroundHitReaction>(reaction, source, path + ".groundHitReaction") : GroundHitReaction.Standard,
            launchGravity: dto.LaunchGravity is { } gravity ? Fraction(gravity, source, path + ".launchGravity") : null,
            launchReaction: dto.LaunchReaction is { } launch ? BuildLaunchReaction(launch, source, path + ".launchReaction") : null,
            rejectsLyingTargets: dto.RejectsLyingTargets,
            rejectsFallingTargets: dto.RejectsFallingTargets,
            retainsGroundGuardOnFollowup: dto.RetainsGroundGuardOnFollowup),
        source, path);

    private static DynamicAttackDamage? BuildDynamicDamage(DynamicAttackDamageDto? damage, string source, string path) =>
        damage is null ? null : new DynamicAttackDamage(
            RequiredInt(damage.NormalBase, source, path + ".normalBase"),
            RequiredInt(damage.CounterBase, source, path + ".counterBase"), damage.UseContactDivisor);

    private static HitstopPair BuildHitstopPair(HitstopPairDto? dto, string source, string path)
    {
        if (dto is null)
            throw Fail(source, path, "必填停顿对象缺失或为 null。");

        int Frames(int? value, string field)
        {
            int frames = RequiredInt(value, source, field);
            if (frames is < 0 or > AttackDefinition.MaxHitstopFrames)
                throw Fail(source, field, $"停顿帧数必须在 0..{AttackDefinition.MaxHitstopFrames} 内。");
            return frames;
        }

        return new HitstopPair(
            Frames(dto.AttackerFrames, path + ".attackerFrames"),
            Frames(dto.DefenderFrames, path + ".defenderFrames"));
    }

    private static CancelDefinition BuildCancel(CancelDto dto, string source, string path) => Guard(
        () => new CancelDefinition(
            targetMoveId: RequiredString(dto.TargetMoveId, source, path + ".targetMoveId"),
            windowFirstFrame: RequiredInt(dto.WindowFirstFrame, source, path + ".windowFirstFrame"),
            windowLastFrame: RequiredInt(dto.WindowLastFrame, source, path + ".windowLastFrame"),
            phases: ParseFlags(
                RequiredString(dto.Phases, source, path + ".phases"),
                source, path + ".phases", MoveCancelPhases.Active | MoveCancelPhases.Recovery),
            requiresHitConfirm: RequiredBool(dto.RequiresHitConfirm, source,
                path + ".requiresHitConfirm"),
            requiresBlockConfirm: RequiredBool(dto.RequiresBlockConfirm, source,
                path + ".requiresBlockConfirm"),
            allowWhiff: RequiredBool(dto.AllowWhiff, source, path + ".allowWhiff"),
            allowSameMove: dto.AllowSameMove,
            standingInputOnly: dto.StandingInputOnly,
            maximumInputAge: dto.MaximumInputAge,
            retainBufferedMotionHelper: dto.RetainBufferedMotionHelper),
        source, path);

    private static ReservedCancelDefinition BuildReservedCancel(ReservedCancelDto dto, string source, string path) => Guard(
        () => new ReservedCancelDefinition(
            RequiredString(dto.CommandId, source, path + ".commandId"),
            RequiredInt(dto.WindowFirstFrame, source, path + ".windowFirstFrame"),
            RequiredInt(dto.WindowLastFrame, source, path + ".windowLastFrame"),
            ParseFlags(RequiredString(dto.Phases, source, path + ".phases"),
                source, path + ".phases", MoveCancelPhases.Active | MoveCancelPhases.Recovery)),
        source, path);

    private static ThrowDefinition BuildThrow(ThrowDto dto, string source, string path) => Guard(
        () => new ThrowDefinition(
            throwId: RequiredString(dto.ThrowId, source, path + ".throwId"),
            range: Fraction(dto.Range, source, path + ".range"),
            firstActiveFrame: RequiredInt(dto.FirstActiveFrame, source, path + ".firstActiveFrame"),
            lastActiveFrame: SentinelOrValue(dto.LastActiveFrame),
            damage: RequiredInt(dto.Damage, source, path + ".damage"),
            knockdownFrames: RequiredInt(dto.KnockdownFrames, source, path + ".knockdownFrames"),
            wakeupFrames: RequiredInt(dto.WakeupFrames, source, path + ".wakeupFrames"),
            pushDistance: Fraction(dto.PushDistance, source, path + ".pushDistance"),
            hitstopFrames: RequiredInt(dto.HitstopFrames, source, path + ".hitstopFrames"),
            targetStates: ParseFlags(
                RequiredString(dto.TargetStates, source, path + ".targetStates"),
                source, path + ".targetStates", ThrowTargetStates.All),
            techable: dto.Techable),
        source, path);

    private static ProjectileDefinition BuildProjectile(
        ProjectileDto dto, string source, string path) => Guard(
        () => new ProjectileDefinition(
            projectileTypeId: RequiredString(dto.ProjectileTypeId, source,
                path + ".projectileTypeId"),
            hit: BuildAttack(
                dto.Hit ?? throw Fail(source, path + ".hit", "必填对象缺失或为 null。"),
                source, path + ".hit"),
            speedX: Fraction(dto.SpeedX, source, path + ".speedX"),
            speedY: Fraction(dto.SpeedY, source, path + ".speedY"),
            lifetimeFrames: RequiredInt(dto.LifetimeFrames, source, path + ".lifetimeFrames"),
            maxAlive: RequiredInt(dto.MaxAlive, source, path + ".maxAlive"),
            spawnAtActiveFrame: RequiredInt(dto.SpawnAtActiveFrame, source,
                path + ".spawnAtActiveFrame"),
            timeline: dto.Timeline is { } timeline ? new ProjectileTimeline(
                RequiredInt(timeline.MovementFrames, source, path + ".timeline.movementFrames"),
                BuildArray(timeline.HitSpans, source, path + ".timeline.hitSpans", BuildProjectileHitSpan),
                timeline.RequestPauseOnHit, timeline.GuardThreatFrames, timeline.RespectsAirFollowupRestriction) : null),
        source, path);

    private static ProjectileHitSpan BuildProjectileHitSpan(ProjectileHitSpanDto dto, string source, string path) => Guard(
        () => new ProjectileHitSpan(
            RequiredInt(dto.FirstFrame, source, path + ".firstFrame"),
            RequiredInt(dto.LastFrame, source, path + ".lastFrame"),
            BuildHurtbox(dto.LocalHitbox ?? throw Fail(source, path + ".localHitbox", "必填对象缺失或为 null。"),
                source, path + ".localHitbox"),
            BuildAttack(dto.Hit ?? throw Fail(source, path + ".hit", "必填对象缺失或为 null。"),
                source, path + ".hit"),
            dto.GuardDamage, BuildDynamicDamage(dto.DynamicDamage, source, path + ".dynamicDamage"), dto.ContactOnEdge,
            dto.LaunchGravity is { } gravity ? Fraction(gravity, source, path + ".launchGravity") : null,
            dto.LaunchReaction is { } reaction ? BuildLaunchReaction(reaction, source, path + ".launchReaction") : null),
        source, path);

    private static LaunchReactionProfile BuildLaunchReaction(LaunchReactionDto dto, string source, string path) => Guard(
        () => new LaunchReactionProfile(
            RequiredInt(dto.ImpactFrames, source, path + ".impactFrames"),
            Fraction(dto.BounceHorizontalRetention, source, path + ".bounceHorizontalRetention"),
            Fraction(dto.BounceVerticalVelocity, source, path + ".bounceVerticalVelocity"),
            Fraction(dto.BounceGravity, source, path + ".bounceGravity"),
            RequiredInt(dto.LieDownFrames, source, path + ".lieDownFrames"),
            RequiredInt(dto.WakeupFrames, source, path + ".wakeupFrames"),
            RequiredInt(dto.RiseFrames, source, path + ".riseFrames"),
            RequiredInt(dto.LieImpactFrames, source, path + ".lieImpactFrames"),
            RequiredInt(dto.TransitionGravitySteps, source, path + ".transitionGravitySteps"), dto.StartInFall,
            dto.GroundTech is { } tech ? BuildGroundTech(tech, source, path + ".groundTech") : null,
            dto.SuppressOwnerProjectilesDuringFall),
        source, path);

    private static GroundTechProfile BuildGroundTech(GroundTechDto dto, string source, string path) => Guard(
        () => new GroundTechProfile(
            RequiredInt(dto.InputBufferFrames, source, path + ".inputBufferFrames"),
            RequiredInt(dto.DurationFrames, source, path + ".durationFrames"),
            RequiredInt(dto.SlideFrames, source, path + ".slideFrames"),
            Fraction(dto.InitialSpeed, source, path + ".initialSpeed"),
            Fraction(dto.VelocityRetention, source, path + ".velocityRetention")),
        source, path);

    // ── 字段级校验 ──────────────────────────────────────────────────

    private static FixedPoint Fraction(FractionDto? dto, string source, string path)
    {
        if (dto is null)
            throw Fail(source, path, "必填定点数字段缺失或为 null。");
        long numerator = RequiredLong(dto.Numerator, source, path + ".numerator");
        long denominator = RequiredLong(dto.Denominator, source, path + ".denominator");
        if (denominator <= 0)
            throw Fail(source, path + ".denominator",
                $"分母必须为正（收到 {denominator}）；负号只能写在分子上。");

        try
        {
            return FixedPoint.FromFraction(numerator, denominator);
        }
        catch (Exception ex) when (ex is DivideByZeroException or OverflowException
            or ArgumentException or ArgumentOutOfRangeException)
        {
            throw new FighterDataException(source, path,
                $"非法定点分数 {numerator}/{denominator}（超出 Q16.16 范围或格式错误）：{ex.Message}", ex);
        }
    }

    private static T[] BuildArray<TDto, T>(
        List<TDto>? list, string source, string path,
        Func<TDto, string, string, T> build)
    {
        if (list is null)
            throw Fail(source, path, "必填数组缺失或为 null。");

        var result = new T[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is null)
                throw Fail(source, $"{path}[{i}]", "数组元素为 null。");
            result[i] = build(list[i], source, $"{path}[{i}]");
        }
        return result;
    }

    private static CommandDirection[] BuildDirections(
        List<string>? list, string source, string path)
    {
        if (list is null)
            throw Fail(source, path, "必填数组缺失或为 null。");
        var result = new CommandDirection[list.Count];
        for (int i = 0; i < list.Count; i++)
            result[i] = ParseEnum<CommandDirection>(list[i], source, $"{path}[{i}]");
        return result;
    }

    private static InputBits BuildButtons(List<string>? list, string source, string path)
    {
        if (list is null)
            throw Fail(source, path, "必填数组缺失或为 null。");
        InputBits bits = InputBits.None;
        for (int i = 0; i < list.Count; i++)
        {
            InputBits button = ParseEnum<InputBits>(list[i], source, $"{path}[{i}]");
            if (button == InputBits.None || (button & ~DirectionResolver.AttackButtons) != InputBits.None)
                throw Fail(source, $"{path}[{i}]",
                    $"只允许攻击键名（{DirectionResolver.AttackButtons}），收到 '{list[i] ?? "null"}'。");
            bits |= button;
        }
        return bits;
    }

    private static int SentinelOrValue(int? value) => value ?? int.MaxValue;

    private static T ParseEnum<T>(string? text, string source, string path) where T : struct, Enum
    {
        if (text is null)
            throw Fail(source, path, "必填枚举字段缺失或为 null。");
        if (!Enum.TryParse(text, ignoreCase: false, out T parsed) || !Enum.IsDefined(parsed))
            throw Fail(source, path,
                $"无效枚举值 '{text}'（允许：{string.Join(", ", Enum.GetNames<T>())}）。");
        return parsed;
    }

    private static T ParseFlags<T>(string? text, string source, string path, T allMask)
        where T : struct, Enum
    {
        if (text is null)
            throw Fail(source, path, "必填枚举字段缺失或为 null。");
        if (!Enum.TryParse(text, ignoreCase: false, out T parsed))
            throw Fail(source, path,
                $"无效枚举组合 '{text}'（允许位：{allMask}）。");

        long value = Convert.ToInt64(parsed);
        long allowed = Convert.ToInt64(allMask);
        if (value == 0 || (value & ~allowed) != 0)
            throw Fail(source, path,
                $"无效枚举组合 '{text}'（不允许空组合或越界位；允许位：{allMask}）。");
        return parsed;
    }

    private static string RequiredString(string? value, string source, string path) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw Fail(source, path, "必填字符串缺失、为 null 或空白。");

    private static int RequiredInt(int? value, string source, string path) =>
        value ?? throw Fail(source, path, "必填整数字段缺失或为 null。");

    private static long RequiredLong(long? value, string source, string path) =>
        value ?? throw Fail(source, path, "必填整数字段缺失或为 null。");

    private static bool RequiredBool(bool? value, string source, string path) =>
        value ?? throw Fail(source, path, "必填布尔字段缺失或为 null。");

    private static T Guard<T>(Func<T> factory, string source, string path)
    {
        try
        {
            return factory();
        }
        catch (FighterDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException
            or InvalidOperationException or OverflowException or DivideByZeroException)
        {
            throw new FighterDataException(source, path, $"构造期校验失败：{ex.Message}", ex);
        }
    }

    private static FighterDataException Fail(string source, string path, string reason) =>
        new(source, path, reason);
}
