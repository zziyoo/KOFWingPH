using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 09 统一集成验收（D104–D116）——把 09A–09D 四个子阶段放在<b>同一条真实流程</b>里验证：
/// 多回合比赛、逻辑帧计时、选人目录、JSON 配置，以及它们与 D73/D89/D96/D99 的交叉行为。
///
/// <para>场景 A–E 对应任务书第五部分；另含 10000 帧比赛级确定性、30/60/144fps 一致性与
/// <c>StepExact</c>/<c>Advance</c> 等价。</para>
/// </summary>
public class Stage09AcceptanceTests
{
    // ─────────────────────────────────────────────────────────────
    // 夹具
    // ─────────────────────────────────────────────────────────────

    private static string Json(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "fighters", name + ".json"));

    private static FighterCatalog JsonCatalog() => FighterDataLoader.LoadCatalog(new[]
    {
        new FighterDataDocument($"fighters/Kai.json", Json("Kai")),
        new FighterDataDocument($"fighters/Rei.json", Json("Rei")),
    });

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) =>
        new(frame, p1, p2);

    /// <summary>把比赛层输入提供器包成两阶段输入源（Prepare 无副作用、Commit 空实现即可）。</summary>
    private sealed class ScriptedInput : IInputSource
    {
        private readonly Func<int, FrameInput> _provider;
        public ScriptedInput(Func<int, FrameInput> provider) => _provider = provider;
        public FrameInput Prepare(int frame) => _provider(frame);
        public void Commit(FrameInput input) { }
    }

    /// <summary>JSON 目录 + 自定义回合初态的比赛会话。</summary>
    private static MatchSession JsonMatch(
        Func<MatchRules, BattleState> initialState,
        MatchRules? rules = null,
        Action<MatchEvent>? events = null,
        FighterCatalog? catalog = null)
    {
        FighterCatalog c = catalog ?? JsonCatalog();
        return new MatchSession(
            rules ?? MatchRules.CreateDefault(),
            (p1, p2) => c.CreateRoster(p1 ?? "Kai", p2 ?? "Rei"),
            initialState,
            events);
    }

    /// <summary>双方 HP 均为 <paramref name="health"/>、间距 30（JSON 5A 正好够得到）的回合初态。</summary>
    private static Func<MatchRules, BattleState> CloseDuel(int health) => rules => new BattleState(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right) with { Health = health },
            FighterState.Create(1, 1000, 30, Facing.Left) with { Health = health },
        },
        BattleState.CreateDefault().StateVersion,
        round: null, projectiles: null, nextProjectileId: 1,
        roundTimerRemainingFrames: rules.EnableTimeout ? rules.RoundTimerFrames : null);

    /// <summary>「快速 KO」配置：1/1/1 裸轻拳、伤害 1000，供 DoubleKO 场景使用。</summary>
    private static FightRoster QuickRoster()
    {
        AttackDefinition Jab() => new(
            offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
            halfWidth: FixedPoint.FromInt(30), halfHeight: FixedPoint.FromInt(30),
            damage: 1000, hitstunFrames: 5, blockstunFrames: 5);

        FighterDefinition Build(string id) => new(id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[] { new MoveDefinition("Jab", "5A", 1, 1, 1, attacks: new[] { Jab() }) });

        return new FightRoster(Build("Kai"), Build("Rei"));
    }

    private static MatchSession QuickMatch() => new(
        MatchRules.CreateDefault(),
        (_, _) => QuickRoster(),
        _ => new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left),
        }, BattleState.CreateDefault().StateVersion));

    private static void PlayQuickRound(MatchSession match, InputBits p1, InputBits p2 = InputBits.None) =>
        match.StepExact(2, f => f == 0 ? F(f, p1, p2) : FrameInput.Empty(f));

    // ─────────────────────────────────────────────────────────────
    // 场景 A：JSON 配置下的完整比赛（两回合 → MATCH WIN → 重开）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioA_FullMatchWithJsonCatalog_ReachesMatchWin_ThenRestartKeepsSelection()
    {
        var match = JsonMatch(CloseDuel(health: 30));
        match.SelectFighters("Kai", "Rei");
        match.StartMatch();

        Assert.Equal(MatchPhase.RoundInProgress, match.State.Phase);
        Assert.Equal(1, match.State.RoundNumber);
        Assert.Equal(MatchRules.CreateDefault().RoundTimerFrames,
            match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);

        // Round 1：5A 命中（JSON startup 3）→ 30 点伤害打空 30 HP。
        match.StepExact(12, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        Assert.Equal(RoundEndReason.Knockout, match.State.CurrentRoundReason);

        // 过渡阶段：不推进战斗帧、不改变比分、事件快照不变。
        int transitionFrame = match.CurrentBattleState!.Frame;
        IReadOnlyList<MatchEvent> transitionEvents = match.LastEvents;
        match.StepExact(30, f => FrameInput.Empty(f));
        Assert.Equal(transitionFrame, match.CurrentBattleState!.Frame);
        Assert.Equal(1, match.State.P1Wins);
        Assert.Same(transitionEvents, match.LastEvents);

        // 下一回合：HP / 位置 / 计时 / 回合内状态全部重置，比分与选人保留。
        match.StartNextRound();
        Assert.Equal(2, match.State.RoundNumber);
        Assert.Equal(1, match.State.RoundsPlayed);
        Assert.Equal(5940, match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);
        Assert.Equal(30, match.CurrentBattleState.Fighter(0).Health);
        Assert.Equal(30, match.CurrentBattleState.Fighter(1).Health);
        Assert.Equal(FixedPoint.FromInt(-30), match.CurrentBattleState.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), match.CurrentBattleState.Fighter(1).PositionX);
        Assert.Empty(match.CurrentBattleState.Projectiles);
        Assert.Equal(0, match.CurrentBattleState.Frame);

        // Round 2：再赢一回合 → 2 胜，比赛正式结束。
        match.StepExact(12, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        Assert.Equal(MatchPhase.MatchFinished, match.State.Phase);
        Assert.Equal(MatchOutcome.P1Win, match.State.Outcome);
        Assert.Equal(2, match.State.P1Wins);
        Assert.Equal(2, match.State.RoundsPlayed);
        Assert.Throws<InvalidOperationException>(() => match.StartNextRound());

        // 重开：保留选人，清零比分与回合，重建满状态。
        match.RestartMatch();
        Assert.Equal(MatchPhase.RoundInProgress, match.State.Phase);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(0, match.State.RoundsPlayed);
        Assert.Equal(1, match.State.RoundNumber);
        Assert.Equal("Kai", match.State.P1FighterId);
        Assert.Equal("Rei", match.State.P2FighterId);
        Assert.Equal(30, match.CurrentBattleState!.Fighter(0).Health);
        Assert.Equal(5940, match.CurrentBattleState.RoundTimerRemainingFrames!.Value);
    }

    // ─────────────────────────────────────────────────────────────
    // 场景 B：Time Over（比例裁决 + 只计分一次 + 计时重置）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioB_TimeOver_DecidesByHealthRatio_AndScoresExactlyOnce()
    {
        var events = new List<MatchEvent>();
        MatchRules rules = new(roundsToWin: 2, maxRounds: 5, roundTimeSeconds: 1);
        var match = new MatchSession(
            rules,
            (_, _) => JsonCatalog().CreateRoster("Kai", "Rei"),
            _ => new BattleState(0, new[]
            {
                // 绝对体力 P2 更高（600 > 500），但比例 P1 更高（50% > 40%）。
                FighterState.Create(0, 1000, -120, Facing.Right) with { Health = 500 },
                FighterState.Create(1, 1500, 120, Facing.Left) with { Health = 600 },
            }, BattleState.CreateDefault().StateVersion,
                round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: rules.RoundTimerFrames),
            events.Add);
        match.SelectFighters("Kai", "Rei");
        match.StartMatch();

        match.StepExact(80, f => FrameInput.Empty(f)); // 60 帧计时归零后回合结束，剩余帧不推进

        Assert.Equal(0, match.State.RemainingTimeFrames);
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(RoundEndReason.TimeOver, match.State.CurrentRoundReason);
        Assert.Equal(MatchPhase.BetweenRounds, match.State.Phase);
        // 超时不产生任何虚假伤害。
        Assert.Equal(500, match.CurrentBattleState!.Fighter(0).Health);
        Assert.Equal(600, match.CurrentBattleState.Fighter(1).Health);
        Assert.Single(events, e => e.Type == BattleEventType.RoundEnded);
        Assert.Single(events, e => e.Type == BattleEventType.TimeOver);
        Assert.DoesNotContain(events, e => e.Type is BattleEventType.Knockout or BattleEventType.DoubleKnockout);

        // 同一回合不会再次计分：过渡阶段再推进 30 帧也不产生新事件/新胜场。
        int countAtTransition = events.Count;
        match.StepExact(30, f => FrameInput.Empty(f));
        Assert.Equal(countAtTransition, events.Count);
        Assert.Equal(1, match.State.P1Wins);

        // 下一回合计时重置。
        match.StartNextRound();
        Assert.Equal(rules.RoundTimerFrames, match.CurrentBattleState!.RoundTimerRemainingFrames!.Value);
    }

    // ─────────────────────────────────────────────────────────────
    // 场景 C：连续 DoubleKO（双方不加分、回合照常推进、比赛不悬置）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioC_ConsecutiveDoubleKO_DoesNotHangTheMatch()
    {
        var match = QuickMatch();
        match.StartMatch();

        PlayQuickRound(match, InputBits.LightPunch, InputBits.LightPunch);
        Assert.Equal(RoundOutcome.DoubleKO, match.State.CurrentRoundOutcome);
        Assert.Equal(RoundEndReason.DoubleKO, match.State.CurrentRoundReason);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(1, match.State.RoundsPlayed);

        match.StartNextRound();
        PlayQuickRound(match, InputBits.LightPunch, InputBits.LightPunch);
        Assert.Equal(0, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
        Assert.Equal(2, match.State.RoundsPlayed);

        match.StartNextRound();
        PlayQuickRound(match, InputBits.LightPunch);
        Assert.Equal(1, match.State.P1Wins);
        Assert.Equal(3, match.State.RoundsPlayed);

        match.StartNextRound();
        PlayQuickRound(match, InputBits.LightPunch);
        Assert.Equal(MatchPhase.MatchFinished, match.State.Phase);
        Assert.Equal(MatchOutcome.P1Win, match.State.Outcome);
        Assert.Equal(2, match.State.P1Wins);
        Assert.Equal(0, match.State.P2Wins);
    }

    // ─────────────────────────────────────────────────────────────
    // 场景 D：镜像角色（Kai vs Kai）+ 飞行道具对撞
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioD_MirrorKai_ProjectilesClash_WithoutSharedFighterState()
    {
        var events = new List<MatchEvent>();
        var match = JsonMatch(
            rules => new BattleState(0, new[]
            {
                FighterState.Create(0, 1000, -120, Facing.Right),
                FighterState.Create(1, 1000, 120, Facing.Left),
            }, BattleState.CreateDefault().StateVersion,
                round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: rules.RoundTimerFrames),
            events: events.Add);
        match.SelectFighters("Kai", "Kai");
        match.StartMatch();

        Assert.Equal("Kai", match.State.P1FighterId);
        Assert.Equal("Kai", match.State.P2FighterId);
        // 镜像共用同一不可变 FighterDefinition，但运行期状态是各自独立的值：
        // 左右站位与朝向不同，且任何一方受击都不会改动另一方的快照。
        Assert.NotEqual(match.CurrentBattleState!.Fighter(0), match.CurrentBattleState.Fighter(1));
        Assert.Equal(Facing.Right, match.CurrentBattleState.Fighter(0).Facing);
        Assert.Equal(Facing.Left, match.CurrentBattleState.Fighter(1).Facing);

        // 双方同帧发 5C（LightKick）：Fireball startup 5，两个实体在中场对撞。
        match.StepExact(40, f => f == 0 ? F(f, InputBits.LightKick, InputBits.LightKick) : FrameInput.Empty(f));

        Assert.Single(events, e => e.Type == BattleEventType.ProjectileClash);
        Assert.DoesNotContain(events, e => e.Type is BattleEventType.ProjectileHit or BattleEventType.ProjectileBlocked);
        Assert.Empty(match.CurrentBattleState.Projectiles);
        Assert.Equal(1000, match.CurrentBattleState.Fighter(0).Health);
        Assert.Equal(1000, match.CurrentBattleState.Fighter(1).Health);
        Assert.Equal(MatchPhase.RoundInProgress, match.State.Phase);
    }

    // ─────────────────────────────────────────────────────────────
    // 场景 E：改 JSON 伤害即改变比赛结果（无需重新编译内核）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioE_EditedJsonDamage_ChangesMatchOutcome_OriginalUntouched()
    {
        static BattleState Duel(MatchRules rules) => new(
            0,
            new[]
            {
                FighterState.Create(0, 1000, -30, Facing.Right),
                FighterState.Create(1, 1000, 30, Facing.Left),
            },
            BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: rules.RoundTimerFrames);

        static string WithJabDamage(string json, int damage) =>
            json.Replace("\"damage\": 30", $"\"damage\": {damage}", StringComparison.Ordinal);

        // 原始 JSON：5A 伤害 30 → 一回合打不死。
        var original = new MatchSession(MatchRules.CreateDefault(),
            (_, _) => JsonCatalog().CreateRoster("Kai", "Rei"), Duel);
        original.SelectFighters("Kai", "Rei");
        original.StartMatch();
        original.StepExact(12, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        Assert.Equal(970, original.CurrentBattleState!.Fighter(1).Health);
        Assert.Equal(MatchPhase.RoundInProgress, original.State.Phase);

        // 仅把 Kai 5A 的 damage 改成 1000 后重新加载 → 同样输入直接 KO。
        string modified = WithJabDamage(Json("Kai"), 1000);
        FighterCatalog modCatalog = FighterDataLoader.LoadCatalog(new[]
        {
            new FighterDataDocument("fighters/Kai.modified.json", modified),
            new FighterDataDocument("fighters/Rei.json", Json("Rei")),
        });
        var mod = new MatchSession(MatchRules.CreateDefault(),
            (_, _) => modCatalog.CreateRoster("Kai", "Rei"), Duel);
        mod.SelectFighters("Kai", "Rei");
        mod.StartMatch();
        mod.StepExact(12, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        Assert.Equal(0, mod.CurrentBattleState!.Fighter(1).Health);
        Assert.Equal(1, mod.State.P1Wins);
        Assert.Equal(RoundEndReason.Knockout, mod.State.CurrentRoundReason);

        // 其他招式与其他角色不受影响；原文件内容未被就地修改。
        FighterDefinition reloaded = FighterDataLoader.Load(Json("Kai"), "fighters/Kai.json");
        Assert.Equal(30, reloaded.Moves[0].Attacks[0].Damage);
        Assert.Equal(50, reloaded.Moves[1].Attacks[0].Damage);
        Assert.Equal(45, FighterDataLoader.Load(Json("Rei"), "fighters/Rei.json")
            .Moves[0].Attacks[0].Damage);
    }

    // ─────────────────────────────────────────────────────────────
    // 10000 帧比赛级确定性（JSON 配置 + 计时 + 攻击/飞行道具）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LongRun_10000Frames_JsonMatch_IsDeterministic_AndEventIdsUnique()
    {
        // 单回合 / 999 秒 / 高体力：保证 10000 帧内不 KO、不超时，跑满整段。
        MatchRules rules = new(roundsToWin: 1, maxRounds: 1, roundTimeSeconds: 999);

        static BattleState Initial(MatchRules rules) => new(
            0,
            new[]
            {
                FighterState.Create(0, 100000, -120, Facing.Right),
                FighterState.Create(1, 100000, 120, Facing.Left),
            },
            BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: rules.RoundTimerFrames);

        static InputBits P1(int f) => f % 37 == 0 ? InputBits.LightPunch
            : f % 53 == 0 ? InputBits.LightKick
            : (f / 120) % 2 == 0 ? InputBits.Left : InputBits.Right;

        static InputBits P2(int f) => f % 41 == 0 ? InputBits.LightPunch
            : f % 71 == 0 ? InputBits.LightKick
            : (f / 120) % 2 == 0 ? InputBits.Right : InputBits.Left;

        (BattleState State, List<MatchEventId> Identities, List<int> RawIds) Run()
        {
            var events = new List<MatchEvent>();
            var match = new MatchSession(
                rules,
                (_, _) => JsonCatalog().CreateRoster("Kai", "Rei"),
                Initial,
                events.Add);
            match.SelectFighters("Kai", "Rei");
            match.StartMatch();
            match.StepExact(10000, f => F(f, P1(f), P2(f)));
            return (match.CurrentBattleState!,
                events.Select(e => e.Identity).ToList(),
                events.Select(e => e.RoundEvent.EventId).ToList());
        }

        (BattleState a, List<MatchEventId> aIds, List<int> aRaw) = Run();
        (BattleState b, List<MatchEventId> bIds, List<int> bRaw) = Run();

        Assert.Equal(10000, a.Frame);
        Assert.Equal(rules.RoundTimerFrames - 10000, a.RoundTimerRemainingFrames!.Value);
        Assert.Equal(a.Fighter(0), b.Fighter(0));
        Assert.Equal(a.Fighter(1), b.Fighter(1));
        Assert.Equal(a.Projectiles, b.Projectiles);
        Assert.Equal(a.NextProjectileId, b.NextProjectileId);
        Assert.Equal(a.Round, b.Round);
        Assert.Equal(aIds, bIds);
        Assert.Equal(aRaw, bRaw);

        Assert.NotEmpty(aIds);
        Assert.Equal(aIds.Count, aIds.Distinct().Count()); // 组合身份全比赛唯一
        Assert.Equal(aRaw.Count, aRaw.Distinct().Count()); // 单回合内裸 EventId 唯一
        Assert.False(a.Round.IsFinished);
    }

    // ─────────────────────────────────────────────────────────────
    // 30 / 60 / 144 fps 相同逻辑帧数 → 完全一致
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RenderRates_30_60_144_AgreeOnTheSameLogicFrames()
    {
        MatchRules rules = new(roundsToWin: 1, maxRounds: 1, roundTimeSeconds: 99, enableTimeout: false);

        static MatchSession NewMatch(MatchRules rules) => new(
            rules,
            (_, _) => JsonCatalog().CreateRoster("Kai", "Rei"),
            _ => new BattleState(0, new[]
            {
                FighterState.Create(0, 1000, -120, Facing.Right),
                FighterState.Create(1, 1000, 120, Facing.Left),
            }, BattleState.CreateDefault().StateVersion));

        static BattleSession Drive(MatchSession match, double delta, int batches)
        {
            var input = new ScriptedInput(f => F(f, InputBits.None));
            for (int i = 0; i < batches; i++)
                match.Advance(delta, input);
            return match.CurrentRound!;
        }

        MatchSession at60 = NewMatch(rules);
        at60.StartMatch();
        BattleSession s60 = Drive(at60, FixedStepScheduler.FixedStepSeconds, 10);

        MatchSession at30 = NewMatch(rules);
        at30.StartMatch();
        BattleSession s30 = Drive(at30, FixedStepScheduler.FixedStepSeconds * 2, 5);

        MatchSession at144 = NewMatch(rules);
        at144.StartMatch();
        BattleSession s144 = Drive(at144, FixedStepScheduler.FixedStepSeconds / 2.4, 24);

        Assert.Equal(10L, s60.Scheduler.TotalSteps);
        Assert.Equal(10L, s30.Scheduler.TotalSteps);
        Assert.Equal(10L, s144.Scheduler.TotalSteps);
        Assert.Equal(s60.CurrentState.Frame, s30.CurrentState.Frame);
        Assert.Equal(s60.CurrentState.Frame, s144.CurrentState.Frame);
        Assert.Equal(s60.CurrentState.Fighter(0), s30.CurrentState.Fighter(0));
        Assert.Equal(s60.CurrentState.Fighter(0), s144.CurrentState.Fighter(0));
        Assert.Equal(s60.CurrentState.Fighter(1), s30.CurrentState.Fighter(1));
        Assert.Equal(s60.CurrentState.Fighter(1), s144.CurrentState.Fighter(1));
        Assert.Equal(1000, s60.CurrentState.Fighter(0).Health);
        Assert.Equal(1000, s60.CurrentState.Fighter(1).Health);
    }

    // ─────────────────────────────────────────────────────────────
    // StepExact 与 Advance 在整回合维度等价
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StepExactAndAdvance_AgreeAcrossAWholeJsonRound()
    {
        MatchRules rules = MatchRules.CreateDefault();

        static Func<MatchRules, BattleState> Duel() => r => new BattleState(
            0,
            new[]
            {
                FighterState.Create(0, 1000, -30, Facing.Right) with { Health = 30 },
                FighterState.Create(1, 1000, 30, Facing.Left) with { Health = 30 },
            },
            BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: r.RoundTimerFrames);

        var exact = JsonMatch(Duel(), rules);
        exact.SelectFighters("Kai", "Rei");
        exact.StartMatch();
        exact.StepExact(12, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        var scheduled = JsonMatch(Duel(), rules);
        scheduled.SelectFighters("Kai", "Rei");
        scheduled.StartMatch();
        var input = new ScriptedInput(f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        for (int i = 0; i < 12 && scheduled.State.IsRoundLive; i++)
            scheduled.Advance(FixedStepScheduler.FixedStepSeconds, input);

        Assert.True(exact.IsFrameSyncIntact);
        Assert.True(scheduled.IsFrameSyncIntact);
        Assert.Equal(exact.State.Phase, scheduled.State.Phase);
        Assert.Equal(exact.State.P1Wins, scheduled.State.P1Wins);
        Assert.Equal(exact.State.RemainingTimeFrames, scheduled.State.RemainingTimeFrames);
        Assert.Equal(exact.CurrentBattleState!.Frame, scheduled.CurrentBattleState!.Frame);
        Assert.Equal(exact.CurrentBattleState.Fighter(0), scheduled.CurrentBattleState.Fighter(0));
        Assert.Equal(exact.CurrentBattleState.Fighter(1), scheduled.CurrentBattleState.Fighter(1));
    }
}
