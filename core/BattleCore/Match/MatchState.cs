using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Match;

/// <summary>比赛阶段（阶段 09A，D105）。</summary>
public enum MatchPhase
{
    /// <summary>尚未开始（选人阶段；没有进行中的回合同话）。</summary>
    NotStarted = 0,

    /// <summary>当前回合进行中（唯一允许推进战斗逻辑帧的阶段）。</summary>
    RoundInProgress = 1,

    /// <summary>回合已结束、下一回合尚未开始（过渡；不推进战斗帧）。</summary>
    BetweenRounds = 2,

    /// <summary>比赛已结束（不允许再启动新回合、不允许重复计分）。</summary>
    MatchFinished = 3,
}

/// <summary>整场比赛的结果（阶段 09A，D105）。</summary>
public enum MatchOutcome
{
    /// <summary>尚无结果（仅 NotStarted / RoundInProgress / BetweenRounds 合法）。</summary>
    None = 0,

    /// <summary>P1 赢得比赛。</summary>
    P1Win = 1,

    /// <summary>P2 赢得比赛。</summary>
    P2Win = 2,

    /// <summary>比赛平局（达到最大回合数且累计胜场相同）。</summary>
    Draw = 3,
}

/// <summary>
/// 权威<b>比赛状态</b>（阶段 09A，D105）——独立于 <see cref="BattleState"/> 的比赛级快照。
///
/// <para><b>职责分层</b>：<see cref="RoundState"/> 只回答「这一回合谁赢、为什么结束」；
/// 本类型回答「打到第几回合、各赢几场、整场比赛谁赢、还剩多少时间」。
/// 比赛层读取已结束回合的 <see cref="RoundOutcome"/> 完成<b>一次</b>计分转换，
/// Godot 只做显示，绝不自行统计胜场。</para>
///
/// <para><b>不变式（Validate 固定）</b>：枚举全部已定义；胜场非负且不超过已结束回合数；
/// 双方胜场之和不超过已结束回合数（平局不计分也不凭空产生胜场）；已结束回合数 ≤ MaxRounds；
/// RoundNumber 与 RoundsPlayed 的阶段关系固定；MatchFinished ⇔ Outcome ≠ None；
/// 只有 RoundInProgress 才允许「当前回合无结果」；选人身份在开赛后必须存在。</para>
///
/// <para>不可变：私有构造 + 纯函数转换；所有集合/配置引用均为不可变对象。</para>
/// </summary>
public sealed record MatchState
{
    private MatchState(
        MatchRules rules,
        MatchPhase phase,
        int roundNumber,
        int roundsPlayed,
        int p1Wins,
        int p2Wins,
        RoundOutcome currentRoundOutcome,
        RoundEndReason currentRoundReason,
        MatchOutcome outcome,
        int remainingTimeFrames,
        string? p1FighterId,
        string? p2FighterId)
    {
        Rules = rules;
        Phase = phase;
        RoundNumber = roundNumber;
        RoundsPlayed = roundsPlayed;
        P1Wins = p1Wins;
        P2Wins = p2Wins;
        CurrentRoundOutcome = currentRoundOutcome;
        CurrentRoundReason = currentRoundReason;
        Outcome = outcome;
        RemainingTimeFrames = remainingTimeFrames;
        P1FighterId = p1FighterId;
        P2FighterId = p2FighterId;
    }

    /// <summary>本场比赛的规则（不可变；重开比赛时保留）。</summary>
    public MatchRules Rules { get; }

    /// <summary>当前比赛阶段。</summary>
    public MatchPhase Phase { get; }

    /// <summary>当前回合序号（1 起始；NotStarted 为 0）。</summary>
    public int RoundNumber { get; }

    /// <summary>已经结束并计分的回合数（含 DoubleKO / 平局）。</summary>
    public int RoundsPlayed { get; }

    /// <summary>P1 已获得的回合胜场。</summary>
    public int P1Wins { get; }

    /// <summary>P2 已获得的回合胜场。</summary>
    public int P2Wins { get; }

