using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一帧内单名角色的动作推进结果（不可变）。
/// </summary>
/// <param name="State">新的角色逻辑状态。</param>
/// <param name="MoveId">进行中的招式（待机为 null）。</param>
/// <param name="PhaseRemainingFrames">当前阶段剩余帧数（待机为 0；动作/预备跳期间恒 ≥ 1）。</param>
/// <param name="Recognition">应用了消费标记后的识别状态（纯函数结果）。</param>
/// <param name="ConsumedMatch">本帧被消费的识别结果（无消费为 null；诊断/测试用）。</param>
/// <param name="Launch">起跳参数（D68）：仅 PreJump 归零同帧起跳时非 null，
/// 由运动阶段据此开始空中积分；其余帧为 null。</param>
public readonly record struct FighterActionTick(
    FighterLogicState State,
    string? MoveId,
    int PhaseRemainingFrames,
    RecognitionState Recognition,
    CommandMatch? ConsumedMatch,
    JumpLaunch? Launch = null,
    int StunRemainingFrames = 0,
    int MoveStartedFrame = -1);

/// <summary>
/// 角色<b>动作状态机</b>（阶段 04/05B）：集中、显式、纯函数的状态转换。
///
/// <para>
/// <b>每帧推进顺序（<see cref="Tick"/>，整数逻辑帧）：</b></para>
/// <list type="number">
/// <item><b>动作阶段推进</b>：若处于 Startup/Active/Recovery，剩余帧数 −1；
///       归零时按招式数据切换到下一阶段（Startup → Active → Recovery）；
///       Recovery 结束回到待机——<b>同帧</b>即可进行待机更新、起跳与消费（B5）。</item>
/// <item><b>起跳预备推进（D67，05B）</b>：PreJump 剩余 −1；归零同帧起跳——
///       按当帧输入锁定跳跃类型（末帧「上」仍按住 → 大跳，否则小跳，D68）与
///       水平初速度（前/后/中立），进入 Airborne 并由 <see cref="FighterActionTick.Launch"/>
///       携带初始速度。PreJump 期间不消费指令、不做待机更新（不可取消）。</item>
/// <item><b>待机更新（B9）</b>：仅待机状态下——①「上」按下边沿
///       （<paramref name="upHeldLastFrame"/> 为上一帧电平，B5 防自动连跳）→ 进入
///       PreJump（站立/蹲下皆可起跳，SOCD 上优先由解析层保证）；
///       ② 否则按当帧解析输入切换 Standing ↔ Crouching。
///       动作期间<b>不</b>被蹲下或起跳覆盖。</item>
/// <item><b>指令消费（D60）</b>：按缓冲顺序（识别帧升序，每帧至多一条胜者）取
///       第一条未消费的有效结果；若其 CommandId 映射到招式、且招式允许从当前
///       待机状态发动 → 消费并进入 Startup；否则该结果<b>保留在缓冲中</b>到有效期
///       结束（失败不消费、不破坏其他结果）。每帧至多消费一条、发动一个动作。
///       Airborne / PreJump 不消费（本阶段无空中招式；缓冲保留到有效期，
///       落地后的第一个地面帧恢复消费——D69 明确语义并测试固定）。</item>
/// </list>
///
/// <para>
/// <b>不允许的状态跳转</b>：转换边唯一（待机 ↔ 蹲；待机 → PreJump → Airborne →
/// 落地 → 待机；待机 → Startup → Active → Recovery → 待机）。落地边在运动阶段
/// （D69）——Tick 对 Airborne 原样保持，由 <c>BattleSimulation</c> 的积分触地后置为待机。
/// 非法输入（未配置的 MoveId 等）立即抛出而非静默修正。所有输入均为不可变数据，
/// 同初态同输入必得同结果。</para>
/// </summary>
public static class FighterStateMachine
{
    /// <summary>
    /// 推进一帧动作状态。不修改任何传入对象；返回新的状态与识别快照。
    /// </summary>
    /// <param name="state">帧开始时的角色逻辑状态。</param>
    /// <param name="moveId">帧开始时进行中的招式（待机为 null）。</param>
    /// <param name="phaseRemainingFrames">帧开始时当前阶段剩余帧数（待机为 0）。</param>
    /// <param name="resolvedInput">本帧按当帧朝向解析后的相对输入（SOCD 已处理）。</param>
    /// <param name="recognition">本帧识别推进<b>之后</b>的识别状态（含本帧新识别结果）。</param>
    /// <param name="definition">该角色的配置（指令表 + 招式表 + 运动数据）。</param>
    /// <param name="frame">本逻辑帧号（诊断与异常信息用）。</param>
    /// <param name="upHeldLastFrame">上一逻辑帧解析后的「上」电平（D67 起跳边沿判定；
    /// 默认 false——首次观察时「按住上」即视为新按下）。</param>
    /// <param name="stunRemainingFrames">帧开始时的硬直剩余帧数（Hitstun/Blockstun 状态使用，
    /// 其他状态为 0；阶段 06B，D78）。</param>
    /// <param name="moveStartedFrame">帧开始时的攻击发动帧（动作实例身份，阶段 06C，D81；
    /// 非动作状态为 -1。动作阶段间携带，消费同帧赋值为当帧号）。</param>
    public static FighterActionTick Tick(
        FighterLogicState state,
        string? moveId,
        int phaseRemainingFrames,
        RelativeInput resolvedInput,
        RecognitionState recognition,
        FighterDefinition definition,
        int frame,
        bool upHeldLastFrame = false,
        int stunRemainingFrames = 0,
        int moveStartedFrame = -1)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(definition);

