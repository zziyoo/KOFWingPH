using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using NewKOF.BattleCore.Team;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 10A（D120）：<b>3v3 队伍赛</b>——队员按序出战、淘汰与体力继承、队伍终局。
///
/// <para>测试用「一击 KO」配置：P1 侧轻拳伤害 1000，P2 侧可配置伤害或完全无招式（沙包）。
/// 队伍人数恒为 <see cref="TeamDefinition.TeamSize"/>，但所有断言都通过 <c>Count</c> 取人数。</para>
/// </summary>
public class TeamMatchTests
{
    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    private static AttackDefinition Jab(int damage) => new(
        offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
        halfWidth: FixedPoint.FromInt(30), halfHeight: FixedPoint.FromInt(30),
        damage: damage, hitstunFrames: 5, blockstunFrames: 5);

    private static ProjectileDefinition Ball() => new(
        "Ball",
        new AttackDefinition(
            offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
            halfWidth: FixedPoint.FromInt(14), halfHeight: FixedPoint.FromInt(14),
            damage: 1, hitstunFrames: 1, blockstunFrames: 1),
        speedX: FixedPoint.FromInt(1), lifetimeFrames: 200);

    /// <summary><paramref name="damage"/> &lt; 0 表示该角色完全没有招式（沙包）。</summary>
    private static FighterDefinition Fighter(string id, int damage, bool fireball = false)
    {
        if (damage < 0)
            return new FighterDefinition(id,
                Array.Empty<CommandDefinition>(), Array.Empty<MoveDefinition>());

        var commands = new List<CommandDefinition>
        {
            new("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                priority: 5, bufferLifetimeFrames: 24),
        };
        var moves = new List<MoveDefinition>
        {
            new("Jab", "5A", 1, 1, 1, attacks: new[] { Jab(damage) }),
        };
        if (fireball)
        {
            commands.Add(new CommandDefinition("5C", Array.Empty<CommandDirection>(),
                InputBits.LightKick, priority: 5, bufferLifetimeFrames: 24));
            moves.Add(new MoveDefinition("Fireball", "5C", 1, 1, 6,
                projectiles: new[] { Ball() }));
        }

        return new FighterDefinition(id, commands, moves);
    }

    /// <summary>P1 侧伤害 <paramref name="p1Damage"/>，P2 侧伤害 <paramref name="p2Damage"/>（-1 = 沙包）。</summary>
    private static FightRoster Roster(int p1Damage = 1000, int p2Damage = -1, bool p1Fireball = false) =>
        new(Fighter(p1Damage < 0 ? "Sandbag1" : "Kai", p1Damage, p1Fireball),
            Fighter(p2Damage < 0 ? "Sandbag2" : "Rei", p2Damage));

    private static TeamDefinition Team(string teamId, params string[] ids)
    {
        var members = ids.Select(id => new TeamMemberDefinition(id, 1000)).ToArray();
        return new TeamDefinition(teamId, members);
    }

    private static TeamDefinition TripleTeam(string teamId, string a, string b, string c) =>
        Team(teamId, a, b, c);

    /// <summary>测试用近站位初态（±30）：默认的 ±120 超出轻拳射程。</summary>
    private static TeamBoutStateFactory CloseBout(
        int? timerFrames = null, int p1Health = -1, int p2Health = -1) =>
        (rules, p1, p2) =>
        {
            FighterState f1 = FighterState.Create(0, p1.ActiveMaxHealth, -30, Facing.Right) with
            {
                Health = p1Health > 0 ? p1Health : p1.ActiveHealth,
            };
            FighterState f2 = FighterState.Create(1, p2.ActiveMaxHealth, 30, Facing.Left) with
            {
                Health = p2Health > 0 ? p2Health : p2.ActiveHealth,
            };
            return new BattleState(0, new[] { f1, f2 }, BattleState.CreateDefault().StateVersion,
                round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: timerFrames
                    ?? (rules.EnableTimeout ? rules.BoutTimerFrames : null));
        };

    private static TeamMatchSession NewSession(
        TeamMatchRules? rules = null,
        FightRoster? roster = null,
        TeamBoutStateFactory? initialState = null,
        Action<TeamMatchEvent>? events = null,
        int maxSteps = 5) =>
        new(rules ?? TeamMatchRules.CreateDefault(),
            (_, _) => roster ?? Roster(),
            initialState ?? CloseBout(),
            events,
            maxSteps);

    private static void SelectAndStart(
        TeamMatchSession session,
        TeamDefinition? p1 = null,
        TeamDefinition? p2 = null)
    {
        session.SelectTeams(p1 ?? TripleTeam("P1", "Kai", "Rei", "Kai"),
            p2 ?? TripleTeam("P2", "Rei", "Kai", "Rei"));
        session.StartMatch();
    }

    /// <summary>开一场交锋并在第 0 帧按键、第 1 帧生效（2 帧结束交锋）。</summary>
    private static void PlayBout(
        TeamMatchSession session, InputBits p1, InputBits p2 = InputBits.None) =>
        session.StepExact(2, f => f == 0 ? F(f, p1, p2) : FrameInput.Empty(f));

    // ─────────────────────────────────────────────────────────────
    // 1. 队伍定义与槽位身份
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Team_BuildsThreeMembersInOrder()
    {
        TeamDefinition team = Team("P1", "Kai", "Rei", "Kai");

        Assert.Equal(TeamDefinition.TeamSize, team.Count);
        Assert.Equal(new[] { "Kai", "Rei", "Kai" }, team.FighterIds);
        Assert.Equal(1000, team.MemberAt(0).MaxHealth);
        Assert.Equal("Kai", team.MemberAt(2).FighterId);
    }

    [Fact]
    public void DuplicateFighterIds_OccupyDistinctSlots()
    {
        TeamDefinition a = TripleTeam("P1", "Kai", "Kai", "Kai");
        TeamDefinition b = TripleTeam("P2", "Kai", "Kai", "Kai");

        // 槽位身份区分同名队员：同队内三个槽位互不相等。
        var slots = Enumerable.Range(0, 3).Select(i => new TeamSlot("P1", i)).ToHashSet();
        Assert.Equal(3, slots.Count);
        Assert.NotEqual(new TeamSlot("P1", 0), new TeamSlot("P2", 0));
        Assert.Equal(a.FighterIds, b.FighterIds); // 阵容可以完全相同
    }

    [Fact]
    public void IllegalTeamDefinitions_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new TeamDefinition("P1", Array.Empty<TeamMemberDefinition>()));
        Assert.Throws<ArgumentException>(() => new TeamDefinition("P1", new[]
        {
            new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Rei", 1000),
        }));
        Assert.Throws<ArgumentException>(() => new TeamDefinition(" ", new[]
        {
            new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Rei", 1000),
            new TeamMemberDefinition("Kai", 1000),
        }));
        Assert.Throws<ArgumentException>(() => new TeamMemberDefinition("", 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TeamMemberDefinition("Kai", 0));
    }

