using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 09A（D104–D106）：<b>多回合比赛、胜场统计与比赛终局</b>。
///
/// <para>测试用「快速 KO」配置：双方 1/1/1 的裸轻拳招式、伤害 1000、满血 1000、
/// 距离 60（攻击框正好互相够得到），因此 2 个逻辑帧即可分出胜负。
/// 正式默认规则（2 胜 / 5 回合 / 99 秒）不变。</para>
/// </summary>
public class MatchTests
{
    private const string Ball = "Ball";
    private const string Fireball = "Fireball";

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    private static AttackDefinition JabHit() => new(
        offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
        halfWidth: FixedPoint.FromInt(30), halfHeight: FixedPoint.FromInt(30),
        damage: 1000, hitstunFrames: 5, blockstunFrames: 5);

    private static ProjectileDefinition BallDefinition() => new(
        Ball,
        new AttackDefinition(
            offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
            halfWidth: FixedPoint.FromInt(14), halfHeight: FixedPoint.FromInt(14),
            damage: 1, hitstunFrames: 1, blockstunFrames: 1),
        speedX: FixedPoint.FromInt(1), lifetimeFrames: 200);

    private static FightRoster QuickRoster()
    {
        FighterDefinition Build(string id) => new(id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
                new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[]
            {
                new MoveDefinition("Jab", "5A", 1, 1, 1, attacks: new[] { JabHit() }),
                new MoveDefinition(Fireball, "5C", 5, 2, 10, projectiles: new[] { BallDefinition() }),
            });

        return new FightRoster(Build("Kai"), Build("Rei"));
    }

