using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一帧内单名角色的动作推进结果（不可变）。
/// </summary>
/// <param name="State">新的角色逻辑状态。</param>
/// <param name="MoveId">进行中的招式（待机为 null）。</param>
/// <param name="PhaseRemainingFrames">当前阶段剩余帧数（待机为 0；动作期间恒 ≥ 1）。</param>
/// <param name="Recognition">应用了消费标记后的识别状态（纯函数结果）。</param>
/// <param name="ConsumedMatch">本帧被消费的识别结果（无消费为 null；诊断/测试用）。</param>
public readonly record struct FighterActionTick(
    FighterLogicState State,
    string? MoveId,
    int PhaseRemainingFrames,
    RecognitionState Recognition,
    CommandMatch? ConsumedMatch);

/// <summary>
/// 角色<b>动作状态机</b>（阶段 04）：集中、显式、纯函数的状态转换。
///
/// <para>
/// <b>每帧推进顺序（<see cref="Tick"/>，整数逻辑帧）：</b></para>
/// <list type="number">
/// <item><b>动作阶段推进</b>：若处于 Startup/Active/Recovery，剩余帧数 −1；
///       归零时按招式数据切换到下一阶段（Startup → Active → Recovery）；
///       Recovery 结束回到待机——<b>同帧</b>即可进行待机更新与消费（B5）。</item>
/// <item><b>待机更新（B9）</b>：仅待机状态下，按当帧解析输入切换
///       Standing ↔ Crouching（「下」按住且「上」未按 → 蹲；SOCD 已由
///       <c>DirectionResolver</c> 处理）。动作期间<b>不</b>被蹲下输入覆盖。</item>
/// <item><b>指令消费（D60）</b>：按缓冲顺序（识别帧升序，每帧至多一条胜者）取
///       第一条未消费的有效结果；若其 CommandId 映射到招式、且招式允许从当前
///       待机状态发动 → 消费并进入 Startup；否则该结果<b>保留在缓冲中</b>到有效期
///       结束（失败不消费、不破坏其他结果）。每帧至多消费一条、发动一个动作。</item>
/// </list>
///
/// <para>
/// <b>不允许的状态跳转</b>：转换边只有「待机 ↔ 蹲」「待机 → Startup →
/// Active → Recovery → 待机」，由本函数唯一产生；非法输入（如未配置的 MoveId）
/// 立即抛出而非静默修正。所有输入（状态、识别缓冲、招式配置）均为不可变数据，
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
    /// <param name="definition">该角色的配置（指令表 + 招式表）。</param>
    /// <param name="frame">本逻辑帧号（诊断与异常信息用）。</param>
    public static FighterActionTick Tick(
        FighterLogicState state,
        string? moveId,
        int phaseRemainingFrames,
        RelativeInput resolvedInput,
        RecognitionState recognition,
        FighterDefinition definition,
        int frame)
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
                return new FighterActionTick(state, moveId, remaining, recognition, ConsumedMatch: null);

            switch (state)
            {
                case FighterLogicState.Startup:
                    return new FighterActionTick(FighterLogicState.Active, moveId,
                        move.ActiveFrames, recognition, ConsumedMatch: null);
                case FighterLogicState.Active:
                    return new FighterActionTick(FighterLogicState.Recovery, moveId,
                        move.RecoveryFrames, recognition, ConsumedMatch: null);
                case FighterLogicState.Recovery:
                    // 收招完成 → 回到待机；同帧继续走待机更新与消费（B5）。
                    break;
            }
        }

        // 2) 待机更新：下方向进入蹲下，否则站立（动作期间不会走到这里——B9）。
        FighterLogicState idle = resolvedInput.Down && !resolvedInput.Up
            ? FighterLogicState.Crouching
            : FighterLogicState.Standing;

        // 3) 指令消费（D60）：缓冲顺序即选择顺序（识别帧升序，确定性）。
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

            // 消费一次（不可变替换），进入 Startup。
            return new FighterActionTick(
                FighterLogicState.Startup,
                move.MoveId,
                move.StartupFrames,
                recognition.WithResultConsumed(match.RecognizedFrame),
                match);
        }

        return new FighterActionTick(idle, MoveId: null, 0, recognition, ConsumedMatch: null);
    }

    /// <summary>是否处于招式阶段（Startup/Active/Recovery）。</summary>
    private static bool IsInMove(FighterLogicState state) =>
        state is FighterLogicState.Startup or FighterLogicState.Active or FighterLogicState.Recovery;
}
