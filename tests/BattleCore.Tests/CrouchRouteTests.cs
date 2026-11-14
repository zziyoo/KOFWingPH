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
