using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class MultiHitCancelTests
{
    private const string Heavy = "NearHeavy";
    private const string Light = "Light";
    private const string Wave = "Wave";
    private const string Helper = "Helper";
    private const uint Modulus = 2147483647;

    [Theory]
    [InlineData(62, false, false, 12, 15, 4, 1u)]
    [InlineData(62, true, true, 12, 15, 4, 1u)]
    [InlineData(62, false, false, 17, 17, 6, 1u)]
    [InlineData(62, false, true, 26, 29, 7, 3u)]
    [InlineData(62, true, false, 26, 29, 7, 3u)]
    [InlineData(62, false, false, 34, 34, 12, 3u)]
    [InlineData(68, false, false, 6, 6, 6, 0u)]
    [InlineData(68, false, false, 15, 18, 7, 2u)]
    public void CancelKeepsSettledContactAndPreemptsUncommittedGroups(int distance, bool guard, bool mirrored,
        int commandFrame, int cancelFrame, int oldTick, uint settledGroups)
    {
        BattleSimulation simulation = Simulation();
        BattleState state = Duel(distance, mirrored);
        FrameInput[] inputs = Enumerable.Range(0, cancelFrame + 32)
            .Select(frame => Inputs(frame, commandFrame, guard, mirrored)).ToArray();
        var results = new List<BattleStepResult>();
        foreach (FrameInput input in inputs)
        {
            BattleStepResult step = simulation.Step(state, input);
            results.Add(step);
            state = step.State;
        }

        BattleState before = results[cancelFrame - 1].State;
        BattleStepResult cancel = results[cancelFrame];
        Assert.Equal((Heavy, 0, oldTick - 1), (before.Fighter(0).MoveId, before.Fighter(0).MoveStartedFrame,
            before.Fighter(0).MoveElapsedFrames));
        Assert.Equal((Wave, cancelFrame, 0, -1, -1), (cancel.State.Fighter(0).MoveId,
            cancel.State.Fighter(0).MoveStartedFrame, cancel.State.Fighter(0).MoveElapsedFrames,
            cancel.State.Fighter(0).HitConfirmFrame, cancel.State.Fighter(0).BlockConfirmFrame));
        Assert.Empty(cancel.Contacts);
        BattleEvent newMove = Assert.Single(cancel.Events);
        Assert.Equal((BattleEventType.MoveStarted, Wave), (newMove.Type, newMove.MoveId));
        Assert.Equal(before.Fighter(1).Health, cancel.State.Fighter(1).Health);
        Assert.Equal(settledGroups, cancel.State.Fighter(1).LastHitContactGroups);
        if (settledGroups != 0)
            Assert.Equal(0, cancel.State.Fighter(1).LastHitAttackStartFrame);
        Assert.Equal(Math.Max(0, before.Fighter(1).HitstopRemainingFrames - 1),
            cancel.State.Fighter(1).HitstopRemainingFrames);

        int[] oldContactFrames = settledGroups switch { 1u => [3], 2u => [6], 3u => [3, 17], _ => [] };
        BattleEvent[] oldContacts = results.SelectMany(result => result.Events).Where(IsHeavyContact).ToArray();
        Assert.Equal(oldContactFrames, oldContacts.Select(contact => contact.Frame));
        Assert.All(oldContacts, contact => Assert.Equal(guard ? BattleEventType.Blocked : BattleEventType.Hit, contact.Type));
        Assert.Equal(1000 - oldContacts.Sum(contact => contact.Amount), cancel.State.Fighter(1).Health);
        Assert.DoesNotContain(results.Skip(cancelFrame).SelectMany(result => result.Contacts), contact => contact.MoveId == Heavy);
        Assert.Equal((1, false), (cancel.State.Fighter(0).DamageScript.ContactDivisor,
            cancel.State.Fighter(0).DamageScript.ContactPending));
        Assert.Equal(NextRandom(before.Fighter(0).DamageScript.RandomState), cancel.State.Fighter(0).DamageScript.RandomState);
        Assert.Equal(cancelFrame, cancel.State.Fighter(0).RecognitionState.LastObservedFrame);

        if (oldContactFrames.Length > 0)
        {
            int lastContact = oldContactFrames[^1];
            FighterState attacker = results[lastContact].State.Fighter(0);
            Assert.Equal(11, attacker.HitstopRemainingFrames);
            Assert.Equal(guard ? 11 : 13, results[lastContact].State.Fighter(1).HitstopRemainingFrames);
            for (int frame = lastContact + 1; frame <= lastContact + 11; frame++)
            {
                FighterState frozen = results[frame].State.Fighter(0);
                Assert.Equal(attacker.MoveElapsedFrames, frozen.MoveElapsedFrames);
                Assert.Equal(attacker.DamageScript.RandomState, frozen.DamageScript.RandomState);
            }
        }

        Assert.Empty(results[cancelFrame + 14].State.Projectiles);
        ProjectileState entity = Assert.Single(results[cancelFrame + 15].State.Projectiles);
        Assert.Equal((Helper, Wave, 0, 25), (entity.TypeId, entity.SourceMoveId, entity.OwnerFighterId, entity.RemainingLifetime));
        Assert.Equal(mirrored ? Facing.Left : Facing.Right, entity.SpawnFacing);
        BattleEvent waveContact = Assert.Single(results.SelectMany(result => result.Events),
            contact => contact.MoveId == Helper && contact.Type is BattleEventType.ProjectileHit or BattleEventType.ProjectileBlocked);
        Assert.Equal(cancelFrame + 16, waveContact.Frame);

        Replay(simulation, results, inputs, 1);
        Replay(simulation, results, inputs, oldContactFrames.LastOrDefault() + 1);
        Replay(simulation, results, inputs, cancelFrame + 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SweepCancelReplaysAcrossShakeAndRecoveryWithoutRearmingWave(bool mirrored)
    {
        BattleSimulation simulation = Simulation(withSweep: true);
        BattleState state = Duel(62, mirrored);
        InputBits forward = mirrored ? InputBits.Left : InputBits.Right;
        FrameInput[] inputs = Enumerable.Range(0, 118)
            .Select(frame => new FrameInput(frame, frame == 0 ? InputBits.Down | InputBits.HeavyKick
                : Motion(frame, 14, forward), InputBits.None)).ToArray();
        var results = new List<BattleStepResult>();
        foreach (FrameInput input in inputs)
        {
            BattleStepResult result = simulation.Step(state, input);
            results.Add(result);
            state = result.State;
        }

        Assert.Equal((11, 13), (results[10].State.Fighter(0).HitstopRemainingFrames,
            results[10].State.Fighter(1).HitstopRemainingFrames));
        Assert.Equal(LaunchReactionPhase.Shake, results[17].State.Fighter(1).LaunchReaction!.Value.Phase);
        BattleStepResult cancel = results[22];
        Assert.Equal((Wave, 22, 0, -1, -1), (cancel.State.Fighter(0).MoveId,
            cancel.State.Fighter(0).MoveStartedFrame, cancel.State.Fighter(0).MoveElapsedFrames,
            cancel.State.Fighter(0).HitConfirmFrame, cancel.State.Fighter(0).BlockConfirmFrame));
        Assert.Equal(results[21].State.Fighter(1).Health, cancel.State.Fighter(1).Health);
        Assert.Empty(cancel.Contacts);
        Assert.Empty(results[36].State.Projectiles);
        Assert.Equal((1, 25, false), (Assert.Single(results[37].State.Projectiles).ProjectileId,
            results[37].State.Projectiles[0].RemainingLifetime, results[37].State.Projectiles[0].HasContact));
        Assert.Empty(results[62].State.Projectiles);
        Assert.Equal(LaunchReactionPhase.Impact, results[55].State.Fighter(1).LaunchReaction!.Value.Phase);
        Assert.Equal(FighterLogicState.Standing, results[107].State.Fighter(1).LogicState);
        Assert.All(results.Skip(10), result => Assert.Equal(results[10].State.Fighter(1).Health,
            result.State.Fighter(1).Health));
        Assert.DoesNotContain(results.SelectMany(result => result.Events), e => e.Type == BattleEventType.ProjectileHit);
        foreach (int snapshot in new[] { 10, 18, 22, 55, 107 })
            Replay(simulation, results, inputs, snapshot);
    }

    [Fact]
    public void RepeatedSweepKeepsItsAttackBoxWithoutHittingTheLyingBounce()
    {
        BattleSimulation simulation = Simulation(withSweep: true);
        BattleState state = Duel(54);
        var hits = new List<int>();
        for (int frame = 0; frame <= 107; frame++)
        {
            InputBits sweep = frame is 0 or 53 ? InputBits.Down | InputBits.HeavyKick
                : frame is 51 or 52 ? InputBits.Right : InputBits.None;
            BattleStepResult result = simulation.Step(state, new FrameInput(frame, sweep, 0));
            state = result.State;
            if (frame == 53)
                Assert.Equal(("Sweep", 53, 0), (state.Fighter(0).MoveId, state.Fighter(0).MoveStartedFrame,
                    state.Fighter(0).MoveElapsedFrames));
            if (frame == 60)
                Assert.Equal(LaunchReactionPhase.Bounce, state.Fighter(1).LaunchReaction!.Value.Phase);
            if (frame == 63)
                Assert.Single(FighterBoxes.GetActiveHitboxes(state.Fighter(0), simulation.Roster.Player1));
            hits.AddRange(result.Events.Where(e => e.Type == BattleEventType.Hit).Select(e => e.Frame));
        }

        Assert.Equal(new[] { 10 }, hits);
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
    }

    [Fact]
    public void BothSourcesCanCancelWithoutReusingTheFirstEntityWhileItIsAlive()
    {
        BattleSimulation simulation = Simulation();
        BattleState state = Duel(240);
        for (int frame = 0; frame <= 21; frame++)
        {
            InputBits p1 = frame == 0 ? InputBits.HeavyPunch : Motion(frame, 4, InputBits.Right);
            InputBits p2 = frame == 0 ? InputBits.LightPunch : Motion(frame, 6, InputBits.Left);
            BattleStepResult step = simulation.Step(state, new FrameInput(frame, p1, p2));
            state = step.State;
            if (frame == 0)
                Assert.Equal(new[] { Heavy, Light }, state.Fighters.Select(fighter => fighter.MoveId));
            if (frame is 4 or 6)
            {
                int seat = frame == 4 ? 0 : 1;
                BattleEvent entry = Assert.Single(step.Events, e => e.Type == BattleEventType.MoveStarted);
                Assert.Equal((seat, Wave, frame, 0), (entry.SourceFighterId, entry.MoveId,
                    state.Fighter(seat).MoveStartedFrame, state.Fighter(seat).MoveElapsedFrames));
            }
            if (frame < 19)
                Assert.Empty(state.Projectiles);
            else if (frame < 21)
                Assert.Equal(1, Assert.Single(state.Projectiles).ProjectileId);
        }

        Assert.Collection(state.Projectiles,
            first => Assert.Equal((1, 0, Facing.Right, 23),
                (first.ProjectileId, first.OwnerFighterId, first.SpawnFacing, first.RemainingLifetime)),
            second => Assert.Equal((2, 1, Facing.Left, 25),
                (second.ProjectileId, second.OwnerFighterId, second.SpawnFacing, second.RemainingLifetime)));
    }

    [Theory]
    [InlineData(false, 13)]
    [InlineData(true, 6)]
    public void ClosedOrAbsentRouteDoesNotTurnOneCommandEdgeIntoALightAttack(bool disabled, int commandTick)
    {
        // Both hit groups remain active in the no-cancel control. The second one starts on tick 6.
        BattleSimulation simulation = Simulation(cancelEnabled: !disabled);
        BattleState state = Duel(62);
        int commandFrame = commandTick + 11 + (commandTick >= 7 ? 11 : 0);
        var contacts = new List<BattleEvent>();
        var moves = new List<string?>();
        for (int frame = 0; frame < 60; frame++)
        {
            BattleStepResult step = simulation.Step(state, Inputs(frame, commandFrame));
            contacts.AddRange(step.Events.Where(IsHeavyContact));
            moves.AddRange(step.Events.Where(e => e.Type == BattleEventType.MoveStarted).Select(e => e.MoveId));
            Assert.Empty(step.State.Projectiles);
            state = step.State;
        }
        Assert.Equal(new[] { 3, 17 }, contacts.Select(e => e.Frame));
        Assert.Equal(new[] { Heavy }, moves);
        Assert.Equal(3u, state.Fighter(1).LastHitContactGroups);
        Assert.Equal(FighterLogicState.Standing, state.Fighter(0).LogicState);
    }

    [Theory]
    [InlineData(45, true, Wave, "236A")]
    [InlineData(50, true, Wave, "236A")]
    [InlineData(50, false, Light, "5A")]
    public void NaturalExitUsesOnlyALiveCommandUnderOrdinaryStandingPermission(int commandFrame, bool useMotion,
        string expected, string selectedCommand)
    {
        // The 28-tick move has two 11-tick hit pauses. Its explicit window closed at frame 35.
        const int naturalExit = 50;
        BattleSimulation simulation = Simulation();
        BattleState state = Duel(62);
        for (int frame = 0; frame < naturalExit; frame++)
        {
            BattleStepResult step = simulation.Step(state, useMotion ? Inputs(frame, commandFrame)
                : new FrameInput(frame, frame == 0 ? InputBits.HeavyPunch : InputBits.None, InputBits.None));
            if (frame > 0)
                Assert.DoesNotContain(step.Events, e => e.Type == BattleEventType.MoveStarted);
            state = step.State;
        }
        Assert.Equal((Heavy, 27, FighterLogicState.Recovery),
            (state.Fighter(0).MoveId, state.Fighter(0).MoveElapsedFrames, state.Fighter(0).LogicState));
        FrameInput input = useMotion ? Inputs(naturalExit, commandFrame)
            : new FrameInput(naturalExit, InputBits.LightPunch, InputBits.None);
        BattleStepResult after = simulation.Step(state, input);
        BattleEvent entry = Assert.Single(after.Events, e => e.Type == BattleEventType.MoveStarted);
        Assert.Equal(expected, entry.MoveId);
        Assert.Equal((naturalExit, 0), (after.State.Fighter(0).MoveStartedFrame, after.State.Fighter(0).MoveElapsedFrames));
        Assert.Contains(after.State.Fighter(0).RecognitionState.Buffer, result => result.Match.CommandId == selectedCommand &&
            result.Match.ButtonFrame == commandFrame && result.Consumed);
    }

    [Fact]
    public void RootCommandCompletedDuringGlobalPauseExpiresWithoutHelperRefresh()
    {
        BattleSimulation simulation = Simulation();
        BattleState state = Duel(62);
        for (int frame = 0; frame < 17; frame++)
            state = simulation.Step(state, Inputs(frame, commandFrame: 17)).State;
        BattleState paused = new(state.Frame, state.Fighters, state.StateVersion,
            scriptedPause: new ScriptedPauseState(2, 0, true));

        BattleStepResult frozen = simulation.Step(paused, Inputs(17, commandFrame: 17));
        Assert.Empty(frozen.Events);
        Assert.Empty(frozen.Contacts);
        for (int seat = 0; seat < 2; seat++)
        {
            FighterState before = paused.Fighter(seat);
            FighterState after = frozen.State.Fighter(seat);
            Assert.Equal(before with { Recognition = after.Recognition }, after);
        }
        Assert.Contains(frozen.State.Fighter(0).RecognitionState.Buffer,
            entry => !entry.Consumed && entry.Match.CommandId == "236A");

        BattleStepResult thaw = simulation.Step(frozen.State, FrameInput.Empty(18));
        Assert.DoesNotContain(thaw.State.Fighter(0).RecognitionState.Buffer, entry => entry.Match.CommandId == "236A");
        Assert.DoesNotContain(thaw.Events, entry => entry.Type == BattleEventType.MoveStarted);
        Assert.Equal(6, thaw.State.Fighter(0).MoveElapsedFrames);
        Assert.Equal(Heavy, Assert.Single(thaw.Events, IsHeavyContact).MoveId);
        Assert.Equal(3u, thaw.State.Fighter(1).LastHitContactGroups);
    }

    [Fact]
    public void GlobalPauseDoesNotSpendThePendingInputOrAnIndividualHitPauseTick()
    {
        BattleSimulation simulation = Simulation();
        BattleState state = Duel(62);
        for (int frame = 0; frame < 14; frame++)
            state = simulation.Step(state, Inputs(frame, commandFrame: 12)).State;
        BattleState paused = new(state.Frame, state.Fighters, state.StateVersion,
            scriptedPause: new ScriptedPauseState(2, 0, true));

        BattleStepResult globallyFrozen = simulation.Step(paused, FrameInput.Empty(14));
        Assert.Empty(globallyFrozen.Events);
        Assert.Equal(1, globallyFrozen.State.Fighter(0).HitstopRemainingFrames);
        BattleStepResult individuallyFrozen = simulation.Step(globallyFrozen.State, FrameInput.Empty(15));
        Assert.Equal(3, individuallyFrozen.State.Fighter(0).MoveElapsedFrames);
        Assert.Equal(0, individuallyFrozen.State.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(paused.Fighter(0).DamageScript.RandomState, individuallyFrozen.State.Fighter(0).DamageScript.RandomState);

        BattleStepResult cancel = simulation.Step(individuallyFrozen.State, FrameInput.Empty(16));
        Assert.Equal(Wave, Assert.Single(cancel.Events).MoveId);
        Assert.Equal(0, cancel.State.Fighter(0).MoveElapsedFrames);
        Assert.Equal(1u, cancel.State.Fighter(1).LastHitContactGroups);
        Assert.Equal(paused.Fighter(1).Health, cancel.State.Fighter(1).Health);
        Assert.Empty(cancel.Contacts);
    }

    // Match the audited 215 clocks and group windows. Narrower synthetic hurtboxes isolate first-group whiff.
    private static BattleSimulation Simulation(bool cancelEnabled = true, bool withSweep = false)
    {
        AttackDefinition Group(FixedPoint forward, FixedPoint halfWidth, int first, int last, int group) =>
            new(forward, 53, halfWidth, 27, first, last, hitstunFrames: 19, blockstunFrames: 18,
                hitstopByOutcome: new(new(11, 13), new(11, 11)), contactOnEdge: true, contactGroup: group,
                dynamicDamage: new DynamicAttackDamage(9, 9, true));
        var heavy = new MoveDefinition(Heavy, "5B", 3, 10, 15,
            attacks: [Group(FixedPoint.FromFraction(61, 2), FixedPoint.FromFraction(33, 2), 0, 2, 1),
                      Group(FixedPoint.FromFraction(105, 2), FixedPoint.FromFraction(45, 2), 3, 9, 2)],
            cancels: cancelEnabled ? [new(Wave, 4, 12, MoveCancelPhases.Active, allowWhiff: true)] : [],
            guardThreatFrames: 13);
        var light = new MoveDefinition(Light, "5A", 3, 4, 5,
            cancels: [new(Wave, 4, 6, MoveCancelPhases.Active, allowWhiff: true)]);
        var hit = new AttackDefinition(0, 0, 1, 1, hitstopByOutcome: new(new(0, 13), new(0, 11)));
        var timeline = new ProjectileTimeline(11, [new ProjectileHitSpan(1, 12, new FixedRect(16, 79, 1, 48), hit,
            guardDamage: 9, dynamicDamage: new DynamicAttackDamage(12, 14, false))],
            respectsAirFollowupRestriction: withSweep);
        var wave = new MoveDefinition(Wave, "236A", 15, 2, 35, projectiles:
            [new ProjectileDefinition(Helper, new AttackDefinition(0, 0, 1, 1), FixedPoint.FromFraction(5, 2),
                lifetimeFrames: 25, timeline: timeline)]);
        var rules = new DynamicDamageRules(new DamageRatio(1923, 200), new DamageRatio(4, 5), DamageRatio.One,
            new DamageRatio(97, 100), varianceMaximum: 2, counterGraceTicks: 2);
        var commands = new List<CommandDefinition> {
             new("5A", [], InputBits.LightPunch, priority: 1, bufferLifetimeFrames: 2),
             new("5B", [], InputBits.HeavyPunch, priority: 0, bufferLifetimeFrames: 1),
             new("236A", [CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward],
                 InputBits.LightPunch, maxSequenceFrames: 16, buttonToleranceFrames: 0, priority: 2,
                 bufferLifetimeFrames: 0, matchHeldMotion: true, bufferPolicy: new CommandBufferPolicy(true, 12, 1, 4)) };
        var moves = new List<MoveDefinition> { heavy, light, wave };
        if (withSweep)
        {
            var reaction = new LaunchReactionProfile(4, FixedPoint.FromFraction(3, 4), 3,
                FixedPoint.FromFraction(1, 2), 13, 25, 8, 3, 2, startInFall: true,
                suppressOwnerProjectilesDuringFall: true);
            var sweepHit = new AttackDefinition(FixedPoint.FromFraction(119, 2), FixedPoint.FromFraction(81, 2),
                FixedPoint.FromFraction(71, 2), FixedPoint.FromFraction(23, 2), 0, 7, AttackHeight.Low,
                hitstopByOutcome: new(new(11, 13), new(11, 11)),
                launchVelocityX: 2, launchVelocityY: 8, knockdownFrames: 17, wakeupFrames: 25,
                launchGravity: FixedPoint.FromFraction(1, 2), launchReaction: reaction,
                dynamicDamage: new DynamicAttackDamage(16, 18, false), rejectsLyingTargets: true);
            commands.Add(new CommandDefinition("2D", [], InputBits.HeavyKick, bufferLifetimeFrames: 2));
            moves.Add(new MoveDefinition("Sweep", "2D", 10, 8, 22, MoveAllowedStates.Crouching,
                attacks: [sweepHit], cancels: [new(Wave, 11, 17, MoveCancelPhases.Active, allowWhiff: true)]));
        }
        var fighter = new FighterDefinition("TwoContactRoutes", commands, moves,
            groundHurtboxes: new GroundHurtboxes([new FixedRect(-19, 19, 1, 88)],
                [new FixedRect(-19, 19, 1, 72)]), damageRules: rules, useCommandPriority: true, forwardGuardDistance: 120);
        return new BattleSimulation(new FightRoster(fighter, fighter));
    }

    private static BattleState Duel(int distance, bool mirrored = false)
    {
        int first = (mirrored ? distance : -distance) / 2;
        return new BattleState(0,
            [FighterState.Create(0, 1000, first, mirrored ? Facing.Left : Facing.Right)
                with { DamageScript = DamageScriptState.FromSeed(1337, 0) },
             FighterState.Create(1, 1000, -first, mirrored ? Facing.Right : Facing.Left)
                with { DamageScript = DamageScriptState.FromSeed(1337, 1) }], BattleState.CreateDefault().StateVersion);
    }

    private static FrameInput Inputs(int frame, int commandFrame, bool guard = false, bool mirrored = false)
    {
        InputBits forward = mirrored ? InputBits.Left : InputBits.Right;
        InputBits player1 = frame == 0 ? InputBits.HeavyPunch : Motion(frame, commandFrame, forward);
        return new FrameInput(frame, player1, guard && frame >= 2 ? forward : InputBits.None);
    }

    private static InputBits Motion(int frame, int commandFrame, InputBits forward) =>
        (commandFrame - frame) switch
        {
            3 => InputBits.Down,
            2 => InputBits.Down | forward,
            1 => forward,
            0 => forward | InputBits.LightPunch,
            >= -3 and < 0 => InputBits.LightPunch,
            _ => InputBits.None,
        };

    private static bool IsHeavyContact(BattleEvent battleEvent) => battleEvent.MoveId == Heavy &&
        battleEvent.Type is BattleEventType.Hit or BattleEventType.Blocked;

    private static uint NextRandom(uint before) => (uint)((ulong)before * 16807 % Modulus);

    private static void Replay(BattleSimulation simulation, IReadOnlyList<BattleStepResult> results,
        IReadOnlyList<FrameInput> inputs, int from)
    {
        BattleState state = results[from - 1].State;
        for (int frame = from; frame < results.Count; frame++)
        {
            BattleStepResult actual = simulation.Step(state, inputs[frame]);
            BattleStepResult expected = results[frame];
            Assert.Equal(expected.State.Fighters, actual.State.Fighters);
            Assert.Equal(expected.State.Projectiles, actual.State.Projectiles);
            Assert.Equal(expected.State.ScriptedPause, actual.State.ScriptedPause);
            Assert.Equal(expected.State.NextProjectileId, actual.State.NextProjectileId);
            Assert.Equal(expected.Contacts, actual.Contacts);
            Assert.Equal(expected.Events, actual.Events);
            state = actual.State;
        }
    }
}
