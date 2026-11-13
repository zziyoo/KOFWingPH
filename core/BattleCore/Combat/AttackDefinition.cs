using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>命中或防御结算后，双方各自冻结的后续逻辑帧数。</summary>
public readonly record struct HitstopPair
{
    public HitstopPair(int attackerFrames, int defenderFrames)
    {
        if (attackerFrames is < 0 or > AttackDefinition.MaxHitstopFrames)
            throw new ArgumentOutOfRangeException(nameof(attackerFrames));
        if (defenderFrames is < 0 or > AttackDefinition.MaxHitstopFrames)
            throw new ArgumentOutOfRangeException(nameof(defenderFrames));
        AttackerFrames = attackerFrames;
        DefenderFrames = defenderFrames;
    }

    public int AttackerFrames { get; }
    public int DefenderFrames { get; }
}

/// <summary>近身攻击在两种结算结果下的独立停顿。相杀和飞行道具沿用各自的规则。</summary>
public readonly record struct HitstopByOutcome(HitstopPair Hit, HitstopPair Block);

/// <summary>
/// 一段<b>攻击判定数据</b>（阶段 06A，D76）：招式在 Active 阶段的攻击框几何、
/// 生效帧范围与攻击高度。纯数据，构造期校验；06B 在此之上接入伤害与硬直结算。
///
/// <para><b>几何</b>：攻击框以角色<b>脚底锚点</b>（PositionX, PositionY）为基准——
/// <see cref="OffsetForward"/> 沿角色面向（世界 X 按朝向镜像：朝右 +，朝左 −），
/// <see cref="OffsetUp"/> 沿世界 Y 向上。框尺寸由半宽/半高给出（必须严格为正）。
/// 攻击框<b>跟随角色当帧权威位置</b>，左右镜像只由权威 <c>Facing</c> 决定，
/// 与 Godot 的精灵翻转状态无关。</para>
///
/// <para><b>生效帧范围</b>：<see cref="FirstActiveFrame"/> / <see cref="LastActiveFrame"/>
/// 是 Active 阶段内的 0 起始帧号（含两端）。D58 时序下，「Startup → Active 转换完成的
/// 那一逻辑帧」就是 Active 第 1 帧（elapsed 0）；Recovery 首帧不再有攻击框。
/// 范围由 <see cref="MoveDefinition"/> 构造期校验（不得超出 ActiveFrames）。</para>
/// </summary>
public sealed class AttackDefinition
{
    /// <param name="offsetForward">攻击框中心沿面向的前向偏移（世界单位，按朝向镜像）。</param>
    /// <param name="offsetUp">攻击框中心相对脚底锚点的向上偏移（世界单位）。</param>
    /// <param name="halfWidth">攻击框半宽（&gt; 0）。</param>
    /// <param name="halfHeight">攻击框半高（&gt; 0）。</param>
    /// <param name="firstActiveFrame">Active 阶段内首个生效帧（0 起始，含）。</param>
    /// <param name="lastActiveFrame">Active 阶段内末个生效帧（含；≥ firstActiveFrame；
    /// <see cref="int.MaxValue"/> = 哨兵「直到 Active 末帧」，由招式的 ActiveFrames 界定）。</param>
    /// <param name="height">攻击高度类别（06B 防御表；默认 Mid）。</param>
    /// <param name="damage">基础伤害（非负整数体力值，06B；默认 0 = 无伤害）。</param>
    /// <param name="hitstunFrames">命中硬直帧数（≥ 1；06B）。</param>
    /// <param name="blockstunFrames">防御硬直帧数（≥ 1；06B）。</param>
    /// <param name="groundKnockback">地面命中击退初速（世界单位/帧，≥ 0；方向 = 远离攻击者，07C/D92）。</param>
    /// <param name="groundKnockbackFriction">地面击退<b>通用</b>摩擦（世界单位/帧²，向 0 衰减）：
    /// 命中击退与防御击退共用；<paramref name="groundKnockback"/> 或
    /// <paramref name="blockKnockback"/> 任一为正时它必须为正（D95）。</param>
    /// <param name="blockKnockback">被防御时的击退初速（≥ 0；默认 0 = 防御不掉位）。</param>
    /// <param name="launchVelocityX">击飞水平初速（≥ 0；方向 = 远离攻击者）。</param>
    /// <param name="launchVelocityY">击飞垂直初速（≥ 0；&gt; 0 即「这一击会击飞」）。</param>
    /// <param name="knockdownFrames">落地后的倒地帧数（击飞时 ≥ 1）。</param>
    /// <param name="wakeupFrames">倒地后的起身帧数（0 = 直接站起）。</param>
    /// <param name="hitstopFrames">命中停顿帧数（阶段 08A，D96）：命中或成功防御后，
    /// 攻击方与被击方各冻结该数量的<b>后续</b>逻辑帧；0 = 不产生停顿。</param>
    /// <param name="hitstopByOutcome">可选的近身命中与防御停顿，覆盖上述对称默认值。</param>
    /// <param name="contactOnEdge">近身攻击是否把边缘相接计为接触；默认要求正面积重叠。</param>
    /// <param name="counterDamage">可选的近身反击伤害，目标处于起手或生效阶段时使用。</param>
    /// <exception cref="ArgumentOutOfRangeException">半宽/半高 ≤ 0、帧号 &lt; 0、
    /// 伤害 &lt; 0 或硬直帧数 &lt; 1，或击退/击飞参数越界。</exception>
    /// <exception cref="ArgumentException">lastActiveFrame &lt; firstActiveFrame，
    /// 或击退/击飞参数组合不自洽。</exception>
    public AttackDefinition(
        FixedPoint offsetForward,
        FixedPoint offsetUp,
        FixedPoint halfWidth,
        FixedPoint halfHeight,
        int firstActiveFrame = 0,
        int lastActiveFrame = int.MaxValue,
        AttackHeight height = AttackHeight.Mid,
        int damage = 0,
        int hitstunFrames = 1,
        int blockstunFrames = 1,
        FixedPoint? groundKnockback = null,
        FixedPoint? groundKnockbackFriction = null,
        FixedPoint? blockKnockback = null,
        FixedPoint launchVelocityX = default,
        FixedPoint launchVelocityY = default,
        int knockdownFrames = 0,
        int wakeupFrames = 0,
        int hitstopFrames = 0,
        HitstopByOutcome? hitstopByOutcome = null,
        bool contactOnEdge = false,
        int? counterDamage = null)
    {
        if (halfWidth <= FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(halfWidth), halfWidth, "攻击框半宽必须为正。");
        if (halfHeight <= FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(halfHeight), halfHeight, "攻击框半高必须为正。");
        if (firstActiveFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(firstActiveFrame), firstActiveFrame,
                "攻击生效首帧不能为负。");
        if (lastActiveFrame < firstActiveFrame)
            throw new ArgumentException(
                $"攻击生效帧范围非法（first={firstActiveFrame} > last={lastActiveFrame}）。", nameof(lastActiveFrame));
        if (damage < 0)
            throw new ArgumentOutOfRangeException(nameof(damage), damage, "伤害不能为负。");
        if (counterDamage < 0)
            throw new ArgumentOutOfRangeException(nameof(counterDamage), counterDamage, "反击伤害不能为负。");
        if (hitstunFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(hitstunFrames), hitstunFrames,
                "命中硬直帧数必须 ≥ 1（命中必进入 Hitstun）。");
        if (blockstunFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(blockstunFrames), blockstunFrames,
                "防御硬直帧数必须 ≥ 1（防御必进入 Blockstun）。");

        // 击退 / 击飞参数（阶段 07C，D92/D93）：全部非负，且组合自洽。
        FixedPoint knockback = groundKnockback ?? FixedPoint.Zero;
        FixedPoint friction = groundKnockbackFriction ?? FixedPoint.Zero;
        FixedPoint blockPush = blockKnockback ?? FixedPoint.Zero;

        ValidateMagnitude(knockback, nameof(groundKnockback), MaxKnockbackUnits);
        ValidateMagnitude(friction, nameof(groundKnockbackFriction), MaxKnockbackUnits);
        ValidateMagnitude(blockPush, nameof(blockKnockback), MaxKnockbackUnits);
        ValidateMagnitude(launchVelocityX, nameof(launchVelocityX), MaxLaunchUnits);
        ValidateMagnitude(launchVelocityY, nameof(launchVelocityY), MaxLaunchUnits);

        // D95：摩擦是<b>地面击退通用</b>配置——命中击退（GroundKnockback）与
        // 防御击退（BlockKnockback）都会在结算时产生水平速度，两者任一为正就必须有正摩擦。
        // 只校验 GroundKnockback 会让「仅防御击退 + 零摩擦」通过构造，在对局中触发
        // FighterState.Validate 的「速度与摩擦必须同时为零或同时非零」而整帧失败。
        if ((knockback > FixedPoint.Zero || blockPush > FixedPoint.Zero) && friction <= FixedPoint.Zero)
            throw new ArgumentException(
                "配置了地面击退或防御击退就必须配置正的击退摩擦，否则角色会无限滑行。",
                nameof(groundKnockbackFriction));
        if (knockdownFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(knockdownFrames), knockdownFrames, "倒地帧数不能为负。");
        if (wakeupFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(wakeupFrames), wakeupFrames, "起身帧数不能为负。");
        if (launchVelocityY > FixedPoint.Zero && knockdownFrames < 1)
            throw new ArgumentException(
                "击飞必须配置倒地帧数（≥ 1）：落地后不允许直接恢复站立。", nameof(knockdownFrames));
        if (hitstopFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(hitstopFrames), hitstopFrames,
                "命中停顿帧数不能为负。");
        if (hitstopFrames > MaxHitstopFrames)
            throw new ArgumentOutOfRangeException(nameof(hitstopFrames), hitstopFrames,
                $"命中停顿帧数超过上限 {MaxHitstopFrames}（防止配置错误导致长时间冻结）。");

        OffsetForward = offsetForward;
        OffsetUp = offsetUp;
        HalfWidth = halfWidth;
        HalfHeight = halfHeight;
        FirstActiveFrame = firstActiveFrame;
        LastActiveFrame = lastActiveFrame;
        Height = height;
        Damage = damage;
        HitstunFrames = hitstunFrames;
        BlockstunFrames = blockstunFrames;
        GroundKnockback = knockback;
        GroundKnockbackFriction = friction;
        BlockKnockback = blockPush;
        LaunchVelocityX = launchVelocityX;
        LaunchVelocityY = launchVelocityY;
        KnockdownFrames = knockdownFrames;
        WakeupFrames = wakeupFrames;
        HitstopFrames = hitstopFrames;
        HitstopByOutcome = hitstopByOutcome;
        ContactOnEdge = contactOnEdge;
        CounterDamage = counterDamage;
    }