    [Fact]
    public void CatalogCreateTeam_ResolvesMaxHealth_AndRejectsUnknownIds()
    {
        FighterCatalog catalog = FighterCatalog.CreateDemo();
        TeamDefinition team = catalog.CreateTeam("P1", new[] { "Kai", "Rei", "Kai" });

        Assert.Equal(3, team.Count);
        Assert.Equal(catalog.Get("Kai").MaxHealth, team.MemberAt(0).MaxHealth);
        Assert.Throws<ArgumentException>(() => catalog.CreateTeam("P1", new[] { "Kai", "Ghost", "Rei" }));
    }

    [Fact]
    public void TeamMatchRules_RejectsIllegalTimer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TeamMatchRules(roundTimeSeconds: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TeamMatchRules(roundTimeSeconds: TeamMatchRules.MaxRoundTimeSeconds + 1));
        Assert.Equal(5940, TeamMatchRules.CreateDefault().BoutTimerFrames);
        Assert.Equal(0, new TeamMatchRules(enableTimeout: false).BoutTimerFrames);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. KO / 接替 / 体力继承
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void P1KnocksOutP2FirstMember_SecondMemberReplaces()
    {
        var session = NewSession();
        SelectAndStart(session);

        Assert.Equal(TeamMatchPhase.BoutInProgress, session.State.Phase);
        Assert.Equal(1, session.State.BoutNumber);
        Assert.Equal("Rei", session.State.P2!.ActiveFighterId); // 槽位 0

        PlayBout(session, InputBits.LightPunch);

        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
        Assert.Equal(1, session.State.BoutsPlayed);
        Assert.True(session.State.P2.Eliminated[0]);
        Assert.False(session.State.P1!.Eliminated[0]);
        Assert.Equal(1, session.State.P2.ActiveSlot);           // 第二名接替
        Assert.Equal("Kai", session.State.P2.ActiveFighterId);
        Assert.Equal(RoundOutcome.P1Win, session.State.LastBoutOutcome);
        Assert.Equal(RoundEndReason.Knockout, session.State.LastBoutReason);
        Assert.Equal(3, session.State.P1.RemainingCount);
        Assert.Equal(2, session.State.P2.RemainingCount);

        session.StartNextBout();
        Assert.Equal(2, session.State.BoutNumber);
        Assert.Equal(TeamMatchPhase.BoutInProgress, session.State.Phase);
        Assert.Equal(0, session.CurrentBattleState!.Frame);
        Assert.Equal(1000, session.CurrentBattleState.Fighter(1).Health); // 新队员满血
    }

