using NewKOF.BattleCore.AI;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;
using System.Text.Json.Nodes;

namespace NewKOF.BattleCore.Tests;

public class AiControllerTests
{
    private static FighterDefinition Load(string id) => FighterDataLoader.Load(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "data", "fighters", id + ".json")));
    private static FightRoster Roster() => new(Load("Kai"), Load("Rei"));
    private static FightRoster TechRoster()
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "fighters", "Kai.json")))!;
        foreach (var move in json["moves"]!.AsArray())
            if (move?["throws"] is JsonArray throws)
                foreach (var definition in throws) definition!["techable"] = true;
        return new(FighterDataLoader.Load(json.ToJsonString()), Load("Rei"));
    }
    private static AiDifficultyProfile Profile(int delay = 1, int reaction = 0, int guard = 0,
        int attack = 1000, int throws = 0, int tech = 1000, int techReaction = 1) =>
        new(delay, reaction, 1, guard, attack, throws, tech, 0, 2, 85, 1000, techReaction);
    private static BattleState Duel(int distance = 70, int frame = 0) => new(frame,
        new[] { FighterState.Create(0, 1000, -distance, Facing.Right), FighterState.Create(1, 1000, 0, Facing.Left) },
        BattleState.CreateDefault().StateVersion);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RealRoster_InputOnlySessionStartsMovesAndDealsDamage(int player)
    {
        var roster = Roster();
        var events = new List<BattleEvent>();
        var session = new BattleSession(Duel(50), simulation: new BattleSimulation(roster),
            eventsCommitted: (_, _, emitted) => events.AddRange(emitted));
        var policy = new AiController(Profile());
        var source = new AiInputSource(() => session.CurrentState, roster, new InputBuffer(),
            player == 0 ? policy : null, player == 1 ? policy : null);
        session.StepExact(240, inputSource: source);
        Assert.Contains(events, e => e.Type == BattleEventType.MoveStarted);
        Assert.Contains(events, e => e.Type == BattleEventType.Hit);
        Assert.True(session.CurrentState.Fighter(1-player).Health < 1000);
        Assert.Equal(240, source.PolicyState(player)!.NextFrame);
        Assert.Equal(121, source.PolicyState(player)!.Observations.Count);
        Assert.Contains(source.History, f => (f.ForPlayer(player) & DirectionResolver.AttackButtons) != 0);
    }

    [Fact]
    public void RemoteOpponent_SelectsConfiguredProjectileAndActuallySpawnsIt()
    {
        var roster = Roster();
        var events = new List<BattleEvent>();
        var session = new BattleSession(Duel(360), simulation: new BattleSimulation(roster),
            eventsCommitted: (_, _, emitted) => events.AddRange(emitted));
        var source = new AiInputSource(() => session.CurrentState, roster, new InputBuffer(), new AiController(Profile()));
        session.StepExact(2, inputSource: source);
        string target = Assert.IsType<string>(source.PolicyState(0)!.TargetMove);
        Assert.NotEmpty(roster.Player1.FindMoveById(target)!.Projectiles);
        session.StepExact(100, inputSource: source);
        Assert.True(session.CurrentState.NextProjectileId > 1);
    }

    [Fact]
    public void CloseOpponent_CanBeThrownThroughRealCommandRecognition()
    {
        var roster = Roster();
        var events = new List<BattleEvent>();
        var session = new BattleSession(Duel(45), simulation: new BattleSimulation(roster),
            eventsCommitted: (_, _, emitted) => events.AddRange(emitted));
        var source = new AiInputSource(() => session.CurrentState, roster, new InputBuffer(),
            new AiController(Profile(attack: 0, throws: 1000)));
        session.StepExact(50, inputSource: source);
        Assert.Contains(events, e => e.Type == BattleEventType.ThrowHit);
    }

    [Fact]
    public void ThreatRequiresObservationDelayPlusReactionAndBlocksRealAttack()
    {
        var roster = Roster();
        var events = new List<BattleEvent>();
        var session = new BattleSession(Duel(70), simulation: new BattleSimulation(roster),
            eventsCommitted: (_, _, emitted) => events.AddRange(emitted));
        var human = new InputBuffer();
        var source = new AiInputSource(() => session.CurrentState, roster, human, player2:
            new AiController(Profile(delay: 1, reaction: 1, guard: 1000, attack: 0)));
        var move = roster.Player1.Moves.First(m => m.Attacks.Count > 0 && m.StartupFrames >= 4);
        var command = roster.Player1.Commands.Single(c => c.CommandId == move.CommandId);
        foreach (var step in AiCommandPlanner.Plan(command))
        {
            human.Sample(AiCommandPlanner.ToInput(step, Facing.Right), InputBits.None);
            session.StepExact(1, inputSource: source);
        }
        human.Sample(InputBits.None, InputBits.None);
        session.StepExact(40, inputSource: source);
        int started = events.First(e => e.Type == BattleEventType.MoveStarted).Frame;
        var firstBack = source.History.First(f => (f.Player2 & InputBits.Right) != 0);
        Assert.True(firstBack.Frame >= started + 3);
        Assert.Contains(events, e => e.Type == BattleEventType.Blocked);
    }

    [Fact]
    public void SameCandidateIsPureAndCommittedSnapshotReplaysExactly()
    {
        var roster = Roster();
        var observation = new AiObservation(Duel(frame: 1), roster, 0);
        var old = new AiPolicyState(1, 123, "Kai", observations: new[] { new AiObservation(Duel(), roster, 0) });
        var policy = new AiController(Profile());
        var a = policy.Decide(observation, old);
        var b = new AiController(Profile()).Decide(observation, old);
        Assert.Equal(a.Bits, b.Bits);
        Assert.Equal(a.State.RandomState, b.State.RandomState);
        Assert.Equal(a.State.TargetMove, b.State.TargetMove);
        Assert.Equal(a.State.Plan.ToArray(), b.State.Plan.ToArray());
        Assert.Equal(123u, old.RandomState);
        Assert.Empty(old.Plan);
        Assert.Equal(1, old.NextFrame);
    }

    [Fact]
    public void TerminalAndLostControlClearScriptsWithoutAttackInput()
    {
        var roster = Roster();
        var old = new AiPolicyState(0, 3, "Kai", new[] { new AiCommandStep(CommandDirection.Forward, InputBits.HeavyPunch) });
        var finished = new BattleState(0, Duel().Fighters, BattleState.CreateDefault().StateVersion,
            RoundState.ForOutcome(RoundOutcome.P1Win, RoundEndReason.Knockout));
        var policy = new AiController(Profile());
        var terminal = policy.Decide(new(finished, roster, 0), old);
        Assert.Equal(InputBits.None, terminal.Bits);
        Assert.Empty(terminal.State.Plan);
        var fighters = Duel().Fighters.ToArray();
        fighters[0] = fighters[0] with { LogicState = FighterLogicState.Hitstun, StunRemainingFrames = 10 };
        var stunned = policy.Decide(new(new BattleState(0, fighters, finished.StateVersion), roster, 0), old);
        Assert.Equal(InputBits.None, stunned.Bits);
        Assert.Empty(stunned.State.Plan);
        Assert.Equal("Interrupted", stunned.State.Action);
    }

    [Fact]
    public void CorruptRestoredCursorIsRejected()
    {
        var roster = Roster();
        Assert.Throws<ArgumentException>(() => new AiController(Profile()).Decide(new(Duel(), roster, 0),
            new AiPolicyState(0, 1, "Kai") with { PlanIndex = 1 }));
    }

    [Theory]
    [InlineData(1, 1000, true)]
    [InlineData(2, 1000, true)]
    [InlineData(3, 1000, false)]
    [InlineData(1, 0, false)]
    public void RealPendingGrab_RespectsTechWindowAndProbability(int reaction, int chance, bool expected)
    {
        var roster = TechRoster();
        var events = new List<BattleEvent>();
        var session = new BattleSession(Duel(45), simulation: new BattleSimulation(roster),
            eventsCommitted: (_, _, emitted) => events.AddRange(emitted));
        var human = new InputBuffer();
        var source = new AiInputSource(() => session.CurrentState, roster, human, player2:
            new AiController(Profile(attack: 0, tech: chance, techReaction: reaction)));
        var move = roster.Player1.Moves.First(m => m.Throws.Count > 0);
        var command = roster.Player1.Commands.Single(c => c.CommandId == move.CommandId);
        foreach (var step in AiCommandPlanner.Plan(command))
        {
            human.Sample(AiCommandPlanner.ToInput(step, Facing.Right), InputBits.None);
            session.StepExact(1, inputSource: source);
        }
        human.Sample(InputBits.None, InputBits.None);
        session.StepExact(20, inputSource: source);
        Assert.Equal(expected, events.Any(e => e.Type == BattleEventType.ThrowTech));
        Assert.Equal(expected ? 1000 : 940, session.CurrentState.Fighter(1).Health);
    }

    [Theory]
    [InlineData(AiDifficulty.Hard)]
    [InlineData(AiDifficulty.Expert)]
    public void HardAndExpertTechSomeGrabsAndMissOthers(AiDifficulty difficulty)
    {
        var roster = TechRoster();
        var sim = new BattleSimulation(roster);
        var move = roster.Player1.Moves.First(m => m.Throws.Count > 0);
        var command = roster.Player1.Commands.Single(c => c.CommandId == move.CommandId);
        var state = Duel(45);
        foreach (var step in AiCommandPlanner.Plan(command))
            state = sim.Step(state, new(state.Frame, AiCommandPlanner.ToInput(step, Facing.Right), InputBits.None)).State;
        while (state.PendingThrow is null)
            state = sim.Step(state, new(state.Frame, InputBits.None, InputBits.None)).State;
        while (state.Frame - state.PendingThrow!.StartedFrame < AiDifficultyProfile.For(difficulty).TechReactionFrames)
            state = sim.Step(state, new(state.Frame, InputBits.None, InputBits.None)).State;
        var observation = new AiObservation(state, roster, 1);
        int successes = 0;
        var policy = new AiController(AiDifficultyProfile.For(difficulty));
        for (uint seed = 1; seed <= 100; seed++)
        {
            var decision = policy.Decide(observation, new AiPolicyState(state.Frame, seed, "Rei"));
            var result = sim.Step(state, new(state.Frame, InputBits.None, decision.Bits));
            if (result.Events.Any(e => e.Type == BattleEventType.ThrowTech)) successes++;
        }
        Assert.InRange(successes, 1, 99);
    }

    [Fact]
    public void PreheldTechChordIsReleasedBeforeFreshAttempt()
    {
        var roster = TechRoster();
        var move = roster.Player1.Moves.First(m => m.Throws.Count > 0);
        const InputBits chord = InputBits.LightPunch | InputBits.LightKick;
        var fighters = Duel(45, frame: 5).Fighters.ToArray();
        fighters[0] = fighters[0] with {
            LogicState = FighterLogicState.Active, MoveId = move.MoveId,
            PhaseRemainingFrames = 1, MoveStartedFrame = 2, MoveElapsedFrames = 2
        };
        fighters[1] = fighters[1] with {
            Recognition = RecognitionState.Empty.Advance(chord, Facing.Left, 4, 1, roster.Player2.Commands),
            LogicState = FighterLogicState.Hitstun, StunRemainingFrames = 1, LastThrowStartFrame = 2
        };
        var state = new BattleState(5, fighters, BattleState.CreateDefault().StateVersion,
            pendingThrow: new PendingThrow(0, 1, 2, move.MoveId, 4, move.Throws[0]));
        var policy = new AiController(Profile());
        var first = policy.Decide(new(state, roster, 1), new AiPolicyState(5, 7, "Rei"));
        Assert.Equal(InputBits.None, first.Bits);
        var next = new BattleSimulation(roster).Step(state, new(5, InputBits.None, first.Bits)).State;
        var second = policy.Decide(new(next, roster, 1), first.State);
        Assert.Equal(chord, second.Bits);
        Assert.Contains(new BattleSimulation(roster).Step(next, new(6, InputBits.None, second.Bits)).Events,
            e => e.Type == BattleEventType.ThrowTech);
    }

    [Theory]
    [InlineData(AttackHeight.Low, false, InputBits.Left | InputBits.Down)]
    [InlineData(AttackHeight.Mid, false, InputBits.Left)]
    [InlineData(AttackHeight.Mid, true, InputBits.Left)]
    [InlineData(AttackHeight.Low, true, InputBits.None)]
    public void DelayedVisibleHeightSelectsGroundAndAirGuard(AttackHeight height, bool air, InputBits expected)
    {
        var roster = Roster();
        var move = roster.Player2.Moves.First(m => m.Attacks.Any(a => a.Height == height));
        var policy = new AiController(Profile(delay: 2, reaction: 2, guard: 1000, attack: 0));
        var snapshot = new AiPolicyState(0, 123, "Kai");
        for (int frame = 0; frame <= 4; frame++)
        {
            var fighters = Duel().Fighters.ToArray();
            if (air) fighters[0] = fighters[0] with { LogicState = FighterLogicState.Airborne, PositionY = 20, JumpKind = JumpKind.ShortHop };
            fighters[1] = fighters[1] with { LogicState = FighterLogicState.Startup, MoveId = move.MoveId,
                PhaseRemainingFrames = 5, MoveStartedFrame = 0, MoveElapsedFrames = frame };
            var observation = new AiObservation(new(frame, fighters, BattleState.CreateDefault().StateVersion), roster, 0);
            var result = policy.Decide(observation, snapshot);
            // The first snapshot with the threat is frame 0. Perception at 2, response at 4.
            if (frame < 4) Assert.Equal(InputBits.None, result.Bits & (InputBits.Left | InputBits.Down));
            else Assert.Equal(expected, result.Bits);
            snapshot = result.State;
        }
    }

    [Fact]
    public void ExistingScriptKeepsRelativeStepsAcrossOpponentChangesAndFacingFlip()
    {
        var roster = Roster();
        var plan = new[] { new AiCommandStep(CommandDirection.Down, InputBits.None),
            new AiCommandStep(CommandDirection.Forward, InputBits.HeavyPunch) };
        var snapshot = new AiPolicyState(1, 5, "Kai", plan,
            new[] { new AiObservation(Duel(), roster, 0) });
        var fighters = Duel().Fighters.ToArray();
        fighters[0] = fighters[0] with { Facing = Facing.Left };
        var state = new BattleState(1, fighters, BattleState.CreateDefault().StateVersion);
        var result = new AiController(Profile(guard: 1000)).Decide(new(state, roster, 0), snapshot with { PlanIndex = 1 });
        Assert.Equal(InputBits.Left | InputBits.HeavyPunch, result.Bits);
        Assert.Equal(5u, result.State.RandomState);
    }
}
