using System.Text.Json.Nodes;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class ProximityGuardTests
{
    private const string MoveId = "Threat";
    private const string EntityId = "ThreatEntity";
    private const int EntityLifetime = 25;
    private const int GuardDistance = 230;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackMovementStopsOnCommittedStartupThreatAndResumesAtTheExclusiveEnd(bool mirrored)
    {
        var (simulation, initial) = Setup(mirrored, moveThreatFrames: 3, targetX: 60);
        int sign = mirrored ? -1 : 1;
        InputBits back = mirrored ? InputBits.Left : InputBits.Right;

        BattleState state = Step(simulation, initial, back, InputBits.LightPunch);
        FixedPoint guardX = sign * FixedPoint.FromFraction(123, 2);
        Assert.Equal(guardX, state.Fighter(1).PositionX);
        Assert.Equal(FighterLogicState.Startup, state.Fighter(0).LogicState);
        Assert.Equal(0, state.Fighter(0).MoveElapsedFrames);

        for (int elapsed = 1; elapsed <= 3; elapsed++)
        {
            state = Step(simulation, state, back);
            Assert.Equal(elapsed, state.Fighter(0).MoveElapsedFrames);
            Assert.Equal(guardX, state.Fighter(1).PositionX);
        }

        state = Step(simulation, state, back);
        Assert.Equal(4, state.Fighter(0).MoveElapsedFrames);
        Assert.Equal(guardX + sign * FixedPoint.FromFraction(3, 2), state.Fighter(1).PositionX);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptInGuardEntryUsesPriorBackwardMomentumUntilBelowOne(bool mirrored)
    {
        var (legacy, initial) = Setup(mirrored, moveThreatFrames: 7, targetX: 60);
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "fighters", "Kai.json"));
        JsonNode configuration = JsonNode.Parse(json)!;
        configuration["preserveGuardEntryMomentum"] = true;
        configuration["movement"]!["walkBackwardSpeed"] = JsonNode.Parse("""{"numerator":811,"denominator":256}""");
        configuration["movement"]!["groundReaction"] = JsonNode.Parse("""
            {"velocityRetention":{"numerator":17,"denominator":20},"lastHitStepScale":{"numerator":3,"denominator":5}}
            """);
        FighterDefinition target = FighterDataLoader.Load(configuration.ToJsonString());
        var simulation = new BattleSimulation(new FightRoster(legacy.Roster.Player1, target));
        int sign = mirrored ? -1 : 1;
        InputBits back = mirrored ? InputBits.Left : InputBits.Right;
        BattleState state = Step(simulation, initial, back, InputBits.LightPunch);
        FixedPoint expected = initial.Fighter(1).PositionX + sign * target.Movement.WalkBackwardSpeed;
        Assert.Equal(expected, state.Fighter(1).PositionX);

        FixedPoint retention = FixedPoint.FromFraction(17, 20);
        FixedPoint residual = target.Movement.WalkBackwardSpeed * retention;
        for (int tick = 1; tick <= 7; tick++)
        {
            if (residual >= FixedPoint.One)
                expected += sign * residual;
            state = Step(simulation, state, back);
            Assert.Equal(expected, state.Fighter(1).PositionX);
            Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
            residual *= retention;
        }

        state = Step(simulation, state, back);
        Assert.Equal(expected + sign * target.Movement.WalkBackwardSpeed, state.Fighter(1).PositionX);
    }

    [Theory]
    [InlineData(229, true, true, true)]
    [InlineData(230, true, true, false)]
    [InlineData(231, true, true, false)]
    [InlineData(60, false, true, false)]
    [InlineData(60, true, false, false)]
    public void ThreatRequiresSourceOptInAndStrictForwardDistance(
        int distance, bool sourceOptIn, bool forward, bool stationary)
    {
        var (simulation, initial) = Setup(mirrored: false, moveThreatFrames: 3, targetX: distance,
            sourceOptIn: sourceOptIn, sourceFacing: forward ? Facing.Right : Facing.Left);
        BattleState started = Step(simulation, initial, InputBits.None, InputBits.LightPunch);
        BattleState back = Step(simulation, started, InputBits.Right);
        FixedPoint expectedX = stationary ? distance : FixedPoint.FromFraction(2 * distance + 3, 2);
        Assert.Equal(expectedX, back.Fighter(1).PositionX);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedEntityThreatUsesItsSpawnFacingAndEndsBeforeItDisappears(bool mirrored)
    {
        const int threatFrames = 13;
        int sign = mirrored ? -1 : 1;
        Facing facing = mirrored ? Facing.Left : Facing.Right;
        var (simulation, initial) = Setup(mirrored, targetX: 60, entityThreatFrames: threatFrames,
            sourceFacing: mirrored ? Facing.Right : Facing.Left);
        InputBits back = mirrored ? InputBits.Left : InputBits.Right;
        ProjectileState entity = new(1, 0, EntityId, MoveId, FixedPoint.Zero, FixedPoint.Zero, sign,
            FixedPoint.Zero, EntityLifetime - (threatFrames - 1), facing, HasContact: true);
        var duringThreat = new BattleState(0, initial.Fighters, initial.StateVersion,
            projectiles: [entity], nextProjectileId: 2);

        BattleState atEnd = Step(simulation, duringThreat, back);
        Assert.Equal(initial.Fighter(1).PositionX, atEnd.Fighter(1).PositionX);
        Assert.Equal(EntityLifetime - threatFrames, Assert.Single(atEnd.Projectiles).RemainingLifetime);
        Assert.Equal(FighterLogicState.Standing, atEnd.Fighter(0).LogicState);

        BattleState afterThreat = Step(simulation, atEnd, back);
        Assert.Equal(initial.Fighter(1).PositionX + sign * FixedPoint.FromFraction(3, 2),
            afterThreat.Fighter(1).PositionX);
        Assert.True(Assert.Single(afterThreat.Projectiles).HasContact);
    }

    private static (BattleSimulation, BattleState) Setup(bool mirrored, int targetX, int? moveThreatFrames = null,
        int? entityThreatFrames = null, bool sourceOptIn = true, Facing? sourceFacing = null)
    {
        var command = new CommandDefinition("5A", [], InputBits.LightPunch);
        ProjectileDefinition[] entities = [];
        if (entityThreatFrames is not null)
        {
            var attack = new AttackDefinition(0, 0, 1, 1);
            var timeline = new ProjectileTimeline(11,
                [new ProjectileHitSpan(1, 2, new FixedRect(0, 1, 0, 1), attack)],
                guardThreatFrames: entityThreatFrames);
            entities = [new ProjectileDefinition(EntityId, attack, 1, lifetimeFrames: EntityLifetime, timeline: timeline)];
        }

        var move = new MoveDefinition(MoveId, command.CommandId, 2, 1, 4,
            projectiles: entities, guardThreatFrames: moveThreatFrames);
        var source = new FighterDefinition("ThreatSource", [command], [move],
            forwardGuardDistance: sourceOptIn ? GuardDistance : null);
        var target = new FighterDefinition("Target", [], []);
        var simulation = new BattleSimulation(new FightRoster(source, target));
        FighterState one = FighterState.Create(0, 1000, 0, sourceFacing ?? (mirrored ? Facing.Left : Facing.Right));
        FighterState two = FighterState.Create(1, 1000, mirrored ? -targetX : targetX, mirrored ? Facing.Right : Facing.Left);
        return (simulation, new BattleState(0, [one, two], BattleState.CreateDefault().StateVersion));
    }

    private static BattleState Step(BattleSimulation simulation, BattleState state, InputBits target,
        InputBits source = InputBits.None) => simulation.Step(state, new FrameInput(state.Frame, source, target)).State;
}
