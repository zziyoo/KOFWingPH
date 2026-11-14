using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class CrouchRouteTests
{
    private const string Crouch = "CrouchLight";
    private const string Stand = "StandLight";
    private const string Kick = "CrouchLightKick";
    private const string Wave = "PowerWave";

    [Theory]
    [InlineData(InputBits.Down, InputBits.LightKick | InputBits.HeavyKick, null, "LightKick")]
    [InlineData(InputBits.Down | InputBits.Left, InputBits.LightKick | InputBits.HeavyKick, null, "LightKick")]
    [InlineData(InputBits.Down | InputBits.Right, InputBits.LightKick | InputBits.HeavyKick, null, "LightKick")]
    [InlineData(InputBits.None, InputBits.HeavyKick, null, "StandingKick")]
    [InlineData(InputBits.Down, InputBits.HeavyKick, "Sweep", "HeavyKick")]
    public void ReservedInputCanUseAlternativeDirections(InputBits direction, InputBits buttons, string? move, string candidate)
    {
        var light = new CommandDefinition("LightKick", [CommandDirection.Down, CommandDirection.DownBack, CommandDirection.DownForward],
            InputBits.LightKick, 1, 0, priority: 1, bufferLifetimeFrames: 0, reserveGroundInput: true);
        var standing = new CommandDefinition("StandingKick", [CommandDirection.Neutral, CommandDirection.Back, CommandDirection.Forward],
            InputBits.HeavyKick, 1, 0, priority: 1, bufferLifetimeFrames: 0, reserveGroundInput: true);
        var kick = new CommandDefinition("HeavyKick", [], InputBits.HeavyKick, 1, 0, bufferLifetimeFrames: 2);
        var sweep = new MoveDefinition("Sweep", "HeavyKick", 10, 8, 22, MoveAllowedStates.Crouching);
        var fighter = new FighterDefinition("Test", [light, standing, kick], [sweep], useCommandPriority: true);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));

        BattleState state = simulation.Step(BattleState.CreateDefault(), new FrameInput(0, direction | buttons, 0)).State;
        Assert.Equal(move, state.Fighter(0).MoveId);
        Assert.Contains(state.Fighter(0).RecognitionState.Buffer, result => result.Match.CommandId == candidate && result.Consumed);
        state = simulation.Step(state, new FrameInput(1, InputBits.Down | buttons, 0)).State;
        Assert.Equal(move, state.Fighter(0).MoveId);
    }

    [Theory]
    [InlineData(39, InputBits.Down, InputBits.Down, false)]
    [InlineData(38, InputBits.None, InputBits.Down, false)]
    [InlineData(37, InputBits.Down, InputBits.Down, true)]
    [InlineData(39, InputBits.Down, InputBits.None, false)]
    public void BufferedReservationUsesCurrentDirectionAndSuppressesTheNewLosingEdge(int lightEdge,
        InputBits lightDirection, InputBits exitDirection, bool startsSweep)
    {
        var light = new CommandDefinition("LightKick", [CommandDirection.Down, CommandDirection.DownBack, CommandDirection.DownForward],
            InputBits.LightKick, 1, 0, priority: 1, bufferLifetimeFrames: 2, reserveGroundInput: true);
        var kick = new CommandDefinition("HeavyKick", [], InputBits.HeavyKick, 1, 0, bufferLifetimeFrames: 2);
        var sweep = new MoveDefinition("Sweep", "HeavyKick", 10, 8, 22, MoveAllowedStates.Crouching, returnsToCrouch: true);
        var fighter = new FighterDefinition("Test", [light, kick], [sweep], useCommandPriority: true);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = BattleState.CreateDefault();
        for (int frame = 0; frame <= 41; frame++)
        {
            InputBits bits = frame == 0 ? InputBits.Down | InputBits.HeavyKick
                : frame == lightEdge ? lightDirection | InputBits.LightKick
                : frame == 40 ? exitDirection | InputBits.HeavyKick : exitDirection;
            state = simulation.Step(state, new FrameInput(frame, bits, 0)).State;
        }

        Assert.Equal(startsSweep ? "Sweep" : null, state.Fighter(0).MoveId);
        if (exitDirection == InputBits.Down && !startsSweep)
            Assert.All(state.Fighter(0).RecognitionState.Buffer, result => Assert.True(result.Consumed));
    }

    [Theory]
    [InlineData(39, InputBits.Down, InputBits.Down, "CrouchLightKick", false)]
    [InlineData(38, InputBits.None, InputBits.Down, "CrouchLightKick", false)]
    [InlineData(37, InputBits.Down, InputBits.Down, "Sweep", true)]
    [InlineData(39, InputBits.Down, InputBits.None, null, false)]
    public void RealGroundMoveUsesCurrentDirectionAndLetsAYoungerLosingEdgeExpire(int lightEdge, InputBits edgeDirection,
        InputBits exitDirection, string? expectedMove, bool heavyConsumed)
    {
        var light = new CommandDefinition("LightKick", [], InputBits.LightKick, 1, 0, priority: 1, bufferLifetimeFrames: 2);
        var heavy = new CommandDefinition("HeavyKick", [], InputBits.HeavyKick, 1, 0, bufferLifetimeFrames: 2);
        var sweep = new MoveDefinition("Sweep", "HeavyKick", 10, 8, 22, MoveAllowedStates.Crouching, returnsToCrouch: true);
        var kick = new MoveDefinition("CrouchLightKick", "LightKick", 3, 3, 8, MoveAllowedStates.Crouching,
            returnsToCrouch: true);
        var fighter = new FighterDefinition("Test", [light, heavy], [sweep, kick], useCommandPriority: true);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = BattleState.CreateDefault();
        for (int frame = 0; frame < 40; frame++)
        {
            InputBits bits = frame == 0 ? InputBits.Down | InputBits.HeavyKick
                : frame == lightEdge ? edgeDirection | InputBits.LightKick : InputBits.Down;
            state = simulation.Step(state, new FrameInput(frame, bits, 0)).State;
        }

        var input = new FrameInput(40, exitDirection | InputBits.HeavyKick, 0);
        BattleStepResult result = simulation.Step(state, input);
        Assert.Equal(expectedMove, result.State.Fighter(0).MoveId);
        Assert.Equal(result.State.Fighters, simulation.Step(state, input).State.Fighters);
        Assert.Equal(heavyConsumed, Assert.Single(result.State.Fighter(0).RecognitionState.Buffer,
            entry => entry.Match.CommandId == "HeavyKick" && entry.Match.ButtonFrame == 40).Consumed);
        state = result.State;
        for (int frame = 41; frame <= 43; frame++)
        {
            result = simulation.Step(state, new FrameInput(frame, exitDirection, 0));
            Assert.DoesNotContain(result.Events, entry => entry.Type == BattleEventType.MoveStarted);
            state = result.State;
        }
        Assert.DoesNotContain(state.Fighter(0).RecognitionState.Buffer, entry => entry.Match.ButtonFrame == 40);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AReservedGroundCommandTakesPriorityOverBothNormalButtons(bool mirror, bool back)
    {
        var heavy = new CommandDefinition("5B", [], InputBits.HeavyPunch, 1, 2);
        var light = new CommandDefinition("5A", [], InputBits.LightPunch, 1, 2, priority: 1);
        var reserved = new CommandDefinition("Reserved", [CommandDirection.DownForward], InputBits.HeavyPunch, 1, 0,
            priority: 2, bufferLifetimeFrames: 0, reserveGroundInput: true);
        var heavyMove = new MoveDefinition("CrouchHeavy", "5B", 5, 8, 14, MoveAllowedStates.Crouching);
        var lightMove = new MoveDefinition(Crouch, "5A", 3, 2, 8, MoveAllowedStates.Crouching);
        var fighter = new FighterDefinition("Test", [heavy, light, reserved], [heavyMove, lightMove], useCommandPriority: true);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        var state = new BattleState(0, [
            FighterState.Create(0, 1000, mirror ? 120 : -120, mirror ? Facing.Left : Facing.Right),
            FighterState.Create(1, 1000, mirror ? -120 : 120, mirror ? Facing.Right : Facing.Left)],
            BattleState.CreateDefault().StateVersion);
        InputBits horizontal = mirror == back ? InputBits.Right : InputBits.Left;
        InputBits heldDirection = InputBits.Down | horizontal;
        InputBits buttons = InputBits.LightPunch | InputBits.HeavyPunch;
        state = simulation.Step(state, new FrameInput(0, heldDirection, 0)).State;
        BattleStepResult first = simulation.Step(state, new FrameInput(1, heldDirection | buttons, 0));
        Assert.Equal(back ? Crouch : null, first.State.Fighter(0).MoveId);
        BattleStepResult release = simulation.Step(first.State, new FrameInput(2, InputBits.Down | buttons, 0));
        Assert.DoesNotContain(release.Events, e => e.Type == BattleEventType.MoveStarted);
        if (!back)
            Assert.Null(release.State.Fighter(0).MoveId);
    }

    [Theory]
    [InlineData(InputBits.Down, Crouch)]
    [InlineData(InputBits.None, Stand)]
    public void OnePhysicalEdgeSelectsTheMoveForTheCurrentPosture(InputBits direction, string expected)
    {
        var fighter = Fighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleStepResult result = simulation.Step(BattleState.CreateDefault(),
            new FrameInput(0, direction | InputBits.LightPunch, InputBits.None));
        Assert.Equal(expected, result.State.Fighter(0).MoveId);
        Assert.Single(result.Events, e => e.Type == BattleEventType.MoveStarted);
    }

    [Theory]
    [InlineData(true, Crouch)]
    [InlineData(false, Stand)]
    public void RecoveryUsesCurrentDirectionForAConfiguredCancel(bool down, string expected)
    {
        var fighter = Fighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = BattleState.CreateDefault();
        for (int frame = 0; frame <= 7; frame++)
        {
            InputBits input = frame == 0 || frame == 7 ? InputBits.LightPunch : InputBits.None;
            if (frame == 7 && down)
                input |= InputBits.Down;
            state = simulation.Step(state, new FrameInput(frame, input, InputBits.None)).State;
        }
        Assert.Equal(expected, state.Fighter(0).MoveId);
        Assert.Equal(7, state.Fighter(0).MoveStartedFrame);
    }

    [Theory]
    [InlineData(3, null, -1)]
    [InlineData(4, Stand, 6)]
    [InlineData(6, Stand, 6)]
    [InlineData(13, Stand, 13)]
    [InlineData(14, Wave, 14)]
    [InlineData(15, Wave, 15)]
    public void KickWaveInputFallsBackWithinChainOrStartsAtNaturalRecovery(int edge, string? expected, int start)
    {
        FighterDefinition fighter = KickFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = BattleState.CreateDefault();
        var followups = new List<(int Frame, string? Move)>();
        for (int frame = 0; frame <= 17; frame++)
        {
            InputBits bits = frame == 0 ? InputBits.Down | InputBits.LightKick : InputBits.None;
            if (frame == edge - 3) bits |= InputBits.Down;
            if (frame == edge - 2) bits |= InputBits.Down | InputBits.Right;
            if (frame == edge - 1) bits |= InputBits.Right;
            if (frame == edge) bits |= InputBits.Right | InputBits.LightPunch;
            BattleStepResult step = simulation.Step(state, new FrameInput(frame, bits, InputBits.None));
            if (frame > 0 && step.Events.Any(evt => evt.Type == BattleEventType.MoveStarted))
                followups.Add((frame, step.State.Fighter(0).MoveId));
            state = step.State;
        }
        if (expected is null)
            Assert.Empty(followups);
        else
            Assert.Equal((start, expected), Assert.Single(followups));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NormalFallbackRetainsOnlyTheHelperMotionAndHoldingTheButtonControlsItsLifetime(
        bool held, bool entersWave)
    {
        FighterDefinition fighter = KickFighter(retainHelper: true);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = BattleState.CreateDefault();
        var starts = new List<(int, string?)>();
        for (int frame = 0; frame <= 20; frame++)
        {
            InputBits bits = frame switch
            {
                0 => InputBits.Down | InputBits.LightKick,
                9 => InputBits.Down,
                10 => InputBits.Down | InputBits.Right,
                11 => InputBits.Right,
                12 => InputBits.Right | InputBits.LightPunch,
                _ => held && frame > 12 ? InputBits.LightPunch : InputBits.None,
            };
            BattleStepResult step = simulation.Step(state, new FrameInput(frame, bits, 0));
            if (step.Events.Any(evt => evt.Type == BattleEventType.MoveStarted))
                starts.Add((frame, step.State.Fighter(0).MoveId));
            if (frame == 12)
            {
                var candidates = step.State.Fighter(0).RecognitionState.Buffer;
                Assert.Contains(candidates, candidate => candidate.Match.CommandId == "5A" && candidate.Consumed);
                Assert.Contains(candidates, candidate => candidate.Match.CommandId == "236A" && !candidate.Consumed);
            }
            state = step.State;
        }
        Assert.Equal(entersWave ? [(0, Kick), (12, Stand), (16, Wave)] : [(0, Kick), (12, Stand)], starts);
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(17, true)]
    [InlineData(19, true)]
    public void KickSelfChainReplaysFromHitPauseAndCreatesANewContactInstance(int edge, bool chains)
    {
        FighterDefinition fighter = KickFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        var initial = new BattleState(0, [FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left)], stateVersion: 15);
        BattleState state = initial;
        var inputs = Enumerable.Range(0, 24).Select(frame => new FrameInput(frame,
            frame == 0 || frame == edge ? InputBits.Down | InputBits.LightKick : InputBits.Down, InputBits.None)).ToArray();
        foreach (FrameInput input in inputs.Take(4))
            state = simulation.Step(state, input).State;
        Assert.Equal(3, state.Fighter(0).HitConfirmFrame);
        Assert.Equal(11, state.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(13, state.Fighter(1).HitstopRemainingFrames);
        BattleState snapshot = state;

        List<BattleStepResult> Run()
        {
            var steps = new List<BattleStepResult>();
            BattleState current = snapshot;
            foreach (FrameInput input in inputs.Skip(snapshot.Frame))
            {
                BattleStepResult step = simulation.Step(current, input);
                steps.Add(step);
                current = step.State;
            }
            return steps;
        }
        List<BattleStepResult> actual = Run();
        foreach ((BattleStepResult step, BattleStepResult replay) in actual.Zip(Run()))
        {
            Assert.Equal(step.State.Fighters, replay.State.Fighters);
            Assert.Equal(step.Events, replay.Events);
            Assert.Equal(step.Contacts, replay.Contacts);
        }
        if (!chains)
        {
            Assert.DoesNotContain(actual.SelectMany(step => step.Events), evt => evt.Type == BattleEventType.MoveStarted);
            return;
        }
        BattleStepResult started = Assert.Single(actual, step => step.Events.Any(evt => evt.Type == BattleEventType.MoveStarted));
        Assert.Equal(Math.Max(17, edge), started.FrameBefore);
        FighterState fresh = started.State.Fighter(0);
        Assert.Equal(Kick, fresh.MoveId);
        Assert.Equal(started.FrameBefore, fresh.MoveStartedFrame);
        Assert.Equal(0, fresh.MoveElapsedFrames);
        Assert.Equal(-1, fresh.HitConfirmFrame);
        Assert.Equal(-1, fresh.BlockConfirmFrame);
        Assert.Equal(1, fresh.DamageScript.ContactDivisor);
        Assert.Single(actual.SelectMany(step => step.Events), evt => evt.Type == BattleEventType.Hit);
        Assert.Equal(started.FrameBefore, actual[^1].State.Fighter(1).LastHitAttackStartFrame);
    }

    [Theory]
    [InlineData(false, InputBits.Down | InputBits.Right, BattleEventType.Hit)]
    [InlineData(true, InputBits.Down | InputBits.Right, BattleEventType.Blocked)]
    [InlineData(true, InputBits.Down, BattleEventType.Blocked)]
    [InlineData(true, InputBits.Right, BattleEventType.Hit)]
    public void NewKickInstanceCanRespectAnExistingCrouchingGuard(bool retainGuard, InputBits afterFirstBlock,
        BattleEventType secondResult)
    {
        FighterDefinition fighter = KickFighter(retainGuard: retainGuard);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        var state = new BattleState(0, [FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left)], stateVersion: 15);
        var contacts = new List<BattleEvent>();
        for (int frame = 0; frame <= 20; frame++)
        {
            InputBits attack = frame is 0 or 17 ? InputBits.Down | InputBits.LightKick : InputBits.Down;
            InputBits defense = frame < 15 ? InputBits.Down | InputBits.Right : afterFirstBlock;
            BattleStepResult step = simulation.Step(state, new FrameInput(frame, attack, defense));
            contacts.AddRange(step.Events.Where(evt => evt.Type is BattleEventType.Hit or BattleEventType.Blocked));
            state = step.State;
        }
        Assert.Collection(contacts,
            first => Assert.Equal((3, BattleEventType.Blocked), (first.Frame, first.Type)),
            second => Assert.Equal((20, secondResult), (second.Frame, second.Type)));
        Assert.Equal(17, state.Fighter(1).LastHitAttackStartFrame);
        if (retainGuard && secondResult == BattleEventType.Hit)
            Assert.False(state.Fighter(1).GroundReaction.Crouching);
    }

    private static FighterDefinition KickFighter(bool retainHelper = false, bool retainGuard = false)
    {
        var light = new CommandDefinition("5A", [], InputBits.LightPunch, 1, 0, priority: 1, bufferLifetimeFrames: 2);
        var kick = new CommandDefinition("2C", [], InputBits.LightKick, 1, 0, bufferLifetimeFrames: 2);
        var wave = new CommandDefinition("236A", [CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward],
            InputBits.LightPunch, 16, 0, priority: 3, bufferLifetimeFrames: 0, matchHeldMotion: true,
            bufferPolicy: new CommandBufferPolicy(true, 12, 1, 4));
        var moves = new MoveDefinition[]
        {
            new(Stand, "5A", 3, 4, 5, MoveAllowedStates.Standing,
                cancels: [new CancelDefinition(Wave, 4, 6, MoveCancelPhases.Active, allowWhiff: true)]),
            new(Wave, "236A", 15, 2, 35),
            new(Kick, "2C", 3, 3, 8, MoveAllowedStates.Crouching,
                attacks: [new AttackDefinition(45, 8, 20, 7, height: AttackHeight.Low, hitstunFrames: 11, blockstunFrames: 10,
                    hitstopByOutcome: new(new(11, 13), new(11, 11)), dynamicDamage: new(7, 8, false),
                    groundHitReaction: GroundHitReaction.LightLow, retainsGroundGuardOnFollowup: retainGuard)],
                cancels: [new CancelDefinition(Stand, 6, 13, allowWhiff: true, standingInputOnly: true, maximumInputAge: 2,
                    retainBufferedMotionHelper: retainHelper),
                    new CancelDefinition(Kick, 6, 13, allowWhiff: true, allowSameMove: true, maximumInputAge: 2)],
                returnsToCrouch: true),
        };
        var rules = new DynamicDamageRules(DamageRatio.One, DamageRatio.One, DamageRatio.One, DamageRatio.One, 1, 2);
        return new FighterDefinition("Test", [light, kick, wave], moves, useCommandPriority: true, damageRules: rules,
            groundHurtboxes: retainGuard ? new GroundHurtboxes([new(-30, 30, 0, 120)], [new(-30, 30, 0, 70)],
                lightHitstun: [new(1, [new(-30, 30, 0, 120)])],
                crouchingLightHitstun: [new(1, [new(-30, 30, 0, 70)])],
                standingGuard: [new(-30, 30, 0, 120)], crouchingGuard: [new(-30, 30, 0, 70)]) : null);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(18)]
    public void ReleasedDirectionReturnsThroughCrouchingThenAuthoredRise(int release)
    {
        var fighter = Fighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = BattleState.CreateDefault();
        for (int frame = 0; frame <= release + 4; frame++)
        {
            InputBits input = frame == 0 ? InputBits.Down | InputBits.LightPunch :
                release > 14 && frame < release ? InputBits.Down : InputBits.None;
            BattleState snapshot = state;
            state = simulation.Step(state, new FrameInput(frame, input, InputBits.None)).State;
            Assert.Equal(state.Fighter(0), simulation.Step(snapshot, new FrameInput(frame, input, InputBits.None)).State.Fighter(0));
            if (frame >= 13 && frame < release)
            {
                Assert.Equal(FighterLogicState.Crouching, state.Fighter(0).LogicState);
                Assert.Equal(-1, state.Fighter(0).CrouchReturnTicks);
            }
            if (frame >= release && frame < release + 4)
            {
                Assert.Equal(frame - release + 1, state.Fighter(0).CrouchReturnTicks);
                Assert.Equal(FixedPoint.FromInt(frame == release ? 76 : 92),
                    Assert.Single(FighterBoxes.GetHurtboxes(state.Fighter(0), fighter)).MaxY);
            }
        }
        Assert.Equal(FighterLogicState.Standing, state.Fighter(0).LogicState);
        Assert.Equal(0, state.Fighter(0).CrouchReturnTicks);
    }

    [Fact]
    public void AuthoredLightReactionHoldsThroughHitPauseAndAdvancesFromTheSameSnapshot()
    {
        var fighter = Fighter(withStrike: true);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = BattleState.CreateDefault();
        for (int frame = 0; frame <= 10; frame++)
        {
            InputBits input = frame == 0 ? InputBits.Down | InputBits.LightPunch : InputBits.None;
            var sample = new FrameInput(frame, input, InputBits.None);
            BattleState snapshot = state;
            state = simulation.Step(state, sample).State;
            var target = state.Fighter(1);
            Assert.Equal(target, simulation.Step(snapshot, sample).State.Fighter(1));
            if (frame is >= 3 and <= 8)
            {
                int expectedTick = Math.Max(1, frame - 5);
                Assert.Equal(expectedTick, target.LightGroundReactionTick);
                Assert.Equal(FixedPoint.FromInt(expectedTick < 3 ? 100 : 72),
                    Assert.Single(FighterBoxes.GetHurtboxes(target, fighter)).MaxY);
            }
        }
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
        Assert.Equal(0, state.Fighter(1).LightGroundReactionTick);
    }

    private static FighterDefinition Fighter(bool withStrike = false)
    {
        var punch = new CommandDefinition("5A", [], InputBits.LightPunch, 1, 1);
        var stand = new MoveDefinition(Stand, "5A", 3, 4, 5, MoveAllowedStates.Standing,
            cancels: [new CancelDefinition(Crouch, 7, 11, allowWhiff: true),
                      new CancelDefinition(Stand, 7, 11, allowWhiff: true, allowSameMove: true, standingInputOnly: true)]);
        var crouch = new MoveDefinition(Crouch, "5A", 3, 2, 8, MoveAllowedStates.Crouching, returnsToCrouch: true,
            attacks: withStrike ? [new AttackDefinition(235, 50, 20, 25, hitstunFrames: 4, hitstopFrames: 3,
                lightGroundReaction: true)] : null);
        static FixedRect Box(int height) => new(-10, 10, 0, height);
        return new FighterDefinition("Test", [punch], [stand, crouch], useCommandPriority: true,
            groundHurtboxes: new GroundHurtboxes([Box(104)], [Box(72)],
                crouchRise: [new(1, [Box(76)]), new(3, [Box(92)])],
                lightHitstun: [new(2, [Box(100)]), new(2, [Box(72)])]));
    }
}
