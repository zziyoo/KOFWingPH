using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Team;

/// <summary>队伍赛阶段（阶段 10A，D120）。</summary>
public enum TeamMatchPhase
{
    /// <summary>尚未开始（选队阶段）。</summary>
    NotStarted = 0,

    /// <summary>当前交锋（bout）进行中——唯一允许推进战斗逻辑帧的阶段。</summary>
    BoutInProgress = 1,

    /// <summary>交锋已结束、下一名队员尚未上场（过渡；不推进战斗帧）。</summary>
    BetweenBouts = 2,

    /// <summary>队伍赛已结束（一方或双方耗尽队员）。</summary>
    MatchFinished = 3,
}

/// <summary>队伍赛最终结果（阶段 10A，D120）。</summary>
public enum TeamMatchOutcome
{
    /// <summary>尚无结果。</summary>
    None = 0,

    /// <summary>P1 队伍获胜。</summary>
    P1TeamWin = 1,

    /// <summary>P2 队伍获胜。</summary>
    P2TeamWin = 2,

    /// <summary>双方同时耗尽队员。</summary>
    TeamDraw = 3,
}

/// <summary>
/// 单侧队伍的权威状态（阶段 10A，D120）。
///
/// <para><b>淘汰是独立状态，不是 <c>Health == 0</c></b>：Time Over 落败的一方可能仍有正体力，
/// 却必须被淘汰。因此 <see cref="Eliminated"/> 才是唯一的存活判据，
/// <see cref="Health"/> 只记录「该槽位最后一战的剩余体力」（用于 HUD 与体力继承）。</para>
///
/// <para><b>不变式</b>：槽位数组长度 == 队伍人数；<see cref="ActiveSlot"/> 在范围内；
/// 每槽体力在 <c>[0, MaxHealth]</c>；活跃槽及所有后位槽位不得已淘汰
/// （队员按顺序出战，前面的槽位全部淘汰后才会推进到当前槽位）。</para>
/// </summary>
public sealed class TeamSideState : IEquatable<TeamSideState>
{
    public TeamSideState(
        TeamDefinition team,
        int activeSlot,
        IReadOnlyList<bool> eliminated,
        IReadOnlyList<int> health)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(eliminated);
        ArgumentNullException.ThrowIfNull(health);

        if (eliminated.Count != team.Count || health.Count != team.Count)
            throw new ArgumentException(
                $"槽位数组长度必须等于队伍人数 {team.Count}（淘汰 {eliminated.Count} / 体力 {health.Count}）。");
        if (activeSlot < 0 || activeSlot >= team.Count)
            throw new ArgumentOutOfRangeException(nameof(activeSlot), activeSlot,
                $"出战槽位越界（0..{team.Count - 1}）。");

        var eliminatedCopy = new bool[team.Count];
        var healthCopy = new int[team.Count];
        for (int i = 0; i < team.Count; i++)
        {
            eliminatedCopy[i] = eliminated[i];
            int max = team.MemberAt(i).MaxHealth;
            if (health[i] < 0 || health[i] > max)
                throw new ArgumentOutOfRangeException(nameof(health), health[i],
                    $"槽位 {i} 的体力越界（0..{max}）。");
            healthCopy[i] = health[i];

            // 队员按顺序出战：活跃槽之前的槽位必须已淘汰，后位槽位不得已淘汰。
            // 活跃槽本身可为「已淘汰」——那正是全队耗尽的表示（保留最后一个槽位索引）。
            if (i < activeSlot && !eliminatedCopy[i])
                throw new ArgumentException($"槽位 {i} 在活跃槽 {activeSlot} 之前却未淘汰。", nameof(eliminated));
            if (i > activeSlot && eliminatedCopy[i])
                throw new ArgumentException($"槽位 {i} 不得在活跃槽 {activeSlot} 之后被淘汰。", nameof(eliminated));
            if (i == activeSlot && !eliminatedCopy[i] && healthCopy[i] < 1)
                throw new ArgumentException(
                    $"活跃槽 {activeSlot} 尚未淘汰，体力必须 ≥ 1（实际 {healthCopy[i]}）。", nameof(health));
        }

