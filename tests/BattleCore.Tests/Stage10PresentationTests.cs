using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using NewKOF.BattleCore.Team;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 10 最终表现层补修（D127/D128）：
/// <list type="bullet">
/// <item><b>逐逻辑帧事件传递</b>：一次 <c>Advance</c> 提交多个逻辑帧时，每一帧的全部事件
///       都必须按时间顺序送达表现层；火花位置取事件发生帧的权威状态，不是最终状态。</item>
/// <item><b>Hitstop 动画时钟</b>：冻结判据取帧开始状态的计数器（递减到 0 的那一帧仍是冻结帧），
///       一次跨多帧的 Advance 必须逐帧观察，不能只看末状态。</item>
/// <item><b>真实宿主</b>：真实 Kai/Rei JSON + 实际战斗逻辑（1v1 / 3v3 / Double KO），
///       以及 30 / 60 / 144 FPS 的一致性。</item>
/// </list>
/// </summary>
public class Stage10PresentationTests
{
    // ─────────────────────────────────────────────────────────────
    // 夹具：真实 JSON + 真实战斗会话
    // ─────────────────────────────────────────────────────────────

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) =>
        new(frame, p1, p2);

    private static string Json(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "fighters", $"{name}.json"));

    private static FighterCatalog Catalog() => FighterDataLoader.LoadCatalog(new[]
    {
        new FighterDataDocument("data/fighters/Kai.json", Json("Kai")),
        new FighterDataDocument("data/fighters/Rei.json", Json("Rei")),
    });

    private sealed class ScriptedInput : IInputSource
    {
        private readonly Func<int, FrameInput> _provider;
        public ScriptedInput(Func<int, FrameInput> provider) => _provider = provider;
        public FrameInput Prepare(int frame) => _provider(frame);
        public void Commit(FrameInput input) { }
    }

    /// <summary>双方 ±30 站位、满血、按规则注入计时的 1v1 初态（JSON 5A 正好够得到）。</summary>
    private static MatchSession NewDuel(List<PresentationFrame>? frames = null)
    {
        FighterCatalog catalog = Catalog();
        return new MatchSession(
            MatchRules.CreateDefault(),
            (p1, p2) => catalog.CreateRoster(p1 ?? "Kai", p2 ?? "Rei"),
            rules => new BattleState(0, new[]
            {
                FighterState.Create(0, 1000, -30, Facing.Right),
                FighterState.Create(1, 1000, 30, Facing.Left),
            }, BattleState.CreateDefault().StateVersion,
                round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: rules.EnableTimeout ? rules.RoundTimerFrames : null),
            framePresented: frames is null ? null : frames.Add);
    }

    private static MatchSession StartDuel(List<PresentationFrame>? frames = null)
    {
        MatchSession match = NewDuel(frames);
        match.SelectFighters("Kai", "Rei");
        match.StartMatch();
        return match;
    }

    /// <summary>「一击必杀」配置：裸轻拳伤害 1000（只在内存里改，不动 JSON 文件）。</summary>
    private static FightRoster LethalRoster()
    {
        FighterCatalog catalog = Catalog();
        FighterDefinition Boost(string id)
        {
            FighterDefinition def = catalog.Get(id).Definition;
            return new FighterDefinition(
                def.FighterId,
                def.Commands,
                def.Moves.Select(m => m.MoveId == "StandLightPunch"
                    ? new MoveDefinition(m.MoveId, m.CommandId, m.StartupFrames, m.ActiveFrames,
                        m.RecoveryFrames,
                        attacks: m.Attacks.Select(a => new AttackDefinition(
                            a.OffsetForward, a.OffsetUp, a.HalfWidth, a.HalfHeight,
                            firstActiveFrame: a.FirstActiveFrame, lastActiveFrame: a.LastActiveFrame,
                            height: a.Height, damage: 1000,
                            hitstunFrames: a.HitstunFrames, blockstunFrames: a.BlockstunFrames,
                            groundKnockback: a.GroundKnockback,
                            groundKnockbackFriction: a.GroundKnockbackFriction,
                            blockKnockback: a.BlockKnockback,
                            launchVelocityX: a.LaunchVelocityX, launchVelocityY: a.LaunchVelocityY,
                            knockdownFrames: a.KnockdownFrames, wakeupFrames: a.WakeupFrames,
                            hitstopFrames: a.HitstopFrames)).ToArray(),
                        allowedFrom: m.AllowedFrom, cancels: m.Cancels,
                        throws: m.Throws, projectiles: m.Projectiles)
                    : m).ToArray(),
                def.Movement);
        }

        return new FightRoster(Boost("Kai"), Boost("Kai"));
    }

    private static MatchSession NewLethalDuel(List<PresentationFrame> frames) => new(
        MatchRules.CreateDefault(),
        (_, _) => LethalRoster(),
        rules => new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left),
        }, BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: rules.EnableTimeout ? rules.RoundTimerFrames : null),
        framePresented: frames.Add);

    private static TeamMatchSession NewTeam(List<PresentationFrame>? frames = null, bool lethal = false)
    {
        FighterCatalog catalog = Catalog();
        return new TeamMatchSession(
            TeamMatchRules.CreateDefault(),
            (p1, p2) => lethal ? LethalRoster() : catalog.CreateRoster(p1, p2),
            (rules, p1, p2) => new BattleState(0, new[]
            {
                FighterState.Create(0, p1.ActiveMaxHealth, -30, Facing.Right) with
                {
                    Health = p1.ActiveHealth,
                },
                FighterState.Create(1, p2.ActiveMaxHealth, 30, Facing.Left) with
                {
                    Health = p2.ActiveHealth,
                },
            }, BattleState.CreateDefault().StateVersion,
                round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: rules.EnableTimeout ? rules.BoutTimerFrames : null),
            framePresented: frames is null ? null : frames.Add);
    }

    private static TeamMatchSession StartTeam(List<PresentationFrame>? frames = null, bool lethal = false)
    {
        TeamMatchSession session = NewTeam(frames, lethal);
        TeamDefinition Team(string id) => new(id, new[]
        {
            new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000),
        });
        session.SelectTeams(Team("P1"), Team("P2"));
        session.StartMatch();
        return session;
    }

    private static IReadOnlyList<FeedbackCue> ConsumeAll(
        IEnumerable<PresentationFrame> frames, FeedbackDirector director)
    {
        var cues = new List<FeedbackCue>();
        foreach (PresentationFrame frame in frames)
        {
            foreach (BattleEvent e in frame.Events)
            {
                if (director.TryConsume(frame.Scope, e, out FeedbackCue cue))
                    cues.Add(cue);
            }
        }
        return cues;
    }

    [Fact]
    public void Match_FrameObserver_CannotRestartOrReturnToSelection()
    {
        MatchSession? match = null;
        int observed = 0;
        match = new MatchSession(MatchRules.CreateDefault(), (_, _) => LethalRoster(),
            framePresented: frame =>
            {
                Assert.Equal(frame.StateAfter.Frame, match!.CurrentBattleState!.Frame);
                Assert.Throws<InvalidOperationException>(() => match.RestartMatch());
                Assert.Throws<InvalidOperationException>(() => match.ReturnToSelection());
                observed++;
            });
        match.SelectFighters("Kai", "Kai");
        match.StartMatch();
        match.StepExact(1);
        Assert.Equal(1, observed);
    }

    [Fact]
    public void Team_FrameObserver_CannotRestartOrReturnToSelection()
    {
        TeamMatchSession? match = null;
        int observed = 0;
        match = new TeamMatchSession(TeamMatchRules.CreateDefault(), (_, _) => LethalRoster(),
            framePresented: frame =>
            {
                Assert.Equal(frame.StateAfter.Frame, match!.CurrentBattleState!.Frame);
                Assert.Throws<InvalidOperationException>(() => match.RestartMatch());
                Assert.Throws<InvalidOperationException>(() => match.ReturnToSelection());
                observed++;
            });
        TeamDefinition Team(string id) => new(id, new[] {
            new TeamMemberDefinition("Kai", 1000), new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000) });
        match.SelectTeams(Team("P1"), Team("P2"));
        match.StartMatch();
        match.StepExact(1);
        Assert.Equal(1, observed);
    }

    [Fact]
    public void Match_TerminalFrameObserver_RejectsNextRoundAndKeepsScope()
    {
        MatchSession? match = null;
        var frames = new List<PresentationFrame>();
        match = new MatchSession(MatchRules.CreateDefault(), (_, _) => LethalRoster(),
            rules => new BattleState(0, new[] { FighterState.Create(0, 1000, -30, Facing.Right),
                FighterState.Create(1, 1000, 30, Facing.Left) }, BattleState.CreateDefault().StateVersion,
                round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: rules.EnableTimeout ? rules.RoundTimerFrames : null),
            framePresented: frame => {
                frames.Add(frame);
                if (frame.StateAfter.Round.IsFinished)
                    Assert.Throws<InvalidOperationException>(() => match!.StartNextRound());
            });
        match.SelectFighters("Kai", "Kai");
        match.StartMatch();
        match.Advance(FixedStepScheduler.FixedStepSeconds * 12,
            new ScriptedInput(f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f)));
        Assert.True(match.CurrentBattleState!.Round.IsFinished);
        Assert.Equal(match.CurrentBattleState.Frame, frames.Count);
        Assert.All(frames, f => Assert.Equal(1, f.Scope));
        Assert.Contains(frames[^1].Events, e => e.Type == BattleEventType.RoundEnded);
        match.StartNextRound();
        match.StepExact(1);
        Assert.Equal(2, frames[^1].Scope);
    }

    [Fact]
    public void Team_TerminalFrameObserver_RejectsNextBoutAndKeepsScope()
    {
        TeamMatchSession? match = null;
        var frames = new List<PresentationFrame>();
        match = new TeamMatchSession(TeamMatchRules.CreateDefault(), (_, _) => LethalRoster(),
            (rules, p1, p2) => new BattleState(0, new[] {
                FighterState.Create(0, p1.ActiveMaxHealth, -30, Facing.Right) with { Health = p1.ActiveHealth },
                FighterState.Create(1, p2.ActiveMaxHealth, 30, Facing.Left) with { Health = p2.ActiveHealth }
            }, BattleState.CreateDefault().StateVersion, round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: rules.EnableTimeout ? rules.BoutTimerFrames : null),
            framePresented: frame => {
                frames.Add(frame);
                if (frame.StateAfter.Round.IsFinished)
                    Assert.Throws<InvalidOperationException>(() => match!.StartNextBout());
            });
        TeamDefinition Team(string id) => new(id, new[] { new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000), new TeamMemberDefinition("Kai", 1000) });
        match.SelectTeams(Team("P1"), Team("P2"));
        match.StartMatch();
        match.Advance(FixedStepScheduler.FixedStepSeconds * 12,
            new ScriptedInput(f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f)));
        Assert.True(match.CurrentBattleState!.Round.IsFinished);
        Assert.Equal(match.CurrentBattleState.Frame, frames.Count);
        Assert.All(frames, f => Assert.Equal(1, f.Scope));
        Assert.Contains(frames[^1].Events, e => e.Type == BattleEventType.RoundEnded);
        match.StartNextBout();
        match.StepExact(1);
        Assert.Equal(2, frames[^1].Scope);
    }

    [Fact]
    public void Match_FrameObserverException_CommitsAndFaultsSession()
    {
        var failure = new ApplicationException("observer");
        var match = new MatchSession(MatchRules.CreateDefault(), (_, _) => LethalRoster(),
            framePresented: _ => throw failure);
        match.SelectFighters("Kai", "Kai");
        match.StartMatch();
        Assert.Same(failure, Assert.Throws<ApplicationException>(() => match.StepExact(1)));
        Assert.Equal(1, match.CurrentBattleState!.Frame);
        Assert.Throws<InvalidOperationException>(() => match.StepExact(1));
        Assert.Equal(1, match.CurrentBattleState.Frame);
    }

    [Fact]
    public void Team_FrameObserverException_CommitsAndFaultsSession()
    {
        var failure = new ApplicationException("observer");
        var match = new TeamMatchSession(TeamMatchRules.CreateDefault(), (_, _) => LethalRoster(),
            framePresented: _ => throw failure);
        TeamDefinition Team(string id) => new(id, new[] { new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000), new TeamMemberDefinition("Kai", 1000) });
        match.SelectTeams(Team("P1"), Team("P2"));
        match.StartMatch();
        Assert.Same(failure, Assert.Throws<ApplicationException>(() => match.StepExact(1)));
        Assert.Equal(1, match.CurrentBattleState!.Frame);
        Assert.Throws<InvalidOperationException>(() => match.StepExact(1));
        Assert.Equal(1, match.CurrentBattleState.Frame);
    }

    // ─────────────────────────────────────────────────────────────
    // A. 事件传递（P1）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Advance_CommitsEveryLogicFrame_InOrder()
    {
        var frames = new List<PresentationFrame>();
        MatchSession match = StartDuel(frames);
        var source = new ScriptedInput(f =>
            f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        // 一个渲染帧推进 5 个逻辑帧（低帧率 / 卡顿）。
        int steps = match.Advance(FixedStepScheduler.FixedStepSeconds * 5, source);

        Assert.Equal(5, steps);
        Assert.Equal(5, frames.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, frames.Select(f => f.LogicFrame));

        foreach (PresentationFrame frame in frames)
        {
            Assert.Equal(1, frame.Scope);
            Assert.Equal(frame.LogicFrame, frame.StateBefore.Frame);
            Assert.Equal(frame.LogicFrame + 1, frame.StateAfter.Frame);
            Assert.All(frame.Events, e => Assert.Equal(frame.LogicFrame, e.Frame));
        }

        // 无事件的帧也必须送达（动画时钟的冻结/恢复信息在这些帧里）。
        Assert.Contains(frames, f => !f.HasEvents);
    }

    [Fact]
    public void HitOnEarlyFrame_SurvivesEmptyTrailingFrames()
    {
        var frames = new List<PresentationFrame>();
        MatchSession match = StartDuel(frames);
        var source = new ScriptedInput(f =>
            f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        match.Advance(FixedStepScheduler.FixedStepSeconds * 5, source);

        // 旧路径（LastEvents）只剩最后一帧：命中与伤害都在，事件却没了。
        Assert.True(match.CurrentBattleState!.Fighter(1).Health < 1000);
        Assert.Empty(match.LastEvents);

        PresentationFrame hit = Assert.Single(frames,
            f => f.Events.Any(e => e.Type == BattleEventType.Hit));
        Assert.Equal(3, hit.LogicFrame);
        Assert.Equal(970, hit.StateAfter.Fighter(1).Health);
        Assert.Equal(4, hit.StateAfter.Fighter(1).HitstopRemainingFrames);

        // 新路径逐帧消费：命中恰好产生一次反馈。
        var director = new FeedbackDirector();
        IReadOnlyList<FeedbackCue> cues = ConsumeAll(frames, director);
        FeedbackCue cue = Assert.Single(cues);
        Assert.Equal(FeedbackKind.Hit, cue.Kind);
        Assert.Equal(1, cue.TargetFighterId);
        Assert.Equal(1, director.EmittedCount);
    }

    [Fact]
    public void MultipleEventsInOneFrame_AreDeliveredInOrder_AndNotOverwritten()
    {
        var frames = new List<PresentationFrame>();
        MatchSession match = StartDuel(frames);
        var source = new ScriptedInput(f =>
            f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        match.Advance(FixedStepScheduler.FixedStepSeconds * 5, source);

        PresentationFrame hit = frames.Single(f => f.HasEvents && f.LogicFrame == 3);

        // 同一帧：进入生效 → 命中 → 受击硬直开始（帧内顺序固定，不互相覆盖）。
        Assert.Equal(
            new[] { BattleEventType.AttackActive, BattleEventType.Hit, BattleEventType.HitstunStarted },
            hit.Events.Select(e => e.Type));
        Assert.Equal(new[] { 0, 1, 2 }, hit.Events.Select(e => e.EventId % BattleEvent.MaxEventsPerFrame));
        Assert.Equal(hit.Events.Select(e => e.EventId).OrderBy(id => id), hit.Events.Select(e => e.EventId));

        var director = new FeedbackDirector();
        IReadOnlyList<FeedbackCue> cues = ConsumeAll(frames, director);
        Assert.Single(cues); // AttackActive / HitstunStarted 不产生瞬时特效
    }

    [Fact]
    public void DrainedFrames_AreNotRedelivered_AndDuplicateIdentityIsSuppressed()
    {
        var frames = new List<PresentationFrame>();
        MatchSession match = StartDuel(frames);
        var source = new ScriptedInput(f =>
            f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        match.Advance(FixedStepScheduler.FixedStepSeconds * 5, source);

        var buffer = new PresentationFrameBuffer();
        foreach (PresentationFrame frame in frames)
            buffer.Enqueue(frame);

        IReadOnlyList<PresentationFrame> drained = buffer.Drain();
        Assert.Equal(frames.Count, drained.Count);
        Assert.Equal(0, buffer.PendingCount);
        Assert.Empty(buffer.Drain()); // 已消费的不会再次出现

        // 即使同一事件被重复投递（回放/重连），身份去重也只允许一次特效。
        var director = new FeedbackDirector();
        BattleEvent hit = drained.Single(f => f.LogicFrame == 3).Events[1];
        Assert.True(director.TryConsume(1, hit, out _));
        Assert.False(director.TryConsume(1, hit, out _));
        Assert.Equal(1, director.EmittedCount);
    }

    [Fact]
    public void SameEventIdInDifferentBouts_IsNotDeduplicated()
    {
        var frames = new List<PresentationFrame>();
        TeamMatchSession session = StartTeam(frames, lethal: true);

        session.StepExact(6, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
        session.StartNextBout();
        session.StepExact(6, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        BattleEvent[] hits = frames
            .SelectMany(f => f.Events)
            .Where(e => e.Type == BattleEventType.Hit)
            .ToArray();
        Assert.Equal(2, hits.Length);
        Assert.Equal(hits[0].EventId, hits[1].EventId); // 裸 EventId 跨交锋必然重号

        var director = new FeedbackDirector();
        Assert.True(director.TryConsume(frames.First(f => f.Scope == 1 && f.Events.Any(e => e.Type == BattleEventType.Hit)).Scope,
            hits[0], out _));
        Assert.True(director.TryConsume(frames.First(f => f.Scope == 2 && f.Events.Any(e => e.Type == BattleEventType.Hit)).Scope,
            hits[1], out _));
        Assert.Equal(2, director.EmittedCount);
    }

    [Fact]
    public void SparkAnchor_UsesEventFramePosition_NotFinalPosition()
    {
        var frames = new List<PresentationFrame>();
        MatchSession match = StartDuel(frames);
        var source = new ScriptedInput(f =>
            f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        match.Advance(FixedStepScheduler.FixedStepSeconds * 5, source);   // 0..4（命中帧 3）
        match.Advance(FixedStepScheduler.FixedStepSeconds * 5, source);   // 5..9（击退已发生）

        PresentationFrame hit = frames.Single(f => f.Events.Any(e => e.Type == BattleEventType.Hit));
        BattleEvent hitEvent = hit.Events.Single(e => e.Type == BattleEventType.Hit);
        var director = new FeedbackDirector();
        Assert.True(director.TryConsume(hit.Scope, hitEvent, out FeedbackCue cue));

        (double eventX, double eventY) = FeedbackGeometry.Anchor(cue, hit.StateAfter);
        (double finalX, _) = FeedbackGeometry.Anchor(cue, match.CurrentBattleState!);

        // 命中帧：被击者仍在 x=30；批次结束时已被击退到 x=33.5。
        Assert.Equal(30d, eventX, 6);
        Assert.Equal(60d, eventY, 6); // 胸口高度
        Assert.Equal(33.5d, finalX, 6);
        Assert.NotEqual(finalX, eventX);
    }

    [Fact]
    public void ClashAnchor_IsTheMidpointOfBothFighters()
    {
        MatchSession match = StartDuel();
        BattleState state = match.CurrentBattleState!;
        var cue = new FeedbackCue(FeedbackKind.StrikeClash, 0, 1, 0, 1, BattleEventType.StrikeClash);

        (double x, double y) = FeedbackGeometry.Anchor(cue, state);

        double expectedX = (state.Fighter(0).PositionX.ToDouble() + state.Fighter(1).PositionX.ToDouble()) / 2d;
        Assert.Equal(expectedX, x, 6);
        Assert.Equal(60d, y, 6);
    }

    [Fact]
    public void FrameBuffer_BoundsMemory_AndNeverAccumulates()
    {
        PresentationFrame frame = SampleFrame(scope: 1, logicFrame: 0);
        var buffer = new PresentationFrameBuffer(capacity: 2);

        buffer.Enqueue(frame);
        buffer.Enqueue(SampleFrame(scope: 1, logicFrame: 1));
        buffer.Enqueue(SampleFrame(scope: 1, logicFrame: 2));

        Assert.Equal(2, buffer.PendingCount);
        Assert.Equal(1, buffer.DroppedCount); // 容量是防御性上限，只丢最旧的帧

        IReadOnlyList<PresentationFrame> drained = buffer.Drain();
        Assert.Equal(2, drained.Count);
        Assert.Equal(new[] { 1, 2 }, drained.Select(f => f.LogicFrame));
        Assert.Equal(0, buffer.PendingCount);
        Assert.Empty(buffer.Drain());
    }

    [Fact]
    public void PresentationFrame_RejectsInconsistentWiring()
    {
        BattleState state = BattleState.CreateDefault();
        BattleState next = new(state.Frame + 1, state.Fighters, state.StateVersion);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PresentationFrame(0, 0, state, next));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PresentationFrame(1, -1, state, next));
        Assert.Throws<ArgumentException>(() =>
            new PresentationFrame(1, 0, next, next));
        Assert.Throws<ArgumentException>(() =>
            new PresentationFrame(1, 0, state, state));

        // 事件的逻辑帧必须与快照一致：装配错误立即失败，不做静默修正。
        var wrongFrame = new BattleEvent(5, 5 * BattleEvent.MaxEventsPerFrame,
            BattleEventType.Hit, 0, 1, null, 30);
        Assert.Throws<ArgumentException>(() =>
            new PresentationFrame(1, 0, state, next, new[] { wrongFrame }));
    }

    private static PresentationFrame SampleFrame(int scope, int logicFrame)
    {
        BattleState state = BattleState.CreateDefault();
        BattleState before = new(logicFrame, state.Fighters, state.StateVersion);
        BattleState after = new(logicFrame + 1, state.Fighters, state.StateVersion);
        return new PresentationFrame(scope, logicFrame, before, after);
    }

    // ─────────────────────────────────────────────────────────────
    // B. Hitstop 动画时钟（P2）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Clock_AdvancesOnEveryNormalFrame()
    {
        var clock = new FighterVisualClock();

        for (int frame = 0; frame <= 5; frame++)
            Assert.Equal(frame, clock.Observe(frame, FighterLogicState.Standing, null, frozenThisFrame: false));
    }

    [Fact]
    public void Clock_FreezesThroughTheWholeHitstopCountdown()
    {
        var clock = new FighterVisualClock();

        // 命中帧本身不冻结：状态切到 Hitstun → 动画从 0 开始。
        Assert.Equal(0, clock.Observe(10, FighterLogicState.Hitstun, null, frozenThisFrame: false));

        // 之后 4 帧是冻结帧（帧开始时计数 4→3→2→1），画面必须完全不动。
        for (int i = 1; i <= 4; i++)
            Assert.Equal(0, clock.Observe(10 + i, FighterLogicState.Hitstun, null, frozenThisFrame: true));

        // 恢复后的第一帧才推进 1 帧。
        Assert.Equal(1, clock.Observe(15, FighterLogicState.Hitstun, null, frozenThisFrame: false));
        Assert.Equal(2, clock.Observe(16, FighterLogicState.Hitstun, null, frozenThisFrame: false));
    }

    [Fact]
    public void Clock_LastFrozenFrame_DoesNotCountAsAnimationTime()
    {
        // 这一帧的「帧结束」状态里 HitstopRemainingFrames 已经是 0，
        // 但它整帧仍然是冻结的——判据必须取帧开始值。
        var clock = new FighterVisualClock();
        clock.Observe(0, FighterLogicState.Hitstun, null, false);
        clock.Observe(1, FighterLogicState.Hitstun, null, true);  // 开始值 4
        clock.Observe(2, FighterLogicState.Hitstun, null, true);  // 开始值 3
        clock.Observe(3, FighterLogicState.Hitstun, null, true);  // 开始值 2

        Assert.Equal(0, clock.Observe(4, FighterLogicState.Hitstun, null, true)); // 开始值 1 → 仍冻结

        Assert.Equal(1, clock.Observe(5, FighterLogicState.Hitstun, null, false));
    }

    [Fact]
    public void Clock_RepeatedObservationOfSameFrame_DoesNotAdvance()
    {
        var clock = new FighterVisualClock();
        clock.Observe(0, FighterLogicState.Standing, null, false);
        clock.Observe(3, FighterLogicState.Standing, null, false);
        Assert.Equal(3, clock.VisualFrame);

        for (int i = 0; i < 4; i++)
            Assert.Equal(3, clock.Observe(3, FighterLogicState.Standing, null, false));

        Assert.Equal(4, clock.Observe(4, FighterLogicState.Standing, null, false));
    }

    [Fact]
    public void Clock_ResetsOnStateOrMoveChange_AndNeverInheritsOldProgress()
    {
        var clock = new FighterVisualClock();
        clock.Observe(0, FighterLogicState.Standing, null, false);
        Assert.Equal(9, clock.Observe(9, FighterLogicState.Standing, null, false));

        Assert.Equal(0, clock.Observe(10, FighterLogicState.Crouching, null, false));
        Assert.Equal(0, clock.Observe(10, FighterLogicState.Startup, "StandLightPunch", false));
        Assert.Equal(0, clock.Observe(12, FighterLogicState.Startup, "FireballPrototype", false));
        Assert.Equal(1, clock.Observe(13, FighterLogicState.Startup, "FireballPrototype", false));
    }

    [Fact]
    public void TwoFighterClocks_AreFullyIndependent()
    {
        var p1 = new FighterVisualClock();
        var p2 = new FighterVisualClock();

        p1.Observe(0, FighterLogicState.Standing, null, false);
        p2.Observe(0, FighterLogicState.Standing, null, false);
        p1.Observe(5, FighterLogicState.Standing, null, false);
        p2.Observe(1, FighterLogicState.Hitstun, null, false);

        Assert.Equal(5, p1.VisualFrame);
        Assert.Equal(0, p2.VisualFrame);
        Assert.Equal(6, p1.Observe(6, FighterLogicState.Standing, null, false));
        Assert.Equal(0, p2.Observe(2, FighterLogicState.Hitstun, null, true));
    }

    // ─────────────────────────────────────────────────────────────
    // C. 真实宿主验收（真实 JSON + 实际战斗逻辑）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Duel_NormalHit_EntersHitstop_ThenRecoversOnTheFirstFreeFrame()
    {
        var frames = new List<PresentationFrame>();
        MatchSession match = StartDuel(frames);
        var source = new ScriptedInput(f =>
            f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        match.Advance(FixedStepScheduler.FixedStepSeconds * 5, source); // 0..4

        var clock = new FighterVisualClock();
        ObserveFrames(clock, frames, fighterIndex: 1);
        Assert.Equal(0, clock.VisualFrame); // 命中帧重置 + 第 1 个冻结帧

        PresentationFrame hit = frames.Single(f => f.Events.Any(e => e.Type == BattleEventType.Hit));
        Assert.Equal(4, hit.StateAfter.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(FighterLogicState.Hitstun, hit.StateAfter.Fighter(1).LogicState);

        frames.Clear();
        match.Advance(FixedStepScheduler.FixedStepSeconds * 5, source); // 5..9
        ObserveFrames(clock, frames, fighterIndex: 1);

        // 帧 5/6/7 仍冻结（开始值 3/2/1），帧 8/9 恢复 → 动画时间 = 2。
        Assert.Equal(2, clock.VisualFrame);
        Assert.Equal(0, match.CurrentBattleState!.Fighter(1).HitstopRemainingFrames);

        // 招式动画仍用权威 MoveElapsedFrames，不受表现时钟影响。
        AnimationSet set = PresentationDataLoader.Load(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "presentation", "Kai.json")),
            "data/presentation/Kai.json");
        ResolvedPose pose = AnimationResolver.Resolve(
            match.CurrentBattleState.Fighter(1), set, clock.VisualFrame);
        Assert.Equal("hurt", pose.ClipName);
    }

    [Fact]
    public void TeamBout_NormalHit_IsDeliveredWithBoutScope()
    {
        var frames = new List<PresentationFrame>();
        TeamMatchSession session = StartTeam(frames);

        session.StepExact(12, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        Assert.All(frames, f => Assert.Equal(1, f.Scope));
        PresentationFrame hit = frames.Single(f => f.Events.Any(e => e.Type == BattleEventType.Hit));
        Assert.Equal(3, hit.LogicFrame);
        Assert.Equal(4, hit.StateAfter.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(970, hit.StateAfter.Fighter(1).Health);

        var director = new FeedbackDirector();
        FeedbackCue cue = Assert.Single(ConsumeAll(frames, director));
        Assert.Equal(FeedbackKind.Hit, cue.Kind);

        // 交锋 1 的反馈锚点 = 命中帧的被击者位置（真实坐标）。
        double expectedX = hit.StateAfter.Fighter(1).PositionX.ToDouble();
        (double x, _) = FeedbackGeometry.Anchor(cue, hit.StateAfter);
        Assert.Equal(expectedX, x, 6);
    }

    [Fact]
    public void RealDoubleKo_DeliversBothSidesFeedback()
    {
        var frames = new List<PresentationFrame>();
        MatchSession match = NewLethalDuel(frames);
        match.SelectFighters("Kai", "Kai");
        match.StartMatch();

        // 双方同帧出 5A（镜像 Kai）→ 双向命中 → 真实 Double KO。
        match.StepExact(8, f => f == 0
            ? F(f, InputBits.LightPunch, InputBits.LightPunch)
            : FrameInput.Empty(f));

        PresentationFrame ko = frames.Single(f =>
            f.Events.Any(e => e.Type == BattleEventType.DoubleKnockout));

        BattleEvent[] hits = ko.Events.Where(e => e.Type == BattleEventType.Hit).ToArray();
        Assert.Equal(2, hits.Length);
        Assert.Equal(new[] { 0, 1 }, hits.Select(e => e.TargetFighterId).OrderBy(id => id));
        Assert.Contains(ko.Events, e => e.Type == BattleEventType.RoundEnded);
        Assert.Equal(0, ko.StateAfter.Fighter(0).Health);
        Assert.Equal(0, ko.StateAfter.Fighter(1).Health);

        var director = new FeedbackDirector();
        IReadOnlyList<FeedbackCue> cues = ConsumeAll(frames, director);
        Assert.Equal(2, cues.Count(c => c.Kind == FeedbackKind.Hit));
        Assert.Single(cues, c => c.Kind == FeedbackKind.DoubleKnockout);
        Assert.Equal(3, director.EmittedCount);
    }

    private static void ObserveFrames(FighterVisualClock clock, IEnumerable<PresentationFrame> frames, int fighterIndex)
    {
        foreach (PresentationFrame frame in frames)
        {
            FighterState before = frame.StateBefore.Fighter(fighterIndex);
            FighterState after = frame.StateAfter.Fighter(fighterIndex);
            clock.Observe(frame.LogicFrame, after.LogicState, after.MoveId,
                before.HitstopRemainingFrames > 0);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // D. 帧率一致性（30 / 60 / 144 FPS）
    // ─────────────────────────────────────────────────────────────

    private readonly record struct ObservedCue(
        long Identity, FeedbackKind Kind, int LogicFrame, int Scope, int Target, double X, double Y);

    private static (MatchState State, BattleState Battle, List<ObservedCue> Cues, int VisualFrame) DriveAt(
        double delta, int renders)
    {
        var frames = new List<PresentationFrame>();
        MatchSession match = StartDuel(frames);
        var source = new ScriptedInput(f =>
            f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        for (int i = 0; i < renders; i++)
            match.Advance(delta, source);

        var cues = new List<ObservedCue>();
        var director = new FeedbackDirector();
        var clock = new FighterVisualClock();

        foreach (PresentationFrame frame in frames)
        {
            FighterState before = frame.StateBefore.Fighter(1);
            FighterState after = frame.StateAfter.Fighter(1);
            clock.Observe(frame.LogicFrame, after.LogicState, after.MoveId,
                before.HitstopRemainingFrames > 0);

            foreach (BattleEvent e in frame.Events)
            {
                if (!director.TryConsume(frame.Scope, e, out FeedbackCue cue))
                    continue;
                (double x, double y) = FeedbackGeometry.Anchor(cue, frame.StateAfter);
                cues.Add(new ObservedCue(cue.Identity, cue.Kind, frame.LogicFrame, frame.Scope,
                    cue.TargetFighterId, x, y));
            }
        }

        return (match.State, match.CurrentBattleState!, cues, clock.VisualFrame);
    }

    /// <summary>MatchState 按可观测字段投影（规则实例是共享不可变对象，不参与跨会话比较）。</summary>
    private static (MatchPhase, int, int, int, int, RoundOutcome, RoundEndReason, MatchOutcome, int, string?, string?)
        Snapshot(MatchState s) => (
            s.Phase, s.RoundNumber, s.RoundsPlayed, s.P1Wins, s.P2Wins,
            s.CurrentRoundOutcome, s.CurrentRoundReason, s.Outcome, s.RemainingTimeFrames,
            s.P1FighterId, s.P2FighterId);

    [Fact]
    public void RenderRates_30_60_144_AgreeOnStateEventsPositionsAndAnimation()
    {
        (MatchState s60, BattleState b60, List<ObservedCue> c60, int v60) =
            DriveAt(FixedStepScheduler.FixedStepSeconds, renders: 30);
        (MatchState s30, BattleState b30, List<ObservedCue> c30, int v30) =
            DriveAt(FixedStepScheduler.FixedStepSeconds * 2, renders: 15);
        (MatchState s144, BattleState b144, List<ObservedCue> c144, int v144) =
            DriveAt(FixedStepScheduler.FixedStepSeconds / 2.4, renders: 72);

        // 权威状态一致（含最终帧号与两名角色的权威状态）。
        Assert.Equal(Snapshot(s60), Snapshot(s30));
        Assert.Equal(Snapshot(s60), Snapshot(s144));
        Assert.Equal(b60.Frame, b30.Frame);
        Assert.Equal(b60.Frame, b144.Frame);
        Assert.Equal(b60.Fighters, b30.Fighters);
        Assert.Equal(b60.Fighters, b144.Fighters);

        // 反馈事件的数量、顺序、身份、逻辑帧与坐标完全一致。
        Assert.Equal(c60, c30);
        Assert.Equal(c60, c144);
        Assert.NotEmpty(c60);

        // 最终动画时间线一致。
        Assert.Equal(v60, v30);
        Assert.Equal(v60, v144);
        Assert.True(v60 > 0);
    }
}
