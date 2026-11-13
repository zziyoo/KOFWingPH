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
    public void StoppedEntityKeepsItsAuthoritativeSpawnFacing()
    {
        var left = new ProjectileState(1, 1, "TerryPowerWave1020", "TerryPowerWave1000",
            default, default, default, default, 10, SpawnFacing: Facing.Left);

        Assert.True(ProjectileVisuals.FacesLeft(left));
        Assert.False(ProjectileVisuals.FacesLeft(left with { SpawnFacing = Facing.Right }));
    }

    [Fact]
    public void OwnerInterruptionDoesNotResetExistingProjectileAge()
    {
        var state = BattleState.CreateDefault();
        var visuals = new ProjectileVisuals();
        var ball = new ProjectileState(1, 0, "Fireball", "Fireball", default, default, default, default, 90);
        visuals.Observe(new BattleState(5, state.Fighters, 15, projectiles: new[] { ball }, nextProjectileId: 2));
        var interrupted = state.Fighter(0) with
        {
            LogicState = FighterLogicState.Hitstun,
            StunRemainingFrames = 12,
            HitstopRemainingFrames = 4,
        };
        ball = ball with { RemainingLifetime = 89 };
        visuals.Observe(new BattleState(6, new[] { interrupted, state.Fighter(1) }, 15,
            projectiles: new[] { ball }, nextProjectileId: 2));
        Assert.Equal(1, visuals.Age(ball));
        visuals.Reset();
        visuals.Observe(new BattleState(1, state.Fighters, 15, projectiles: new[] { ball }, nextProjectileId: 2));
        Assert.Equal(0, visuals.Age(ball));
    }

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
