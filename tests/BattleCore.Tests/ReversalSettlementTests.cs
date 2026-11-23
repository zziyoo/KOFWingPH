using System.Text.Json.Nodes;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class ReversalSettlementTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void ReversalSuppressesTheMatchedStrikeWithoutDamageAndEnablesFreshCancellation(int counterSeat)
    {
        JsonNode strike = LoadProfile();
        strike["useCommandPriority"] = true;
        strike["moves"]![0]!["reservedCancels"] = JsonNode.Parse("""
            [{"commandId":"6A","windowFirstFrame":4,"windowLastFrame":4,"phases":"Active",
              "requiresReversalConfirm":true,"maximumContactAge":1}]
            """);
        var commands = (JsonArray)strike["commands"]!;
        commands.Add(JsonNode.Parse("""
            {"commandId":"6A","directions":["Forward"],"buttons":["HeavyPunch"],"reserveGroundInput":true,
             "maxSequenceFrames":2,"buttonToleranceFrames":0,"priority":40,"bufferLifetimeFrames":0,
             "chargeHoldFrames":0,"chargeReleaseToleranceFrames":0}
            """));
        JsonNode counter = LoadProfile();
        var attacks = (JsonArray)counter["moves"]![0]!["attacks"]!;
        attacks.Clear();
        counter["moves"]![0]!["reversals"] = JsonNode.Parse("""
            [{"firstFrame":3,"lastFrame":3,"sourceHitstop":2,"targetHitstop":2,"boxes":[
              {"minX":{"numerator":0,"denominator":1},"maxX":{"numerator":60,"denominator":1},
               "minY":{"numerator":0,"denominator":1},"maxY":{"numerator":110,"denominator":1}}]}]
            """);
        FighterDefinition strikeFighter = FighterDataLoader.Load(strike.ToJsonString());
        FighterDefinition counterFighter = FighterDataLoader.Load(counter.ToJsonString());
        var simulation = new BattleSimulation(counterSeat == 1 ? new FightRoster(strikeFighter, counterFighter)
            : new FightRoster(counterFighter, strikeFighter));
        var initial = new BattleState(0, [
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left)], BattleState.CreateDefault().StateVersion);
        BattleState current = initial;
        var frames = new List<BattleStepResult>();
        for (int frame = 0; frame < 14; frame++)
        {
            InputBits a = frame == 0 ? InputBits.LightPunch : InputBits.None;
            InputBits b = a;
            if (frame == 6)
            {
                if (counterSeat == 1) a = InputBits.Right | InputBits.HeavyPunch;
                else b = InputBits.Left | InputBits.HeavyPunch;
            }
            BattleStepResult result = simulation.Step(current, new FrameInput(frame, a, b));
            current = result.State;
            frames.Add(result);
        }
        BattleEvent reversed = Assert.Single(frames[3].Events, e => e.Type.ToString() == "Reversed");
        Assert.Equal((counterSeat, 1 - counterSeat, 0),
            (reversed.SourceFighterId, reversed.TargetFighterId, reversed.Amount));
        Assert.DoesNotContain(frames.SelectMany(frame => frame.Events), e => e.Type == BattleEventType.Hit);
        Assert.All(frames[3].State.Fighters, fighter => Assert.Equal((1000, 2), (fighter.Health, fighter.HitstopRemainingFrames)));
        Assert.Contains(frames[6].State.Fighter(1 - counterSeat).RecognitionState.Buffer,
            result => result.Consumed && result.Match.CommandId == "6A");
        Assert.Equal(FighterLogicState.Standing, current.Fighter(1 - counterSeat).LogicState);
    }

    private static JsonNode LoadProfile() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "data", "fighters", "Kai.json")))!;
}