        Team = team;
        ActiveSlot = activeSlot;
        Eliminated = Array.AsReadOnly(eliminatedCopy);
        Health = Array.AsReadOnly(healthCopy);
    }

    /// <summary>队伍定义（不可变）。</summary>
    public TeamDefinition Team { get; }

    /// <summary>当前出战槽位。</summary>
    public int ActiveSlot { get; }

    /// <summary>每槽淘汰标记（只读）。这是**唯一**的存活判据。</summary>
    public IReadOnlyList<bool> Eliminated { get; }

    /// <summary>每槽剩余体力（只读）。已淘汰队员仍保留最后一战的实际值。</summary>
    public IReadOnlyList<int> Health { get; }

    /// <summary>当前出战角色 ID。</summary>
    public string ActiveFighterId => Team.FighterIds[ActiveSlot];

    /// <summary>当前出战角色的满血值。</summary>
    public int ActiveMaxHealth => Team.MemberAt(ActiveSlot).MaxHealth;

    /// <summary>当前出战角色的剩余体力。</summary>
    public int ActiveHealth => Health[ActiveSlot];

    /// <summary>尚未淘汰的队员数。</summary>
    public int RemainingCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Eliminated.Count; i++)
                if (!Eliminated[i]) count++;
            return count;
        }
    }

    /// <summary>队伍是否已耗尽（无人可出战）。</summary>
    public bool IsExhausted => RemainingCount == 0;

    /// <summary>全队满血、全员存活（开局状态）。</summary>
    public static TeamSideState Fresh(TeamDefinition team)
    {
        ArgumentNullException.ThrowIfNull(team);
        var eliminated = new bool[team.Count];
        var health = new int[team.Count];
        for (int i = 0; i < team.Count; i++)
            health[i] = team.MemberAt(i).MaxHealth;
        return new TeamSideState(team, 0, eliminated, health);
    }

    /// <summary>
    /// 记录本场交锋的结局：写入活跃槽的最终体力；若被淘汰则标记并推进到下一名存活队员。
    /// <b>体力只在未淘汰时被继承</b>；接替队员使用自己的满血值（初始即满血）。
    /// </summary>
    public TeamSideState ApplyBoutResult(int finalHealth, bool eliminated)
    {
        var eliminatedCopy = Eliminated.ToArray();
        var healthCopy = Health.ToArray();
        healthCopy[ActiveSlot] = Math.Clamp(finalHealth, 0, ActiveMaxHealth);

        if (!eliminated)
            return new TeamSideState(Team, ActiveSlot, eliminatedCopy, healthCopy);

        eliminatedCopy[ActiveSlot] = true;
        int next = -1;
        for (int i = ActiveSlot + 1; i < Team.Count; i++)
        {
            if (!eliminatedCopy[i])
            {
                next = i;
                break;
            }
        }

        return new TeamSideState(Team, next >= 0 ? next : ActiveSlot, eliminatedCopy, healthCopy);
    }

    public bool Equals(TeamSideState? other) =>
        other is not null
        && Team.Equals(other.Team)
        && ActiveSlot == other.ActiveSlot
        && Eliminated.SequenceEqual(other.Eliminated)
        && Health.SequenceEqual(other.Health);

    public override bool Equals(object? obj) => obj is TeamSideState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Team);
        hash.Add(ActiveSlot);
        foreach (bool flag in Eliminated) hash.Add(flag);
        foreach (int hp in Health) hash.Add(hp);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var parts = new List<string>(Team.Count);
        for (int i = 0; i < Team.Count; i++)
        {
            string tag = Eliminated[i] ? "X" : i == ActiveSlot ? ">" : "-";
            parts.Add($"{tag}{Team.MemberAt(i).FighterId}:{Health[i]}");
        }
        return string.Join(" ", parts);
    }
}

