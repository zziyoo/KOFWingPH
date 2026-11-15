using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class GroundBlowbackTests
{
    private const InputBits Chord = InputBits.HeavyPunch | InputBits.HeavyKick;
    private const string Blowback = "Blowback";
    private const string ForwardPunch = "ForwardPunch";
    private const string Wave = "Wave";

    [Theory]
    [InlineData(InputBits.None, Chord, true, Blowback)]
    [InlineData(InputBits.HeavyPunch, Chord, true, "HeavyKick")]
    [InlineData(InputBits.HeavyKick, Chord, true, "HeavyPunch")]
    [InlineData(Chord, Chord, true, null)]
    [InlineData(Chord, Chord, false, Blowback)]
    [InlineData(InputBits.None, InputBits.Down | Chord, true, "HeavyPunch")]
    public void RealReservationRespectsFreshPhysicalEdgesAndGroundDirection(InputBits previous,
        InputBits current, bool adjacent, string? expected)
    {
        FighterDefinition fighter = DefineFighter();
        RecognitionState recognition = Recognize(fighter, RecognitionState.Empty, previous, 0);
        recognition = TickIdle(fighter, recognition, previous, 0).Recognition;
        if (!adjacent)
            recognition = Recognize(fighter, recognition, InputBits.None, 1);
        int frame = recognition.LastObservedFrame + 1;
        recognition = Recognize(fighter, recognition, current, frame);
        FighterActionTick action = TickIdle(fighter, recognition, current, frame);
        Assert.Equal(expected, action.MoveId);
        if (expected is not null)
            Assert.All(action.Recognition.Buffer.Where(candidate => candidate.Match.RecognizedFrame == frame),
                candidate => Assert.True(candidate.Consumed));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void GlobalPauseChordHelperDecaysAndConsumesOnce(int age, bool starts)
    {
        FighterDefinition fighter = DefineFighter();
        RecognitionState recognition = Recognize(fighter, RecognitionState.Empty, Chord, 0, globalPaused: true);
        for (int frame = 1; frame <= age; frame++)
            recognition = Recognize(fighter, recognition, InputBits.None, frame, globalPaused: frame < age);
        FighterActionTick first = TickIdle(fighter, recognition, InputBits.None, age);
        Assert.Equal(starts ? Blowback : null, first.MoveId);
        recognition = Recognize(fighter, first.Recognition, InputBits.None, age + 1);
        Assert.Null(TickIdle(fighter, recognition, InputBits.None, age + 1).MoveId);
    }

    [Theory]
    [InlineData(23, false, Blowback)]
    [InlineData(24, false, ForwardPunch)]
    [InlineData(25, false, ForwardPunch)]
    [InlineData(26, false, Blowback)]
    [InlineData(23, true, Blowback)]
    [InlineData(24, true, Wave)]
    [InlineData(25, true, Wave)]
    [InlineData(26, true, Blowback)]
    public void AuthoredWindowChoosesEligibleDestinationInCommandOrder(int tick, bool motion, string expected)
    {
        FighterDefinition fighter = DefineFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = Start(240);
        for (int frame = 0; frame <= tick; frame++)
        {
            InputBits input = frame == 0 ? Chord : frame == tick ? InputBits.Right | InputBits.LightPunch
                : motion && frame == tick - 2 ? InputBits.Down
                : motion && frame == tick - 1 ? InputBits.Down | InputBits.Right : InputBits.None;
            state = simulation.Step(state, new FrameInput(frame, input, InputBits.None)).State;
        }

        FighterState attacker = state.Fighter(0);
        Assert.Equal(expected, attacker.MoveId);
        Assert.Equal(expected == Blowback ? tick : 0, attacker.MoveElapsedFrames);
        Assert.Equal(expected == Blowback ? 0 : tick, attacker.MoveStartedFrame);
        Assert.Equal(-1, attacker.HitConfirmFrame);
    }

    [Fact]
    public void EarlyForwardPunchWaitsForTheWindowAndNaturalRestartNeedsAnotherChord()
    {
        FighterDefinition fighter = DefineFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState initial = Start(240);
        BattleState state = initial;
        for (int frame = 0; frame <= 24; frame++)
        {
            InputBits input = frame == 0 ? Chord : frame == 23 ? InputBits.Right | InputBits.LightPunch : InputBits.None;
            state = simulation.Step(state, new FrameInput(frame, input, InputBits.None)).State;
            if (frame == 23)
                Assert.Equal(Blowback, state.Fighter(0).MoveId);
        }
        Assert.Equal(ForwardPunch, state.Fighter(0).MoveId);
        Assert.Equal(24, state.Fighter(0).MoveStartedFrame);

        state = initial;
        for (int frame = 0; frame < 49; frame++)
            state = simulation.Step(state, new FrameInput(frame, frame == 0 ? Chord : InputBits.None, InputBits.None)).State;
        Assert.Equal(48, state.Fighter(0).MoveElapsedFrames);
        BattleStepResult recovered = simulation.Step(state, FrameInput.Empty(49));
        Assert.Null(recovered.State.Fighter(0).MoveId);
        BattleStepResult restart = simulation.Step(state, new FrameInput(49, Chord, InputBits.None));
        Assert.Equal(Blowback, restart.State.Fighter(0).MoveId);
        Assert.Equal(49, restart.State.Fighter(0).MoveStartedFrame);
        Assert.Equal(0, restart.State.Fighter(0).MoveElapsedFrames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoredStrikeLaunchesOnceWithMirroredMotionAndIndependentPause(bool mirrored)
    {
        FighterDefinition fighter = DefineFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = Start(100, mirrored);
        int sign = mirrored ? -1 : 1;
        int contacts = 0;
        for (int frame = 0; frame <= 60; frame++)
        {
            var input = new FrameInput(frame, frame == 0 ? Chord : InputBits.None, InputBits.None);
            BattleStepResult step = simulation.Step(state, input);
            Assert.Equal(step.State.Fighters.ToArray(), simulation.Step(state, input).State.Fighters.ToArray());
            state = step.State;
            contacts += step.Events.Count(entry => entry.Type == BattleEventType.Hit);
            if (frame == 23)
            {
                Assert.Equal(1, contacts);
                Assert.Equal(11, state.Fighter(0).HitstopRemainingFrames);
                Assert.Equal(13, state.Fighter(1).HitstopRemainingFrames);
                Assert.Equal(LaunchReactionPhase.Shake, state.Fighter(1).LaunchReaction!.Value.Phase);
                Assert.True(state.Fighter(1).LaunchReaction!.Value.RestrictsAirFollowupFrom(0));
            }
            if (frame is >= 24 and <= 36)
                Assert.Equal(FixedPoint.Zero, state.Fighter(1).PositionY);
            if (frame == 35)
                Assert.Equal(24, state.Fighter(0).MoveElapsedFrames);
            if (frame == 37)
            {
                Assert.Equal(26, state.Fighter(0).MoveElapsedFrames);
                Assert.Equal(FixedPoint.FromInt(22 * sign), state.Fighter(0).PositionX);
                Assert.Equal(FixedPoint.FromFraction(109 * sign, 2), state.Fighter(1).PositionX);
                Assert.Equal(FixedPoint.FromFraction(13, 2), state.Fighter(1).PositionY);
                Assert.Equal(LaunchReactionPhase.Lift, state.Fighter(1).LaunchReaction!.Value.Phase);
            }
        }
        Assert.Equal(1, contacts);
        Assert.Null(state.Fighter(0).MoveId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuardPausesBothFightersAndFrozenCommandCancelsOnThaw(bool crouching)
    {
        FighterDefinition fighter = DefineFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = Start(100);
        int blocks = 0;
        for (int frame = 0; frame <= 36; frame++)
        {
            InputBits attack = frame == 0 ? Chord : frame == 33 ? InputBits.Right | InputBits.LightPunch
                : frame is 34 or 35 ? InputBits.Right : InputBits.None;
            InputBits defense = frame >= 17 ? InputBits.Right | (crouching ? InputBits.Down : InputBits.None) : InputBits.None;
            BattleStepResult step = simulation.Step(state, new FrameInput(frame, attack, defense));
            state = step.State;
            blocks += step.Events.Count(entry => entry.Type == BattleEventType.Blocked);
            if (frame == 23)
            {
                Assert.Equal(11, state.Fighter(0).HitstopRemainingFrames);
                Assert.Equal(11, state.Fighter(1).HitstopRemainingFrames);
                Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);
                Assert.Equal(crouching, state.Fighter(1).GroundGuardCrouching);
            }
            if (frame is > 23 and < 35)
                Assert.Equal(Blowback, state.Fighter(0).MoveId);
            if (frame == 35)
            {
                Assert.Equal(ForwardPunch, state.Fighter(0).MoveId);
                Assert.Equal(frame, state.Fighter(0).MoveStartedFrame);
                Assert.Equal(0, state.Fighter(0).MoveElapsedFrames);
                Assert.Equal(-1, state.Fighter(0).BlockConfirmFrame);
                Assert.Equal(22 - 1, state.Fighter(1).StunRemainingFrames);
                Assert.Equal(FixedPoint.FromFraction(121, 2), state.Fighter(1).PositionX);
            }
        }
        Assert.Equal(1, blocks);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Null(state.Fighter(1).LaunchReaction);
    }

    [Fact]
    public void JumpingDefenderCannotAirGuardAnAuthoredGroundOnlyStrike()
    {
        FighterDefinition fighter = DefineFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = Start(100);
        for (int frame = 0; frame < 23; frame++)
            state = simulation.Step(state, new FrameInput(frame, frame == 0 ? Chord : InputBits.None, InputBits.None)).State;

        FighterState airborne = state.Fighter(1) with
        {
            LogicState = FighterLogicState.Airborne,
            PositionY = FixedPoint.FromInt(20),
            JumpKind = JumpKind.ShortHop,
        };
        var before = new BattleState(state.Frame, [state.Fighter(0), airborne], state.StateVersion);
        BattleStepResult hit = simulation.Step(before, new FrameInput(23, InputBits.None, InputBits.Right));
        Assert.Contains(hit.Events, entry => entry.Type == BattleEventType.Hit);
        Assert.DoesNotContain(hit.Events, entry => entry.Type == BattleEventType.Blocked);
        Assert.Equal(LaunchReactionPhase.Shake, hit.State.Fighter(1).LaunchReaction!.Value.Phase);
    }

    [Theory]
    [InlineData(LaunchReactionPhase.Shake, true)]
    [InlineData(LaunchReactionPhase.Lift, true)]
    [InlineData(LaunchReactionPhase.Rise, true)]
    [InlineData(LaunchReactionPhase.Fall, true)]
    [InlineData(LaunchReactionPhase.Impact, false)]
    [InlineData(LaunchReactionPhase.Bounce, false)]
    public void OwnerWaveRestrictionSpansInitialLaunchUntilLanding(LaunchReactionPhase phase, bool restricted)
    {
        var profile = new LaunchReactionProfile(4, FixedPoint.FromFraction(3, 4),
            FixedPoint.FromFraction(7, 2), FixedPoint.FromFraction(1, 2), 13, 25,
            riseFrames: 8, lieImpactFrames: 3, transitionGravitySteps: 2,
            suppressOwnerProjectilesDuringFall: true);
        var launch = new LaunchReactionState(profile, phase, phase == LaunchReactionPhase.Shake ? 0 : 1, LaunchOwner: 0);
        Assert.Equal(restricted, launch.RestrictsAirFollowupFrom(0));
        Assert.False(launch.RestrictsAirFollowupFrom(1));
    }

    [Fact]
    public void AuthoredCounterLaunchAllowsFollowupWithoutAffectingItsReaction()
    {
        FighterDefinition fighter = DefineFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = Start(100);
        for (int frame = 0; frame <= 23; frame++)
        {
            InputBits attack = frame == 0 ? Chord : InputBits.None;
            InputBits opponent = frame == 22 ? InputBits.HeavyPunch : InputBits.None;
            state = simulation.Step(state, new FrameInput(frame, attack, opponent)).State;
        }

        LaunchReactionState launched = state.Fighter(1).LaunchReaction!.Value;
        Assert.Equal(LaunchReactionPhase.Shake, launched.Phase);
        Assert.Equal(13, state.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(-1, launched.LaunchOwner);
        Assert.False(launched.RestrictsAirFollowupFrom(0));
    }

    [Fact]
    public void NormalLaunchRejectsTheOwnersMeleeFollowupWithOverlappingBoxes()
    {
        FighterDefinition fighter = DefineFighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = Start(100);
        int contacts = 0;
        BattleStepResult secondAttack = default!;
        for (int frame = 0; frame <= 46; frame++)
        {
            InputBits attack = frame == 0 ? Chord : frame == 35 ? InputBits.Right | InputBits.LightPunch : InputBits.None;
            secondAttack = simulation.Step(state, new FrameInput(frame, attack, InputBits.None));
            state = secondAttack.State;
            contacts += secondAttack.Events.Count(entry => entry.Type == BattleEventType.Hit);
        }

        FighterState attacker = state.Fighter(0);
        FighterState target = state.Fighter(1);
        Assert.Equal(ForwardPunch, attacker.MoveId);
        Assert.Contains(secondAttack.Events, entry => entry.Type == BattleEventType.AttackActive && entry.MoveId == ForwardPunch);
        Assert.Equal(1, contacts);
        Assert.Equal(LaunchReactionPhase.Fall, target.LaunchReaction!.Value.Phase);
        FixedPoint expectedBottom = target.PositionY + FixedPoint.FromInt(37);
        Assert.Equal(expectedBottom, Assert.Single(FighterBoxes.GetHurtboxes(target, fighter)).MinY);
    }

    private static RecognitionState Recognize(FighterDefinition fighter, RecognitionState previous, InputBits input,
        int frame, bool globalPaused = false) => previous.Advance(input, Facing.Right, frame, 0, fighter.Commands,
        useCommandPriority: true, globalPaused: globalPaused);

    private static FighterActionTick TickIdle(FighterDefinition fighter, RecognitionState recognition, InputBits input,
        int frame) => FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
        DirectionResolver.Resolve(input, Facing.Right), recognition, fighter, frame);

    private static BattleState Start(int distance, bool mirrored = false)
    {
        int x = distance / 2 * (mirrored ? -1 : 1);
        return new BattleState(0, [FighterState.Create(0, 1000, -x, mirrored ? Facing.Left : Facing.Right),
            FighterState.Create(1, 1000, x, mirrored ? Facing.Right : Facing.Left)], BattleState.CreateDefault().StateVersion);
    }

    private static FighterDefinition DefineFighter()
    {
        var chordHelper = new CommandBufferPolicy(true, 3, 1, 1, helperRunsDuringGlobalPause: true);
        var directionHelper = new CommandBufferPolicy(true, 4, 1, 1, helperRunsDuringGlobalPause: true);
        var launch = new LaunchReactionProfile(4, FixedPoint.FromFraction(3, 4),
            FixedPoint.FromFraction(7, 2), FixedPoint.FromFraction(1, 2), 13, 25,
            riseFrames: 8, lieImpactFrames: 3, transitionGravitySteps: 2,
            suppressOwnerProjectilesDuringFall: true, allowOwnerProjectilesOnCounter: true);
        var hit = new AttackDefinition(FixedPoint.FromFraction(63, 2), FixedPoint.FromFraction(121, 2),
            FixedPoint.FromFraction(63, 2), FixedPoint.FromFraction(31, 2), 0, 2,
            damage: 100, hitstunFrames: 23, blockstunFrames: 22,
            groundKnockbackFriction: FixedPoint.One, blockKnockback: FixedPoint.FromFraction(21, 2),
            launchVelocityX: FixedPoint.FromFraction(9, 2), launchVelocityY: FixedPoint.FromFraction(13, 2),
            knockdownFrames: 17, wakeupFrames: 25, launchGravity: FixedPoint.FromFraction(1, 2),
            launchReaction: launch, hitstopByOutcome: new HitstopByOutcome(new HitstopPair(11, 13), new HitstopPair(11, 11)),
            groundGuardOnly: true);
        return new FighterDefinition("Duelist",
            [new(Blowback, [CommandDirection.Neutral, CommandDirection.Forward, CommandDirection.Back], Chord,
                maxSequenceFrames: 1, buttonToleranceFrames: 0, priority: 6, bufferLifetimeFrames: 0,
                bufferPolicy: chordHelper, reserveGroundInput: true),
             new(ForwardPunch, [CommandDirection.Forward], InputBits.LightPunch, buttonToleranceFrames: 0,
                priority: 4, bufferLifetimeFrames: 0, bufferPolicy: directionHelper,
                reserveGroundInput: true, reserveOnEdgeDirection: true),
             new(Wave, [CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward], InputBits.LightPunch,
                buttonToleranceFrames: 0, priority: 8),
             new("HeavyPunch", [], InputBits.HeavyPunch, priority: 1, bufferLifetimeFrames: 0),
             new("HeavyKick", [], InputBits.HeavyKick, priority: -2, bufferLifetimeFrames: 0)],
            [new(Blowback, Blowback, 23, 3, 23, attacks: [hit], guardThreatFrames: 26,
                cancels: [new(Wave, 24, 25, MoveCancelPhases.Active, allowWhiff: true)],
                reservedCancels: [new(ForwardPunch, 24, 25, MoveCancelPhases.Active)],
                positionSteps: [.. new[] { 8, 11, 13, 15, 17, 19, 21, 23, 26, 30 }.Select(tick => new MovePositionStep(tick, 8)),
                    new(33, 5)]),
             new(ForwardPunch, ForwardPunch, 11, 9, 27, attacks: [new(
                 FixedPoint.FromFraction(119, 2), FixedPoint.FromFraction(153, 2),
                 FixedPoint.FromFraction(47, 2), FixedPoint.FromFraction(31, 2), 0, 2, damage: 50,
                 respectsAirFollowupRestriction: true)]),
             new(Wave, Wave, 15, 2, 35),
             new("HeavyPunch", "HeavyPunch", 3, 4, 5), new("HeavyKick", "HeavyKick", 3, 4, 5)],
            groundBodyWidths: new GroundBodyWidths(15, 16), useCommandPriority: true, forwardGuardDistance: 230,
            groundHurtboxes: new GroundHurtboxes([new(-19, 19, 1, 88)], [new(-19, 19, 1, 72)],
                standingGuard: [new(-19, 19, 1, 88)], crouchingGuard: [new(-19, 19, 1, 72)],
                launchAir: [new(-28, 27, 37, 92)]));
    }
}
