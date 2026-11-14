using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class CommandPriorityTests
{
    private const string Light = "Light";
    private const string Heavy = "Heavy";
    private const string Wave = "Wave";

    private static readonly InputBits[] Motion =
        [InputBits.Down, InputBits.Down | InputBits.Right, InputBits.Right, InputBits.Right | InputBits.LightPunch];

    private static FighterDefinition Fighter(bool useCommandPriority = true, int waveMinDistance = 0,
        bool matchCompletedMotion = false) => new("Test", new CommandDefinition[]
    {
        new("5A", [], InputBits.LightPunch, priority: 1, bufferLifetimeFrames: 2),
        new("5B", [], InputBits.HeavyPunch, priority: 0, bufferLifetimeFrames: 2),
        new("236A", [CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward],
            InputBits.LightPunch, maxSequenceFrames: 16, buttonToleranceFrames: 0, priority: 2,
            bufferLifetimeFrames: 0, matchHeldMotion: true, bufferPolicy: new CommandBufferPolicy(true, 12, 1, 4),
            matchCompletedMotion: matchCompletedMotion),
    }, new MoveDefinition[]
    {
        new(Light, "5A", 3, 4, 5, cancels:
        [
            new CancelDefinition(Light, 7, 11, allowWhiff: true, allowSameMove: true,
                standingInputOnly: true, maximumInputAge: 2),
            new CancelDefinition(Wave, 4, 6, MoveCancelPhases.Active, allowWhiff: true),
        ]),
        new(Heavy, "5B", 3, 4, 5),
        new(Wave, "236A", 15, 2, 35, minimumStartDistance: waveMinDistance),
    }, useCommandPriority: useCommandPriority);

    private static RecognitionState Advance(FighterDefinition fighter, IEnumerable<InputBits> inputs,
        int first = 0, Facing facing = Facing.Right, bool frozen = false, RecognitionState? prior = null,
        bool globalPaused = false)
    {
        RecognitionState state = prior ?? RecognitionState.Empty;
        int frame = first;
        foreach (InputBits bits in inputs)
            state = state.Advance(bits, facing, frame++, 0, fighter.Commands,
                useCommandPriority: fighter.UseCommandPriority, inHitPause: frozen, globalPaused: globalPaused);
        return state;
    }

    private static FighterActionTick Tick(FighterDefinition fighter, RecognitionState recognition, int tick,
        int frame, InputBits input = InputBits.None)
    {
        FighterLogicState before = tick <= 7 ? FighterLogicState.Active : FighterLogicState.Recovery;
        int remaining = tick <= 7 ? 8 - tick : 13 - tick;
        return FighterStateMachine.Tick(before, Light, remaining, DirectionResolver.Resolve(input, Facing.Right),
            recognition, fighter, frame, moveStartedFrame: 0, moveElapsedFrames: tick);
    }

    [Theory]
    [InlineData(4, Wave)]
    [InlineData(5, Wave)]
    [InlineData(6, Wave)]
    [InlineData(7, Light)]
    public void OnePhysicalPressSelectsTheHighestEligibleRoute(int tick, string expected)
    {
        FighterDefinition fighter = Fighter();
        RecognitionState recognition = Advance(fighter, Motion);
        Assert.Equal(2, recognition.Buffer.Count);
        FighterActionTick action = Tick(fighter, recognition, tick, 3);
        Assert.Equal(expected, action.MoveId);
        Assert.Equal(3, action.MoveStartedFrame);
        Assert.Equal(0, action.MoveElapsedFrames);
        Assert.All(action.Recognition.Buffer, entry => Assert.True(entry.Consumed));
        Assert.All(recognition.Buffer, entry => Assert.False(entry.Consumed));
    }

    [Theory]
    [InlineData(false, Heavy)]
    [InlineData(true, Light)]
    public void TerminalSelectionKeepsLegacyFifoAndUsesNativePriorityOnlyWhenOptedIn(bool optedIn, string expected)
    {
        FighterDefinition fighter = Fighter(optedIn);
        RecognitionState recognition = Advance(fighter, [InputBits.HeavyPunch, InputBits.LightPunch], first: 11);
        Assert.Equal(expected, Tick(fighter, recognition, 12, 12).MoveId);
    }

    [Fact]
    public void TerminalSpecialBeatsAnOlderHeavyAndAnOlderSpecialBeatsCurrentHeavy()
    {
        FighterDefinition fighter = Fighter();
        var olderHeavy = new[] { Motion[0], Motion[1], Motion[2] | InputBits.HeavyPunch, Motion[3] };
        Assert.Equal(Wave, Tick(fighter, Advance(fighter, olderHeavy, first: 9), 12, 12).MoveId);
        var olderWave = new[] { Motion[0], Motion[1], Motion[2], Motion[3], InputBits.HeavyPunch };
        Assert.Equal(Wave, Tick(fighter, Advance(fighter, olderWave, first: 8), 12, 12).MoveId);
    }

    [Fact]
    public void FailedSpecialDistanceDoesNotConsumeItsLegalSameEdgeFallback()
    {
        FighterDefinition fighter = Fighter(waveMinDistance: 10);
        FighterActionTick action = Tick(fighter, Advance(fighter, Motion), 12, 3);
        Assert.Equal(Light, action.MoveId);
        Assert.All(action.Recognition.Buffer, entry => Assert.True(entry.Consumed));
    }

    [Fact]
    public void ADeferredMotionAtNaturalRecoveryDoesNotReplayTheSamePunch()
    {
        FighterDefinition profile = Fighter();
        CommandDefinition normal = profile.Commands[0];
        var reserved = new CommandDefinition("ReservedMotion", [CommandDirection.Down, CommandDirection.DownForward,
            CommandDirection.Forward], InputBits.LightPunch, maxSequenceFrames: 16, buttonToleranceFrames: 0,
            priority: 2, bufferLifetimeFrames: 0, matchHeldMotion: true, reserveGroundInput: true,
            bufferPolicy: new CommandBufferPolicy(true, 12, 1, 4));
        var light = new MoveDefinition(Light, normal.CommandId, 3, 4, 5);
        var fighter = new FighterDefinition("Test", [normal, reserved], [light], useCommandPriority: true);
        RecognitionState motion = Advance(fighter, Motion, first: 8);
        RecognitionState buffered = Advance(fighter, [InputBits.Down], first: 12, prior: motion);
        FighterActionTick result = Tick(fighter, buffered, 12, 12, InputBits.Down);
        Assert.Null(result.MoveId);
        Assert.Null(result.ConsumedMatch);
        Assert.All(result.Recognition.Buffer, candidate => Assert.True(candidate.Consumed));
    }

    [Fact]
    public void HelperBridgesTheEarlyBoundaryAndExpiresAfterThreeReleasedTicksHaveRun()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState early = Advance(fighter,
            [InputBits.Down, InputBits.None, InputBits.Down | InputBits.Right, InputBits.Right, Motion[3]]);
        Assert.Null(Tick(fighter, early, 3, 4).ConsumedMatch);
        RecognitionState ageOne = Advance(fighter, [InputBits.None], first: 5, prior: early);
        Assert.Equal(Wave, Tick(fighter, ageOne, 4, 5).MoveId);
        RecognitionState ageThree = Advance(fighter, [InputBits.None, InputBits.None, InputBits.None], first: 5, prior: early);
        Assert.Equal(Wave, Tick(fighter, ageThree, 6, 7).MoveId);
        RecognitionState ageFour = Advance(fighter, [InputBits.None], first: 8, prior: ageThree);
        Assert.DoesNotContain(ageFour.Buffer, entry => entry.Match.CommandId is "236A" or "5A");
    }

    [Fact]
    public void FrozenRootRetainsMotionAfterReleasedHelperExpiresAndDoesNotKeepLightFallback()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState frozen = Advance(fighter, Motion, first: 8, frozen: true);
        var blanks = Enumerable.Repeat(InputBits.None, 8);
        RecognitionState continued = Advance(fighter, blanks, first: 12, frozen: true, prior: frozen);
        Assert.DoesNotContain(continued.Buffer, entry => entry.Match.CommandId == "5A");
        Assert.Equal(continued, Advance(fighter, blanks, first: 12, frozen: true, prior: frozen));
        RecognitionState thaw = Advance(fighter, [InputBits.None], first: 20, prior: continued);
        Assert.Equal(Wave, Tick(fighter, thaw, 4, 20).MoveId);
        RecognitionState afterThaw = Advance(fighter, [InputBits.None], first: 21, prior: thaw);
        Assert.DoesNotContain(afterThaw.Buffer, entry => entry.Match.CommandId == "236A");
    }

    [Fact]
    public void HeldFinalDirectionSupportsMirroredPhysicalMotionAndAgedMotionIsRejected()
    {
        FighterDefinition fighter = Fighter();
        InputBits[] mirrored = [InputBits.Down, InputBits.Down | InputBits.Left, InputBits.Left,
            InputBits.Left, InputBits.Left | InputBits.LightPunch];
        Assert.Equal(Wave, Tick(fighter, Advance(fighter, mirrored, facing: Facing.Left), 4, 4).MoveId);
        var stale = new[] { Motion[0], Motion[1], Motion[2] }
            .Concat(Enumerable.Repeat(InputBits.Right, 16)).Append(Motion[3]);
        Assert.DoesNotContain(Advance(fighter, stale).Buffer, entry => entry.Match.CommandId == "236A");
    }

    [Fact]
    public void MotionTimeStartsWhenPureDownIsReleased()
    {
        FighterDefinition fighter = Fighter();
        InputBits[] chargedDown = Enumerable.Repeat(InputBits.Down, 20).Concat(Motion.Skip(1)).ToArray();
        CommandMatch match = Assert.Single(Advance(fighter, chargedDown).Buffer, entry => entry.Match.CommandId == "236A").Match;
        Assert.Equal(20, match.SequenceStartFrame);
        Assert.Equal(22, match.ButtonFrame);

        // Native completion is checked before expiration at the final allowed elapsed tick.
        var exactBoundary = Motion.Take(3).Concat(Enumerable.Repeat(InputBits.Right, 14)).Append(Motion[3]);
        Assert.Contains(Advance(fighter, exactBoundary).Buffer, entry => entry.Match.CommandId == "236A");
        var expired = Motion.Take(3).Concat(Enumerable.Repeat(InputBits.Right, 15)).Append(Motion[3]);
        Assert.DoesNotContain(Advance(fighter, expired).Buffer, entry => entry.Match.CommandId == "236A");
    }

    [Theory]
    [InlineData(false, InputBits.Down | InputBits.Right, 1, true, false)]
    [InlineData(true, InputBits.Down | InputBits.Right, 1, true, true)]
    [InlineData(true, InputBits.Down | InputBits.Left, 2, true, true)]
    [InlineData(true, InputBits.Left, 3, true, true)]
    [InlineData(true, InputBits.None, 3, true, true)]
    [InlineData(true, InputBits.Down | InputBits.Right, 1, false, false)]
    [InlineData(true, InputBits.Down | InputBits.Right, 16, true, false)]
    public void CompletedMotionKeepsItsFinalCardinalStepUntilTheButtonOrDeadline(bool enabled,
        InputBits finish, int delay, bool includedForward, bool expected)
    {
        FighterDefinition fighter = Fighter(matchCompletedMotion: enabled);
        var sequence = new List<InputBits> { InputBits.Down, InputBits.Down | InputBits.Right };
        if (includedForward)
            sequence.Add(InputBits.Right);
        sequence.AddRange(Enumerable.Repeat(finish, delay - 1));
        sequence.Add(finish | InputBits.LightPunch);
        RecognitionState recognized = Advance(fighter, sequence);
        Assert.Equal(expected, recognized.Buffer.Any(entry => entry.Match.CommandId == "236A"));
        if (!expected)
            return;
        CommandMatch special = Assert.Single(recognized.Buffer, entry => entry.Match.CommandId == "236A").Match;
        Assert.Equal(2, special.SequenceEndFrame);
        Assert.Equal(sequence.Count - 1, special.ButtonFrame);
        RecognitionState second = Advance(fighter, [finish, finish | InputBits.LightPunch],
            first: sequence.Count, prior: recognized);
        Assert.DoesNotContain(second.Buffer, entry => entry.Match.CommandId == "236A" &&
            entry.Match.ButtonFrame != special.ButtonFrame);
    }

    [Fact]
    public void DownInsideLightWindowDiscardsBothRoutesFromTheSameEdge()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState recognition = Advance(fighter, Motion);
        FighterActionTick reject = Tick(fighter, recognition, 7, 3, InputBits.Down);
        Assert.Null(reject.ConsumedMatch);
        Assert.All(reject.Recognition.Buffer, entry => Assert.True(entry.Consumed));
        RecognitionState release = Advance(fighter, [InputBits.None], first: 4, prior: reject.Recognition);
        Assert.Null(Tick(fighter, release, 8, 4).ConsumedMatch);
    }

    [Fact]
    public void GlobalPauseHoldsExistingHelperWithoutExtendingRootOrAcceptingNewHelperCommand()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState recognized = Advance(fighter, Motion);
        RecognitionState paused = Advance(fighter, Enumerable.Repeat(InputBits.None, 5), first: 4,
            prior: recognized, globalPaused: true);
        Assert.Equal(Wave, Tick(fighter, Advance(fighter, [InputBits.None], first: 9, prior: paused), 4, 9).MoveId);

        RecognitionState motionBeforePause = Advance(fighter, Motion.Take(3));
        RecognitionState recognizedDuringPause = Advance(fighter, [Motion[3]], first: 3,
            prior: motionBeforePause, globalPaused: true);
        Assert.Contains(recognizedDuringPause.Buffer, entry => entry.Match.CommandId == "236A");
        RecognitionState thaw = Advance(fighter, [InputBits.None], first: 4, prior: recognizedDuringPause);
        Assert.DoesNotContain(thaw.Buffer, entry => entry.Match.CommandId == "236A");
    }

    [Fact]
    public void RootMotionRecognizedDuringGlobalPauseDoesNotReplaceEarlierHelperMotion()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState first = Advance(fighter, Motion);
        RecognitionState beforePause = Advance(fighter,
            [Motion[0] | InputBits.LightPunch, Motion[1] | InputBits.LightPunch, Motion[2]],
            first: 4, prior: first);
        RecognitionState paused = Advance(fighter, [Motion[3]], first: 7, prior: beforePause, globalPaused: true);
        Assert.Equal(7, paused.Last?.ButtonFrame);

        RecognitionState thaw = Advance(fighter, [InputBits.None], first: 8, prior: paused);
        FighterActionTick action = Tick(fighter, thaw, 4, 8);
        Assert.Equal(Wave, action.MoveId);
        Assert.Equal(3, action.ConsumedMatch?.ButtonFrame);
        Assert.Contains(action.Recognition.Buffer, entry => entry.Match.CommandId == "5A" &&
            entry.Match.ButtonFrame == 7 && !entry.Consumed);
    }

    [Fact]
    public void BufferedMotionWithSameEdgeCannotReplayAfterSuccessfulFallback()
    {
        FighterDefinition fighter = Fighter();
        FighterActionTick fallback = Tick(fighter, Advance(fighter, Motion), 7, 3);
        Assert.Equal(Light, fallback.MoveId);
        RecognitionState held = Advance(fighter, Enumerable.Repeat(Motion[3], 8), first: 4, prior: fallback.Recognition);
        Assert.DoesNotContain(held.Buffer, entry => !entry.Consumed);
        Assert.Null(Tick(fighter, held, 11, 11).ConsumedMatch);
    }

    [Fact]
    public void BattleSimulationCommitsBothMirroredCancellationsAfterTheirOwnHitPause()
    {
        FighterDefinition fighter = Fighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        FighterState p1 = FighterState.Create(0, 1000, -50, Facing.Right) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = Light,
            MoveStartedFrame = 5,
            MoveElapsedFrames = 3,
            PhaseRemainingFrames = 4,
            HitstopRemainingFrames = 11,
            HitConfirmFrame = 8,
        };
        FighterState p2 = p1 with { Id = 1, PositionX = 50, Facing = Facing.Left, HitConfirmFrame = -1, BlockConfirmFrame = 8 };
        var initial = new BattleState(9, [p1, p2], stateVersion: 1);

        BattleState Replay()
        {
            BattleState state = initial;
            for (int frame = 9; frame <= 20; frame++)
            {
                InputBits one = frame switch
                {
                    9 => InputBits.Down,
                    10 => InputBits.Down | InputBits.Right,
                    11 => InputBits.Right | InputBits.LightPunch,
                    _ => InputBits.None,
                };
                InputBits two = (one & ~InputBits.Right) | ((one & InputBits.Right) != 0 ? InputBits.Left : InputBits.None);
                state = simulation.Step(state, new FrameInput(frame, one, two)).State;
                if (frame < 20)
                    Assert.All(state.Fighters, side => Assert.Equal(3, side.MoveElapsedFrames));
            }
            return state;
        }

        BattleState actual = Replay();
        Assert.Equal(actual.Fighters, Replay().Fighters);
        Assert.All(actual.Fighters, side =>
        {
            Assert.Equal(Wave, side.MoveId);
            Assert.Equal(20, side.MoveStartedFrame);
            Assert.Equal(0, side.MoveElapsedFrames);
            Assert.Equal(-1, side.HitConfirmFrame);
            Assert.Equal(-1, side.BlockConfirmFrame);
        });
    }
}
