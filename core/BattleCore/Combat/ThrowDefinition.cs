using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 投技可抓取的<b>目标状态类别</b>（阶段 08B，D97）。按语义分桶而不是逐状态罗列，
/// 这样新增状态时只需要在 <see cref="ThrowDefinition.AllowsTarget"/> 里归类一次。
/// </summary>
[Flags]
public enum ThrowTargetStates
{
    /// <summary>不允许（非法配置）。</summary>
    None = 0,

    /// <summary>地面待机：Standing / Crouching。</summary>
    Standing = 1 << 0,

    /// <summary>地面动作中：Startup / Active / Recovery。</summary>
    Attacking = 1 << 1,

    /// <summary>地面受击/防御硬直：Hitstun / Blockstun。</summary>
    Stunned = 1 << 2,

    /// <summary>全部可投类别（默认）。</summary>
    All = Standing | Attacking | Stunned,
}

/// <summary>
/// 一条<b>投技定义</b>（阶段 08B，D97）：挂在 <see cref="MoveDefinition.Throws"/> 上，
/// 由现有指令识别器 + 招式时间线驱动（<b>不</b>另建一套投技按键或指令系统）。
///
/// <para><b>判定方式</b>：不是 Hitbox × Hurtbox，而是「投技者处于 Active 阶段且在本招式的
/// 生效帧窗口内 + 目标状态可投 + 双方权威水平距离 ≤ <see cref="Range"/> + 目标在投技者<b>面前</b>」。
/// 距离与方向全部使用 Q16.16 定点数与权威坐标（D63），不读 Godot。</para>
///
/// <para><b>不可投的状态</b>（无论 <see cref="TargetStates"/> 怎么配）：
/// PreJump / Airborne / AirHitstun（空中）、Knockdown / Wakeup（倒地与起身）。
/// 这些状态由 <see cref="AllowsTarget"/> 统一拒绝——倒地/起身的投技免疫是<b>规则</b>，
/// 不是「因为没有 Hurtbox 所以抓不到」。</para>
///
/// <para><b>命中后的处理</b>：扣血 → 目标进入 <see cref="FighterLogicState.Knockdown"/>
/// （<see cref="KnockdownFrames"/> 帧）→ <see cref="FighterLogicState.Wakeup"/>
/// （<see cref="WakeupFrames"/> 帧）→ 待机；并沿「远离投技者」方向平移
/// <see cref="PushDistance"/>（受 D70 场地边界与 D73 最小间距约束，见 D97）。
/// 同一投技实例（投技者的 <c>MoveStartedFrame</c>）对同一目标至多结算一次。</para>
/// </summary>
public sealed class ThrowDefinition
{
    /// <param name="throwId">投技 ID（同一招式内唯一；用于事件与诊断）。</param>
    /// <param name="range">最大可抓取水平中心距离（世界单位，&gt; 0）。</param>
    /// <param name="firstActiveFrame">Active 阶段内首个生效帧（0 起始，含）。</param>
    /// <param name="lastActiveFrame">Active 阶段内末个生效帧（含；<see cref="int.MaxValue"/> = 至末帧）。</param>
    /// <param name="damage">抓取成功的基础伤害（≥ 0）。</param>
    /// <param name="knockdownFrames">被投后的倒地帧数（≥ 1）。</param>
    /// <param name="wakeupFrames">倒地后的起身帧数（≥ 0；0 = 直接站起）。</param>
    /// <param name="pushDistance">被投后的水平平移距离（≥ 0；方向 = 远离投技者）。</param>
    /// <param name="hitstopFrames">抓取成功后的命中停顿（0..<see cref="AttackDefinition.MaxHitstopFrames"/>）。</param>
    /// <param name="targetStates">允许被抓取的目标状态类别（默认全部地面类别）。</param>
    /// <exception cref="ArgumentException">ID 为空 / 帧窗口倒置 / 目标状态非法。</exception>
    /// <exception cref="ArgumentOutOfRangeException">距离、伤害、帧数或平移越界。</exception>
    public ThrowDefinition(
        string throwId,
        FixedPoint range,
        int firstActiveFrame = 0,
        int lastActiveFrame = int.MaxValue,
        int damage = 0,
        int knockdownFrames = 12,
        int wakeupFrames = 8,
        FixedPoint pushDistance = default,
        int hitstopFrames = 0,
        ThrowTargetStates targetStates = ThrowTargetStates.All)
    {
        if (string.IsNullOrWhiteSpace(throwId))
            throw new ArgumentException("投技 ID 不能为空。", nameof(throwId));
        if (range <= FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(range), range, "投技范围必须为正。");
        if (range > FixedPoint.FromInt(MaxRangeUnits))
            throw new ArgumentOutOfRangeException(nameof(range), range,
                $"投技范围超过上限 {MaxRangeUnits} 世界单位。");
        if (firstActiveFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(firstActiveFrame), firstActiveFrame,
                "投技生效首帧不能为负。");
        if (lastActiveFrame < firstActiveFrame)
            throw new ArgumentException(
                $"投技生效帧范围非法（first={firstActiveFrame} > last={lastActiveFrame}）。",
                nameof(lastActiveFrame));
        if (damage < 0)
            throw new ArgumentOutOfRangeException(nameof(damage), damage, "投技伤害不能为负。");
        if (knockdownFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(knockdownFrames), knockdownFrames,
                "被投后必须进入倒地（倒地帧数 ≥ 1）。");
        if (wakeupFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(wakeupFrames), wakeupFrames, "起身帧数不能为负。");
        if (pushDistance < FixedPoint.Zero)
            throw new ArgumentOutOfRangeException(nameof(pushDistance), pushDistance,
                "投技平移距离必须是非负大小（方向由权威相对位置决定）。");
        if (pushDistance > FixedPoint.FromInt(MaxPushUnits))
            throw new ArgumentOutOfRangeException(nameof(pushDistance), pushDistance,
                $"投技平移距离超过上限 {MaxPushUnits} 世界单位。");
        if (hitstopFrames < 0 || hitstopFrames > AttackDefinition.MaxHitstopFrames)
            throw new ArgumentOutOfRangeException(nameof(hitstopFrames), hitstopFrames,
                $"投技停顿帧数必须在 0..{AttackDefinition.MaxHitstopFrames} 内。");
        if (targetStates == ThrowTargetStates.None
            || (targetStates & ~ThrowTargetStates.All) != 0)
            throw new ArgumentException(
                $"投技目标状态只能是 Standing/Attacking/Stunned 的组合，收到 {targetStates}。",
                nameof(targetStates));

        ThrowId = throwId;
        Range = range;
        FirstActiveFrame = firstActiveFrame;
        LastActiveFrame = lastActiveFrame;
        Damage = damage;
        KnockdownFrames = knockdownFrames;
        WakeupFrames = wakeupFrames;
        PushDistance = pushDistance;
        HitstopFrames = hitstopFrames;
        TargetStates = targetStates;
    }

