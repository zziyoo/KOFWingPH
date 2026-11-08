using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 09C（D110–D112）：<b>角色目录、镜像选人、完整比赛流程与输入隔离</b>。
/// 全部为纯 C# 测试（不依赖 Godot）；Godot 选人界面的按键隔离由图形实测覆盖。
/// </summary>
public class FighterCatalogTests
{
    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    /// <summary>低血量（30）+ 距离 60 的初态：Kai 5A（伤害 30）4 帧即可 KO。</summary>
    private static BattleState InitialState(MatchRules rules, int health = 30, int distance = 30) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -distance, Facing.Right) with { Health = health },
            FighterState.Create(1, 1000, distance, Facing.Left) with { Health = health },
        },
        BattleState.CreateDefault().StateVersion,
        round: null, projectiles: null, nextProjectileId: 1,
        roundTimerRemainingFrames: rules.EnableTimeout ? rules.RoundTimerFrames : null);

    private static MatchSession NewMatch(
        FighterCatalog catalog, string p1Id, string p2Id,
        int health = 30, int distance = 30, int seconds = 99,
        Action<MatchEvent>? eventsCommitted = null) => new(
        new MatchRules(roundsToWin: 2, maxRounds: 5, roundTimeSeconds: seconds),
        (p1, p2) => catalog.CreateRoster(p1 ?? p1Id, p2 ?? p2Id),
        rules => InitialState(rules, health, distance),
        eventsCommitted);

    /// <summary>Kai（P1）用 5A 在 4 帧内 KO 对手。</summary>
    private static void KaiWinsRound(MatchSession match) =>
        match.StepExact(4, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

    // ─────────────────────────────────────────────────────────────
    // 1. 角色目录
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DemoCatalog_HasUniqueIdsAndBothFighters()
    {
        FighterCatalog catalog = FighterCatalog.CreateDemo();

        Assert.Equal(2, catalog.Entries.Count);
        Assert.Equal(new[] { "Kai", "Rei" }, catalog.Entries.Select(e => e.FighterId).ToArray());
        Assert.Equal("Kai", catalog.DisplayName("Kai"));
        Assert.Equal("Rei", catalog.DisplayName("Rei"));
        Assert.NotEmpty(catalog.Get("Kai").Source);
        Assert.Throws<ArgumentException>(() => catalog.Get("Unknown"));
        Assert.Throws<ArgumentException>(() => catalog.Get(""));
        Assert.Throws<ArgumentException>(() => catalog.CreateRoster("Kai", "Unknown"));
    }

    [Fact]
    public void Catalog_RejectsEmptyAndDuplicateIds()
    {
        Assert.Throws<ArgumentException>(() => new FighterCatalog(Array.Empty<FighterCatalogEntry>()));
        FighterCatalogEntry a = new("Kai", "Kai", "x", DemoRoster.CreateKai());
        Assert.Throws<ArgumentException>(() => new FighterCatalog(new[] { a, a }));
        Assert.Throws<ArgumentException>(() =>
            new FighterCatalogEntry(" ", "x", "x", DemoRoster.CreateKai()));
    }

    [Theory]
    [InlineData("Kai", "Rei")]
    [InlineData("Rei", "Kai")]
    [InlineData("Kai", "Kai")]
    [InlineData("Rei", "Rei")]
    public void AllFourSelections_ProduceValidRoster(string p1, string p2)
    {
        FighterCatalog catalog = FighterCatalog.CreateDemo();
        FightRoster roster = catalog.CreateRoster(p1, p2);

        Assert.Equal(p1, roster.Player1.FighterId);
        Assert.Equal(p2, roster.Player2.FighterId);
        Assert.NotEmpty(roster.Player1.Moves);
        Assert.NotEmpty(roster.Player2.Moves);

        // 选人结果进入权威比赛状态，且能开赛。
        var match = NewMatch(catalog, p1, p2);
        match.SelectFighters(p1, p2);
        match.StartMatch();
        Assert.Equal(p1, match.State.P1FighterId);
        Assert.Equal(p2, match.State.P2FighterId);
        Assert.True(match.State.IsRoundLive);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 镜像角色：配置共享但战斗状态独立
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MirrorMatch_KaiVsKai_StatesAreIndependent()
    {
        FighterCatalog catalog = FighterCatalog.CreateDemo();
        var match = NewMatch(catalog, "Kai", "Kai");
        match.StartMatch();

        Assert.Same(catalog.Get("Kai").Definition, catalog.Get("Kai").Definition); // 同一不可变配置
        KaiWinsRound(match);

        BattleState state = match.CurrentBattleState!;
        Assert.Equal(30, state.Fighter(0).Health);            // P1 未受击
        Assert.Equal(0, state.Fighter(1).Health);             // P2 被 KO
        Assert.Equal(FighterLogicState.Active, state.Fighter(0).LogicState); // 攻击方仍在生效帧
        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(1).LogicState);
        Assert.NotEqual(state.Fighter(0).LogicState, state.Fighter(1).LogicState);
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(RoundOutcome.P1Win, match.State.CurrentRoundOutcome);
    }

    [Fact]
    public void MirrorMatch_ProjectilesFromBothSidesClash()
    {
        // Kai vs Kai、远距离：双方同帧发 5C，两枚弹相向飞行并同级抵消。
        FighterCatalog catalog = FighterCatalog.CreateDemo();
        FightRoster roster = catalog.CreateRoster("Kai", "Kai");
        var sim = new BattleSimulation(roster);
        BattleState state = new(0, new[]
        {
            FighterState.Create(0, 1000, -400, Facing.Right),
            FighterState.Create(1, 1000, 400, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        int clashes = 0;
        for (int f = 0; f < 120; f++)
        {
            InputBits press = f == 0 ? InputBits.LightKick : InputBits.None;
            BattleStepResult r = sim.Step(state, F(f, press, press));
            state = r.State;
            clashes += r.Events.Count(e => e.Type == BattleEventType.ProjectileClash);
        }

        Assert.Equal(1, clashes);
        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Empty(state.Projectiles);
    }

    [Fact]
    public void MirrorMatch_ScoringWorksThroughFullMatch()
    {
        var match = NewMatch(FighterCatalog.CreateDemo(), "Kai", "Kai");
        match.StartMatch();

        KaiWinsRound(match);
        match.StartNextRound();
        KaiWinsRound(match);

        Assert.Equal(MatchPhase.MatchFinished, match.State.Phase);
        Assert.Equal(MatchOutcome.P1Win, match.State.Outcome);
        Assert.Equal(2, match.State.P1Wins);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 完整比赛流程与回合过渡
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CompleteFlow_Select_Fight_Transition_MatchWin_Restart_BackToSelection()
    {
        FighterCatalog catalog = FighterCatalog.CreateDemo();
        var match = NewMatch(catalog, "Kai", "Rei");

        // 选人 → 开赛
        Assert.Equal(MatchPhase.NotStarted, match.State.Phase);
        match.SelectFighters("Kai", "Rei");
        match.StartMatch();
        Assert.Equal(MatchPhase.RoundInProgress, match.State.Phase);

        // Round 1 → P1 胜 → 过渡
        KaiWinsRound(match);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        Assert.Equal(1, match.State.P1Wins);

        // 过渡：不推进、不重复计分、不重复投递 RoundEnded
        int frameAtTransition = match.CurrentBattleState!.Frame;
        var eventsAtTransition = match.LastEvents;
        match.StepExact(10, f => FrameInput.Empty(f));
        Assert.Equal(frameAtTransition, match.CurrentBattleState!.Frame);
        Assert.Equal(1, match.State.P1Wins);
        // 过渡期间不产生新事件：最近事件列表仍是回合结束那一帧的快照。
        Assert.Same(eventsAtTransition, match.LastEvents);

        // Round 2：HP / 状态 / 计时 / 弹 / 输入缓冲全部重置
        var firstBuffer = match.RoundInputBuffer;
        match.StartNextRound();
        Assert.Equal(2, match.State.RoundNumber);
        Assert.Equal(30, match.CurrentBattleState!.Fighter(0).Health);
        Assert.Equal(30, match.CurrentBattleState.Fighter(1).Health);
        Assert.Equal(5940, match.CurrentBattleState.RoundTimerRemainingFrames!.Value);
        Assert.Empty(match.CurrentBattleState.Projectiles);
        Assert.NotSame(firstBuffer, match.RoundInputBuffer);
        Assert.Null(match.RoundInputBuffer.LastInput);

        // Round 2 → 比赛终局
        KaiWinsRound(match);
        Assert.Equal(MatchPhase.MatchFinished, match.State.Phase);
        Assert.Equal(MatchOutcome.P1Win, match.State.Outcome);
        Assert.Throws<InvalidOperationException>(() => match.StartNextRound());

        // F5 重开：保留角色组合、清零比分
        match.RestartMatch();
        Assert.Equal("Kai", match.State.P1FighterId);
        Assert.Equal("Rei", match.State.P2FighterId);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(1, match.State.RoundNumber);
        Assert.True(match.State.IsRoundLive);

        // ESC 返回选人 → 改为 Rei vs Kai → 再次开赛
        match.ReturnToSelection();
        Assert.Equal(MatchPhase.NotStarted, match.State.Phase);
        Assert.Null(match.State.P1FighterId);
        match.SelectFighters("Rei", "Kai");
        match.StartMatch();
        Assert.Equal("Rei", match.State.P1FighterId);
        Assert.Equal("Kai", match.State.P2FighterId);
    }

    [Fact]
    public void NewRound_DoesNotInheritPauseState()
    {
        var match = NewMatch(FighterCatalog.CreateDemo(), "Kai", "Rei");
        match.StartMatch();
        match.CurrentRound!.Scheduler.Pause();
        Assert.True(match.CurrentRound.Scheduler.IsPaused);

        // StepExact 是显式单步，暂停下依然生效（D02 语义），用于推进到回合结束。
        KaiWinsRound(match);
        match.StartNextRound();

        Assert.False(match.CurrentRound!.Scheduler.IsPaused);
        Assert.Equal(0, match.CurrentRound.Scheduler.TotalSteps);
    }

    [Fact]
    public void MatchEvents_DoNotLeakAcrossRounds()
    {
        var collected = new List<MatchEvent>();
        var match = NewMatch(FighterCatalog.CreateDemo(), "Kai", "Rei",
            eventsCommitted: collected.Add);
        match.StartMatch();

        KaiWinsRound(match);
        int afterFirst = collected.Count;
        Assert.Contains(collected, e => e.RoundNumber == 1 && e.Type == BattleEventType.RoundEnded);

        match.StartNextRound();
        KaiWinsRound(match);

        Assert.Equal(2, collected.Count(e => e.Type == BattleEventType.RoundEnded));
        Assert.Contains(collected, e => e.RoundNumber == 2);
        Assert.All(collected, e => Assert.True(e.RoundNumber is 1 or 2));
        Assert.Equal(collected.Count, collected.Select(e => e.Identity).Distinct().Count());
        Assert.True(afterFirst > 0);
    }

    [Fact]
    public void SelectionConfirmation_CannotLeakIntoFirstBattleFrame()
    {
        var match = NewMatch(FighterCatalog.CreateDemo(), "Kai", "Rei");
        match.SelectFighters("Kai", "Rei");

        // 模拟「选人确认键」被界面层拦截：比赛层的新输入缓冲在开赛前没有任何历史。
        Assert.Null(match.RoundInputBuffer.LastInput);
        match.StartMatch();
        Assert.Null(match.RoundInputBuffer.LastInput);
        Assert.Empty(match.RoundInputBuffer.History);

        // 首帧输入完全由本帧提供器决定，不含确认键。
        match.StepExact(1); // 走当前回合专属 InputBuffer（两阶段提交）
        Assert.Equal(FrameInput.Empty(0), match.RoundInputBuffer.LastInput!.Value);
    }

    [Fact]
    public void TimeOverDraw_RoundTransition_KeepsScoreAndResetsTimer()
    {
        var match = NewMatch(FighterCatalog.CreateDemo(), "Kai", "Rei",
            health: 1000, distance: 120, seconds: 1); // 60 帧，双方比例相同 → Draw
        match.StartMatch();

        match.StepExact(60, f => FrameInput.Empty(f));

        Assert.Equal(RoundOutcome.Draw, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.TimeOverDraw, match.State.CurrentRoundReason);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);

        match.StartNextRound();
        Assert.Equal(60, match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);
        Assert.Equal(60, match.State.RemainingTimeFrames);
        Assert.Equal(1, match.State.RoundsPlayed);
    }
}
