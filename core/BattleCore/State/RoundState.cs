namespace NewKOF.BattleCore.State;

/// <summary>回合阶段（阶段 07A，D89）。</summary>
public enum RoundPhase
{
    /// <summary>进行中（唯一可发生战斗判定的阶段）。</summary>
    InProgress = 0,

    /// <summary>已结束（终局冻结：不再产生命中、伤害或事件）。</summary>
    Finished = 1,
}

/// <summary>回合结果（阶段 07A，D89；09B 扩展 Draw）。</summary>
public enum RoundOutcome
{
    /// <summary>尚无结果（仅 <see cref="RoundPhase.InProgress"/> 合法）。</summary>
    None = 0,

    /// <summary>P1 获胜（P2 体力归零，P1 未归零）。</summary>
    P1Win = 1,

    /// <summary>P2 获胜（P1 体力归零，P2 未归零）。</summary>
    P2Win = 2,

    /// <summary>双方同帧体力归零。</summary>
    DoubleKO = 3,

    /// <summary>
    /// 回合平局（阶段 09B，D109）：时间耗尽且双方剩余体力比例完全相同。
    /// <b>只由 Time Over 裁决产生</b>，不与 DoubleKO 混用。
    /// </summary>
    Draw = 4,
}

/// <summary>
/// 权威<b>回合状态</b>（阶段 07A，D89）——属于 <see cref="BattleState"/> 快照的一部分。
///
/// <para><b>不变式</b>：</para>
/// <list type="bullet">
/// <item><see cref="RoundPhase.InProgress"/> ⇔ <see cref="Outcome"/> == <see cref="RoundOutcome.None"/>
///       且 <see cref="WinnerFighterId"/> == null 且 <see cref="Reason"/> == None；</item>
/// <item><see cref="RoundPhase.Finished"/> ⇔ Outcome 非 None；DoubleKO / Draw 无胜者（null），
///       P1Win/P2Win 的胜者必须是 0/1 且与结果一致；</item>
/// <item>已结束的回合不得再被判定：<see cref="Finish"/> 只允许 InProgress → Finished 一次；</item>
/// <item>09B：<see cref="Reason"/> 与 Outcome 必须自洽（KO 胜利 ↔ Knockout、DoubleKO ↔ DoubleKO、
///       Time Over 胜利 ↔ TimeOver、Draw ↔ TimeOverDraw）；</item>
/// <item>枚举值必须是已定义值（与 D62 同一模式，新增枚举值不能绕过校验）；</item>
/// <item>不可变：所有字段只读，只能通过工厂方法/纯函数获得新实例。</item>
/// </list>
///
/// <para><b>胜负只由最终体力决定</b>：<see cref="FromFinalHealth"/> 是 KO 的唯一判定入口，
/// 输入是本帧<b>全部结算完成之后</b>的双方体力——不存在「先结算 P1 就先赢」的路径（D80 延伸）。
/// 09B 的 Time Over 裁决走 <see cref="ForOutcome(RoundOutcome, RoundEndReason)"/>，
/// 不会伪装成 Knockout 事件。</para>
/// </summary>
public sealed record RoundState
{
    private RoundState(RoundPhase phase, RoundOutcome outcome, int? winnerFighterId, RoundEndReason reason)
    {
        Phase = phase;
        Outcome = outcome;
        WinnerFighterId = winnerFighterId;
        Reason = reason;
    }

    /// <summary>进行中（默认初值；与 <c>new BattleState(...)</c> 缺省参数同一实例）。</summary>
    public static RoundState InProgress { get; } =
        new(RoundPhase.InProgress, RoundOutcome.None, null, RoundEndReason.None);

    /// <summary>回合阶段。</summary>
    public RoundPhase Phase { get; }

    /// <summary>回合结果（进行中为 <see cref="RoundOutcome.None"/>）。</summary>
    public RoundOutcome Outcome { get; }

    /// <summary>胜者角色 ID（0/1）；平局或进行中为 null。</summary>
    public int? WinnerFighterId { get; }

    /// <summary>回合结束原因（阶段 09B，D109）；进行中为 None。</summary>
    public RoundEndReason Reason { get; }

    /// <summary>回合是否已结束（终局冻结）。</summary>
    public bool IsFinished => Phase == RoundPhase.Finished;

