using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class MultiSourceCancelTests
{
    private const string Near = "Near";
    private const string Far = "Far";
    private const string Wave = "Wave";

    [Theory]
    [InlineData(Near, 4, 31, Wave)]
    [InlineData(Near, 6, 29, Wave)]
    [InlineData(Far, 4, 29, Wave)]
    [InlineData(Near, 7, 16, Near)]
    [InlineData(Near, 7, 17, Far)]
    [InlineData(Far, 7, 16, Near)]
    [InlineData(Far, 7, 17, Far)]
    public void OneMotionUsesTheSourceWindowThenTheCurrentDistance(string source, int tick, int bodyDistance, string target)
    {
        FighterDefinition fighter = Fighter();
        RecognitionState input = RecognitionState.Empty;
        for (int frame = 0; frame < 4; frame++)
            input = input.Advance(MotionAt(frame, Facing.Right), Facing.Right, frame, 0,
                fighter.Commands, useCommandPriority: true);
        Assert.Equal(new[] { "236A", "5A" }, input.Buffer.Select(candidate => candidate.Match.CommandId).Order());

        FighterActionTick action = FighterStateMachine.Tick(FighterLogicState.Active, source, 8 - tick,
            DirectionResolver.Resolve(MotionAt(3, Facing.Right), Facing.Right), input, fighter, 3,
            moveStartedFrame: 0, moveElapsedFrames: tick, opponentBodyDistance: bodyDistance);

        Assert.Equal(target, action.MoveId);
        Assert.Equal(0, action.MoveElapsedFrames);
        Assert.All(action.Recognition.Buffer, candidate => Assert.True(candidate.Consumed));
        Assert.All(input.Buffer, candidate => Assert.False(candidate.Consumed));
    }

    [Fact]
    public void SharedDestinationUsesIndependentClocksAndSpawnsAfterEachSourceThaws()
    {
        FighterDefinition fighter = Fighter();
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        FighterState priorNear = FighterState.Create(0, 1000, -50, Facing.Right) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = Near,
            MoveStartedFrame = 5,
            MoveElapsedFrames = 3,
            PhaseRemainingFrames = 4,
            HitstopRemainingFrames = 11,
            HitConfirmFrame = 8,
        };
        FighterState priorFar = priorNear with
        {
            Id = 1,
            PositionX = 50,
            Facing = Facing.Left,
            MoveId = Far,
            HitstopRemainingFrames = 12,
            HitConfirmFrame = -1,
            BlockConfirmFrame = 8,
        };
        var initial = new BattleState(9, [priorNear, priorFar], BattleState.CreateDefault().StateVersion);

        BattleState state = initial;
        for (int frame = 9; frame <= 36; frame++)
        {
            var input = new FrameInput(frame, MotionAt(frame - 9, Facing.Right), MotionAt(frame - 9, Facing.Left));
            BattleStepResult result = simulation.Step(state, input);
            BattleStepResult replay = simulation.Step(state, input);
            Assert.Equal(result.State.Fighters, replay.State.Fighters);
            Assert.Equal(result.State.Projectiles, replay.State.Projectiles);
            Assert.Equal(result.Events, replay.Events);
            state = result.State;
            if (frame < 20)
                Assert.Equal(3, state.Fighter(0).MoveElapsedFrames);
            if (frame < 21)
                Assert.Equal(3, state.Fighter(1).MoveElapsedFrames);
            if (frame is 20 or 21)
            {
                int seat = frame - 20;
                FighterState side = state.Fighter(seat);
                Assert.Equal((Wave, frame, 0, -1, -1),
                    (side.MoveId, side.MoveStartedFrame, side.MoveElapsedFrames, side.HitConfirmFrame, side.BlockConfirmFrame));
                Assert.Empty(FighterBoxes.GetActiveHitboxes(side, fighter));
                Assert.Single(result.Events, e => e.Type == BattleEventType.MoveStarted && e.SourceFighterId == seat);
            }
            if (frame < 35)
                Assert.Empty(state.Projectiles);
            if (frame == 35)
                Assert.Equal(0, Assert.Single(state.Projectiles).OwnerFighterId);
        }

        Assert.Equal(new[] { 0, 1 }, state.Projectiles.Select(entity => entity.OwnerFighterId));
        Assert.Equal(new Facing?[] { Facing.Right, Facing.Left }, state.Projectiles.Select(entity => entity.SpawnFacing));
        Assert.Equal(new[] { 24, 25 }, state.Projectiles.Select(entity => entity.RemainingLifetime));
        Assert.Equal(2, state.Projectiles.Select(entity => entity.ProjectileId).Distinct().Count());
    }

    private static InputBits MotionAt(int frame, Facing facing)
    {
        InputBits forward = facing == Facing.Right ? InputBits.Right : InputBits.Left;
        return frame switch
        {
            0 => InputBits.Down,
            1 => InputBits.Down | forward,
            2 => forward,
            3 => forward | InputBits.LightPunch,
            _ => InputBits.None,
        };
    }

    private static FighterDefinition Fighter()
    {
        MoveDefinition Light(string id, int last, FixedPoint? distance = null) => new(id, "5A", 3, 4, 5,
            attacks: [new AttackDefinition(5, 30, 1, 1, damage: 10)],
            cancels:
            [
                new(Wave, 4, last, MoveCancelPhases.Active, allowWhiff: true),
                new(Near, 7, 11, allowWhiff: true, allowSameMove: id == Near,
                    standingInputOnly: true, maximumInputAge: 2),
                new(Far, 7, 11, allowWhiff: true, allowSameMove: id == Far,
                    standingInputOnly: true, maximumInputAge: 2),
            ], maximumStartBodyDistance: distance);

        var hit = new AttackDefinition(0, 0, 1, 1);
        var timeline = new ProjectileTimeline(11, [new ProjectileHitSpan(1, 12, new FixedRect(1, 2, 1, 2), hit)]);
        return new FighterDefinition("Routes",
            [
                new("5A", [], InputBits.LightPunch, priority: 1, bufferLifetimeFrames: 2),
                new("236A", [CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward],
                    InputBits.LightPunch, maxSequenceFrames: 16, buttonToleranceFrames: 0, priority: 2,
                    bufferLifetimeFrames: 0, matchHeldMotion: true, bufferPolicy: new CommandBufferPolicy(true, 12, 1, 4)),
            ],
            [Light(Near, 6, 16), Light(Far, 5), new(Wave, "236A", 15, 2, 35,
                projectiles: [new ProjectileDefinition("Entity", hit, 2, lifetimeFrames: 25, timeline: timeline)])],
            groundBodyWidths: new GroundBodyWidths(15, 16), useCommandPriority: true);
    }
}
