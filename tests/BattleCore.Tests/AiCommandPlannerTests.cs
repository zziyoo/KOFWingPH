using NewKOF.BattleCore.AI;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class AiCommandPlannerTests
{
    [Theory]
    [InlineData("Kai", 0)]
    [InlineData("Kai", 1)]
    [InlineData("Rei", 0)]
    [InlineData("Rei", 1)]
    public void EveryRosterMove_ReachesStartup_FromEitherSide(string fighterId, int player)
    {
        var fighter = FighterDataLoader.Load(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "data", "fighters", fighterId + ".json")));
        foreach (var move in fighter.Moves)
            AssertStarts(fighter, move, player);
    }

    public static IEnumerable<object[]> CustomCommands()
    {
        yield return new object[] { new CommandDefinition("arbitrary-bare", Array.Empty<CommandDirection>(), InputBits.HeavyKick) };
        yield return new object[] { new CommandDefinition("arbitrary-single", new[] { CommandDirection.Down }, InputBits.HeavyPunch, buttonToleranceFrames: 0) };
        yield return new object[] { new CommandDefinition("arbitrary-neutral", new[] { CommandDirection.Neutral }, InputBits.LightPunch, buttonToleranceFrames: 0) };
        yield return new object[] { new CommandDefinition("arbitrary-double", new[] {
            CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward,
            CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            InputBits.LightPunch | InputBits.HeavyKick, maxSequenceFrames: 5, buttonToleranceFrames: 0) };
        yield return new object[] { new CommandDefinition("arbitrary-charge", new[] { CommandDirection.Back, CommandDirection.Forward },
            InputBits.HeavyPunch | InputBits.LightKick, maxSequenceFrames: 0, buttonToleranceFrames: 0,
            chargeHoldFrames: 45, chargeReleaseToleranceFrames: 0) };
        yield return new object[] { new CommandDefinition("arbitrary-charge-tail", new[] {
            CommandDirection.Back, CommandDirection.Down, CommandDirection.Forward }, InputBits.HeavyKick,
            maxSequenceFrames: 1, buttonToleranceFrames: 0, chargeHoldFrames: 4, chargeReleaseToleranceFrames: 1) };
        yield return new object[] { new CommandDefinition("arbitrary-direction-only", new[] { CommandDirection.Forward, CommandDirection.Neutral, CommandDirection.Forward }) };
    }

    [Theory]
    [MemberData(nameof(CustomCommands))]
    public void DataDrivenCommands_ReachStartup_WithReleaseEdges(CommandDefinition command)
    {
        var move = new MoveDefinition("unrelated-move-name", command.CommandId, 3, 2, 4);
        var fighter = new FighterDefinition("Custom", new[] { command }, new[] { move });
        AssertStarts(fighter, move, 0);
        AssertStarts(fighter, move, 1);
        var plan = AiCommandPlanner.Plan(command);
        Assert.Equal(InputBits.None, plan[0].Buttons);
        Assert.Equal(InputBits.None, plan[^1].Buttons);
        if (command.RequiredButtons != InputBits.None)
            Assert.Equal(command.RequiredButtons, Assert.Single(plan, s => s.Buttons != InputBits.None).Buttons);
        Assert.Throws<NotSupportedException>(() => ((IList<AiCommandStep>)plan)[0] = default);
    }

    [Fact]
    public void ChargePlan_HoldsExactlyTheMinimumBeforeRelease()
    {
        var command = new CommandDefinition("charge", new[] { CommandDirection.Back, CommandDirection.Forward },
            InputBits.LightPunch, chargeHoldFrames: 45, chargeReleaseToleranceFrames: 0);
        var plan = AiCommandPlanner.Plan(command);
        Assert.Equal(45, plan.Count(s => s.Direction == CommandDirection.Back));
        var recognizer = new CommandRecognizer(new[] { command });
        var shortened = plan.Where((_, i) => i != 1).ToArray();
        for (int frame = 0; frame < shortened.Length; frame++)
            recognizer.Observe(new FrameInput(frame, AiCommandPlanner.ToInput(shortened[frame], Facing.Right), InputBits.None), Facing.Right, Facing.Left);
        Assert.Null(recognizer.LastMatch(0));
    }

    [Fact]
    public void ReleaseFrame_RearmsPreviouslyHeldButtons_AndAllowsRepeatedScripts()
    {
        var command = new CommandDefinition("bare", Array.Empty<CommandDirection>(), InputBits.LightPunch | InputBits.HeavyKick);
        var recognizer = new CommandRecognizer(new[] { command });
        int frame = 0;
        recognizer.Observe(new FrameInput(frame++, command.RequiredButtons, InputBits.None), Facing.Right, Facing.Left);
        for (int repetition = 0; repetition < 2; repetition++)
        {
            int expectedPress = frame + 1;
            foreach (var step in AiCommandPlanner.Plan(command))
                recognizer.Observe(new FrameInput(frame++, AiCommandPlanner.ToInput(step, Facing.Right), InputBits.None), Facing.Right, Facing.Left);
            Assert.Equal(expectedPress, recognizer.LastMatch(0)!.ButtonFrame);
        }
    }

    [Fact]
    public void FacingCanChangeBetweenEveryScriptFrame()
    {
        var command = new CommandDefinition("custom", new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward }, InputBits.HeavyPunch);
        var recognizer = new CommandRecognizer(new[] { command });
        int frame = 0;
        foreach (var step in AiCommandPlanner.Plan(command))
        {
            var facing = frame % 2 == 0 ? Facing.Right : Facing.Left;
            recognizer.Observe(new FrameInput(frame++, AiCommandPlanner.ToInput(step, facing), InputBits.None), facing, Facing.Left);
        }
        Assert.Equal(command.CommandId, recognizer.LastMatch(0)!.CommandId);
    }

    [Fact]
    public void ImpossibleOrUnboundedCommands_ReturnEmptyFallback()
    {
        Assert.Empty(AiCommandPlanner.Plan(new CommandDefinition("tight", new[] {
            CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward }, InputBits.LightPunch, maxSequenceFrames: 1)));
        Assert.Empty(AiCommandPlanner.Plan(new CommandDefinition("huge", new[] { CommandDirection.Back, CommandDirection.Forward },
            InputBits.LightPunch, chargeHoldFrames: int.MaxValue)));
        Assert.Empty(AiCommandPlanner.Plan(new CommandDefinition("release-too-tight", new[] {
            CommandDirection.Back, CommandDirection.Down, CommandDirection.Forward }, InputBits.LightPunch,
            chargeHoldFrames: 4, chargeReleaseToleranceFrames: 0)));
        Assert.Empty(AiCommandPlanner.Plan(new CommandDefinition("charge-reset", new[] {
            CommandDirection.Back, CommandDirection.Forward, CommandDirection.Back }, InputBits.LightPunch, chargeHoldFrames: 4)));
        Assert.Empty(AiCommandPlanner.Plan(new CommandDefinition("tail-pruned", new[] {
            CommandDirection.Back, CommandDirection.Down, CommandDirection.Forward }, InputBits.LightPunch,
            maxSequenceFrames: 0, buttonToleranceFrames: 0, chargeHoldFrames: 4)));
        Assert.Empty(AiCommandPlanner.Plan(new CommandDefinition("history-overflow",
            Enumerable.Range(0, 65).Select(i => i % 2 == 0 ? CommandDirection.Down : CommandDirection.Forward),
            InputBits.LightPunch, maxSequenceFrames: 100)));
    }

    [Fact]
    public void EveryDirection_ConvertsUsingCurrentFacing()
    {
        foreach (var direction in Enum.GetValues<CommandDirection>())
        foreach (var facing in new[] { Facing.Right, Facing.Left })
        {
            var input = AiCommandPlanner.ToInput(new AiCommandStep(direction, InputBits.LightPunch), facing);
            var relative = DirectionResolver.Resolve(input, facing);
            Assert.Equal(direction is CommandDirection.Forward or CommandDirection.UpForward or CommandDirection.DownForward, relative.Forward);
            Assert.Equal(direction is CommandDirection.Back or CommandDirection.UpBack or CommandDirection.DownBack, relative.Back);
            Assert.Equal(direction is CommandDirection.Up or CommandDirection.UpBack or CommandDirection.UpForward, relative.Up);
            Assert.Equal(direction is CommandDirection.Down or CommandDirection.DownBack or CommandDirection.DownForward, relative.Down);
            Assert.Equal(InputBits.LightPunch, relative.Attacks);
        }
    }

    private static void AssertStarts(FighterDefinition fighter, MoveDefinition move, int player)
    {
        var command = fighter.Commands.Single(c => c.CommandId == move.CommandId);
        var plan = AiCommandPlanner.Plan(command);
        Assert.NotEmpty(plan);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        var state = BattleState.CreateDefault();
        bool started = false;
        foreach (var step in plan)
        {
            var input = AiCommandPlanner.ToInput(step, state.Fighter(player).Facing);
            state = simulation.Step(state, new FrameInput(state.Frame,
                player == 0 ? input : InputBits.None, player == 1 ? input : InputBits.None)).State;
            started |= state.Fighter(player).LogicState == FighterLogicState.Startup && state.Fighter(player).MoveId == move.MoveId;
        }
        Assert.True(started, $"{fighter.FighterId}/{move.MoveId} did not reach Startup for player {player}.");
    }
}
