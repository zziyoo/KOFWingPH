using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
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
/// 阶段 10 统一集成验收：用<b>真实 JSON 配置</b>跑完整的 3v3 队伍赛，验证
/// 一名角色连挑三人、全场比赛确定性、不同渲染帧率一致、StepExact 与 Advance 等价、
/// 跨交锋事件身份唯一，以及动画/反馈在整场比赛中都不改变权威结果。
/// </summary>
public class Stage10AcceptanceTests
{
    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) =>
        new(frame, p1, p2);

    private static FighterCatalog Catalog() => FighterDataLoader.LoadCatalog(new[]
    {
        new FighterDataDocument("data/fighters/Kai.json", Json("Kai")),
        new FighterDataDocument("data/fighters/Rei.json", Json("Rei")),
    });

    private static string Json(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "fighters", $"{name}.json"));

    /// <summary>
    /// 把 JSON 角色的 5A 伤害改大，让一场交锋几帧内结束（不改 JSON 文件本身）。
    /// <b>必须按传入的角色 ID 取定义</b>——固定成「P1=Kai / P2=Rei」会让 P2 的招式集与选到的角色不符。
    /// </summary>
    private static FightRoster LethalRoster(string p1FighterId, string p2FighterId)
    {
        FighterCatalog catalog = Catalog();
        FighterDefinition Boost(FighterDefinition def) => new(
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

        return new FightRoster(
            Boost(catalog.Get(p1FighterId).Definition),
            Boost(catalog.Get(p2FighterId).Definition));
    }

    /// <summary>±30 站位、满血、按规则注入计时的交锋初态。</summary>
    private static BattleState CloseBout(TeamMatchRules rules, TeamSideState p1, TeamSideState p2)
    {
        FighterState f1 = FighterState.Create(0, p1.ActiveMaxHealth, -30, Facing.Right) with
        {
            Health = p1.ActiveHealth,
        };
        FighterState f2 = FighterState.Create(1, p2.ActiveMaxHealth, 30, Facing.Left) with
        {
            Health = p2.ActiveHealth,
        };
        return new BattleState(0, new[] { f1, f2 }, BattleState.CreateDefault().StateVersion,
            round: null, projectiles: null, nextProjectileId: 1,
            roundTimerRemainingFrames: rules.EnableTimeout ? rules.BoutTimerFrames : null);
    }

    private static TeamMatchSession NewTeamSession(
        Action<TeamMatchEvent>? events = null,
        TeamMatchRules? rules = null) =>
        new(rules ?? TeamMatchRules.CreateDefault(),
            (p1, p2) => LethalRoster(p1, p2),
            CloseBout,
            events);

    private static void StartTeam(TeamMatchSession session)
    {
        TeamDefinition p1 = new("P1", new[]
        {
            new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000),
        });
        // 双方都用 Kai：Rei 的 JSON 没有裸 5A（只有 236A/214B/5D/5C），
        // 只按轻拳的一方在镜像以外根本不会出招。
        TeamDefinition p2 = new("P2", new[]
        {
            new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000),
            new TeamMemberDefinition("Kai", 1000),
        });
        session.SelectTeams(p1, p2);
        session.StartMatch();
    }

    /// <summary>真实 JSON 的 5A 是 3 帧前摇：按下一拳需要 6 帧才结算完。</summary>
    private const int BoutFrames = 6;

    private static void PlayBout(TeamMatchSession session) =>
        session.StepExact(BoutFrames,
            f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

    [Fact]
    public void ScenarioA_OneFighterBeatsThreeAndEndsTheTeamMatch()
    {
        var session = NewTeamSession();
        StartTeam(session);

        for (int i = 0; i < 3; i++)
        {
            PlayBout(session);
            if (i < 2)
            {
                Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
                session.StartNextBout();
                Assert.Equal(i + 1, session.State.P2!.ActiveSlot);
                Assert.Equal(0, session.State.P1!.ActiveSlot);
            }
        }

        Assert.Equal(TeamMatchPhase.MatchFinished, session.State.Phase);
        Assert.Equal(TeamMatchOutcome.P1TeamWin, session.State.Outcome);
        Assert.True(session.State.P2!.IsExhausted);
        Assert.Equal(3, session.State.BoutsPlayed);
        Assert.Equal(3, session.State.P1!.RemainingCount);
    }

    [Fact]
    public void ScenarioB_AlternatingWinsReachTheDecider()
    {
        // 双方都用「一击必杀」，谁先出手谁赢 → 交替获胜，第 3 场决胜。
        var session = NewTeamSession();
        StartTeam(session);

        // 第 1 场：P1 出手。
        session.StepExact(BoutFrames,
            f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        session.StartNextBout();

        // 第 2 场：只有 P2 出手（P1 不按）。
        session.StepExact(BoutFrames,
            f => f == 0 ? F(f, InputBits.None, InputBits.LightPunch) : FrameInput.Empty(f));

        Assert.Equal(TeamMatchPhase.BetweenBouts, session.State.Phase);
        Assert.True(session.State.P1!.Eliminated[0]);
        Assert.True(session.State.P2!.Eliminated[0]);
        Assert.Equal(1, session.State.P1.ActiveSlot);
        Assert.Equal(1, session.State.P2.ActiveSlot);
        Assert.Equal(2, session.State.BoutsPlayed);
    }

    [Fact]
    public void ScenarioC_FullMatchEvents_HaveUniqueIdentityAcrossBouts()
    {
        var collected = new List<TeamMatchEvent>();
        var session = NewTeamSession(collected.Add);
        StartTeam(session);
        for (int i = 0; i < 3; i++)
        {
            PlayBout(session);
            if (i < 2) session.StartNextBout();
        }

        Assert.NotEmpty(collected);
        Assert.Equal(3, collected.Select(e => e.BoutNumber).Distinct().Count());
        Assert.Equal(collected.Count, collected.Select(e => e.Identity).Distinct().Count());
        Assert.All(collected, e => Assert.InRange(e.BoutNumber, 1, 3));

        // 裸 EventId 在跨交锋时必然重号 —— 组合身份才是唯一键。
        Assert.True(collected.Select(e => e.BoutEvent.EventId).Distinct().Count()
            < collected.Count);
    }

    [Fact]
    public void ScenarioD_SameInputs_ProduceIdenticalMatchStateAndEvents()
    {
        static (TeamMatchState State, List<TeamMatchEventId> Events) Run()
        {
            var events = new List<TeamMatchEvent>();
            var session = NewTeamSession(events.Add);
            StartTeam(session);
            for (int i = 0; i < 3; i++)
            {
                PlayBout(session);
                if (session.State.Phase == TeamMatchPhase.BetweenBouts)
                    session.StartNextBout();
            }
            return (session.State, events.Select(e => e.Identity).ToList());
        }

        (TeamMatchState s1, List<TeamMatchEventId> e1) = Run();
        (TeamMatchState s2, List<TeamMatchEventId> e2) = Run();

        Assert.Equal(s1, s2);
        Assert.Equal(e1, e2);
    }

    [Fact]
    public void ScenarioE_TeamMatch_10000Frames_IsDeterministic()
    {
        static (TeamMatchState State, int Frames, List<TeamMatchEventId> Events) Run()
        {
            var events = new List<TeamMatchEvent>();
            var session = NewTeamSession(events.Add);
            StartTeam(session);

            // 输入用一个固定但复杂的模式：每 7 帧按一次轻拳，交替按方向。
            int guard = 0;
            while (guard < 10000 && session.State.Phase == TeamMatchPhase.BoutInProgress)
            {
                int f = session.CurrentBattleState!.Frame;
                InputBits p1 = f % 7 == 0 ? InputBits.LightPunch : InputBits.None;
                InputBits p2 = f % 11 == 0 ? InputBits.LightPunch : InputBits.None;
                session.StepExact(1, frame => new FrameInput(frame, p1, p2));
                guard++;
            }

            return (session.State, guard, events.Select(e => e.Identity).ToList());
        }

        (TeamMatchState s1, int frames1, List<TeamMatchEventId> e1) = Run();
        (TeamMatchState s2, int frames2, List<TeamMatchEventId> e2) = Run();

        Assert.Equal(s1, s2);
        Assert.Equal(frames1, frames2);
        Assert.Equal(e1, e2);
        Assert.True(frames1 <= 10000);
        Assert.Equal(e1.Count, e1.Distinct().Count());
    }

    [Fact]
    public void ScenarioF_RenderRates_AgreeOnTeamBouts()
    {
        static TeamMatchSession Drive(double delta, int batches)
        {
            var session = NewTeamSession();
            StartTeam(session);
            var source = new ScriptedInput(f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
            for (int i = 0; i < batches; i++)
                session.Advance(delta, source);
            return session;
        }

        TeamMatchSession at60 = Drive(FixedStepScheduler.FixedStepSeconds, 10);
        TeamMatchSession at30 = Drive(FixedStepScheduler.FixedStepSeconds * 2, 5);
        TeamMatchSession at144 = Drive(FixedStepScheduler.FixedStepSeconds / 2.4, 24);

        Assert.Equal(at60.State, at30.State);
        Assert.Equal(at60.State, at144.State);
        Assert.Equal(at60.CurrentBattleState!.Fighter(0), at30.CurrentBattleState!.Fighter(0));
        Assert.Equal(at60.CurrentBattleState.Fighter(1), at144.CurrentBattleState!.Fighter(1));
        Assert.True(at60.IsFrameSyncIntact);
        Assert.True(at30.IsFrameSyncIntact);
        Assert.True(at144.IsFrameSyncIntact);
    }

    [Fact]
    public void ScenarioG_StepExactAndAdvance_AgreeOnATeamBout()
    {
        static TeamMatchSession Exact()
        {
            var session = NewTeamSession();
            StartTeam(session);
            session.StepExact(12, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
            return session;
        }

        static TeamMatchSession Scheduled()
        {
            var session = NewTeamSession();
            StartTeam(session);
            var source = new ScriptedInput(f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
            for (int i = 0; i < 12 && session.State.IsBoutLive; i++)
                session.Advance(FixedStepScheduler.FixedStepSeconds, source);
            return session;
        }

        TeamMatchSession exact = Exact();
        TeamMatchSession scheduled = Scheduled();

        Assert.Equal(exact.State, scheduled.State);
        Assert.Equal(exact.CurrentBattleState!.Frame, scheduled.CurrentBattleState!.Frame);
        Assert.Equal(exact.CurrentBattleState.Fighter(0), scheduled.CurrentBattleState.Fighter(0));
        Assert.Equal(exact.CurrentBattleState.Fighter(1), scheduled.CurrentBattleState.Fighter(1));
    }

    [Fact]
    public void ScenarioH_AnimationAndFeedback_NeverChangeTheMatchResult()
    {
        static TeamMatchState Run(bool drivePresentation)
        {
            var session = NewTeamSession();
            StartTeam(session);
            if (drivePresentation)
            {
                AnimationSet set = PresentationDataLoader.Load(
                    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "presentation", "Kai.json")),
                    "data/presentation/Kai.json");
                var director = new FeedbackDirector();
                var clock = new FighterVisualClock();

                for (int i = 0; i < 3; i++)
                {
                    session.StepExact(BoutFrames,
                        f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
                    BattleState state = session.CurrentBattleState!;
                    // 只读投影：解析动画、消费反馈、推进视觉时钟。
                    AnimationResolver.Resolve(state.Fighter(0), set,
                        clock.Sync(state.Frame, state.Fighter(0).LogicState,
                            state.Fighter(0).MoveId, state.Fighter(0).HitstopRemainingFrames));
                    foreach (TeamMatchEvent e in session.LastEvents)
                        director.TryConsume(e.BoutNumber, e.BoutEvent, out _);

                    if (i < 2) session.StartNextBout();
                }
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    session.StepExact(BoutFrames,
                        f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
                    if (i < 2) session.StartNextBout();
                }
            }

            return session.State;
        }

        Assert.Equal(Run(drivePresentation: false), Run(drivePresentation: true));
    }

    private sealed class ScriptedInput : IInputSource
    {
        private readonly Func<int, FrameInput> _provider;
        public ScriptedInput(Func<int, FrameInput> provider) => _provider = provider;
        public FrameInput Prepare(int frame) => _provider(frame);
        public void Commit(FrameInput input) { }
    }
}