    [Fact]
    public void WinnerKeepsRemainingHealth_AcrossBouts()
    {
        // P1 的拳伤害 1000、P2 的拳伤害 300：同帧互中 → P2 死（1000），P1 剩 700 并获胜。
        var session = NewSession(roster: Roster(p1Damage: 1000, p2Damage: 300));
        SelectAndStart(session);

        PlayBout(session, InputBits.LightPunch, InputBits.LightPunch);

        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
        Assert.Equal(700, session.State.P1!.Health[0]);
        Assert.Equal(0, session.State.P2!.Health[0]);
        Assert.True(session.State.P2.Eliminated[0]);

        session.StartNextBout();

        // 胜者继承 700；新上场的 P2 第二名满血 1000。
        Assert.Equal(700, session.CurrentBattleState!.Fighter(0).Health);
        Assert.Equal(1000, session.CurrentBattleState.Fighter(0).MaxHealth);
        Assert.Equal(1000, session.CurrentBattleState.Fighter(1).Health);
        Assert.Equal(700, session.State.P1.ActiveHealth);
    }

    [Fact]
    public void Winner_ClearsAllTransientStateFromPreviousBout()
    {
        var session = NewSession(roster: Roster(p1Damage: 1000, p2Damage: 300));
        SelectAndStart(session);
        PlayBout(session, InputBits.LightPunch, InputBits.LightPunch);

        session.StartNextBout();
        BattleState fresh = session.CurrentBattleState!;
        FighterState p1 = fresh.Fighter(0);

        Assert.Equal(FighterLogicState.Standing, p1.LogicState);
        Assert.Null(p1.MoveId);
        Assert.Equal(-1, p1.MoveStartedFrame);
        Assert.Equal(0, p1.MoveElapsedFrames);
        Assert.Equal(0, p1.HitstopRemainingFrames);
        Assert.Equal(0, p1.StunRemainingFrames);
        Assert.Equal(0, p1.ProjectileSpawnMask);
        Assert.Equal(FixedPoint.Zero, p1.VelocityX);
        Assert.Equal(FixedPoint.Zero, p1.VelocityY);
        Assert.Equal(0, p1.Combo.Count);
        Assert.Equal(-1, p1.HitConfirmFrame);
        Assert.False(fresh.Round.IsFinished);
        Assert.Empty(fresh.Projectiles);
        Assert.Equal(1, fresh.NextProjectileId);
    }

