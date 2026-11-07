using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

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
    /// <exception cref="ArgumentOutOfRangeException">半宽/半高 ≤ 0、帧号 &lt; 0、
    /// 伤害 &lt; 0 或硬直帧数 &lt; 1。</exception>
    /// <exception cref="ArgumentException">lastActiveFrame &lt; firstActiveFrame。</exception>
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
        int blockstunFrames = 1)
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
        if (hitstunFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(hitstunFrames), hitstunFrames,
                "命中硬直帧数必须 ≥ 1（命中必进入 Hitstun）。");
        if (blockstunFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(blockstunFrames), blockstunFrames,
                "防御硬直帧数必须 ≥ 1（防御必进入 Blockstun）。");

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
}