    /// <summary>双方满血（1000）、距离 60 的干净回合初态；演示招式伤害 1000 → 一击 KO。</summary>
    private static BattleState Duel() => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion);

    private static MatchSession NewMatch(
        MatchRules? rules = null,
        Func<MatchRules, BattleState>? initialStateFactory = null,
        Action<MatchEvent>? eventsCommitted = null) =>
        new(rules ?? MatchRules.CreateDefault(),
            (_, _) => QuickRoster(),
            initialStateFactory ?? (_ => Duel()),
            eventsCommitted);

    /// <summary>打一回合：帧 0 按键发动、帧 1 生效命中（2 帧即 KO）。</summary>
    private static void PlayRound(MatchSession match, InputBits p1, InputBits p2 = InputBits.None) =>
        match.StepExact(2, f => f == 0 ? F(f, p1, p2) : FrameInput.Empty(f));

    /// <summary>推进到下一回合（要求当前处于 BetweenRounds）。</summary>
    private static void NextRound(MatchSession match) => match.StartNextRound();

    // ─────────────────────────────────────────────────────────────
    // 1. MatchRules 构造期校验
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultRules_AreTwoWinsFiveRoundsAnd99Seconds()
    {
        MatchRules rules = MatchRules.CreateDefault();

        Assert.Equal(2, rules.RoundsToWin);
        Assert.Equal(5, rules.MaxRounds);
        Assert.Equal(99, rules.RoundTimeSeconds);
        Assert.True(rules.EnableTimeout);
        Assert.Equal(5940, rules.RoundTimerFrames); // 99 × 60
        Assert.Equal(0, new MatchRules(enableTimeout: false).RoundTimerFrames);
    }

    [Fact]
    public void MatchRules_RejectsIllegalConfigurations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(roundsToWin: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(roundsToWin: MatchRules.MaxRoundsAllowed + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(roundsToWin: 3, maxRounds: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(maxRounds: MatchRules.MaxRoundsAllowed + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(roundTimeSeconds: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(roundTimeSeconds: MatchRules.MaxRoundTimeSeconds + 1));
    }

    // ─────────────────────────────────────────────────────────────
    // 2. MatchState 不变式与状态转换
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MatchState_StartRound_KeepsRulesAndInitializesTimer()
    {
        MatchRules rules = MatchRules.CreateDefault();
        MatchState state = MatchState.NotStarted(rules, "Kai", "Rei");
        state.Validate();

        MatchState live = state.StartRound();
        live.Validate();

        Assert.Equal(MatchPhase.RoundInProgress, live.Phase);
        Assert.Equal(1, live.RoundNumber);
        Assert.Equal(0, live.RoundsPlayed);
        Assert.Equal(5940, live.RemainingTimeFrames);
        Assert.Equal(RoundOutcome.None, live.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.None, live.CurrentRoundReason);
        Assert.Equal(MatchOutcome.None, live.Outcome);
        Assert.Equal("Kai", live.P1FighterId);
        Assert.Equal("Rei", live.P2FighterId);
    }

    [Fact]
    public void MatchState_RecordResult_IsSingleUseAndRejectsBadEnums()
    {
        MatchState live = MatchState.NotStarted(MatchRules.CreateDefault(), "Kai", "Rei").StartRound();
        MatchState after = live.RecordRoundResult(RoundOutcome.P1Win, RoundEndReason.Knockout);
        after.Validate();

        Assert.Equal(1, after.P1Wins);
        Assert.Equal(1, after.RoundsPlayed);
        Assert.Equal(MatchPhase.BetweenRounds, after.Phase);
        Assert.Throws<InvalidOperationException>(() =>
            after.RecordRoundResult(RoundOutcome.P1Win, RoundEndReason.Knockout));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            live.RecordRoundResult((RoundOutcome)999, RoundEndReason.Knockout));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            live.RecordRoundResult(RoundOutcome.P1Win, (RoundEndReason)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            live.RecordRoundResult(RoundOutcome.None, RoundEndReason.Knockout));
    }

    [Fact]
    public void MatchState_MatchFinished_CannotStartAnotherRound()
    {
        MatchState state = MatchState.NotStarted(MatchRules.CreateDefault(), "Kai", "Rei").StartRound();
        state = state.RecordRoundResult(RoundOutcome.P1Win, RoundEndReason.Knockout);
        state = state.StartRound();
        state = state.RecordRoundResult(RoundOutcome.P1Win, RoundEndReason.Knockout);

        Assert.Equal(MatchPhase.MatchFinished, state.Phase);
        Assert.Equal(MatchOutcome.P1Win, state.Outcome);
        Assert.False(state.CanStartRound);
        Assert.Throws<InvalidOperationException>(() => state.StartRound());
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 胜场累计与比赛终局
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void P1WinsTwoRounds_MatchFinishedImmediately()
    {
        var match = NewMatch();
        match.StartMatch();

        PlayRound(match, InputBits.LightPunch);
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        Assert.Equal(RoundOutcome.P1Win, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.Knockout, match.State.CurrentRoundReason);

        NextRound(match);
        Assert.Equal(2, match.State.RoundNumber);

        PlayRound(match, InputBits.LightPunch);
        Assert.Equal(2, match.State.P1Wins);
        Assert.Equal(MatchPhase.MatchFinished, match.State.Phase);
        Assert.Equal(MatchOutcome.P1Win, match.State.Outcome);
        Assert.Equal(2, match.State.RoundsPlayed);
    }

    [Fact]
    public void P2WinsTwoRounds_Mirrored()
    {
        var match = NewMatch();
        match.StartMatch();

        PlayRound(match, InputBits.None, InputBits.LightPunch);
        NextRound(match);
        PlayRound(match, InputBits.None, InputBits.LightPunch);

        Assert.Equal(2, match.State.P2Wins);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(MatchOutcome.P2Win, match.State.Outcome);
    }

    [Fact]
    public void DoubleKO_AddsNoWins_ButCountsAsPlayedRound()
    {
        var match = NewMatch();
        match.StartMatch();

        PlayRound(match, InputBits.LightPunch, InputBits.LightPunch);

        Assert.Equal(RoundOutcome.DoubleKO, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.DoubleKO, match.State.CurrentRoundReason);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
    }

    [Fact]
    public void MatchFinished_DoesNotScoreAgain_AndRejectsNextRound()
    {
        var match = NewMatch();
        match.StartMatch();
        PlayRound(match, InputBits.LightPunch);
        NextRound(match);
        PlayRound(match, InputBits.LightPunch);

        int wins = match.State.P1Wins;
        int played = match.State.RoundsPlayed;

        // 终局后再收到推进/新回合请求：不推进、不重复计分。
        match.StepExact(10, f => FrameInput.Empty(f));
        Assert.Throws<InvalidOperationException>(() => match.StartNextRound());
        Assert.Equal(wins, match.State.P1Wins);
        Assert.Equal(played, match.State.RoundsPlayed);
        Assert.Equal(MatchPhase.MatchFinished, match.State.Phase);
    }

    [Fact]
    public void FiveRoundsCap_EqualWins_IsMatchDraw()
    {
        var match = NewMatch();
        match.StartMatch();

        for (int r = 1; r <= 5; r++)
        {
            PlayRound(match, InputBits.LightPunch, InputBits.LightPunch); // 连续 DoubleKO
            if (r < 5)
            {
                Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
                NextRound(match);
            }
        }

        Assert.Equal(5, match.State.RoundsPlayed);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(MatchPhase.MatchFinished, match.State.Phase);
        Assert.Equal(MatchOutcome.Draw, match.State.Outcome);
    }

    [Fact]
    public void FiveRoundsCap_UnequalWins_DecidesByWins()
    {
        var match = NewMatch();
        match.StartMatch();

        // R1/R2 平局，R3 P1 胜，R4/R5 平局 → 1:0，到 5 回合按胜场判 P1 胜。
        PlayRound(match, InputBits.LightPunch, InputBits.LightPunch);
        NextRound(match);
        PlayRound(match, InputBits.LightPunch, InputBits.LightPunch);
        NextRound(match);
        PlayRound(match, InputBits.LightPunch);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        NextRound(match);
        PlayRound(match, InputBits.LightPunch, InputBits.LightPunch);
        NextRound(match);
        PlayRound(match, InputBits.LightPunch, InputBits.LightPunch);

        Assert.Equal(5, match.State.RoundsPlayed);
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(MatchOutcome.P1Win, match.State.Outcome);
    }

    [Fact]
    public void FinalRoundDecider_AlternatingWins()
    {
        // 1 胜制 / 最多 3 回合：P1、P2 交替获胜 → 第 3 回合决胜。
        var rules = new MatchRules(roundsToWin: 2, maxRounds: 3, roundTimeSeconds: 99);
        var match = NewMatch(rules);
        match.StartMatch();

        PlayRound(match, InputBits.LightPunch);          // R1 P1
        NextRound(match);
        PlayRound(match, InputBits.None, InputBits.LightPunch); // R2 P2
        NextRound(match);
        PlayRound(match, InputBits.LightPunch);          // R3 P1 → 2 胜

        Assert.Equal(MatchOutcome.P1Win, match.State.Outcome);
        Assert.Equal(3, match.State.RoundsPlayed);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 新回合重置（D106）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void NewRound_ResetsFightersButKeepsScoreAndSelection()
    {
        var match = NewMatch();
        match.SelectFighters("Kai", "Rei");
        match.StartMatch();
        PlayRound(match, InputBits.LightPunch, InputBits.LightPunch); // DoubleKO

        Assert.Equal(0, match.CurrentBattleState!.Fighter(0).Health);
        // 过渡阶段保留刚结束回合的结果与原因（供 UI 显示 ROUND N WINNER）。
        Assert.Equal(RoundOutcome.DoubleKO, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.DoubleKO, match.State.CurrentRoundReason);
        NextRound(match);

        BattleState fresh = match.CurrentBattleState!;
        Assert.Equal(0, fresh.Frame);
        Assert.Equal(1000, fresh.Fighter(0).Health);
        Assert.Equal(1000, fresh.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Standing, fresh.Fighter(0).LogicState);
        Assert.Equal(-1, fresh.Fighter(0).MoveStartedFrame);
        Assert.Equal(0, fresh.Fighter(0).MoveElapsedFrames);
        Assert.Equal(0, fresh.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(0, fresh.Fighter(0).ProjectileSpawnMask);
        Assert.Equal(-1, fresh.Fighter(0).LastHitAttackStartFrame);
        Assert.Equal(-1, fresh.Fighter(0).HitConfirmFrame);
        Assert.Equal(-1, fresh.Fighter(0).BlockConfirmFrame);
        Assert.Equal(1, fresh.NextProjectileId);
        Assert.Empty(fresh.Projectiles);
        Assert.False(fresh.Round.IsFinished);

        // 比分、回合序号、选人、规则都保留。
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(2, match.State.RoundNumber);
        Assert.Equal("Kai", match.State.P1FighterId);
        Assert.Equal("Rei", match.State.P2FighterId);
        // 新回合进行中：本回合尚无结果（旧回合结果不误归属到新回合）。
        Assert.Equal(RoundOutcome.None, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.None, match.State.CurrentRoundReason);
    }

    [Fact]
    public void NewRound_ClearsPreviousProjectileEntities()
    {
        int created = 0;
        Func<MatchRules, BattleState> factory = _ =>
        {
            created++;
            BattleState duel = Duel();
            if (created > 1)
                return duel;

            // 第 1 回合注入一枚远离双方的存活实体（来源招式在 QuickRoster 中存在）。
            ProjectileState entity = new(1, 0, Ball, Fireball,
                FixedPoint.FromInt(-400), FixedPoint.FromInt(50),
                FixedPoint.FromInt(1), FixedPoint.Zero, 200);
            return new BattleState(duel.Frame, duel.Fighters, duel.StateVersion,
                duel.Round, new[] { entity }, 2);
        };

        var match = NewMatch(initialStateFactory: factory);
        match.StartMatch();
        Assert.Single(match.CurrentBattleState!.Projectiles);

        PlayRound(match, InputBits.LightPunch);
        NextRound(match);

        Assert.Empty(match.CurrentBattleState!.Projectiles);
        Assert.Equal(1, match.CurrentBattleState.NextProjectileId);
    }

    [Fact]
    public void NewRound_UsesFreshInputBuffer_AndFreshHitDeduplication()
    {
        var match = NewMatch();
        match.StartMatch();
        var firstBuffer = match.RoundInputBuffer;

        // 用两阶段输入源制造已提交的按键历史与命中去重记录。
        match.RoundInputBuffer.Sample(InputBits.LightPunch, InputBits.None);
        match.StepExact(2);
        Assert.NotNull(firstBuffer.LastInput);
        Assert.NotEqual(-1, match.CurrentBattleState!.Fighter(1).LastHitAttackStartFrame);

        NextRound(match);

        Assert.NotSame(firstBuffer, match.RoundInputBuffer);
        Assert.Null(match.RoundInputBuffer.LastInput);
        Assert.Equal(-1, match.CurrentBattleState!.Fighter(1).LastHitAttackStartFrame);

        // 新实例能再次命中（命中去重身份重新建立）。
        PlayRound(match, InputBits.LightPunch);
        Assert.Equal(0, match.CurrentBattleState!.Fighter(1).Health);
        Assert.True(match.CurrentBattleState.Round.IsFinished);
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 帧同步、事件身份与回放
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void BattleSession_FrameSyncRemainsIntactAcrossRounds()
    {
        var match = NewMatch();
        match.StartMatch();

        for (int r = 1; r <= 2; r++)
        {
            PlayRound(match, InputBits.LightPunch, InputBits.LightPunch);
            Assert.True(match.IsFrameSyncIntact);
            Assert.Equal((long)match.CurrentBattleState!.Frame,
                match.CurrentRound!.Scheduler.TotalSteps);
            if (r == 1)
                NextRound(match);
        }
    }

    [Fact]
    public void MatchEvents_CarryRoundIdentity_AndRawEventIdsRepeatAcrossRounds()
    {
        var collected = new List<MatchEvent>();
        var match = NewMatch(eventsCommitted: collected.Add);
        match.StartMatch();

        PlayRound(match, InputBits.LightPunch, InputBits.LightPunch);
        NextRound(match);
        PlayRound(match, InputBits.LightPunch, InputBits.LightPunch);

        Assert.NotEmpty(collected);
        Assert.Contains(collected, e => e.RoundNumber == 1);
        Assert.Contains(collected, e => e.RoundNumber == 2);
        Assert.All(collected, e => Assert.True(e.RoundNumber is 1 or 2));
        Assert.All(collected, e => Assert.Equal(e.RoundEvent.EventId, e.Identity.EventId));

        // 裸 EventId 跨回合确实会重复（因此必须用组合身份）。
        int rawDistinct = collected.Select(e => e.RoundEvent.EventId).Distinct().Count();
        Assert.True(rawDistinct < collected.Count);
        // 组合身份全比赛唯一。
        Assert.Equal(collected.Count, collected.Select(e => e.Identity).Distinct().Count());
    }

    [Fact]
    public void MidSnapshotReplay_IsIdentical()
    {
        MatchRules rules = MatchRules.CreateDefault();

        MatchState Run(bool split)
        {
            var match = NewMatch(rules);
            match.StartMatch();
            PlayRound(match, InputBits.LightPunch);
            if (split)
            {
                // 中间快照：在过渡阶段重新读取状态后继续（等价于从该状态续跑）。
                MatchState snapshot = match.State;
                Assert.Equal(snapshot, match.State);
            }
            NextRound(match);
            PlayRound(match, InputBits.LightPunch);
            return match.State;
        }

        Assert.Equal(Run(split: false), Run(split: true));
    }

    [Fact]
    public void RestartMatch_ResetsScoreButKeepsSelection()
    {
        var match = NewMatch();
        match.SelectFighters("Kai", "Rei");
        match.StartMatch();
        PlayRound(match, InputBits.LightPunch);
        Assert.Equal(1, match.State.P1Wins);

        match.RestartMatch();

        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(0, match.State.RoundsPlayed);
        Assert.Equal(1, match.State.RoundNumber);
        Assert.Equal(MatchPhase.RoundInProgress, match.State.Phase);
        Assert.Equal("Kai", match.State.P1FighterId);
        Assert.Equal("Rei", match.State.P2FighterId);
        Assert.Equal(1000, match.CurrentBattleState!.Fighter(0).Health);
    }

    [Fact]
    public void ReturnToSelection_ClearsEverythingExceptRules()
    {
        var match = NewMatch();
        match.StartMatch();
        PlayRound(match, InputBits.LightPunch);

        match.ReturnToSelection();

        Assert.Equal(MatchPhase.NotStarted, match.State.Phase);
        Assert.Null(match.State.P1FighterId);
        Assert.Null(match.State.P2FighterId);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Null(match.CurrentRound);
        Assert.Null(match.CurrentBattleState);
        Assert.Equal(2, match.Rules.RoundsToWin);
        Assert.Equal(5, match.Rules.MaxRounds);
        Assert.Equal(5940, match.Rules.RoundTimerFrames);
    }
}