    [Fact]
    public void OneFighterCanBeatAllThreeOpponents()
    {
        var session = NewSession(); // P2 是沙包（无招式）
        SelectAndStart(session);

        for (int i = 0; i < 3; i++)
        {
            PlayBout(session, InputBits.LightPunch);
            Assert.Equal(i + 1, session.State.BoutsPlayed);
            if (i < 2)
            {
                Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
                session.StartNextBout();
                Assert.Equal(i + 1, session.State.P2!.ActiveSlot);
                Assert.Equal(0, session.State.P1!.ActiveSlot); // 胜者一直是同一名队员
            }
        }

        Assert.Equal(TeamMatchPhase.MatchFinished, session.State.Phase);
        Assert.Equal(TeamMatchOutcome.P1TeamWin, session.State.Outcome);
        Assert.Equal(3, session.State.P1!.RemainingCount);
        Assert.True(session.State.P2!.IsExhausted);
        Assert.Equal(3, session.State.BoutsPlayed);
    }

    [Fact]
    public void BothSidesLoseMembers_AlternatingBouts()
    {
        // P1 强拳、P2 弱拳：交替让一方获胜。
        var session = NewSession(roster: Roster(p1Damage: 1000, p2Damage: 300));
        SelectAndStart(session);

        // 交锋 1：P1 胜（P2 淘汰 1 名）。
        PlayBout(session, InputBits.LightPunch);
        session.StartNextBout();

        // 交锋 2：P2 胜（P1 挨打三次 → 700 − 900 < 0 来不及，用 P1 不出招 + 连续命中）。
        // P2 单次 300，需要 4 次；这里直接用 P1 不出招、P2 连续出招直到 KO。
        session.StepExact(12, f => f % 3 == 0 ? F(f, InputBits.None, InputBits.LightPunch) : FrameInput.Empty(f));

        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
        Assert.True(session.State.P1!.Eliminated[0]);
        Assert.Equal(1, session.State.P1.ActiveSlot);
        Assert.Equal(2, session.State.P1.RemainingCount);
        Assert.Equal(2, session.State.BoutsPlayed);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. DoubleKO / Time Over
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DoubleKo_EliminatesBothAndAdvancesBoth()
    {
        var session = NewSession(roster: Roster(p1Damage: 1000, p2Damage: 1000));
        SelectAndStart(session);

        PlayBout(session, InputBits.LightPunch, InputBits.LightPunch);

        Assert.Equal(RoundOutcome.DoubleKO, session.State.LastBoutOutcome);
        Assert.Equal(RoundEndReason.DoubleKO, session.State.LastBoutReason);
        Assert.True(session.State.P1!.Eliminated[0]);
        Assert.True(session.State.P2!.Eliminated[0]);
        Assert.Equal(1, session.State.P1.ActiveSlot);
        Assert.Equal(1, session.State.P2.ActiveSlot);
        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
        Assert.Equal(2, session.State.P1.RemainingCount);
        Assert.Equal(2, session.State.P2.RemainingCount);
    }

    [Fact]
    public void LastMemberDoubleKo_IsTeamDraw()
    {
        var session = NewSession(roster: Roster(p1Damage: 1000, p2Damage: 1000));
        SelectAndStart(session);

        // 三场连续 DoubleKO → 最后一场双方同时耗尽。
        for (int i = 0; i < 3; i++)
        {
            PlayBout(session, InputBits.LightPunch, InputBits.LightPunch);
            if (i < 2)
                session.StartNextBout();
        }

        Assert.Equal(TeamMatchPhase.MatchFinished, session.State.Phase);
        Assert.Equal(TeamMatchOutcome.TeamDraw, session.State.Outcome);
        Assert.True(session.State.P1!.IsExhausted);
        Assert.True(session.State.P2!.IsExhausted);
        Assert.Equal(3, session.State.BoutsPlayed);
    }

    /// <summary>把<b>第一场</b>交锋计时压到 <paramref name="frames"/> 帧；后续交锋回到规则计时。</summary>
    private static TeamBoutStateFactory TimedBout(int frames, int p1Health = -1, int p2Health = -1)
    {
        bool first = true;
        return (rules, p1, p2) =>
        {
            TeamBoutStateFactory factory = first
                ? CloseBout(timerFrames: frames, p1Health: p1Health, p2Health: p2Health)
                : CloseBout(p1Health: p1Health, p2Health: p2Health);
            first = false;
            return factory(rules, p1, p2);
        };
    }

    [Fact]
    public void TimeOverLoserWithPositiveHealth_IsEliminated()
    {
        // P1 满血 1000、P2 只有 500：比例 100% vs 50% → P1 Time Over 胜，P2 被淘汰（HP 仍 > 0）。
        var session = NewSession(
            rules: new TeamMatchRules(roundTimeSeconds: 99),
            roster: Roster(p1Damage: -1, p2Damage: -1),
            initialState: TimedBout(frames: 4, p1Health: 1000, p2Health: 500));
        SelectAndStart(session);

        session.StepExact(4, f => FrameInput.Empty(f));

        Assert.Equal(RoundOutcome.P1Win, session.State.LastBoutOutcome);
        Assert.Equal(RoundEndReason.TimeOver, session.State.LastBoutReason);
        Assert.True(session.State.P2!.Eliminated[0]);
        Assert.Equal(500, session.State.P2.Health[0]);      // 体力保留为正
        Assert.False(session.State.P1!.Eliminated[0]);
        Assert.Equal(1000, session.State.P1.Health[0]);
        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
    }

    [Fact]
    public void TimeOver_ResetsTimerForNextBout()
    {
        var session = NewSession(
            rules: new TeamMatchRules(roundTimeSeconds: 1),
            roster: Roster(p1Damage: -1, p2Damage: -1),
            initialState: TimedBout(frames: 3, p1Health: 1000, p2Health: 500));
        SelectAndStart(session);

        session.StepExact(3, f => FrameInput.Empty(f));
        session.StartNextBout();

        Assert.Equal(session.Rules.BoutTimerFrames, session.State.RemainingTimeFrames);
        Assert.Equal(session.Rules.BoutTimerFrames,
            session.CurrentBattleState!.RoundTimerRemainingFrames);
    }

    [Fact]
    public void TimeOverDraw_ConsumesBothMembers()
    {
        var session = NewSession(
            rules: new TeamMatchRules(roundTimeSeconds: 99),
            roster: Roster(p1Damage: -1, p2Damage: -1),
            initialState: TimedBout(frames: 3));
        SelectAndStart(session);

        session.StepExact(3, f => FrameInput.Empty(f));

        Assert.Equal(RoundOutcome.Draw, session.State.LastBoutOutcome);
        Assert.Equal(RoundEndReason.TimeOverDraw, session.State.LastBoutReason);
        Assert.True(session.State.P1!.Eliminated[0]);
        Assert.True(session.State.P2!.Eliminated[0]);
        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 交锋隔离：飞行道具 / 输入 / 事件身份 / 帧同步
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProjectilesDoNotCarryIntoNextBout()
    {
        var session = NewSession(
            rules: new TeamMatchRules(roundTimeSeconds: 99),
            roster: Roster(p1Damage: -1, p2Damage: -1, p1Fireball: true),
            initialState: TimedBout(frames: 6));
        SelectAndStart(session);

        // 第 0 帧发弹，第 1 帧实体生成并在场；第 5 帧计时归零结束交锋。
        session.StepExact(6, f => f == 0 ? F(f, InputBits.LightKick) : FrameInput.Empty(f));
        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);

        session.StartNextBout();
        Assert.Empty(session.CurrentBattleState!.Projectiles);
        Assert.Equal(1, session.CurrentBattleState.NextProjectileId);
    }

    [Fact]
    public void InputHistoryDoesNotCarryIntoNextBout()
    {
        var session = NewSession();
        SelectAndStart(session);
        var firstBuffer = session.BoutInputBuffer;

        session.BoutInputBuffer.Sample(InputBits.LightPunch, InputBits.None);
        session.StepExact(2);
        Assert.NotNull(firstBuffer.LastInput);

        session.StartNextBout();

        Assert.NotSame(firstBuffer, session.BoutInputBuffer);
        Assert.Null(session.BoutInputBuffer.LastInput);
        Assert.Equal(0, session.CurrentBattleState!.Frame);
    }

    [Fact]
    public void BoutEvents_CarryUniqueIdentity_AndRawIdsRepeat()
    {
        var collected = new List<TeamMatchEvent>();
        var session = NewSession(events: collected.Add);
        SelectAndStart(session);

        PlayBout(session, InputBits.LightPunch);
        session.StartNextBout();
        PlayBout(session, InputBits.LightPunch);

        Assert.NotEmpty(collected);
        Assert.Contains(collected, e => e.BoutNumber == 1);
        Assert.Contains(collected, e => e.BoutNumber == 2);
        Assert.Equal(collected.Count, collected.Select(e => e.Identity).Distinct().Count());

        // 裸 EventId 跨交锋确实会重复（因此必须用组合身份）。
        int rawDistinct = collected.Select(e => e.BoutEvent.EventId).Distinct().Count();
        Assert.True(rawDistinct < collected.Count);
    }

    [Fact]
    public void BatchAdvance_StopsAtBoutTerminalFrame()
    {
        var session = NewSession();
        SelectAndStart(session);

        int committed = session.Advance(
            5 * FixedStepScheduler.FixedStepSeconds,
            new RecordingInput(f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f)));

        Assert.Equal(2, committed);
        Assert.Equal(2, session.CurrentBattleState!.Frame);
        Assert.Equal(2L, session.CurrentBout!.Scheduler.TotalSteps);
        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
        Assert.True(session.IsFrameSyncIntact);
    }