    /// <summary>攻击框中心沿面向的前向偏移（世界单位；朝向镜像在查询时按权威 Facing 施加）。</summary>
    public FixedPoint OffsetForward { get; }

    /// <summary>攻击框中心相对脚底锚点的向上偏移（世界单位）。</summary>
    public FixedPoint OffsetUp { get; }

    /// <summary>攻击框半宽（世界单位，&gt; 0）。</summary>
    public FixedPoint HalfWidth { get; }

    /// <summary>攻击框半高（世界单位，&gt; 0）。</summary>
    public FixedPoint HalfHeight { get; }

    /// <summary>Active 阶段内首个生效帧（0 起始，含）。</summary>
    public int FirstActiveFrame { get; }

    /// <summary>Active 阶段内末个生效帧（含）。</summary>
    public int LastActiveFrame { get; }

    /// <summary>攻击高度类别（06B 防御表使用）。</summary>
    public AttackHeight Height { get; }

    /// <summary>基础伤害（非负整数体力值；命中时 Health = max(0, Health − Damage)）。</summary>
    public int Damage { get; }

    /// <summary>命中硬直帧数（≥ 1）。</summary>
    public int HitstunFrames { get; }

    /// <summary>防御硬直帧数（≥ 1）。</summary>
    public int BlockstunFrames { get; }

