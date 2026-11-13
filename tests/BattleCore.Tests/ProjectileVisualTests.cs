using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class ProjectileVisualTests
{
    [Fact]
    public void AgeFollowsIndividualEntitiesThroughRealClashAndBoutReset()
    {
        var kai = DemoRoster.CreateKai();
        var simulation = new BattleSimulation(new FightRoster(kai, kai));
        var state = BattleState.CreateDefault();
        var visuals = new ProjectileVisuals();
        bool two = false, clash = false;
        ProjectileState removed = default;
        for (int frame = 0; frame < 45; frame++)
        {
            var result = simulation.Step(state, new FrameInput(frame,
                frame == 0 ? InputBits.LightKick : InputBits.None,
                frame == 4 ? InputBits.LightKick : InputBits.None));
            state = result.State;
            visuals.Observe(state);
            if (state.Projectiles.Count == 2)
            {
                two = true;
                var a = state.Projectiles[0];
                var b = state.Projectiles[1];
                Assert.Equal(4, visuals.Age(a) - visuals.Age(b));
                Assert.False(ProjectileVisuals.FacesLeft(a));
                Assert.True(ProjectileVisuals.FacesLeft(b));
                removed = a;
            }
            if (result.Events.Any(e => e.Type == BattleEventType.ProjectileClash))
            {
                clash = true;
                Assert.Empty(state.Projectiles);
                Assert.Throws<KeyNotFoundException>(() => visuals.Age(removed));
            }
        }
        Assert.True(two);
        Assert.True(clash);
        visuals.Reset();
        Assert.Throws<KeyNotFoundException>(() => visuals.Age(removed));
    }
}