    private sealed class RecordingInput : IInputSource
    {
        private readonly Func<int, FrameInput> _provider;
        public RecordingInput(Func<int, FrameInput> provider) => _provider = provider;
        public List<int> Committed { get; } = new();
        public FrameInput Prepare(int frame) => _provider(frame);
        public void Commit(FrameInput input) => Committed.Add(input.Frame);
    }

    [Fact]
    public void ObserverSeesUpdatedTeamState()
    {
        TeamMatchSession? session = null;
        TeamMatchState? seen = null;
        session = NewSession(events: e =>
        {
            if (e.Type == BattleEventType.RoundEnded)
                seen = session!.State;
        });
        SelectAndStart(session);
        PlayBout(session, InputBits.LightPunch);

        Assert.NotNull(seen);
        Assert.Equal(TeamMatchPhase.BetweenBouts, seen!.Phase);
        Assert.True(seen.P2!.Eliminated[0]);
        Assert.Equal(1, seen.BoutsPlayed);
        Assert.Equal(RoundEndReason.Knockout, seen.LastBoutReason);
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 不变式、终局唯一性、对称性、确定性
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TeamState_IsImmutable_AndCollectionsAreReadOnly()
    {
        var session = NewSession();
        SelectAndStart(session);
        TeamMatchState state = session.State;

        Assert.IsAssignableFrom<IList<bool>>(state.P1!.Eliminated);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<bool>)state.P1.Eliminated)[0] = true);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<int>)state.P1.Health)[0] = 1);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)state.P1.Team.FighterIds)[0] = "X");

        PlayBout(session, InputBits.LightPunch);
        Assert.NotSame(state, session.State); // 旧快照不被污染
        Assert.Equal(TeamMatchPhase.BoutInProgress, state.Phase);
    }

    [Fact]
    public void MatchFinished_JudgesOnce_AndRejectsNextBout()
    {
        var session = NewSession();
        SelectAndStart(session);
        for (int i = 0; i < 3; i++)
        {
            PlayBout(session, InputBits.LightPunch);
            if (i < 2) session.StartNextBout();
        }

        TeamMatchState finished = session.State;
        Assert.Throws<InvalidOperationException>(() => session.StartNextBout());
        session.StepExact(10, f => FrameInput.Empty(f));
        Assert.Equal(finished, session.State); // 终局后不再推进、不再计分
    }

    [Fact]
    public void RecordBoutResult_RejectsInconsistentPair()
    {
        var session = NewSession();
        SelectAndStart(session);
        TeamMatchState live = session.State;

        Assert.Throws<InvalidOperationException>(() =>
            live.RecordBoutResult(RoundOutcome.DoubleKO, RoundEndReason.Knockout, 0, 0));
        Assert.Throws<InvalidOperationException>(() =>
            live.RecordBoutResult(RoundOutcome.Draw, RoundEndReason.Knockout, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            live.RecordBoutResult((RoundOutcome)99, RoundEndReason.Knockout, 0, 0));
    }

    [Fact]
    public void MirroredSwap_ProducesSymmetricResult()
    {
        static (TeamMatchOutcome Outcome, int P1Slot, int P2Slot, int P1Remaining, int P2Remaining) Run(bool swap)
        {
            var session = NewSession(roster: Roster(p1Damage: 1000, p2Damage: 1000));
            TeamDefinition a = TripleTeam("A", "Kai", "Rei", "Kai");
            TeamDefinition b = TripleTeam("B", "Rei", "Kai", "Rei");
            session.SelectTeams(swap ? b : a, swap ? a : b);
            session.StartMatch();
            PlayBout(session, InputBits.LightPunch, InputBits.LightPunch);
            return (session.State.Outcome,
                session.State.P1!.ActiveSlot, session.State.P2!.ActiveSlot,
                session.State.P1.RemainingCount, session.State.P2.RemainingCount);
        }

        Assert.Equal(Run(false), Run(true));
    }

    [Fact]
    public void SameInputs_TwoRuns_AreIdentical()
    {
        static TeamMatchState Run()
        {
            var session = NewSession(roster: Roster(p1Damage: 1000, p2Damage: 300));
            SelectAndStart(session);
            for (int i = 0; i < 3; i++)
            {
                PlayBout(session, InputBits.LightPunch, InputBits.LightPunch);
                if (session.State.Phase == TeamMatchPhase.BetweenBouts)
                    session.StartNextBout();
            }
            return session.State;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void MirrorMatch_UsesIndependentFighterState()
    {
        var session = NewSession(roster: Roster(p1Damage: 1000, p2Damage: 1000));
        session.SelectTeams(TripleTeam("P1", "Kai", "Kai", "Kai"),
            TripleTeam("P2", "Kai", "Kai", "Kai"));
        session.StartMatch();

        BattleState state = session.CurrentBattleState!;
        Assert.Equal(Facing.Right, state.Fighter(0).Facing);
        Assert.Equal(Facing.Left, state.Fighter(1).Facing);
        Assert.NotEqual(state.Fighter(0), state.Fighter(1));
    }

    [Fact]
    public void RestartAndReturnToSelection_ResetTeamProgress()
    {
        var session = NewSession();
        SelectAndStart(session);
        PlayBout(session, InputBits.LightPunch);
        Assert.Equal(1, session.State.BoutsPlayed);

        session.RestartMatch();
        Assert.Equal(TeamMatchPhase.BoutInProgress, session.State.Phase);
        Assert.Equal(0, session.State.BoutsPlayed);
        Assert.Equal(0, session.State.P1!.ActiveSlot);
        Assert.False(session.State.P2!.Eliminated[0]);
        Assert.Equal(1000, session.CurrentBattleState!.Fighter(1).Health);

        session.ReturnToSelection();
        Assert.Equal(TeamMatchPhase.NotStarted, session.State.Phase);
        Assert.Null(session.State.P1);
        Assert.Null(session.CurrentBout);
        Assert.Null(session.CurrentBattleState);
    }

    [Fact]
    public void StartMatch_RequiresBothTeams()
    {
        var session = NewSession();
        Assert.Throws<InvalidOperationException>(() => session.StartMatch());

        session.SelectTeams(TripleTeam("P1", "Kai", "Rei", "Kai"),
            TripleTeam("P2", "Rei", "Kai", "Rei"));
        session.StartMatch();
        Assert.Equal(TeamMatchPhase.BoutInProgress, session.State.Phase);
    }

    [Fact]
    public void SameTeamId_IsRejected()
    {
        var session = NewSession();
        Assert.Throws<ArgumentException>(() => session.SelectTeams(
            TripleTeam("X", "Kai", "Rei", "Kai"),
            TripleTeam("X", "Rei", "Kai", "Rei")));
    }

    [Fact]
    public void FrameSync_RemainsIntactAcrossBouts()
    {
        var session = NewSession();
        SelectAndStart(session);
        for (int i = 0; i < 3; i++)
        {
            PlayBout(session, InputBits.LightPunch);
            Assert.True(session.IsFrameSyncIntact);
            Assert.Equal((long)session.CurrentBattleState!.Frame,
                session.CurrentBout!.Scheduler.TotalSteps);
            if (i < 2) session.StartNextBout();
        }
    }
}