/// <summary>
/// <b>队伍赛权威状态</b>（阶段 10A，D120）——场上永远是 1v1，本状态只负责组织
/// 「多个连续的 1v1 交锋」：谁在场上、谁被淘汰、下一场谁上、还有多少体力、队伍谁赢。
///
/// <para><b>职责分离</b>：<see cref="RoundState"/> 回答单场交锋谁赢、为什么；
/// <see cref="TeamMatchState"/> 回答队伍层面的淘汰、接替与终局；
/// 战斗判定、伤害、推挤仍完全由 <c>BattleSimulation</c> 负责。</para>
///
/// <para>不可变：所有转换都是纯函数；集合在构造期复制并只读。</para>
/// </summary>
public sealed class TeamMatchState : IEquatable<TeamMatchState>
{
    private TeamMatchState(
        TeamMatchRules rules,
        TeamMatchPhase phase,
        TeamSideState? p1,
        TeamSideState? p2,
        int boutNumber,
        int boutsPlayed,
        int remainingTimeFrames,
        RoundOutcome lastBoutOutcome,
        RoundEndReason lastBoutReason,
        TeamMatchOutcome outcome)
    {
        Rules = rules;
        Phase = phase;
        P1 = p1;
        P2 = p2;
        BoutNumber = boutNumber;
        BoutsPlayed = boutsPlayed;
        RemainingTimeFrames = remainingTimeFrames;
        LastBoutOutcome = lastBoutOutcome;
        LastBoutReason = lastBoutReason;
        Outcome = outcome;
    }

    /// <summary>本场比赛规则（不可变；重开时保留）。</summary>
    public TeamMatchRules Rules { get; }

    /// <summary>当前阶段。</summary>
    public TeamMatchPhase Phase { get; }

    /// <summary>P1 队伍状态（NotStarted 可为 null）。</summary>
    public TeamSideState? P1 { get; }

    /// <summary>P2 队伍状态（NotStarted 可为 null）。</summary>
    public TeamSideState? P2 { get; }

    /// <summary>当前交锋序号（1 起始；NotStarted 为 0）。</summary>
    public int BoutNumber { get; }

    /// <summary>已经结束的交锋数。</summary>
    public int BoutsPlayed { get; }

    /// <summary>当前交锋剩余逻辑帧（未启用计时恒为 0）。</summary>
    public int RemainingTimeFrames { get; }

    /// <summary>上一场交锋的结果（进行中/未开始为 None）。</summary>
    public RoundOutcome LastBoutOutcome { get; }

    /// <summary>上一场交锋的结束原因（进行中/未开始为 None）。</summary>
    public RoundEndReason LastBoutReason { get; }

    /// <summary>队伍赛最终结果（未结束为 None）。</summary>
    public TeamMatchOutcome Outcome { get; }

    /// <summary>是否处于允许推进战斗逻辑帧的阶段。</summary>
    public bool IsBoutLive => Phase == TeamMatchPhase.BoutInProgress;

    /// <summary>是否还可以开始下一场交锋。</summary>
    public bool CanStartBout => Phase is TeamMatchPhase.NotStarted or TeamMatchPhase.BetweenBouts;

    /// <summary>创建「未开始」状态（可附带已选好的双方队伍）。</summary>
    public static TeamMatchState NotStarted(
        TeamMatchRules rules, TeamDefinition? p1Team = null, TeamDefinition? p2Team = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if ((p1Team is null) != (p2Team is null))
            throw new ArgumentException("未开始的队伍赛：双方队伍必须同时存在或同时为空。");

        return new TeamMatchState(
            rules, TeamMatchPhase.NotStarted,
            p1Team is null ? null : TeamSideState.Fresh(p1Team),
            p2Team is null ? null : TeamSideState.Fresh(p2Team),
            boutNumber: 0, boutsPlayed: 0, remainingTimeFrames: 0,
            RoundOutcome.None, RoundEndReason.None, TeamMatchOutcome.None);
    }