    /// <summary>地面命中击退初速（世界单位/帧，≥ 0；方向 = 远离攻击者）。0 = 不击退。</summary>
    public FixedPoint GroundKnockback { get; }

    /// <summary>地面击退<b>通用</b>摩擦（世界单位/帧²，向 0 衰减）：命中击退与防御击退共用；
    /// 配置了任一地面击退（命中或防御）时必须为正（D95）。</summary>
    public FixedPoint GroundKnockbackFriction { get; }

    /// <summary>被防御时的击退初速（≥ 0；默认 0）。</summary>
    public FixedPoint BlockKnockback { get; }

    /// <summary>击飞水平初速（≥ 0；方向 = 远离攻击者）。</summary>
    public FixedPoint LaunchVelocityX { get; }

    /// <summary>击飞垂直初速（≥ 0）；&gt; 0 表示这一击会击飞。</summary>
    public FixedPoint LaunchVelocityY { get; }

    /// <summary>落地后的倒地帧数（击飞时必须 ≥ 1）。</summary>
    public int KnockdownFrames { get; }

    /// <summary>倒地后的起身帧数（0 = 直接从倒地回到待机）。</summary>
    public int WakeupFrames { get; }

    /// <summary>
    /// 命中停顿帧数（阶段 08A，D96）：命中或成功防御后，攻击方与被击方各冻结
    /// 该数量的<b>后续</b>逻辑帧（命中当帧照常完成结算）。0 = 不产生停顿。
    /// </summary>
    public int HitstopFrames { get; }

    public HitstopByOutcome? HitstopByOutcome { get; }

    public bool ContactOnEdge { get; }

    public int? CounterDamage { get; }

    public HitstopPair ResolveMeleeHitstop(bool blocked) => HitstopByOutcome is { } outcome
        ? (blocked ? outcome.Block : outcome.Hit)
        : new HitstopPair(HitstopFrames, HitstopFrames);

    /// <summary>这一击是否会击飞（垂直初速 &gt; 0）。</summary>
    public bool Launches => LaunchVelocityY > FixedPoint.Zero;

    /// <summary>单帧击退/摩擦配置上限（健全性边界）。</summary>
    public const int MaxKnockbackUnits = 32;

    /// <summary>单帧击飞初速上限（健全性边界）。</summary>
    public const int MaxLaunchUnits = 64;

    /// <summary>命中停顿帧数上限（健全性边界：0.5 秒）。</summary>
    public const int MaxHitstopFrames = 30;

    private static void ValidateMagnitude(FixedPoint value, string paramName, int maxUnits)
    {
        if (value < FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(paramName, value,
                "击退/击飞数值必须是非负的<b>大小</b>；方向由命中时双方的权威相对位置决定。");
        if (value > FixedPoint.FromInt(maxUnits))
            throw new ArgumentOutOfRangeException(paramName, value,
                $"击退/击飞数值超过每帧上限 {maxUnits}：{value}。");
    }
}
