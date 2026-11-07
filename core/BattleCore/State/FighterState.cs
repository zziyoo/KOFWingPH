using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.State;

/// <summary>角色朝向。阶段 01 固定，不做自动转身。</summary>
public enum Facing
{
    Right = 1,
    Left = -1,
}

/// <summary>
/// 单名角色的当前数据。
///
/// <para>阶段 04（D57）起，在原有「身份 + 初始数据」之上正式扩展<b>动作状态</b>与
/// <b>识别状态</b>——两者都是权威状态的一部分，随快照携带、可重放：</para>
/// <list type="bullet">
/// <item><see cref="LogicState"/> / <see cref="MoveId"/> / <see cref="PhaseRemainingFrames"/>：
///       角色动作状态机（见 <c>FighterStateMachine</c>），全部在整数逻辑帧上推进。</item>
/// <item><see cref="Recognition"/>：该角色输入的指令识别快照
///       （<see cref="RecognitionState"/>，不可变，含方向历史/蓄力挂起/结果缓冲/消费标记）。</item>
/// </list>
///
/// <para>阶段 05A（D63）起，坐标升级为 <b>Q16.16 定点数</b>（<see cref="FixedPoint"/>）：
/// <see cref="PositionX"/> / <see cref="PositionY"/> 是唯一权威表示（世界单位，
/// Y 轴向上、地面为 0）。旧整数坐标语义由 <see cref="PositionXUnits"/> /
/// <see cref="PositionYUnits"/>（向零截断的只读视图）与「int → FixedPoint 隐式转换 = 世界单位」
/// 共同保留——旧调用点 <c>PositionX: -120</c> 语义不变（-120 单位，不是原始定点值）。</para>
///
/// <para>阶段 05B（D68/D69）起，新增空中运动字段：<see cref="VelocityX"/> /
/// <see cref="VelocityY"/>（世界单位/帧，空中积分，落地归零）、<see cref="JumpKind"/>
/// （小跳/大跳，随快照携带供只读显示）、<see cref="UpHeldLastFrame"/>（上一逻辑帧解析后的
/// 「上」电平——起跳边沿判定的事实来源，防自动连跳 B5）。</para>
///
/// <para>仍然不包含：碰撞、受击。</para>
///
/// <para><b>不可变快照契约不退化</b>：本类型仍是 readonly record struct；新字段全部指向
/// 不可变对象或标量，旧快照不会被新一帧修改。<see cref="RecognitionState"/> 重写了
/// 结构化相等，因此跨实例的确定性比较仍然成立。</para>
/// </summary>
public readonly record struct FighterState(
    int Id,
    int MaxHealth,
    int Health,
    FixedPoint PositionX,
    FixedPoint PositionY,
    Facing Facing,
    FighterLogicState LogicState = FighterLogicState.Standing,
    string? MoveId = null,
    int PhaseRemainingFrames = 0,
    RecognitionState? Recognition = null,
    FixedPoint VelocityX = default,
    FixedPoint VelocityY = default,
    JumpKind JumpKind = Combat.JumpKind.None,
    bool UpHeldLastFrame = false,
    int StunRemainingFrames = 0,
    int MoveStartedFrame = -1,
    int LastHitAttackStartFrame = -1,
    ComboState? ComboRecord = null,
    int HitConfirmFrame = -1,
    int BlockConfirmFrame = -1,
    FixedPoint KnockbackFriction = default,
    int LandingKnockdownFrames = 0,
    int LandingWakeupFrames = 0)
{
    public static FighterState Create(int id, int maxHealth, int positionX, Facing facing) =>
        new(id, maxHealth, maxHealth, positionX, 0, facing);

    /// <summary>旧整数坐标只读视图（X，向零截断）：1 单位 = 1 旧整数坐标单位。</summary>
    public int PositionXUnits => PositionX.ToIntTruncated();

    /// <summary>旧整数坐标只读视图（Y，向零截断）。</summary>
    public int PositionYUnits => PositionY.ToIntTruncated();

    /// <summary>该角色的指令识别快照；null（旧构造路径）视为未观察的初始状态。</summary>
    public RecognitionState RecognitionState => Recognition ?? RecognitionState.Empty;

    /// <summary>
    /// 该角色作为<b>被击方</b>的连段记录（阶段 07B，D90）；null（旧构造路径）视为无连段。
    /// </summary>
    public ComboState Combo => ComboRecord ?? ComboState.None;

    /// <summary>是否处于招式阶段（Startup/Active/Recovery）。</summary>
    public bool IsInMove =>
        LogicState is FighterLogicState.Startup
            or FighterLogicState.Active
            or FighterLogicState.Recovery;

    /// <summary>校验基础不变式。用于尽早暴露非法初态，而不是让错误无声传播。</summary>
    public void Validate()
    {
        if (Id < 0)
            throw new InvalidOperationException($"角色 Id 不能为负：{Id}。");
        if (MaxHealth <= 0)
            throw new InvalidOperationException($"角色 {Id} 的 MaxHealth 必须为正：{MaxHealth}。");
        if (Health < 0 || Health > MaxHealth)
            throw new InvalidOperationException($"角色 {Id} 的 Health 越界：{Health}/{MaxHealth}。");

        // 合法枚举检查（D62）：未定义的 FighterLogicState 无论其他字段是什么都拒绝——
        // 否则 (FighterLogicState)999 + 空 MoveId + 0 帧会被误认为正常待机。
        // Enum.IsDefined 以枚举定义本身为唯一事实来源，不会与状态机维护出两份清单。
        if (!Enum.IsDefined(LogicState))
            throw new InvalidOperationException(
                $"角色 {Id} 携带未定义的角色逻辑状态：{LogicState}（{(int)LogicState}）。");

        // 跳跃类型完整枚举检查（05 专项补修 P2，与 D62 同一模式）：
        // 未定义的 JumpKind（如 (JumpKind)999）此前能借「Airborne + Y>0 + 999 ≠ None」
        // 通过校验进入权威快照。None 合法但只能用于地面（下方分支强制）。
        if (!Enum.IsDefined(JumpKind))
            throw new InvalidOperationException(
                $"角色 {Id} 携带未定义的跳跃类型：{JumpKind}（{(int)JumpKind}）。");

        // 动作状态不变式（D57）：阶段剩余帧数不允许为负；动作期间必须带 MoveId 且
        // 剩余 ≥ 1（阶段切换在归零的同一帧完成，不产生 0 帧的中间状态）；
        // 待机状态必须无 MoveId 且剩余为 0。
        if (PhaseRemainingFrames < 0)
            throw new InvalidOperationException(
                $"角色 {Id} 的阶段剩余帧数不能为负：{PhaseRemainingFrames}。");

        if (IsInMove)
        {
            if (string.IsNullOrEmpty(MoveId))
                throw new InvalidOperationException(
                    $"角色 {Id} 处于 {LogicState} 但没有 MoveId。");
            if (PhaseRemainingFrames < 1)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于 {LogicState} 但阶段剩余帧数为 {PhaseRemainingFrames}（必须 ≥ 1）。");
        }
        else
        {
            if (MoveId is not null)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于待机状态 {LogicState} 但携带招式 {MoveId}。");

            // PreJump 是唯一携带非零剩余帧的非动作状态（预备窗口倒计时，D67）；
            // 其余待机/空中状态剩余必须为 0。
            if (LogicState != FighterLogicState.PreJump && PhaseRemainingFrames != 0)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于待机状态 {LogicState} 但阶段剩余帧数为 {PhaseRemainingFrames}（必须为 0）。");
        }

        // 空中 / 地面几何与速度不变式（阶段 05B D68/D69；阶段 07C D92/D93 扩展）：
        // - Airborne：必须在空中（Y > 0）且携带跳跃类型（顶点垂直速度可为 0）；
        // - AirHitstun：<b>击飞当帧 Y 仍为 0</b>（位置由下一帧的空中积分推进），
        //   因此允许 Y == 0 但要求垂直速度为正；不携带跳跃类型（击飞不是跳跃）；
        // - 地面状态一律 Y == 0、VelocityY == 0；除受击/防御硬直的击退外速度必须为零。
        // 新增枚举值不能绕过校验：Enum.IsDefined 通过后仍按状态逐项检查（D62 的延伸）。
        if (LogicState is FighterLogicState.Airborne or FighterLogicState.AirHitstun)
        {
            if (LogicState == FighterLogicState.Airborne)
            {
                if (PositionY <= FixedPoint.Zero)
                    throw new InvalidOperationException(
                        $"角色 {Id} 处于空中但高度为 {PositionY}（空中状态必须 Y > 0；触地在同一帧内完成修正）。");
                if (JumpKind == Combat.JumpKind.None)
                    throw new InvalidOperationException($"角色 {Id} 处于空中但没有跳跃类型。");
            }
            else
            {
                if (PositionY < FixedPoint.Zero)
                    throw new InvalidOperationException(
                        $"角色 {Id} 处于空中受击但高度为负：{PositionY}。");
                if (PositionY == FixedPoint.Zero && VelocityY <= FixedPoint.Zero)
                    throw new InvalidOperationException(
                        $"角色 {Id} 处于空中受击且高度为 0，但垂直速度 {VelocityY} 不为正" +
                        "（击飞首帧之后必须由空中积分抬升；地面受击不应进入该状态）。");
                if (JumpKind != Combat.JumpKind.None)
                    throw new InvalidOperationException(
                        $"角色 {Id} 处于空中受击但携带跳跃类型 {JumpKind}（击飞不是跳跃，必须为 None）。");
            }

            if (KnockbackFriction != FixedPoint.Zero)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于空中状态 {LogicState} 但携带地面击退摩擦 {KnockbackFriction}。");
        }
        else
        {
            if (PositionY != FixedPoint.Zero)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于地面状态 {LogicState} 但高度为 {PositionY}（必须为 0）。");
            if (VelocityY != FixedPoint.Zero)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于地面状态 {LogicState} 但携带垂直速度 {VelocityY}（必须为 0）。");
            if (JumpKind != Combat.JumpKind.None)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于地面状态 {LogicState} 但携带跳跃类型 {JumpKind}。");
            if (LogicState == FighterLogicState.PreJump && PhaseRemainingFrames < 1)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于起跳预备但剩余帧数为 {PhaseRemainingFrames}（必须 ≥ 1）。");

            // 地面击退（阶段 07C，D92）：只有受击/防御硬直可以携带水平速度，
            // 且速度与摩擦必须同时存在或同时为 0（避免「有速度但永不衰减」的非法组合）。
            bool carriesKnockback = LogicState is FighterLogicState.Hitstun or FighterLogicState.Blockstun;
            if (carriesKnockback)
            {
                if ((VelocityX == FixedPoint.Zero) != (KnockbackFriction == FixedPoint.Zero))
                    throw new InvalidOperationException(
                        $"角色 {Id} 处于 {LogicState}，击退速度 {VelocityX} 与摩擦 {KnockbackFriction} " +
                        "必须同时为零或同时非零。");
            }
            else
            {
                if (VelocityX != FixedPoint.Zero)
                    throw new InvalidOperationException(
                        $"角色 {Id} 处于地面状态 {LogicState} 但携带水平速度 {VelocityX}（必须为 0）。");
                if (KnockbackFriction != FixedPoint.Zero)
                    throw new InvalidOperationException(
                        $"角色 {Id} 处于地面状态 {LogicState} 但携带击退摩擦 {KnockbackFriction}（必须为 0）。");
            }
        }

        // 硬直计数（阶段 06B D78 / 阶段 07C D93）：受击、防御、倒地、起身携带 ≥ 1 的剩余帧；
        // AirHitstun 与其余状态不得携带（空中受击持续到落地，没有独立计时）。
        if (LogicState is FighterLogicState.Hitstun or FighterLogicState.Blockstun
            or FighterLogicState.Knockdown or FighterLogicState.Wakeup)
        {
            if (StunRemainingFrames < 1)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于 {LogicState} 但硬直剩余帧数为 {StunRemainingFrames}（必须 ≥ 1）。");
        }
        else if (StunRemainingFrames != 0)
        {
            throw new InvalidOperationException(
                $"角色 {Id} 处于 {LogicState} 但携带硬直剩余帧数 {StunRemainingFrames}（必须为 0）。");
        }

        // 落地计划（阶段 07C，D93）：AirHitstun 携带「倒地/起身帧数」计划；
        // 落地后倒地帧数转为 StunRemaining，起身帧数继续携带到倒地结束。
        if (LogicState is FighterLogicState.AirHitstun or FighterLogicState.Knockdown)
        {
            if (LandingKnockdownFrames < 0 || LandingWakeupFrames < 0)
                throw new InvalidOperationException(
                    $"角色 {Id} 的落地计划帧数不能为负：({LandingKnockdownFrames}, {LandingWakeupFrames})。");

            // 空中受击允许「无倒地计划」（被非击飞攻击打中空中目标 → 落地直接站起，D93）；
            // 击飞攻击的「落地必倒地」由 AttackDefinition 构造期保证。
            if (LogicState == FighterLogicState.Knockdown && LandingKnockdownFrames != 0)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于倒地但重复携带倒地计划 {LandingKnockdownFrames}（必须为 0）。");
        }
        else if (LandingKnockdownFrames != 0 || LandingWakeupFrames != 0)
        {
            throw new InvalidOperationException(
                $"角色 {Id} 处于 {LogicState} 但携带落地计划 " +
                $"({LandingKnockdownFrames}, {LandingWakeupFrames})（必须为 0）。");
        }

        // 攻击实例身份（阶段 06C，D81）：动作期间必须携带发动帧（≥ 0），
        // 非动作状态必须为 -1——「发动帧 + 攻击者 ID」构成攻击实例唯一标识，
        // 供重复命中抑制与事件流使用。被击记录无不变式（单调记录，随快照携带）。
        if (IsInMove)
        {
            if (MoveStartedFrame < 0)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于 {LogicState} 但缺少攻击发动帧（MoveStartedFrame={MoveStartedFrame}）。");
        }
        else if (MoveStartedFrame != -1)
        {
            throw new InvalidOperationException(
                $"角色 {Id} 处于非动作状态 {LogicState} 但携带攻击发动帧 {MoveStartedFrame}（必须为 -1）。");
        }

        // 连段记录（阶段 07B，D90）：无连段必须 AttackerId=-1；有连段时攻击者必须是 0/1 且不是自己。
        Combo.Validate(Id);

        // 命中/防御确认帧（阶段 07B，D91）：只在招式期间存在，且必须属于<b>当前</b>攻击实例
        // （换招/中断/回待机后必须清空——否则旧实例的确认会解锁新招式的取消）。
        if (HitConfirmFrame < -1)
            throw new InvalidOperationException(
                $"角色 {Id} 的命中确认帧非法：{HitConfirmFrame}（必须 ≥ -1）。");
        if (BlockConfirmFrame < -1)
            throw new InvalidOperationException(
                $"角色 {Id} 的防御确认帧非法：{BlockConfirmFrame}（必须 ≥ -1）。");

        if (IsInMove)
        {
            if (HitConfirmFrame >= 0 && HitConfirmFrame < MoveStartedFrame)
                throw new InvalidOperationException(
                    $"角色 {Id} 的命中确认帧 {HitConfirmFrame} 早于当前攻击实例的发动帧 {MoveStartedFrame}。");
            if (BlockConfirmFrame >= 0 && BlockConfirmFrame < MoveStartedFrame)
                throw new InvalidOperationException(
                    $"角色 {Id} 的防御确认帧 {BlockConfirmFrame} 早于当前攻击实例的发动帧 {MoveStartedFrame}。");
        }
        else if (HitConfirmFrame != -1 || BlockConfirmFrame != -1)
        {
            throw new InvalidOperationException(
                $"角色 {Id} 处于非动作状态 {LogicState} 但携带命中/防御确认帧 " +
                $"({HitConfirmFrame}/{BlockConfirmFrame})（必须为 -1）。");
        }
    }
}
