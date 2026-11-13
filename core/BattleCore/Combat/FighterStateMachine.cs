using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

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
    int MoveStartedFrame = -1,
    int MoveElapsedFrames = 0);

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
    /// <param name="hitConfirmFrame">本攻击实例最近一次有效命中的帧（阶段 07B，D91；-1 = 尚未命中）。</param>
    /// <param name="blockConfirmFrame">本攻击实例最近一次被防御的帧（阶段 07B，D91；-1 = 尚未被防御）。</param>
    /// <param name="landingWakeupFrames">倒地结束后要进入的起身帧数（阶段 07C，D93；0 = 直接站起）。</param>
    /// <param name="moveElapsedFrames">当前攻击实例的<b>动作有效经过帧数</b>（阶段 08A，D96）：
    /// 由调用方按「上一帧值 + 1」给出（发动/取消当帧为 0）。取消窗口用它而不是
    /// <c>frame − moveStartedFrame</c>，这样 Hitstop 不会让窗口提前过期。</param>
    /// <param name="opponentDistance">本逻辑帧开始时双方的水平中心距。</param>
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
        int moveStartedFrame = -1,
        int hitConfirmFrame = -1,
        int blockConfirmFrame = -1,
        int landingWakeupFrames = 0,
        int moveElapsedFrames = 0,
        FixedPoint opponentDistance = default,
        FixedPoint opponentBodyDistance = default)
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

            // 取消判定（阶段 07B，D91）：必须在<b>阶段推进之前</b>用帧开始状态判定——
            // 否则「Recovery 首帧」会被推进成「剩余帧减一」，窗口语义随实现漂移。
            // 取消窗口用<b>动作有效经过帧数</b>（D96）：Hitstop 冻结帧不推进它，
            // 因此停顿期间窗口不会悄悄过期（无 Hitstop 时它恒等于 frame − moveStartedFrame）。
            FighterActionTick? cancel = TryCancel(
                state, move, definition, recognition, resolvedInput, frame,
                moveStartedFrame, moveElapsedFrames, hitConfirmFrame, blockConfirmFrame,
                opponentDistance, opponentBodyDistance);
            if (cancel is FighterActionTick cancelled)
                return cancelled;

            int remaining = phaseRemainingFrames - 1;
            if (remaining > 0)
                return new FighterActionTick(state, moveId, remaining, recognition, ConsumedMatch: null,
                    MoveStartedFrame: moveStartedFrame, MoveElapsedFrames: moveElapsedFrames);

            switch (state)
            {
                case FighterLogicState.Startup:
                    return new FighterActionTick(FighterLogicState.Active, moveId,
                        move.ActiveFrames, recognition, ConsumedMatch: null,
                        MoveStartedFrame: moveStartedFrame, MoveElapsedFrames: moveElapsedFrames);
                case FighterLogicState.Active:
                    return new FighterActionTick(FighterLogicState.Recovery, moveId,
                        move.RecoveryFrames, recognition, ConsumedMatch: null,
                        MoveStartedFrame: moveStartedFrame, MoveElapsedFrames: moveElapsedFrames);
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
        else if (state == FighterLogicState.AirBlockstun)
        {
            if (stunRemainingFrames < 1) throw new InvalidOperationException("Invalid air blockstun timer.");
            int remaining = stunRemainingFrames - 1;
            return new FighterActionTick(remaining > 0 ? state : FighterLogicState.Airborne,
                null, 0, recognition, ConsumedMatch: null, StunRemainingFrames: remaining);
        }
        else if (state is FighterLogicState.Airborne or FighterLogicState.AirHitstun)
        {
            // 空中 / 空中受击：状态机原样保持（重力积分与落地在运动阶段完成，D69/D93）。
            // 不消费指令、不做待机更新、不响应「上」（空中无跳，B5）。
            return new FighterActionTick(state, null, 0, recognition, ConsumedMatch: null);
        }
        else if (state is FighterLogicState.Knockdown or FighterLogicState.Wakeup)
        {
            // 倒地 / 起身（D93）：只倒计时，不消费指令、不做待机更新、不能起跳。
            if (stunRemainingFrames < 1)
                throw new InvalidOperationException(
                    $"帧 {frame}：角色 {definition.FighterId} 处于 {state} 但剩余帧数为 " +
                    $"{stunRemainingFrames}（状态损坏，不允许静默修正为待机）。");

            int remaining = stunRemainingFrames - 1;
            if (remaining > 0)
                return new FighterActionTick(state, null, 0, recognition, ConsumedMatch: null,
                    StunRemainingFrames: remaining);

            // 倒地归零：有起身帧数 → 起身；否则同帧回到待机更新（可消费，D60）。
            if (state == FighterLogicState.Knockdown && landingWakeupFrames > 0)
            {
                return new FighterActionTick(FighterLogicState.Wakeup, null, 0,
                    recognition, ConsumedMatch: null,
                    StunRemainingFrames: landingWakeupFrames);
            }
            // 归零：继续执行待机更新与消费（无独立 return）。
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
            MoveAllowedStates required = idle == FighterLogicState.Crouching
                ? MoveAllowedStates.Crouching
                : MoveAllowedStates.Standing;
            bool eligiblePosture = false;
            foreach (MoveDefinition move in definition.FindMoves(match.CommandId))
            {
                if ((move.AllowedFrom & required) == 0)
                    continue;
                eligiblePosture = true;
                if (!move.CanStartAtDistance(opponentDistance, opponentBodyDistance))
                    continue;

                return new FighterActionTick(
                    FighterLogicState.Startup,
                    move.MoveId,
                    move.StartupFrames,
                    recognition.WithResultConsumed(match.RecognizedFrame),
                    match,
                    MoveStartedFrame: frame,
                    MoveElapsedFrames: 0);
            }

            // 距离未通过的输入不能等到距离变化时自动发动；姿态未通过仍沿用缓冲规则。
            if (eligiblePosture)
                recognition = recognition.WithResultConsumed(match.RecognizedFrame);
        }

        return new FighterActionTick(idle, MoveId: null, 0, recognition, ConsumedMatch: null);
    }

    /// <summary>
    /// 当前帧是否处于<b>任一取消窗口</b>内（只判窗口帧数与阶段，不含确认与指令缓冲条件）。
    /// 供表现层只读显示使用——与 <see cref="TryCancel"/> 共用同一个窗口谓词与同一个时间基准
    /// （<see cref="FighterState.MoveElapsedFrames"/>，D96），不产生第二套规则。
    /// </summary>
    public static bool IsInCancelWindow(FighterState fighter, FighterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!fighter.IsInMove || fighter.MoveId is null || fighter.MoveStartedFrame < 0)
            return false;

        MoveDefinition? move = definition.FindMoveById(fighter.MoveId);
        if (move is null || move.Cancels.Count == 0)
            return false;

        int elapsed = fighter.MoveElapsedFrames;
        foreach (CancelDefinition cancel in move.Cancels)
        {
            if (IsWindowOpen(cancel, fighter.LogicState, elapsed))
                return true;
        }

        return false;
    }

    /// <summary>取消窗口谓词（帧数 + 阶段；确认与缓冲另判）。</summary>
    private static bool IsWindowOpen(CancelDefinition cancel, FighterLogicState state, int elapsed) =>
        elapsed >= cancel.WindowFirstFrame
        && elapsed <= cancel.WindowLastFrame
        && cancel.AllowsPhase(state);

    /// <summary>
    /// 尝试取消（阶段 07B，D91）。返回 null 表示本帧不能取消（不动任何状态）。
    ///
    /// <para><b>判定顺序（全部满足才成立）</b>：① 来源招式配置了取消规则；
    /// ② 经过帧数在窗口内；③ 帧开始状态属于允许阶段（Active/Recovery）；
    /// ④ 确认条件满足（命中/防御确认必须来自<b>更早</b>帧的同一攻击实例，或规则允许空挥）；
    /// ⑤ 识别缓冲中存在<b>未消费</b>的、正好映射到目标招式的指令。</para>
    ///
    /// <para><b>选择顺序</b>：取消规则按配置顺序，缓冲按 D60 的识别顺序——
    /// 第一个满足全部条件的组合胜出并被消费，其余缓冲结果保持原样（不消费、不破坏）。
    /// 每帧至多取消一次（本函数返回后 Tick 立即结束），因此不存在同帧连锁取消。</para>
    /// </summary>
    private static FighterActionTick? TryCancel(
        FighterLogicState state,
        MoveDefinition move,
        FighterDefinition definition,
        RecognitionState recognition,
        RelativeInput input,
        int frame,
        int moveStartedFrame,
        int moveElapsedFrames,
        int hitConfirmFrame,
        int blockConfirmFrame,
        FixedPoint opponentDistance,
        FixedPoint opponentBodyDistance)
    {
        if (move.Cancels.Count == 0 || moveStartedFrame < 0)
            return null;

        // 窗口时间基准 = 动作有效经过帧数（D96），不是全局帧差。
        int elapsed = moveElapsedFrames;
        if (elapsed < 0)
            return null;

        foreach (CancelDefinition cancel in move.Cancels)
        {
            if (cancel.StandingInputOnly && input.Down)
                continue;
            if (!IsWindowOpen(cancel, state, elapsed))
                continue;
            if (!cancel.ConfirmSatisfied(frame, hitConfirmFrame, blockConfirmFrame, moveStartedFrame))
                continue;

            MoveDefinition? target = definition.FindMoveById(cancel.TargetMoveId);
            if (target is null || !target.CanStartAtDistance(opponentDistance, opponentBodyDistance))
                continue;

            foreach (BufferedResult result in recognition.Buffer)
            {
                if (result.Consumed)
                    continue;
                if (!string.Equals(result.Match.CommandId, target.CommandId, StringComparison.Ordinal))
                    continue;
                if (cancel.MaximumInputAge is { } age &&
                    (result.Match.ButtonFrame < 0 || frame - result.Match.ButtonFrame > age))
                    continue;

                // 取消成功：旧招式被中断，目标招式作为<b>新的独立攻击实例</b>发动
                // （MoveStartedFrame = 当帧，不继承旧实例的命中/防御确认与重复命中标记）。
                return new FighterActionTick(
                    FighterLogicState.Startup,
                    target.MoveId,
                    target.StartupFrames,
                    recognition.WithResultConsumed(result.Match.RecognizedFrame),
                    result.Match,
                    MoveStartedFrame: frame,
                    MoveElapsedFrames: 0);
            }
        }

        return null;
    }

    /// <summary>是否处于招式阶段（Startup/Active/Recovery）。</summary>
    private static bool IsInMove(FighterLogicState state) =>
        state is FighterLogicState.Startup or FighterLogicState.Active or FighterLogicState.Recovery;
}