        // 入口合法性校验（D62）：直接调用 Tick 也必须拒绝未定义状态——
        // 不允许未知枚举被当作待机「修正」，也不依赖 BattleState 构造代为校验。
        // Enum.IsDefined 以枚举定义本身为唯一事实来源（与 FighterState.Validate 一致）。
        if (!Enum.IsDefined(state))
        {
            throw new InvalidOperationException(
                $"帧 {frame}：未定义的角色逻辑状态 {state}（{(int)state}）不能推进。");
        }

        // 1) 动作阶段推进。
        if (IsInMove(state))
        {
            if (moveId is null)
                throw new InvalidOperationException(
                    $"帧 {frame}：角色 {definition.FighterId} 处于 {state} 但没有 MoveId（状态损坏）。");
            MoveDefinition move = definition.FindMoveById(moveId)
                ?? throw new InvalidOperationException(
                    $"帧 {frame}：角色 {definition.FighterId} 携带了未配置的招式 {moveId}。");

            int remaining = phaseRemainingFrames - 1;
            if (remaining > 0)
                return new FighterActionTick(state, moveId, remaining, recognition, ConsumedMatch: null,
                    MoveStartedFrame: moveStartedFrame);

            switch (state)
            {
                case FighterLogicState.Startup:
                    return new FighterActionTick(FighterLogicState.Active, moveId,
                        move.ActiveFrames, recognition, ConsumedMatch: null,
                        MoveStartedFrame: moveStartedFrame);
                case FighterLogicState.Active:
                    return new FighterActionTick(FighterLogicState.Recovery, moveId,
                        move.RecoveryFrames, recognition, ConsumedMatch: null,
                        MoveStartedFrame: moveStartedFrame);
                case FighterLogicState.Recovery:
                    // 收招完成 → 回到待机；同帧继续走待机更新、起跳与消费（B5）。
                    break;
            }
        }
        else if (state == FighterLogicState.PreJump)
        {
            // 2) 起跳预备推进（D67）：剩余 −1，归零同帧起跳（D68）。
            //    跳跃类型与水平方向都按「本帧」（预备窗口末帧）的输入锁定——
            //    不预知未来输入，也不回溯修改已提交状态。
            int remaining = phaseRemainingFrames - 1;
            if (remaining > 0)
                return new FighterActionTick(FighterLogicState.PreJump, null, remaining,
                    recognition, ConsumedMatch: null);

            JumpKind kind = resolvedInput.Up ? JumpKind.HighJump : JumpKind.ShortHop;
            FixedPoint horizontal = resolvedInput.Forward == resolvedInput.Back
                ? FixedPoint.Zero
                : (resolvedInput.Forward ? 1 : -1) * definition.Movement.HorizontalJumpSpeed;
            FixedPoint vertical = kind == JumpKind.HighJump
                ? definition.Movement.HighJumpInitialVelocity
                : definition.Movement.ShortHopInitialVelocity;

            return new FighterActionTick(FighterLogicState.Airborne, null, 0,
                recognition, ConsumedMatch: null,
                Launch: new JumpLaunch(kind, horizontal, vertical));
        }
        else if (state == FighterLogicState.Airborne)
        {
            // 空中：状态机原样保持（重力积分与落地在运动阶段完成，D69）。
            // 不消费指令、不做待机更新、不响应「上」（空中无跳，B5）。
            return new FighterActionTick(FighterLogicState.Airborne, null, 0,
                recognition, ConsumedMatch: null);
        }
        else if (state is FighterLogicState.Hitstun or FighterLogicState.Blockstun)
        {
            // 硬直（D78）：剩余 −1；归零同帧落到待机更新与消费（与 Recovery 完成同语义）。
            // 期间不步行、不响应「上」边沿、不消费指令（下方 return 拦截）。
            if (stunRemainingFrames < 1)
                throw new InvalidOperationException(
                    $"帧 {frame}：角色 {definition.FighterId} 处于 {state} 但硬直剩余帧数为 " +
                    $"{stunRemainingFrames}（状态损坏，不允许静默修正为待机）。");

            int stunRemaining = stunRemainingFrames - 1;
            if (stunRemaining > 0)
                return new FighterActionTick(state, null, 0, recognition, ConsumedMatch: null,
                    StunRemainingFrames: stunRemaining);
            // 归零：继续执行待机更新与消费（无独立 return）。
        }