    /// <summary>投技 ID（同一招式内唯一）。</summary>
    public string ThrowId { get; }

    /// <summary>最大可抓取水平中心距离（世界单位）。</summary>
    public FixedPoint Range { get; }

    /// <summary>Active 阶段内首个生效帧（0 起始，含）。</summary>
    public int FirstActiveFrame { get; }

    /// <summary>Active 阶段内末个生效帧（含；int.MaxValue = 至 Active 末帧）。</summary>
    public int LastActiveFrame { get; }

    /// <summary>抓取成功的基础伤害。</summary>
    public int Damage { get; }

    /// <summary>被投后的倒地帧数（≥ 1）。</summary>
    public int KnockdownFrames { get; }

    /// <summary>倒地后的起身帧数（0 = 直接站起）。</summary>
    public int WakeupFrames { get; }

    /// <summary>被投后的水平平移距离（方向 = 远离投技者）。</summary>
    public FixedPoint PushDistance { get; }

    /// <summary>抓取成功后的命中停顿帧数。</summary>
    public int HitstopFrames { get; }

    /// <summary>允许被抓取的目标状态类别。</summary>
    public ThrowTargetStates TargetStates { get; }

    /// <summary>单次投技范围上限（世界单位）。</summary>
    public const int MaxRangeUnits = 200;

    /// <summary>单次投技平移上限（世界单位）。</summary>
    public const int MaxPushUnits = 300;

    /// <summary>
    /// 目标状态是否可被本投技抓取（纯函数，规则唯一来源）。
    ///
    /// <para>空中（Airborne / AirHitstun）与倒地/起身（Knockdown / Wakeup）以及
    /// PreJump <b>一律不可投</b>——这条不受 <see cref="TargetStates"/> 影响。</para>
    /// </summary>
    public bool AllowsTarget(FighterLogicState state)
    {
        if (state is FighterLogicState.PreJump
            or FighterLogicState.Airborne
            or FighterLogicState.AirHitstun
            or FighterLogicState.Knockdown
            or FighterLogicState.Wakeup)
        {
            return false;
        }

        ThrowTargetStates bucket = state switch
        {
            FighterLogicState.Standing or FighterLogicState.Crouching => ThrowTargetStates.Standing,
            FighterLogicState.Startup or FighterLogicState.Active or FighterLogicState.Recovery
                => ThrowTargetStates.Attacking,
            FighterLogicState.Hitstun or FighterLogicState.Blockstun => ThrowTargetStates.Stunned,
            _ => ThrowTargetStates.None,
        };

        return (TargetStates & bucket) != 0;
    }
}
