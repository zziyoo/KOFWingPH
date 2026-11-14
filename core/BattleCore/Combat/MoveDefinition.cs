using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>从动作时钟采样的连续本地受击框。空框列表表示该时段无可接触矩形。</summary>
public sealed class MoveHurtboxSpan
{
    public MoveHurtboxSpan(int frames, IReadOnlyList<FixedRect> boxes)
    {
        if (frames < 1)
            throw new ArgumentOutOfRangeException(nameof(frames), "受击框时段必须至少持续一帧。");
        Frames = frames;
        Boxes = GroundHurtboxes.CopyLocalBoxes(boxes, nameof(boxes));
    }

    public int Frames { get; }
    public IReadOnlyList<FixedRect> Boxes { get; }
}

/// <summary>A facing-relative horizontal displacement at one effective move tick.</summary>
public readonly record struct MovePositionStep(int Frame, FixedPoint X);

/// <summary>
/// 一条<b>招式定义</b>（阶段 04，纯数据）：说明「哪个指令触发哪个动作、
/// 动作各阶段持续多少帧、允许从哪些待机状态发动」。
///
/// <para><b>职责分离（B3）</b>：<see cref="CommandRecognizer"/> 只负责把输入识别成
/// <see cref="CommandDefinition.CommandId"/>；本类负责把 CommandId 映射为动作并定义
/// 时间轴。识别器不认识任何招式，招式表不参与识别流程。</para>
///
/// <para>MoveId 只是逻辑上的动作 ID（如 <c>FireballPrototype</c>），
/// <b>不</b>生成飞行道具、不执行攻击判定、不扣生命值——判定/伤害属于后续阶段。</para>
/// </summary>
public sealed class MoveDefinition
{
/// <param name="moveId">招式唯一标识（同一角色内不得重复）。</param>
/// <param name="commandId">触发的指令 ID；必须存在于同角色的指令表中。</param>
/// <param name="startupFrames">启动阶段帧数（≥ 1）。</param>
/// <param name="activeFrames">生效阶段帧数（≥ 1；本阶段不执行判定）。</param>
/// <param name="recoveryFrames">收招阶段帧数（≥ 1）。</param>
/// <param name="allowedFrom">允许发动的待机状态集合，不能为 <see cref="MoveAllowedStates.None"/>。</param>
/// <param name="attacks">攻击判定数据（阶段 06A，D76）；null/空 = 不带攻击框的招式
/// （识别与状态机时间线照常，只是永不产生命中）。</param>
/// <param name="cancels">取消规则（阶段 07B，D91）：本招式可取消到哪些招式；
/// null/空 = 不可取消。目标必须在同一角色配置中且不是自身（由
/// <see cref="FighterDefinition"/> 构造期校验）。</param>
/// <param name="throws">投技定义（阶段 08B，D97）；null/空 = 本招式没有投技判定。
/// 生效帧窗口必须落在 Active 阶段内，且同一招式内 ThrowId 不得重复。</param>
/// <param name="projectiles">飞行道具定义（阶段 08D，D99）；null/空 = 本招式不发射道具。
/// 出生帧必须在 Active 阶段内，且同一招式内 ProjectileTypeId 不得重复。</param>
/// <param name="minimumStartDistance">发动或取消到此招时，帧开始的最小水平中心距。缺省无距离限制。</param>
/// <param name="hurtboxTimeline">可选的局部受击框时间线，持续时间必须覆盖完整招式。</param>
/// <param name="positionSteps">Optional displacements indexed by zero-based effective move ticks.</param>
/// <exception cref="ArgumentException">ID 为空 / 阶段帧数 &lt; 1 / 允许状态为空。</exception>
/// <exception cref="ArgumentException">攻击数据的生效帧范围超出 ActiveFrames。</exception>
public MoveDefinition(
    string moveId,
    string commandId,
    int startupFrames,
    int activeFrames,
    int recoveryFrames,
    MoveAllowedStates allowedFrom = MoveAllowedStates.GroundIdle,
    IReadOnlyList<AttackDefinition>? attacks = null,
    IReadOnlyList<CancelDefinition>? cancels = null,
    IReadOnlyList<ThrowDefinition>? throws = null,
    IReadOnlyList<ProjectileDefinition>? projectiles = null,
    FixedPoint minimumStartDistance = default,
    IReadOnlyList<MoveHurtboxSpan>? hurtboxTimeline = null,
    FixedPoint? maximumStartBodyDistance = null,
    int? guardThreatFrames = null,
    bool returnsToCrouch = false,
    IReadOnlyList<ReservedCancelDefinition>? reservedCancels = null,
    bool walkOnCrouchRecovery = false,
    IReadOnlyList<MovePositionStep>? positionSteps = null)
    {
        if (string.IsNullOrWhiteSpace(moveId))
            throw new ArgumentException("招式 ID 不能为空。", nameof(moveId));
        if (string.IsNullOrWhiteSpace(commandId))
            throw new ArgumentException("招式必须关联一个指令 ID。", nameof(commandId));
        if (startupFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(startupFrames), startupFrames,
                "启动阶段帧数必须 ≥ 1（本阶段不支持 0 帧阶段）。");
        if (activeFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(activeFrames), activeFrames,
                "生效阶段帧数必须 ≥ 1。");
        if (recoveryFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(recoveryFrames), recoveryFrames,
                "收招阶段帧数必须 ≥ 1。");
        if (allowedFrom == MoveAllowedStates.None || (allowedFrom & ~MoveAllowedStates.GroundIdle) != 0)
            throw new ArgumentException(
                $"允许发动状态只能是 Standing/Crouching 的组合，收到 {allowedFrom}。", nameof(allowedFrom));

        if (minimumStartDistance < FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumStartDistance), "最小发动距离不能为负。");
        if (maximumStartBodyDistance is { } maximum && maximum < FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumStartBodyDistance), "最大发动身体距离不能为负。");
        if (guardThreatFrames is { } threat && (threat < 1 || threat > (long)startupFrames + activeFrames + recoveryFrames))
            throw new ArgumentOutOfRangeException(nameof(guardThreatFrames));
        if (walkOnCrouchRecovery && !returnsToCrouch)
            throw new ArgumentException("Walking on crouch recovery requires a crouch return.", nameof(walkOnCrouchRecovery));

        MoveId = moveId;
        CommandId = commandId;
        StartupFrames = startupFrames;
        ActiveFrames = activeFrames;
        RecoveryFrames = recoveryFrames;
        AllowedFrom = allowedFrom;
        ReturnsToCrouch = returnsToCrouch;
        WalkOnCrouchRecovery = walkOnCrouchRecovery;
        MinimumStartDistance = minimumStartDistance;
        MaximumStartBodyDistance = maximumStartBodyDistance;
        GuardThreatFrames = guardThreatFrames;
        if (hurtboxTimeline is not null)
        {
            MoveHurtboxSpan[] spans = hurtboxTimeline.ToArray();
            if (spans.Any(span => span is null) ||
                spans.Sum(span => (long)span.Frames) != (long)startupFrames + activeFrames + recoveryFrames)
                throw new ArgumentException("受击框时段必须覆盖整个招式。", nameof(hurtboxTimeline));
            HurtboxTimeline = Array.AsReadOnly(spans);
        }
        if (positionSteps is not null)
        {
            var steps = positionSteps.OrderBy(step => step.Frame).ToArray();
            long total = (long)startupFrames + activeFrames + recoveryFrames;
            if (steps.Any(step => step.Frame < 0 || step.Frame >= total || step.X == FixedPoint.Zero) ||
                steps.Select(step => step.Frame).Distinct().Count() != steps.Length)
                throw new ArgumentException("Position steps require unique move ticks and nonzero horizontal displacement.",
                    nameof(positionSteps));
            PositionSteps = Array.AsReadOnly(steps);
        }

        // 攻击判定数据（D76）：防御复制 + 生效帧范围必须在 Active 阶段内
        // （0 起始含两端）。LastActiveFrame == int.MaxValue 是「直到 Active 末帧」的
        // 显式哨兵（默认值）；显式给出的其他越界范围构造期失败，不进入模拟。
        var attackCopy = (attacks ?? Array.Empty<AttackDefinition>()).ToArray();
        if (attackCopy.Any(attack => attack.ContactGroup != 0) && attackCopy.Any(attack => attack.ContactGroup == 0))
            throw new ArgumentException("招式的判定框必须统一使用显式接触组或整招一次结算。", nameof(attacks));
        foreach (AttackDefinition attack in attackCopy)
        {
            if (attack.LastActiveFrame != int.MaxValue && attack.LastActiveFrame >= activeFrames)
                throw new ArgumentException(
                    $"招式 {moveId} 的攻击生效帧范围非法（lastActiveFrame={attack.LastActiveFrame}，" +
                    $"ActiveFrames={activeFrames}；范围须在 0..{activeFrames - 1} 内，或用默认哨兵表示至末帧）。", nameof(attacks));
        }

        Attacks = Array.AsReadOnly(attackCopy);

        // 取消规则（D91）：防御复制；目标是否存在/是否自身由 FighterDefinition 校验（它知道全表）。
        Cancels = Array.AsReadOnly((cancels ?? Array.Empty<CancelDefinition>()).ToArray());
        var reservedCopy = (reservedCancels ?? Array.Empty<ReservedCancelDefinition>()).ToArray();
        if (reservedCopy.Any(rule => rule is null || rule.WindowLastFrame >= (long)startupFrames + activeFrames + recoveryFrames))
            throw new ArgumentException("Reserved cancel windows must end before natural move recovery.", nameof(reservedCancels));
        ReservedCancels = Array.AsReadOnly(reservedCopy);

        // 投技定义（D97）：生效帧窗口必须在 Active 阶段内，ThrowId 在本招式内唯一。
        ThrowDefinition[] throwCopy = (throws ?? Array.Empty<ThrowDefinition>()).ToArray();
        var seenThrowIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ThrowDefinition definition in throwCopy)
        {
            if (definition.LastActiveFrame != int.MaxValue && definition.LastActiveFrame >= activeFrames)
                throw new ArgumentException(
                    $"招式 {moveId} 的投技 {definition.ThrowId} 生效帧范围非法" +
                    $"（lastActiveFrame={definition.LastActiveFrame}，ActiveFrames={activeFrames}；" +
                    $"范围须在 0..{activeFrames - 1} 内，或用默认哨兵表示至末帧）。", nameof(throws));
            if (!seenThrowIds.Add(definition.ThrowId))
                throw new ArgumentException(
                    $"招式 {moveId} 内投技 ID 重复：{definition.ThrowId}。", nameof(throws));
        }

        Throws = Array.AsReadOnly(throwCopy);

        // 飞行道具（D99）：出生帧必须在 Active 阶段内，类型 ID 在本招式内唯一。
        ProjectileDefinition[] projectileCopy = (projectiles ?? Array.Empty<ProjectileDefinition>()).ToArray();
        var seenProjectileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProjectileDefinition definition in projectileCopy)
        {
            if (definition.SpawnAtActiveFrame >= activeFrames)
                throw new ArgumentException(
                    $"招式 {moveId} 的投掷物 {definition.ProjectileTypeId} 出生帧非法" +
                    $"（spawnAtActiveFrame={definition.SpawnAtActiveFrame}，ActiveFrames={activeFrames}）。",
                    nameof(projectiles));
            if (!seenProjectileIds.Add(definition.ProjectileTypeId))
                throw new ArgumentException(
                    $"招式 {moveId} 内投掷物类型 ID 重复：{definition.ProjectileTypeId}。", nameof(projectiles));
        }

        Projectiles = Array.AsReadOnly(projectileCopy);
    }

    /// <summary>招式唯一标识（同一角色内唯一）。</summary>
    public string MoveId { get; }

    /// <summary>触发该招式的指令 ID（与角色指令表中的 <see cref="CommandDefinition.CommandId"/> 对应）。</summary>
    public string CommandId { get; }

    /// <summary>启动阶段帧数（≥ 1）。</summary>
    public int StartupFrames { get; }

    /// <summary>生效阶段帧数（≥ 1；本阶段不执行判定）。</summary>
    public int ActiveFrames { get; }

    /// <summary>收招阶段帧数（≥ 1）。</summary>
    public int RecoveryFrames { get; }

    /// <summary>允许发动的待机状态集合。</summary>
    public MoveAllowedStates AllowedFrom { get; }
    public bool ReturnsToCrouch { get; }
    public bool WalkOnCrouchRecovery { get; }

    public FixedPoint MinimumStartDistance { get; }
    public FixedPoint? MaximumStartBodyDistance { get; }

    /// <summary>Optional exclusive end of the action's forward proximity-guard threat.</summary>
    public int? GuardThreatFrames { get; }

    internal bool CanStartAtDistance(FixedPoint centerDistance, FixedPoint bodyDistance) =>
        centerDistance >= MinimumStartDistance &&
        (MaximumStartBodyDistance is not { } maximum || bodyDistance <= maximum);

    public IReadOnlyList<MoveHurtboxSpan>? HurtboxTimeline { get; }

    public IReadOnlyList<MovePositionStep> PositionSteps { get; } = Array.Empty<MovePositionStep>();

    internal FixedPoint PositionStepAt(int elapsed)
    {
        foreach (MovePositionStep step in PositionSteps)
        {
            if (step.Frame >= elapsed)
                return step.Frame == elapsed ? step.X : FixedPoint.Zero;
        }
        return FixedPoint.Zero;
    }

    internal IReadOnlyList<FixedRect>? HurtboxesAt(int elapsed)
    {
        if (HurtboxTimeline is null)
            return null;
        foreach (MoveHurtboxSpan span in HurtboxTimeline)
        {
            if (elapsed < span.Frames)
                return span.Boxes;
            elapsed -= span.Frames;
        }
        return HurtboxTimeline[^1].Boxes;
    }

    /// <summary>攻击判定数据（阶段 06A，D76；只读，可为空列表 = 无攻击框的招式）。</summary>
    public IReadOnlyList<AttackDefinition> Attacks { get; }

    /// <summary>取消规则（阶段 07B，D91；只读，可为空列表 = 不可取消）。</summary>
    public IReadOnlyList<CancelDefinition> Cancels { get; }

    public IReadOnlyList<ReservedCancelDefinition> ReservedCancels { get; }

    /// <summary>投技定义（阶段 08B，D97；只读，可为空列表 = 本招式没有投技判定）。</summary>
    public IReadOnlyList<ThrowDefinition> Throws { get; }

    /// <summary>飞行道具定义（阶段 08D，D99；只读，可为空列表 = 本招式不发射道具）。</summary>
    public IReadOnlyList<ProjectileDefinition> Projectiles { get; }

    /// <summary>招式总帧数（诊断用）。</summary>
    public int TotalFrames => StartupFrames + ActiveFrames + RecoveryFrames;
}