    /// <summary>选择/更换双方队伍（仅 NotStarted 允许）。</summary>
    public TeamMatchState WithTeams(TeamDefinition p1Team, TeamDefinition p2Team)
    {
        if (Phase != TeamMatchPhase.NotStarted)
            throw new InvalidOperationException(
                $"WithTeams 只允许在 NotStarted 阶段调用，当前是 {Phase}。");
        ArgumentNullException.ThrowIfNull(p1Team);
        ArgumentNullException.ThrowIfNull(p2Team);
        if (p1Team.TeamId == p2Team.TeamId)
            throw new ArgumentException("双方队伍 ID 不能相同。", nameof(p2Team));

        return NotStarted(Rules, p1Team, p2Team);
    }

    /// <summary>开始一场新交锋：重置计时与上一场结果，队员状态与体力原样保留。</summary>
    public TeamMatchState StartBout()
    {
        if (!CanStartBout)
            throw new InvalidOperationException($"阶段 {Phase} 不允许开始新交锋。");
        if (P1 is null || P2 is null)
            throw new InvalidOperationException("开赛前必须先选择双方队伍。");
        if (P1.IsExhausted || P2.IsExhausted)
            throw new InvalidOperationException("队伍已耗尽，不能再开始新交锋。");

        return new TeamMatchState(
            Rules, TeamMatchPhase.BoutInProgress, P1, P2,
            boutNumber: BoutsPlayed + 1,
            boutsPlayed: BoutsPlayed,
            remainingTimeFrames: Rules.BoutTimerFrames,
            RoundOutcome.None, RoundEndReason.None, TeamMatchOutcome.None);
    }

