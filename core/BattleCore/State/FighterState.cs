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
    bool UpHeldLastFrame = false)
{
    public static FighterState Create(int id, int maxHealth, int positionX, Facing facing) =>
        new(id, maxHealth, maxHealth, positionX, 0, facing);

    /// <summary>旧整数坐标只读视图（X，向零截断）：1 单位 = 1 旧整数坐标单位。</summary>
    public int PositionXUnits => PositionX.ToIntTruncated();

    /// <summary>旧整数坐标只读视图（Y，向零截断）。</summary>
    public int PositionYUnits => PositionY.ToIntTruncated();

    /// <summary>该角色的指令识别快照；null（旧构造路径）视为未观察的初始状态。</summary>
    public RecognitionState RecognitionState => Recognition ?? RecognitionState.Empty;

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

        // 空中运动不变式（阶段 05B，D68/D69）：
        // - 地面状态（待机/预备跳/动作三阶段）必须在地面（Y=0）、零速度、无跳跃类型——
        //   本阶段不做空中招式，动作只在地面发生；
        // - PreJump 携带 1..∞ 的预备窗口剩余帧（由运动配置限制）；
        // - Airborne 必须在空中（Y > 0）且携带跳跃类型（垂直速度在顶点可以为 0）。
        // 新增枚举值不能绕过校验：Enum.IsDefined 通过后仍按状态逐项检查（D62 的延伸）。
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
            if (PositionY != FixedPoint.Zero)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于地面状态 {LogicState} 但高度为 {PositionY}（必须为 0）。");
            if (VelocityX != FixedPoint.Zero || VelocityY != FixedPoint.Zero)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于地面状态 {LogicState} 但携带速度 ({VelocityX}, {VelocityY})（必须为零）。");
            if (JumpKind != Combat.JumpKind.None)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于地面状态 {LogicState} 但携带跳跃类型 {JumpKind}。");
            if (LogicState == FighterLogicState.PreJump && PhaseRemainingFrames < 1)
                throw new InvalidOperationException(
                    $"角色 {Id} 处于起跳预备但剩余帧数为 {PhaseRemainingFrames}（必须 ≥ 1）。");
        }
    }
}
