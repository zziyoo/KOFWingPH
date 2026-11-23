using System.Text.Json.Nodes;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class AuthoredMoveMotionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommittedMoveLiftsIntegratesAndLandsBeforeReturningControl(bool mirrored)
    {
        JsonNode profile = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "data", "fighters", "Kai.json")))!;
        profile["moves"]![0]!["airMotion"] = JsonNode.Parse("""
            {"takeoffFrame":3,"landingFrame":7,
             "forwardVelocity":{"numerator":1,"denominator":1},
             "upwardVelocity":{"numerator":8,"denominator":1},
             "gravity":{"numerator":1,"denominator":2},
             "horizontalRetention":{"numerator":9,"denominator":10}}
            """);
        FighterDefinition fighter = FighterDataLoader.Load(profile.ToJsonString());
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        var initial = BattleState.CreateDefault();
        BattleState state = mirrored ? new BattleState(0, [
            FighterState.Create(0, 1000, 120, Facing.Left),
            FighterState.Create(1, 1000, -120, Facing.Right)], initial.StateVersion) : initial;
        int side = mirrored ? -1 : 1;
        FixedPoint initialX = state.Fighter(0).PositionX;

        for (int frame = 0; frame <= 11; frame++)
        {
            var input = new FrameInput(frame, frame == 0 ? InputBits.LightPunch : InputBits.None, InputBits.None);
            state = simulation.Step(state, input).State;
            FighterState actual = state.Fighter(0);
            if (frame == 3)
                Assert.Equal((initialX + side, (FixedPoint)8, (FixedPoint)(side * 1), (FixedPoint)8),
                    (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
            if (frame == 4)
                Assert.Equal(((FixedPoint)15 + FixedPoint.FromFraction(1, 2),
                        side * FixedPoint.FromFraction(9, 10), FixedPoint.FromFraction(15, 2)),
                    (actual.PositionY, actual.VelocityX, actual.VelocityY));
            if (frame == 7)
                Assert.Equal((FighterLogicState.Recovery, FixedPoint.Zero, FixedPoint.Zero, FixedPoint.Zero),
                    (actual.LogicState, actual.PositionY, actual.VelocityX, actual.VelocityY));
            if (frame == 11)
                Assert.Equal(FighterLogicState.Standing, actual.LogicState);
        }
    }
}