    /// <summary>
    /// 记录<b>一次</b>交锋结果并推进队员：胜方队员保留体力继续出战，败方队员被淘汰
    /// （即使 Time Over 时仍有正体力）；双方同时出局则双方各淘汰一名。
    /// 某一方耗尽队员即判定队伍胜负；两方同时耗尽为 <see cref="TeamMatchOutcome.TeamDraw"/>。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前不是进行中的交锋，或阶段/结果不自洽。</exception>
    public TeamMatchState RecordBoutResult(
        RoundOutcome outcome, RoundEndReason reason, int p1FinalHealth, int p2FinalHealth)
    {
        if (Phase != TeamMatchPhase.BoutInProgress)
            throw new InvalidOperationException(
                $"RecordBoutResult 只允许在 BoutInProgress 阶段调用，当前是 {Phase}。");
        if (P1 is null || P2 is null)
            throw new InvalidOperationException("进行中的交锋必须携带双方队伍。");

        if (!Enum.IsDefined(outcome) || outcome == RoundOutcome.None)
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome,
                "交锋结果必须是已结束交锋的确定结果。");
        if (!Enum.IsDefined(reason) || reason == RoundEndReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason), reason,
                "已结束交锋必须给出结束原因。");

        // D119：与单回合层共用同一份「结果/原因」合法性规则。
        RoundState.ValidateOutcomeReasonPair(outcome, reason);

        bool p1Lost = outcome != RoundOutcome.P1Win;
        bool p2Lost = outcome != RoundOutcome.P2Win;

        TeamSideState p1 = P1.ApplyBoutResult(p1FinalHealth, p1Lost);
        TeamSideState p2 = P2.ApplyBoutResult(p2FinalHealth, p2Lost);

        TeamMatchOutcome matchOutcome = p1.IsExhausted && p2.IsExhausted ? TeamMatchOutcome.TeamDraw
            : p1.IsExhausted ? TeamMatchOutcome.P2TeamWin
            : p2.IsExhausted ? TeamMatchOutcome.P1TeamWin
            : TeamMatchOutcome.None;

        TeamMatchPhase phase = matchOutcome == TeamMatchOutcome.None
            ? TeamMatchPhase.BetweenBouts
            : TeamMatchPhase.MatchFinished;

        return new TeamMatchState(
            Rules, phase, p1, p2,
            boutNumber: BoutNumber,
            boutsPlayed: BoutsPlayed + 1,
            remainingTimeFrames: RemainingTimeFrames,
            outcome, reason, matchOutcome);
    }

    /// <summary>同步当前交锋剩余逻辑帧（只读投影；仅进行中交锋有效）。</summary>
    public TeamMatchState WithRemainingTime(int frames)
    {
        if (!IsBoutLive || frames == RemainingTimeFrames)
            return this;
        return new TeamMatchState(
            Rules, Phase, P1, P2, BoutNumber, BoutsPlayed, frames,
            LastBoutOutcome, LastBoutReason, Outcome);
    }

    /// <summary>重新开始整场比赛：保留规则与双方队伍，清零交锋序号与队员状态。</summary>
    public TeamMatchState Restart() => NotStarted(Rules, P1?.Team, P2?.Team);

    /// <summary>返回选队：保留规则，清空队伍。</summary>
    public TeamMatchState ReturnToSelection() => NotStarted(Rules);

    /// <summary>校验全部不变式。</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Rules);

        if (!Enum.IsDefined(Phase))
            throw new InvalidOperationException($"队伍赛阶段是未定义枚举值：{Phase}。");
        if (!Enum.IsDefined(Outcome))
            throw new InvalidOperationException($"队伍赛结果是未定义枚举值：{Outcome}。");
        if (!Enum.IsDefined(LastBoutOutcome))
            throw new InvalidOperationException($"交锋结果是未定义枚举值：{LastBoutOutcome}。");
        if (!Enum.IsDefined(LastBoutReason))
            throw new InvalidOperationException($"交锋结束原因是未定义枚举值：{LastBoutReason}。");

        int timerCap = Rules.BoutTimerFrames;
        if (RemainingTimeFrames < 0 || RemainingTimeFrames > timerCap)
            throw new InvalidOperationException(
                $"剩余时间 {RemainingTimeFrames} 越界（0..{timerCap}）。");
        if (BoutsPlayed < 0)
            throw new InvalidOperationException($"已结束交锋数不能为负：{BoutsPlayed}。");
        if (BoutNumber < 0)
            throw new InvalidOperationException($"交锋序号不能为负：{BoutNumber}。");

        switch (Phase)
        {
            case TeamMatchPhase.NotStarted:
                if ((P1 is null) != (P2 is null))
                    throw new InvalidOperationException("未开始状态必须同时携带或同时不带双方队伍。");
                if (BoutNumber != 0 || BoutsPlayed != 0)
                    throw new InvalidOperationException("未开始的队伍赛不能携带交锋序号或已结束交锋。");
                if (Outcome != TeamMatchOutcome.None
                    || LastBoutOutcome != RoundOutcome.None
                    || LastBoutReason != RoundEndReason.None)
                    throw new InvalidOperationException("未开始的队伍赛不能携带任何结果。");
                if (RemainingTimeFrames != 0)
                    throw new InvalidOperationException("未开始的队伍赛不能携带剩余时间。");
                if (P1 is not null && (P1.IsExhausted || P2!.IsExhausted))
                    throw new InvalidOperationException("未开始的队伍赛不能携带已耗尽的队伍。");
                break;

            case TeamMatchPhase.BoutInProgress:
                if (P1 is null || P2 is null)
                    throw new InvalidOperationException("进行中的交锋必须携带双方队伍。");
                if (BoutNumber != BoutsPlayed + 1)
                    throw new InvalidOperationException(
                        $"进行中交锋序号 {BoutNumber} 必须等于已结束交锋数 + 1（{BoutsPlayed + 1}）。");
                if (P1.IsExhausted || P2.IsExhausted)
                    throw new InvalidOperationException("队伍已耗尽，不能处于进行中交锋。");
                if (LastBoutOutcome != RoundOutcome.None || LastBoutReason != RoundEndReason.None)
                    throw new InvalidOperationException("进行中的交锋不能携带上一场结果。");
                if (Outcome != TeamMatchOutcome.None)
                    throw new InvalidOperationException("进行中的队伍赛不能已经决出胜负。");
                break;

            case TeamMatchPhase.BetweenBouts:
                if (P1 is null || P2 is null)
                    throw new InvalidOperationException("过渡阶段必须携带双方队伍。");
                if (BoutNumber != BoutsPlayed)
                    throw new InvalidOperationException(
                        $"过渡阶段交锋序号 {BoutNumber} 必须等于已结束交锋数 {BoutsPlayed}。");
                if (P1.IsExhausted || P2.IsExhausted)
                    throw new InvalidOperationException("队伍已耗尽时必须判定终局，不能停在过渡阶段。");
                if (LastBoutOutcome == RoundOutcome.None || LastBoutReason == RoundEndReason.None)
                    throw new InvalidOperationException("过渡阶段必须携带刚结束交锋的结果与原因。");
                RoundState.ValidateOutcomeReasonPair(LastBoutOutcome, LastBoutReason);
                if (Outcome != TeamMatchOutcome.None)
                    throw new InvalidOperationException("过渡阶段不能已经决出队伍胜负。");
                break;

            case TeamMatchPhase.MatchFinished:
                if (P1 is null || P2 is null)
                    throw new InvalidOperationException("终局必须携带双方队伍。");
                if (BoutNumber != BoutsPlayed)
                    throw new InvalidOperationException(
                        $"终局交锋序号 {BoutNumber} 必须等于已结束交锋数 {BoutsPlayed}。");
                if (LastBoutOutcome == RoundOutcome.None || LastBoutReason == RoundEndReason.None)
                    throw new InvalidOperationException("队伍赛终局必须携带最后一场交锋的结果与原因。");
                RoundState.ValidateOutcomeReasonPair(LastBoutOutcome, LastBoutReason);
                if (Outcome == TeamMatchOutcome.None)
                    throw new InvalidOperationException("队伍赛终局必须给出结果。");

                bool p1Out = P1.IsExhausted;
                bool p2Out = P2.IsExhausted;
                bool consistent = Outcome switch
                {
                    TeamMatchOutcome.TeamDraw => p1Out && p2Out,
                    TeamMatchOutcome.P1TeamWin => !p1Out && p2Out,
                    TeamMatchOutcome.P2TeamWin => !p2Out && p1Out,
                    _ => false,
                };
                if (!consistent)
                    throw new InvalidOperationException(
                        $"队伍结果 {Outcome} 与耗尽状态不自洽（P1 {(p1Out ? "耗尽" : "存活")}，" +
                        $"P2 {(p2Out ? "耗尽" : "存活")}）。");
                break;
        }
    }

    public bool Equals(TeamMatchState? other) =>
        other is not null
        && Rules.Equals(other.Rules)
        && Phase == other.Phase
        && Equals(P1, other.P1)
        && Equals(P2, other.P2)
        && BoutNumber == other.BoutNumber
        && BoutsPlayed == other.BoutsPlayed
        && RemainingTimeFrames == other.RemainingTimeFrames
        && LastBoutOutcome == other.LastBoutOutcome
        && LastBoutReason == other.LastBoutReason
        && Outcome == other.Outcome;

    public override bool Equals(object? obj) => obj is TeamMatchState other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Rules, Phase, P1, P2, BoutNumber, BoutsPlayed,
            RemainingTimeFrames, HashCode.Combine(LastBoutOutcome, LastBoutReason, Outcome));

    /// <summary>调试文本。</summary>
    public override string ToString() =>
        $"{Phase}/B{BoutNumber}/played={BoutsPlayed}/time={RemainingTimeFrames}/" +
        $"P1[{P1}] P2[{P2}]/last={LastBoutOutcome}({LastBoutReason})/match={Outcome}";
}