    /// <summary>最近一个已结束回合的结果（进行中为 None）。</summary>
    public RoundOutcome CurrentRoundOutcome { get; }

    /// <summary>最近一个已结束回合的结束原因（进行中为 None）。</summary>
    public RoundEndReason CurrentRoundReason { get; }

    /// <summary>整场比赛结果（未结束时为 None）。</summary>
    public MatchOutcome Outcome { get; }

    /// <summary>当前回合剩余逻辑帧；未启用计时恒为 0。</summary>
    public int RemainingTimeFrames { get; }

    /// <summary>P1 选中的角色 ID（权威配置标识；NotStarted 可为 null）。</summary>
    public string? P1FighterId { get; }

    /// <summary>P2 选中的角色 ID。</summary>
    public string? P2FighterId { get; }

    /// <summary>是否处于允许推进战斗逻辑帧的阶段。</summary>
    public bool IsRoundLive => Phase == MatchPhase.RoundInProgress;

    /// <summary>是否还可以开始下一回合（MatchFinished 明确禁止）。</summary>
    public bool CanStartRound => Phase is MatchPhase.NotStarted or MatchPhase.BetweenRounds;

    /// <summary>创建「未开始」状态（可附带已选角色）。</summary>
    public static MatchState NotStarted(
        MatchRules rules, string? p1FighterId = null, string? p2FighterId = null) =>
        new(rules, MatchPhase.NotStarted, 0, 0, 0, 0,
            RoundOutcome.None, RoundEndReason.None, MatchOutcome.None, 0,
            p1FighterId, p2FighterId);

    /// <summary>选择/更换双方角色。仅 NotStarted 允许；不会清零比分以外的任何比赛信息。</summary>
    public MatchState WithSelection(string p1FighterId, string p2FighterId)
    {
        EnsurePhase(MatchPhase.NotStarted, nameof(WithSelection));
        if (string.IsNullOrWhiteSpace(p1FighterId) || string.IsNullOrWhiteSpace(p2FighterId))
            throw new ArgumentException("角色 ID 不能为空。", nameof(p1FighterId));

        return NotStarted(Rules, p1FighterId, p2FighterId);
    }

    /// <summary>
    /// 开始一个新回合（NotStarted / BetweenRounds 允许）。<b>不重置</b>规则、胜场、
    /// 已结束回合数与选人；只重置「当前回合结果」和计时器，并把回合序号推进到
    /// <c>RoundsPlayed + 1</c>。
    /// </summary>
    public MatchState StartRound()
    {
        if (!CanStartRound)
            throw new InvalidOperationException(
                $"比赛阶段 {Phase} 不允许启动新回合（MatchFinished 后不得再开回合）。");

        if (Phase != MatchPhase.NotStarted
            && (string.IsNullOrWhiteSpace(P1FighterId) || string.IsNullOrWhiteSpace(P2FighterId)))
            throw new InvalidOperationException("开赛后必须携带双方选人身份。");

        return new MatchState(
            Rules, MatchPhase.RoundInProgress,
            roundNumber: RoundsPlayed + 1,
            roundsPlayed: RoundsPlayed,
            p1Wins: P1Wins, p2Wins: P2Wins,
            RoundOutcome.None, RoundEndReason.None, MatchOutcome.None,
            Rules.RoundTimerFrames, P1FighterId, P2FighterId);
    }

    /// <summary>
    /// 记录<b>一次</b>已结束回合的结果并累计胜场（D105 的唯一计分入口）。
    /// P1Win → P1 +1；P2Win → P2 +1；DoubleKO / Draw 均不加胜场但计入已进行回合数。
    /// 先达到 RoundsToWin 立即结束比赛；达到 MaxRounds 时按累计胜场比较，相同则 MatchDraw。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前不是进行中的回合，或结果非法。</exception>
    public MatchState RecordRoundResult(RoundOutcome roundOutcome, RoundEndReason reason)
    {
        EnsurePhase(MatchPhase.RoundInProgress, nameof(RecordRoundResult));
        if (!Enum.IsDefined(roundOutcome) || roundOutcome == RoundOutcome.None)
            throw new ArgumentOutOfRangeException(nameof(roundOutcome), roundOutcome,
                "回合结果必须是已结束回合的确定结果。");
        if (!Enum.IsDefined(reason) || reason == RoundEndReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason), reason,
                "已结束回合必须给出结束原因。");

