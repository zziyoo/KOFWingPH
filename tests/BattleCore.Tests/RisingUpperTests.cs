using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class RisingUpperTests
{
    private const string Upper = "Upper";
    private const string Wave = "Wave";
    private const string ReservedWave = "ReservedWave";
    private const string ReservedMotion = "ReservedMotion";
    private const string Light = "Light";

    [Theory]
    [InlineData(50, false, 15, Wave)]
    [InlineData(50, true, 15, Wave)]
    [InlineData(50, false, 16, Upper)]
    [InlineData(82, false, 18, Wave)]
    [InlineData(82, false, 19, Upper)]
    [InlineData(120, false, 4, Upper)]
    [InlineData(120, false, 33, Wave)]
    public void ContactPermissionFollowsTheActualEffectiveContactTick(int distance, bool guard,
        int commandFrame, string expected)
    {
        var (simulation, snapshots, inputs) = Run(distance, guard, commandFrame);
        Assert.Equal(Upper, snapshots[0].State.Fighter(0).MoveId);
        Assert.Equal(expected, snapshots[commandFrame].State.Fighter(0).MoveId);
        Assert.Equal(distance == 120 ? 0 : 1, snapshots.SelectMany(step => step.Events)
            .Count(entry => entry.MoveId == Upper && entry.Type is BattleEventType.Hit or BattleEventType.Blocked));

        if (expected == Wave)
        {
            FighterState newMove = snapshots[commandFrame].State.Fighter(0);
            Assert.Equal((0, -1, -1), (newMove.MoveElapsedFrames, newMove.HitConfirmFrame, newMove.BlockConfirmFrame));
            Assert.Null(newMove.LastContactMoveTick);
            Assert.Empty(snapshots[commandFrame].Contacts);
        }

        if (distance < 120)
        {
            int contactTick = distance == 50 ? 3 : 6;
            for (int frame = contactTick; frame < contactTick + 12; frame++)
            {
                FighterState frozen = snapshots[frame].State.Fighter(0);
                Assert.Equal(contactTick, frozen.LastContactMoveTick);
                Assert.Equal(contactTick, frozen.MoveElapsedFrames);
            }
        }

        BattleState checkpoint = snapshots[commandFrame - 1].State;
        BattleStepResult replay = simulation.Step(checkpoint, inputs[commandFrame]);
        Assert.Equal(snapshots[commandFrame].State.Fighter(0), replay.State.Fighter(0));
        Assert.Equal(snapshots[commandFrame].Events.ToArray(), replay.Events.ToArray());
    }

    [Theory]
    [InlineData(50, 15, true)]
    [InlineData(50, 16, false)]
    [InlineData(120, 4, false)]
    public void AnUnboundTargetOnlyConsumesAnInputWithFreshContactPermission(int distance, int commandFrame,
        bool consumed)
    {
        var (_, snapshots, _) = Run(distance, guard: false, commandFrame, mode2: true);
        FighterState after = snapshots[commandFrame].State.Fighter(0);
        Assert.Equal(Upper, after.MoveId);
        Assert.Contains(after.RecognitionState.Buffer, result =>
            result.Match.CommandId == ReservedWave && result.Consumed == consumed);
    }

    [Theory]
    [InlineData(50, 15, Upper, true)]
    [InlineData(82, 18, Wave, false)]
    public void HigherPriorityMotionReadsTheInitialDiagonalRelease(int distance, int commandFrame, string move,
        bool reserved)
    {
        var (_, snapshots, _) = Run(distance, guard: false, commandFrame, reserveEarlyMotion: true);
        FighterState attacker = snapshots[commandFrame].State.Fighter(0);
        Assert.Equal(move, attacker.MoveId);
        Assert.Equal(reserved, attacker.RecognitionState.Buffer.Any(result =>
            result.Match.CommandId == ReservedMotion && result.Consumed));
    }

    [Theory]
    [InlineData(false, FighterLogicState.Hitstun)]
    [InlineData(true, FighterLogicState.AirHitstun)]
    public void AuthoredAirLaunchDoesNotLaunchAGroundTarget(bool airborne, FighterLogicState reaction)
    {
        var (_, snapshots, _) = Run(50, guard: false, commandFrame: 10, airborne: airborne);
        FighterState target = snapshots[3].State.Fighter(1);
        Assert.Equal(reaction, target.LogicState);
        Assert.Equal(13, target.HitstopRemainingFrames);
        Assert.Equal(airborne ? FixedPoint.FromFraction(13, 2) : FixedPoint.Zero, target.VelocityY);
        Assert.Equal(airborne ? FixedPoint.FromFraction(9, 2) : FixedPoint.Zero, target.VelocityX);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(-1, true, false)]
    [InlineData(1, false, false)]
    [InlineData(1, true, true)]
    public void ContactAgeRejectsInvalidOrUnconfirmedPolicies(int age, bool confirmed, bool whiff)
    {
        Assert.ThrowsAny<ArgumentException>(() => new CancelDefinition(Wave, 4, 9, requiresHitConfirm: confirmed,
            allowWhiff: whiff, maximumContactAge: age));
        if (!whiff)
            Assert.ThrowsAny<ArgumentException>(() => new ReservedCancelDefinition(ReservedWave, 4, 9,
                MoveCancelPhases.Active, requiresHitConfirm: confirmed, maximumContactAge: age));
    }

    [Fact]
    public void AuthoredFacingChangeAppliesAfterReadingTheCommandAgainstTheOldFacing()
    {
        var (_, snapshots, _) = Run(50, guard: false, commandFrame: 10, opponentBehind: true);
        FighterState start = snapshots[0].State.Fighter(0);
        Assert.Equal((Upper, 0, Facing.Left), (start.MoveId, start.MoveElapsedFrames, start.Facing));
        Assert.Contains(snapshots[3].Events, entry => entry.Type == BattleEventType.Hit && entry.MoveId == Upper);
    }

    [Theory]
    [InlineData(InputBits.Down, FighterLogicState.Standing, null, false)]
    [InlineData(InputBits.Down | InputBits.Right | InputBits.HeavyPunch, FighterLogicState.Standing, null, false)]
    [InlineData(InputBits.Right, FighterLogicState.Standing, null, true)]
    [InlineData(InputBits.Up, FighterLogicState.PreJump, null, false)]
    [InlineData(InputBits.Down | InputBits.LightPunch, FighterLogicState.Startup, "CrouchLight", false)]
    public void NaturalExitDefersPassiveCrouchWithoutDelayingDecidedActions(InputBits held,
        FighterLogicState state, string? move, bool walks)
    {
        FighterDefinition fighter = MakeFighter(mode2: false, reserveEarlyMotion: false);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState current = new(0, [FighterState.Create(0, 1000, -120, Facing.Right),
            FighterState.Create(1, 1000, 120, Facing.Left)], BattleState.CreateDefault().StateVersion);
        InputBits initial = InputBits.Down | InputBits.Right | InputBits.HeavyPunch;
        bool heldThrough = (held & InputBits.HeavyPunch) != 0;
        for (int frame = 0; frame < 33; frame++)
            current = simulation.Step(current, new FrameInput(frame,
                frame == 0 || heldThrough ? initial : InputBits.None, InputBits.None)).State;

        FighterState before = current.Fighter(0);
        var exit = simulation.Step(current, new FrameInput(33, held, InputBits.None)).State;
        Assert.Equal((state, move), (exit.Fighter(0).LogicState, exit.Fighter(0).MoveId));
        Assert.Equal(walks, before.PositionX != exit.Fighter(0).PositionX);
        if (state == FighterLogicState.Standing && (held & InputBits.Down) != 0)
            Assert.Equal(FighterLogicState.Crouching, simulation.Step(exit, new FrameInput(34, held, InputBits.None))
                .State.Fighter(0).LogicState);
    }

    private static (BattleSimulation Runner, BattleStepResult[] Snapshots, FrameInput[] Inputs) Run(
        int distance, bool guard, int commandFrame, bool mode2 = false, bool airborne = false,
        bool reserveEarlyMotion = false, bool opponentBehind = false)
    {
        FighterDefinition fighter = MakeFighter(mode2, reserveEarlyMotion);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        int halfDistance = distance / 2 * (opponentBehind ? -1 : 1);
        FighterState target = FighterState.Create(1, 1000, halfDistance, opponentBehind ? Facing.Right : Facing.Left);
        if (airborne)
            target = target with { LogicState = FighterLogicState.Airborne, PositionY = 20, JumpKind = JumpKind.HighJump };
        BattleState state = new(0,
            [FighterState.Create(0, 1000, -halfDistance, Facing.Right), target], BattleState.CreateDefault().StateVersion);
        FrameInput[] inputs = Enumerable.Range(0, commandFrame + 1).Select(frame => new FrameInput(frame,
            frame == 0 ? InputBits.Right | InputBits.Down | InputBits.HeavyPunch
            : frame == commandFrame - 2 ? InputBits.Down
            : frame == commandFrame - 1 ? InputBits.Down | InputBits.Right
            : frame == commandFrame ? InputBits.Right | InputBits.LightPunch : InputBits.None,
            guard && frame >= 3 ? InputBits.Right : InputBits.None)).ToArray();
        var snapshots = new BattleStepResult[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            snapshots[i] = simulation.Step(state, inputs[i]);
            state = snapshots[i].State;
        }
        return (simulation, snapshots, inputs);
    }

    private static FighterDefinition MakeFighter(bool mode2, bool reserveEarlyMotion) => new("ContactAge",
        [
            new(Upper, [CommandDirection.DownForward], InputBits.HeavyPunch, buttonToleranceFrames: 0,
                priority: 3, reserveGroundInput: true, reserveOnEdgeDirection: true,
                bufferPolicy: new CommandBufferPolicy(true, 4, 1, 1)),
            new(mode2 ? ReservedWave : Wave, [CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward],
                InputBits.LightPunch, buttonToleranceFrames: 0, priority: 8, bufferLifetimeFrames: 0,
                matchHeldMotion: true, matchCompletedMotion: true, reserveGroundInput: mode2,
                bufferPolicy: new CommandBufferPolicy(true, 12, 1, 4)),
            .. reserveEarlyMotion ? [new CommandDefinition(ReservedMotion,
                [CommandDirection.Forward, CommandDirection.Down, CommandDirection.DownForward], InputBits.LightPunch,
                maxSequenceFrames: 14, buttonToleranceFrames: 0, priority: 9, bufferLifetimeFrames: 0,
                matchHeldMotion: true, matchCompletedMotion: true, matchInitialCardinalRelease: true,
                reserveGroundInput: true, bufferPolicy: new CommandBufferPolicy(true, 12, 1, 4))]
                : Array.Empty<CommandDefinition>(),
            new(Light, [], InputBits.LightPunch),
        ],
        [
            new(Upper, Upper, 3, 6, 24, attacks:
                [Hitbox(43, 0), Hitbox(67, 3)],
                cancels: mode2 ? [] : [new(Wave, 4, 9, MoveCancelPhases.Active,
                    requiresHitConfirm: true, requiresBlockConfirm: true, maximumContactAge: 1)],
                reservedCancels: mode2 || reserveEarlyMotion ? [new(mode2 ? ReservedWave : ReservedMotion,
                    4, 9, MoveCancelPhases.Active, requiresHitConfirm: true, requiresBlockConfirm: true,
                    maximumContactAge: 1)] : [], faceOpponentOnStart: true, deferCrouchOnNaturalExit: true),
            .. mode2 ? Array.Empty<MoveDefinition>() : [new(Wave, Wave, 15, 2, 30)],
            new(Light, Light, 3, 4, 5, MoveAllowedStates.Standing),
            new("CrouchLight", Light, 3, 2, 8, MoveAllowedStates.Crouching),
        ], useCommandPriority: true, groundHurtboxes: new GroundHurtboxes(
            [new(-10, 10, 1, 85)], [new(-10, 10, 1, 65)]));

    private static AttackDefinition Hitbox(int x, int firstFrame) => new(
        x, 55, 10, 10, firstFrame, firstFrame + 2, damage: 13,
        launchVelocityX: FixedPoint.FromFraction(9, 2), launchVelocityY: FixedPoint.FromFraction(13, 2),
        knockdownFrames: 17, wakeupFrames: 25, launchGravity: FixedPoint.FromFraction(1, 2),
        hitstopByOutcome: new HitstopByOutcome(new(11, 13), new(11, 11)), groundGuardOnly: true,
        launchOnAirborneOnly: true);
}
