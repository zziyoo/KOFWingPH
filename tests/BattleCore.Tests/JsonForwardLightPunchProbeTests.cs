using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using System.IO;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class JsonForwardLightPunchProbeTests
{
    [Fact]
    public void LoadedStage28ProfileStartsBackKnuckleFromIdle()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "data", "imported-characters", "terry98-stage28-final", "default.fighter.json");
        if (!File.Exists(path))
            return;
        FighterDefinition fighter = FighterDataLoader.Load(File.ReadAllText(path), path);
        CommandDefinition command = fighter.Commands.Single(c => c.CommandId == "ForwardLightPunch");
        Assert.True(command.ReserveGroundInput);
        Assert.True(command.ReserveOnEdgeDirection);
        Assert.Equal(4, command.Priority);
        RecognitionState state = RecognitionState.Empty.Advance(
            InputBits.Right | InputBits.LightPunch, Facing.Right, 0, 0, fighter.Commands,
            useCommandPriority: fighter.UseCommandPriority);
        FighterActionTick action = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            DirectionResolver.Resolve(InputBits.Right | InputBits.LightPunch, Facing.Right), state, fighter, 0,
            opponentDistance: FixedPoint.FromInt(60), opponentBodyDistance: FixedPoint.FromInt(29),
            opponentGroundedNonHitstun: true);
        Assert.Equal("TerryBackKnuckle300", action.MoveId);
    }
}