        int p1 = P1Wins + (roundOutcome == RoundOutcome.P1Win ? 1 : 0);
        int p2 = P2Wins + (roundOutcome == RoundOutcome.P2Win ? 1 : 0);
        int played = RoundsPlayed + 1;

        MatchOutcome matchOutcome = MatchOutcome.None;
        MatchPhase nextPhase = MatchPhase.BetweenRounds;

        if (p1 >= Rules.RoundsToWin)
        {
            matchOutcome = MatchOutcome.P1Win;
            nextPhase = MatchPhase.MatchFinished;
        }
        else if (p2 >= Rules.RoundsToWin)
        {
            matchOutcome = MatchOutcome.P2Win;
            nextPhase = MatchPhase.MatchFinished;
        }
        else if (played >= Rules.MaxRounds)
        {
            matchOutcome = p1 > p2 ? MatchOutcome.P1Win
                : p2 > p1 ? MatchOutcome.P2Win
                : MatchOutcome.Draw;
            nextPhase = MatchPhase.MatchFinished;
        }

        return new MatchState(
            Rules, nextPhase,
            roundNumber: RoundNumber,
            roundsPlayed: played,
            p1Wins: p1, p2Wins: p2,
            roundOutcome, reason, matchOutcome,
            RemainingTimeFrames, P1FighterId, P2FighterId);
    }

    /// <summary>
    /// 同步当前回合剩余逻辑帧（09A 由比赛层按已提交帧数递减；09B 起由
    /// <c>BattleState.RoundTimerRemainingFrames</c> 权威字段投影）。仅进行中回合有效。
    /// </summary>
    public MatchState WithRemainingTime(int frames)
    {
        if (!IsRoundLive)
            return this;
        if (frames == RemainingTimeFrames)
            return this;
        return new MatchState(
            Rules, Phase, RoundNumber, RoundsPlayed, P1Wins, P2Wins,
            CurrentRoundOutcome, CurrentRoundReason, Outcome,
            frames, P1FighterId, P2FighterId);
    }

    /// <summary>重新开始整场比赛：保留规则与选人，清零比分与回合序号（回到 NotStarted）。</summary>
    public MatchState Restart() => NotStarted(Rules, P1FighterId, P2FighterId);

    /// <summary>返回选人：保留规则，清空选人与比分。</summary>
    public MatchState ReturnToSelection() => NotStarted(Rules);

    /// <summary>校验全部不变式。</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Rules);

        if (!Enum.IsDefined(Phase))
            throw new InvalidOperationException($"比赛阶段是未定义枚举值：{Phase}（{(int)Phase}）。");
        if (!Enum.IsDefined(Outcome))
            throw new InvalidOperationException($"比赛结果是未定义枚举值：{Outcome}（{(int)Outcome}）。");
        if (!Enum.IsDefined(CurrentRoundOutcome))
            throw new InvalidOperationException(
                $"当前回合结果是未定义枚举值：{CurrentRoundOutcome}（{(int)CurrentRoundOutcome}）。");
        if (!Enum.IsDefined(CurrentRoundReason))
            throw new InvalidOperationException(
                $"回合结束原因是未定义枚举值：{CurrentRoundReason}（{(int)CurrentRoundReason}）。");

        if (P1Wins < 0 || P2Wins < 0)
            throw new InvalidOperationException($"胜场不能为负：({P1Wins}, {P2Wins})。");
        if (P1Wins > Rules.RoundsToWin || P2Wins > Rules.RoundsToWin)
            throw new InvalidOperationException(
                $"胜场 ({P1Wins}, {P2Wins}) 超过夺冠所需 {Rules.RoundsToWin}（先胜即结束）。");
        if (RoundsPlayed < 0 || RoundsPlayed > Rules.MaxRounds)
            throw new InvalidOperationException(
                $"已结束回合数 {RoundsPlayed} 越界（0..{Rules.MaxRounds}）。");
        if (P1Wins + P2Wins > RoundsPlayed)
            throw new InvalidOperationException(
                $"双方胜场之和 {P1Wins + P2Wins} 超过已结束回合数 {RoundsPlayed}（平局不能凭空产生胜场）。");

        if (RemainingTimeFrames < 0 || RemainingTimeFrames > Rules.RoundTimerFrames)
            throw new InvalidOperationException(
                $"剩余时间 {RemainingTimeFrames} 越界（0..{Rules.RoundTimerFrames}）。");

        switch (Phase)
        {
            case MatchPhase.NotStarted:
                if (RoundNumber != 0 || RoundsPlayed != 0 || P1Wins != 0 || P2Wins != 0)
                    throw new InvalidOperationException("未开始的比赛不能携带回合序号、已结束回合或胜场。");
                if (Outcome != MatchOutcome.None || CurrentRoundOutcome != RoundOutcome.None
                    || CurrentRoundReason != RoundEndReason.None)
                    throw new InvalidOperationException("未开始的比赛不能携带任何结果。");
                if (RemainingTimeFrames != 0)
                    throw new InvalidOperationException("未开始的比赛不能携带剩余时间。");
                break;

            case MatchPhase.RoundInProgress:
                if (RoundNumber != RoundsPlayed + 1)
                    throw new InvalidOperationException(
                        $"进行中回合序号 {RoundNumber} 必须等于已结束回合数 + 1（{RoundsPlayed + 1}）。");
                if (CurrentRoundOutcome != RoundOutcome.None || CurrentRoundReason != RoundEndReason.None)
                    throw new InvalidOperationException("进行中回合不能携带上一回合结果作为当前结果。");
                if (Outcome != MatchOutcome.None)
                    throw new InvalidOperationException("进行中的比赛不能已经决出胜负。");
                EnsureSelection();
                break;

            case MatchPhase.BetweenRounds:
                if (RoundNumber != RoundsPlayed)
                    throw new InvalidOperationException(
                        $"过渡阶段回合序号 {RoundNumber} 必须等于已结束回合数 {RoundsPlayed}。");
                if (CurrentRoundOutcome == RoundOutcome.None || CurrentRoundReason == RoundEndReason.None)
                    throw new InvalidOperationException("过渡阶段必须携带刚结束回合的结果与原因。");
                if (Outcome != MatchOutcome.None)
                    throw new InvalidOperationException("过渡阶段不能已经决出比赛胜负。");
                EnsureSelection();
                break;

            case MatchPhase.MatchFinished:
                if (RoundNumber != RoundsPlayed)
                    throw new InvalidOperationException(
                        $"终局回合序号 {RoundNumber} 必须等于已结束回合数 {RoundsPlayed}。");
                if (CurrentRoundOutcome == RoundOutcome.None || CurrentRoundReason == RoundEndReason.None)
                    throw new InvalidOperationException("比赛终局必须携带最后回合的结果与原因。");
                if (Outcome == MatchOutcome.None)
                    throw new InvalidOperationException("比赛终局必须给出比赛结果。");
                EnsureSelection();
                break;
        }
    }

    private void EnsurePhase(MatchPhase expected, string operation)
    {
        if (Phase != expected)
            throw new InvalidOperationException(
                $"{operation} 只允许在 {expected} 阶段调用，当前是 {Phase}。");
    }

    private void EnsureSelection()
    {
        if (string.IsNullOrWhiteSpace(P1FighterId) || string.IsNullOrWhiteSpace(P2FighterId))
            throw new InvalidOperationException("开赛后必须携带双方选人身份。");
    }

    /// <summary>调试文本。</summary>
    public override string ToString() =>
        $"{Phase}/R{RoundNumber}/played={RoundsPlayed}/wins={P1Wins}:{P2Wins}/" +
        $"round={CurrentRoundOutcome}({CurrentRoundReason})/match={Outcome}/time={RemainingTimeFrames}";
}
