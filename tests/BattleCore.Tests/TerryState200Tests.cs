using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class TerryState200Tests
{
    private const string Move = "TerryStandLightPunch200";

    // AIR timing and Clsn1 from the audited source. Damage, stun and pause are test values;
    // the imported profile is exercised against the measured values in the Godot runs.
    private static FighterDefinition TestDefinition() => new("TerryS200",
        new[] { new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
            bufferLifetimeFrames: 0) },
        new[]
        {
            new MoveDefinition(Move, "5A", 3, 4, 5, MoveAllowedStates.Standing, attacks: new[]
            {
                new AttackDefinition(FixedPoint.FromFraction(63, 2), FixedPoint.FromFraction(161, 2),
                    FixedPoint.FromFraction(55, 2), FixedPoint.FromFraction(15, 2),
                    firstActiveFrame: 0, lastActiveFrame: 3, damage: 7, hitstunFrames: 12,
                    blockstunFrames: 8, hitstopByOutcome: new(new(2, 4), new(3, 1))),
            }),
        });

    private static BattleState Start(int distance) => new(0, new[]
    {
        FighterState.Create(0, 1000, -30, Facing.Right),
        FighterState.Create(1, 1000, distance - 30, Facing.Left),
    }, BattleState.CreateDefault().StateVersion);

    private static BattleStepResult Step(BattleSimulation sim, BattleState state, InputBits p1 = InputBits.None,
        InputBits p2 = InputBits.None) => sim.Step(state, new FrameInput(state.Frame, p1, p2));

    [Fact]
    public void Whiff_UsesSourceTimelineAndExactWorldBox()
    {
        FighterDefinition terry = TestDefinition();
        var sim = new BattleSimulation(new FightRoster(terry, terry));
        BattleState state = Start(89); // right edge +59 touches target's left edge exactly
        for (int tick = 0; tick <= 12; tick++)
        {
            BattleStepResult result = Step(sim, state, tick == 0 ? InputBits.LightPunch : InputBits.None);
            state = result.State;
            FighterState attacker = state.Fighter(0);
            Assert.Equal(tick < 3 ? FighterLogicState.Startup : tick < 7 ? FighterLogicState.Active :
                tick < 12 ? FighterLogicState.Recovery : FighterLogicState.Standing, attacker.LogicState);
            Assert.Equal(tick < 12 ? Move : null, attacker.MoveId);
            Assert.Equal(tick < 12 ? tick : 0, attacker.MoveElapsedFrames);
            Assert.Equal(FixedPoint.FromInt(-30), attacker.PositionX);
            IReadOnlyList<FixedRect> boxes = FighterBoxes.GetActiveHitboxes(attacker, terry);
            if (tick is >= 3 and <= 6)
                Assert.Equal(new FixedRect(-26, 29, 73, 88), Assert.Single(boxes));
            else
                Assert.Empty(boxes);
            Assert.Equal(tick == 0 ? 1 : 0, result.Events.Count(e => e.Type == BattleEventType.MoveStarted));
            Assert.DoesNotContain(result.Events, e => e.Type is BattleEventType.Hit or BattleEventType.Blocked);
            Assert.Equal(1000, state.Fighter(1).Health);
        }
    }

    [Theory]
    [InlineData(0, false, false, BattleEventType.Hit)]
    [InlineData(1, false, false, BattleEventType.Hit)]
    [InlineData(0, true, false, BattleEventType.Blocked)]
    [InlineData(1, true, false, BattleEventType.Blocked)]
    [InlineData(0, true, true, null)]
    public void GroundContact_MirrorsAndSettlesAtMostOnce(int seat, bool guarding, bool crouching,
        BattleEventType? expected)
    {
        FighterDefinition terry = TestDefinition();
        var sim = new BattleSimulation(new FightRoster(terry, terry));
        BattleState state = Start(60);
        InputBits back = guarding ? (seat == 0 ? InputBits.Right : InputBits.Left) : InputBits.None;
        if (crouching) back |= InputBits.Down;
        int target = 1 - seat;
        var contacts = new List<BattleEvent>();
        int start = -1;
        for (int frame = 0; frame < 32; frame++)
        {
            InputBits punch = frame == 0 ? InputBits.LightPunch : InputBits.None;
            InputBits defense = frame is >= 3 and <= 6 ? back : InputBits.None;
            BattleStepResult result = seat == 0 ? Step(sim, state, punch, defense) : Step(sim, state, defense, punch);
            state = result.State;
            foreach (BattleEvent evt in result.Events)
            {
                if (evt.Type == BattleEventType.MoveStarted) start = evt.Frame;
                if (evt.Type is BattleEventType.Hit or BattleEventType.Blocked) contacts.Add(evt);
            }
            if (frame == 3)
            {
                FixedRect box = Assert.Single(FighterBoxes.GetActiveHitboxes(state.Fighter(seat), terry));
                Assert.Equal(seat == 0 ? new FixedRect(-26, 29, 73, 88) : new FixedRect(-29, 26, 73, 88), box);
            }
        }
        Assert.Equal(0, start);
        if (expected is { } type)
        {
            BattleEvent contact = Assert.Single(contacts);
            Assert.Equal(3, contact.Frame);
            Assert.Equal(type, contact.Type);
            Assert.Equal(seat, contact.SourceFighterId);
            Assert.Equal(target, contact.TargetFighterId);
        }
        else
            Assert.Empty(contacts);
        Assert.Equal(expected == BattleEventType.Hit ? 993 : 1000, state.Fighter(target).Health);
        Assert.All(state.Fighters, f => Assert.Equal(FighterLogicState.Standing, f.LogicState));
    }

    [Fact]
    public void IllegalInputDoesNotLingerAndOpponentCanInterruptStartup()
    {
        FighterDefinition terry = TestDefinition();
        var sim = new BattleSimulation(new FightRoster(terry, terry));
        BattleState state = Start(60);
        state = Step(sim, state, InputBits.Down | InputBits.LightPunch).State;
        Assert.Equal(FighterLogicState.Crouching, state.Fighter(0).LogicState);
        state = Step(sim, state, InputBits.None, InputBits.LightPunch).State;
        Assert.Null(state.Fighter(0).MoveId);
        state = Step(sim, state, InputBits.LightPunch).State;
        Assert.Equal(Move, state.Fighter(0).MoveId);
        state = Step(sim, state).State;
        state = Step(sim, state).State;
        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(0).LogicState);
        Assert.Null(state.Fighter(0).MoveId);
        Assert.Empty(FighterBoxes.GetActiveHitboxes(state.Fighter(0), terry));
    }
}