        // 3) 待机更新（B9）：起跳边沿优先于蹲下切换。
        bool jumpRequested = resolvedInput.Up && !upHeldLastFrame;
        if (jumpRequested)
        {
            return new FighterActionTick(FighterLogicState.PreJump, null,
                definition.Movement.PreJumpFrames, recognition, ConsumedMatch: null);
        }

        FighterLogicState idle = resolvedInput.Down
            ? FighterLogicState.Crouching
            : FighterLogicState.Standing;

        // 4) 指令消费（D60）：缓冲顺序即选择顺序（识别帧升序，确定性）。
        foreach (BufferedResult result in recognition.Buffer)
        {
            if (result.Consumed)
                continue;

            CommandMatch match = result.Match;
            MoveDefinition? move = definition.FindMove(match.CommandId);
            if (move is null)
                continue; // 该指令不触发本角色的任何招式 → 保留在缓冲中。

            MoveAllowedStates required = idle == FighterLogicState.Crouching
                ? MoveAllowedStates.Crouching
                : MoveAllowedStates.Standing;
            if ((move.AllowedFrom & required) == 0)
                continue; // 当前待机状态不允许发动 → 保留，恢复可行动后再消费（B5）。

            // 消费一次（不可变替换），进入 Startup；攻击实例身份 = 当帧发动帧（D81）。
            return new FighterActionTick(
                FighterLogicState.Startup,
                move.MoveId,
                move.StartupFrames,
                recognition.WithResultConsumed(match.RecognizedFrame),
                match,
                MoveStartedFrame: frame);
        }

        return new FighterActionTick(idle, MoveId: null, 0, recognition, ConsumedMatch: null);
    }

    /// <summary>是否处于招式阶段（Startup/Active/Recovery）。</summary>
    private static bool IsInMove(FighterLogicState state) =>
        state is FighterLogicState.Startup or FighterLogicState.Active or FighterLogicState.Recovery;
}
