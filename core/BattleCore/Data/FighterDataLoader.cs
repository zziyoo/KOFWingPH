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

        FighterDefinition definition = Guard(
            () => new FighterDefinition(fighterId, commands, moves, movement),
            sourceName, "<definition>");
        return new FighterDataResult(fighterId, displayName, definition);
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
                "movement.horizontalJumpSpeed")),
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
                path + ".chargeReleaseToleranceFrames")),
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
            projectiles: BuildArray(dto.Projectiles, source, path + ".projectiles", BuildProjectile)),
        source, path);

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
            hitstopFrames: RequiredInt(dto.HitstopFrames, source, path + ".hitstopFrames")),
        source, path);

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
            allowWhiff: RequiredBool(dto.AllowWhiff, source, path + ".allowWhiff")),
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
                source, path + ".targetStates", ThrowTargetStates.All)),
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
                path + ".spawnAtActiveFrame")),
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