    /// <summary>是否双方同帧归零。</summary>
    public bool IsDoubleKO => Outcome == RoundOutcome.DoubleKO;

    /// <summary>是否时间耗尽且双方体力比例完全相同的平局。</summary>
    public bool IsTimeOverDraw => Outcome == RoundOutcome.Draw;

    /// <summary>是否由时间耗尽裁决（含平局）。</summary>
    public bool IsTimeOver => Reason is RoundEndReason.TimeOver or RoundEndReason.TimeOverDraw;

    /// <summary>
    /// 由结果构造已结束回合（None 非法）。结束原因按结果推断：
    /// P1Win/P2Win → Knockout、DoubleKO → DoubleKO、Draw → TimeOverDraw。
    /// 阶段 07A 起的旧调用点保持逐位兼容。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">结果为 <see cref="RoundOutcome.None"/> 或未定义值。</exception>
    public static RoundState ForOutcome(RoundOutcome outcome) =>
        ForOutcome(outcome, DefaultReasonFor(outcome));

    /// <summary>
    /// 由结果与<b>显式结束原因</b>构造已结束回合（阶段 09B）。
    /// Time Over 胜利用 <c>ForOutcome(P1Win, RoundEndReason.TimeOver)</c>，
    /// 与 KO 胜利在结果上相同但原因不同——事件层据此不发送虚假的 Knockout。
    /// </summary>
    public static RoundState ForOutcome(RoundOutcome outcome, RoundEndReason reason)
    {
        if (!Enum.IsDefined(outcome) || outcome == RoundOutcome.None)
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome,
                "回合结果必须是 P1Win / P2Win / DoubleKO / Draw（None 只用于进行中的回合）。");
        if (!Enum.IsDefined(reason) || reason == RoundEndReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason), reason,
                "已结束回合必须给出确定的结束原因。");
        ValidateOutcomeReasonPair(outcome, reason);

        int? winner = outcome switch
        {
            RoundOutcome.P1Win => 0,
            RoundOutcome.P2Win => 1,
            _ => null,
        };
        return new RoundState(RoundPhase.Finished, outcome, winner, reason);
    }

    /// <summary>
    /// 依据<b>最终</b>体力判定 KO 结果（D89）。已结束的回合原样返回——不会二次判定、
    /// 也不会改写既有结果。双方归零 → DoubleKO（不存在先手优势）。
    /// <b>本方法不处理 Time Over</b>：计时裁决在 <c>BattleSimulation.Step</c> 中本方法之后、
    /// 且仅当回合仍进行中时才执行（KO 优先，D108）。
    /// </summary>
    /// <param name="current">当前回合状态。</param>
    /// <param name="p1Health">P1 结算后的最终体力（≥ 0）。</param>
    /// <param name="p2Health">P2 结算后的最终体力（≥ 0）。</param>
    public static RoundState FromFinalHealth(RoundState current, int p1Health, int p2Health)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.IsFinished)
            return current;

        bool p1Down = p1Health == 0;
        bool p2Down = p2Health == 0;

        if (!p1Down && !p2Down)
            return InProgress;
        if (p1Down && p2Down)
            return ForOutcome(RoundOutcome.DoubleKO);

        return ForOutcome(p1Down ? RoundOutcome.P2Win : RoundOutcome.P1Win);
    }

    /// <summary>
    /// 进入已结束状态（纯函数）。只允许 InProgress → Finished，且结果不得为 None。
    /// 结束原因按结果推断（旧调用点兼容）。
    /// </summary>
    /// <exception cref="InvalidOperationException">回合已结束。</exception>
    public RoundState Finish(RoundOutcome outcome) => Finish(outcome, DefaultReasonFor(outcome));

    /// <summary>带显式结束原因的 Finish（阶段 09B）。</summary>
    public RoundState Finish(RoundOutcome outcome, RoundEndReason reason)
    {
        if (IsFinished)
            throw new InvalidOperationException(
                $"回合已结束（{Outcome}），不能重新判定为 {outcome}。");
        if (outcome == RoundOutcome.None)
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "结束回合必须给出确定结果。");

        return ForOutcome(outcome, reason);
    }

    /// <summary>校验不变式（在 <see cref="BattleState"/> 构造期调用）。</summary>
    /// <exception cref="InvalidOperationException">阶段/结果/胜者/原因不自洽，或枚举未定义。</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(Phase))
            throw new InvalidOperationException($"回合阶段是未定义枚举值：{Phase}（{(int)Phase}）。");
        if (!Enum.IsDefined(Outcome))
            throw new InvalidOperationException($"回合结果是未定义枚举值：{Outcome}（{(int)Outcome}）。");
        if (!Enum.IsDefined(Reason))
            throw new InvalidOperationException($"回合结束原因是未定义枚举值：{Reason}（{(int)Reason}）。");

        if (Phase == RoundPhase.InProgress)
        {
            if (Outcome != RoundOutcome.None)
                throw new InvalidOperationException($"进行中的回合不能携带结果 {Outcome}。");
            if (WinnerFighterId is not null)
                throw new InvalidOperationException(
                    $"进行中的回合不能有胜者（收到 {WinnerFighterId}）。");
            if (Reason != RoundEndReason.None)
                throw new InvalidOperationException($"进行中的回合不能携带结束原因 {Reason}。");
            return;
        }

        // Finished
        if (Outcome == RoundOutcome.None)
            throw new InvalidOperationException("已结束的回合必须有确定结果（不能是 None）。");
        if (Reason == RoundEndReason.None)
            throw new InvalidOperationException("已结束的回合必须有结束原因。");
        ValidateOutcomeReasonPair(Outcome, Reason);

        int? expectedWinner = Outcome switch
        {
            RoundOutcome.P1Win => 0,
            RoundOutcome.P2Win => 1,
            _ => null,
        };
        if (WinnerFighterId != expectedWinner)
            throw new InvalidOperationException(
                $"回合结果 {Outcome} 要求胜者为 {expectedWinner?.ToString() ?? "null"}，" +
                $"实际 {WinnerFighterId?.ToString() ?? "null"}。");
    }

    /// <summary>调试文本（如 <c>Finished/P1Win/Winner=0/Knockout</c>）。</summary>
    public override string ToString() =>
        IsFinished
            ? $"Finished/{Outcome}/Winner={(WinnerFighterId?.ToString() ?? "null")}/{Reason}"
            : "InProgress";

    private static RoundEndReason DefaultReasonFor(RoundOutcome outcome) => outcome switch
    {
        RoundOutcome.P1Win or RoundOutcome.P2Win => RoundEndReason.Knockout,
        RoundOutcome.DoubleKO => RoundEndReason.DoubleKO,
        RoundOutcome.Draw => RoundEndReason.TimeOverDraw,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome,
            "回合结果必须是 P1Win / P2Win / DoubleKO / Draw。"),
    };

    /// <summary>
    /// 判定「回合结果 + 结束原因」组合是否<b>自洽</b>（D119 的唯一权威实现）。
    ///
    /// <para>合法组合只有六种：P1Win/P2Win × Knockout/TimeOver、DoubleKO + DoubleKO、
    /// Draw + TimeOverDraw。单回合层（<see cref="ForOutcome(RoundOutcome, RoundEndReason)"/>、
    /// <see cref="Validate"/>）与比赛层（<c>MatchState.RecordRoundResult</c>、
    /// <c>MatchState.Validate</c>）都必须调用同一份规则，避免两处 switch 日后分叉。</para>
    /// </summary>
    public static bool IsValidOutcomeReasonPair(RoundOutcome outcome, RoundEndReason reason) =>
        outcome switch
        {
            RoundOutcome.P1Win or RoundOutcome.P2Win =>
                reason is RoundEndReason.Knockout or RoundEndReason.TimeOver,
            RoundOutcome.DoubleKO => reason == RoundEndReason.DoubleKO,
            RoundOutcome.Draw => reason == RoundEndReason.TimeOverDraw,
            _ => false,
        };

    /// <summary>组合不自洽时抛出（与 <see cref="IsValidOutcomeReasonPair"/> 同一份规则）。</summary>
    /// <exception cref="InvalidOperationException">结果与原因不自洽。</exception>
    internal static void ValidateOutcomeReasonPair(RoundOutcome outcome, RoundEndReason reason)
    {
        if (!IsValidOutcomeReasonPair(outcome, reason))
            throw new InvalidOperationException(
                $"回合结果 {outcome} 与结束原因 {reason} 不自洽（Time Over 胜利不得伪装成 KO，平局不得伪装成 DoubleKO）。");
    }
}
