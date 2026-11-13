using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class ThrowVisualTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    public void ProjectsRealGrabOutcome(int thrower, bool tech, bool whiff)
    {
        var fighter = DemoRoster.CreateKai();
        var sim = new BattleSimulation(new FightRoster(fighter, fighter));
        int d = whiff ? 120 : 30;
        var state = new BattleState(0, new[] { FighterState.Create(0, 1000, -d, Facing.Right),
            FighterState.Create(1, 1000, d, Facing.Left) }, 15);
        var visual = new ThrowVisual();
        int target = 1 - thrower;
        bool pending = false, hit = false, escaped = false;
        for (int i = 0; i < 36; i++)
        {
            InputBits[] bits = { InputBits.None, InputBits.None };
            if (i == 0) bits[thrower] = InputBits.HeavyKick;
            if (tech && state.PendingThrow is not null)
                bits[target] = InputBits.LightPunch | InputBits.LightKick;
            var result = sim.Step(state, new FrameInput(i, bits[0], bits[1]));
            state = result.State;
            visual.Observe(state, result.Events);
            if (state.PendingThrow is not null)
            {
                pending = true;
                Assert.Equal("throw_hold", visual.Clip(state.Fighter(thrower)));
                Assert.Equal("throw_caught", visual.Clip(state.Fighter(target)));
                Assert.Equal(1000, state.Fighter(target).Health);
            }
            if (result.Events.Any(e => e.Type == BattleEventType.ThrowHit))
            {
                hit = true;
                Assert.Equal("throw_release", visual.Clip(state.Fighter(thrower)));
                Assert.Null(visual.Clip(state.Fighter(target)));
                Assert.Equal(FighterLogicState.Knockdown, state.Fighter(target).LogicState);
            }
            if (result.Events.Any(e => e.Type == BattleEventType.ThrowTech))
            {
                escaped = true;
                Assert.Null(visual.Clip(state.Fighter(thrower)));
                Assert.Null(visual.Clip(state.Fighter(target)));
            }
            if (whiff)
                Assert.All(state.Fighters, f => Assert.Null(visual.Clip(f)));
        }
        Assert.Equal(!whiff, pending);
        Assert.Equal(!whiff && !tech, hit);
        Assert.Equal(tech, escaped);
        Assert.All(state.Fighters, f => Assert.Null(visual.Clip(f)));
    }
}
