using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class ProjectileTimelineTests
{
    private const string Wave = "Wave";
    private static readonly FixedPoint Speed = FixedPoint.FromRaw(163840);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovesOnSpawnThenStopsAndExpiresByEntityAge(bool mirrored)
    {
        var simulation = CreateSimulation();
        BattleState state = Duel(180, mirrored);
        state = Cast(simulation, state, both: false);
        int direction = mirrored ? -1 : 1;
        ProjectileState projectile = Assert.Single(state.Projectiles);
        Assert.Equal((Facing?)(mirrored ? Facing.Left : Facing.Right), projectile.SpawnFacing);
        Assert.Equal(-direction * (FixedPoint.FromInt(90) - Speed), projectile.PositionX);
        Assert.Equal(25, projectile.RemainingLifetime);

        for (int age = 1; age < 25; age++)
        {
            state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
            projectile = Assert.Single(state.Projectiles);
            Assert.Equal(-direction * (FixedPoint.FromInt(90) - Math.Min(age + 1, 11) * Speed), projectile.PositionX);
            Assert.Equal(age < 11 ? direction * Speed : FixedPoint.Zero, projectile.VelocityX);
            Assert.Equal(25 - age, projectile.RemainingLifetime);
        }
        Assert.Empty(simulation.Step(state, FrameInput.Empty(state.Frame)).State.Projectiles);
    }

    [Theory]
    [InlineData(false, false, 3, 13)]
    [InlineData(true, false, 3, 13)]
    [InlineData(false, true, 9, 11)]
    [InlineData(true, true, 9, 11)]
    public void FirstContactUsesLiveOwnerDamageOrChipAndRetainsEntity(bool mirrored, bool guard, int damage, int stop)
    {
        var simulation = CreateSimulation(varyDamage: true);
        BattleState start = Cast(simulation, Duel(60, mirrored), both: false);
        Assert.Equal(1, start.Fighter(0).DamageScript.Variance);
        InputBits guardInput = !guard ? InputBits.None : mirrored ? InputBits.Left : InputBits.Right;
        var input = new FrameInput(start.Frame, InputBits.None, guardInput);
        BattleStepResult result = simulation.Step(start, input);
        Assert.Contains(result.Events, e => e.Type == (guard ? BattleEventType.ProjectileBlocked : BattleEventType.ProjectileHit));
        Assert.Equal(1000 - damage, result.State.Fighter(1).Health);
        Assert.Equal(stop, result.State.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(0, result.State.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(2, result.State.Fighter(0).DamageScript.Variance);
        Assert.Equal(guard ? FighterLogicState.Blockstun : FighterLogicState.AirHitstun, result.State.Fighter(1).LogicState);
        Assert.Equal(guard ? null : (FixedPoint?)FixedPoint.FromFraction(1, 2), result.State.Fighter(1).HitLaunchGravity);
        Assert.True(Assert.Single(result.State.Projectiles).HasContact);
        Assert.Equal(result.State.Projectiles.ToArray(), simulation.Step(start, input).State.Projectiles.ToArray());
        Assert.DoesNotContain(simulation.Step(result.State, FrameInput.Empty(result.State.Frame)).Events,
            e => e.Type is BattleEventType.ProjectileHit or BattleEventType.ProjectileBlocked);
    }

    [Fact]
    public void EntityLaunchUsesItsOwnGravityAcrossFrozenFrames()
    {
        var simulation = CreateSimulation();
        BattleState state = Cast(simulation, Duel(60), both: false);
        state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
        for (int freeze = 0; freeze < 13; freeze++)
        {
            state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
            Assert.Equal(FixedPoint.Zero, state.Fighter(1).PositionY);
        }
        state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
        Assert.Equal(FixedPoint.FromFraction(13, 2), state.Fighter(1).PositionY);
        Assert.Equal(FixedPoint.FromInt(6), state.Fighter(1).VelocityY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LethalHitOrChipClearsTimelineEntitiesAtRoundEnd(bool guard)
    {
        var simulation = CreateSimulation();
        BattleState initial = Duel(60);
        var fighters = initial.Fighters.ToArray();
        fighters[1] = fighters[1] with { Health = 1 };
        BattleState state = Cast(simulation, new BattleState(0, fighters, initial.StateVersion), both: false);
        BattleStepResult result = simulation.Step(state,
            new FrameInput(state.Frame, InputBits.None, guard ? InputBits.Right : InputBits.None));
        Assert.Equal(0, result.State.Fighter(1).Health);
        Assert.True(result.State.Round.IsFinished);
        Assert.Empty(result.State.Projectiles);
        Assert.Contains(result.Events, e => e.Type == BattleEventType.Knockout);
        Assert.Empty(simulation.Step(result.State, FrameInput.Empty(result.State.Frame)).State.Projectiles);
    }

    [Fact]
    public void LateSpanDoesNotLaunchAndDualEntitiesSurviveBothOwnerInterruptions()
    {
        var simulation = CreateSimulation();
        BattleState state = Cast(simulation, Duel(60), both: true);
        BattleStepResult exchange = simulation.Step(state, FrameInput.Empty(state.Frame));
        Assert.Equal(2, exchange.Events.Count(e => e.Type == BattleEventType.ProjectileHit));
        Assert.DoesNotContain(exchange.Events, e => e.Type == BattleEventType.ProjectileClash);
        Assert.Equal(2, exchange.State.Projectiles.Select(p => p.ProjectileId).Distinct().Count());
        Assert.All(exchange.State.Projectiles, p => Assert.True(p.HasContact));
        Assert.All(exchange.State.Fighters, fighter => Assert.Equal(FighterLogicState.AirHitstun, fighter.LogicState));

        state = Cast(simulation, Duel(142), both: false);
        for (int age = 1; age < 9; age++)
        {
            BattleStepResult step = simulation.Step(state, FrameInput.Empty(state.Frame));
            Assert.DoesNotContain(step.Events, e => e.Type == BattleEventType.ProjectileHit);
            state = step.State;
        }
        BattleStepResult late = simulation.Step(state, FrameInput.Empty(state.Frame));
        Assert.Contains(late.Events, e => e.Type == BattleEventType.ProjectileHit);
        Assert.Equal(FighterLogicState.Hitstun, late.State.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.FromRaw(688128), late.State.Fighter(1).VelocityX);
    }

    [Fact]
    public void OpposingTimelineEntitiesCanOverlapWithoutClashing()
    {
        var simulation = CreateSimulation();
        BattleState cast = Cast(simulation, Duel(180), both: true);
        ProjectileState[] overlapping = cast.Projectiles.Select(projectile =>
            projectile with { PositionX = -projectile.VelocityX }).ToArray();
        var state = new BattleState(cast.Frame, cast.Fighters, cast.StateVersion, cast.Round,
            overlapping, cast.NextProjectileId);
        BattleStepResult result = simulation.Step(state, FrameInput.Empty(state.Frame));
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.ProjectileClash);
        Assert.Equal(2, result.State.Projectiles.Count);
        Assert.All(result.State.Projectiles, entity => Assert.Equal(FixedPoint.Zero, entity.PositionX));
    }

    [Fact]
    public void OwnerInterruptedBeforeSpawnDoesNotCreateAnEntity()
    {
        var simulation = CreateSimulation();
        BattleState state = Duel(60);
        state = simulation.Step(state, new FrameInput(0, InputBits.LightKick, InputBits.LightPunch)).State;
        BattleStepResult interrupted = simulation.Step(state, FrameInput.Empty(state.Frame));
        Assert.Equal(FighterLogicState.Hitstun, interrupted.State.Fighter(0).LogicState);
        state = interrupted.State;
        while (state.Frame < 30)
        {
            Assert.Empty(state.Projectiles);
            state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
        }
        Assert.Equal(1, state.NextProjectileId);
    }

    private static BattleSimulation CreateSimulation(bool varyDamage = false)
    {
        AttackDefinition Hit(bool launch) => new(
            0, 0, 1, 1, height: AttackHeight.Mid, hitstunFrames: 18, blockstunFrames: 17,
            groundKnockback: launch ? FixedPoint.Zero : FixedPoint.FromRaw(688128),
            groundKnockbackFriction: 1, blockKnockback: FixedPoint.FromRaw(688128),
            launchVelocityX: launch ? FixedPoint.FromRaw(294912) : FixedPoint.Zero,
            launchVelocityY: launch ? FixedPoint.FromRaw(425984) : FixedPoint.Zero,
            knockdownFrames: launch ? 10 : 0, wakeupFrames: launch ? 8 : 0,
            hitstopByOutcome: new HitstopByOutcome(new HitstopPair(0, 13), new HitstopPair(0, 11)));
        var damage = new DynamicAttackDamage(varyDamage ? 7 : 5, varyDamage ? 7 : 5, false);
        var timeline = new ProjectileTimeline(11, new[]
        {
            new ProjectileHitSpan(1, 8, new FixedRect(16, 79, 1, 48), Hit(true), 9, damage,
                launchGravity: FixedPoint.FromFraction(1, 2)),
            new ProjectileHitSpan(9, 12, new FixedRect(80, 119, 1, 88), Hit(false), 9, damage),
        });
        var wave = new ProjectileDefinition(Wave, new AttackDefinition(0, 0, 1, 1), Speed,
            lifetimeFrames: 25, timeline: timeline);
        var move = new MoveDefinition(Wave, "5C", 15, 2, 35, projectiles: new[] { wave });
        var command = new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick,
            priority: 5, bufferLifetimeFrames: 2);
        var jabCommand = new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
            priority: 5, bufferLifetimeFrames: 2);
        var jab = new MoveDefinition("Jab", "5A", 1, 2, 5,
            attacks: new[] { new AttackDefinition(45, 60, 30, 30, damage: 10, hitstunFrames: 10) });
        var rules = new DynamicDamageRules(new DamageRatio(3, 2), new DamageRatio(1, 2),
            DamageRatio.One, DamageRatio.One, varianceMaximum: varyDamage ? 2 : 0, counterGraceTicks: 2);
        var movement = new MovementDefinition(2, 2, 2, 6, 8, 1, 2);
        FighterDefinition Build(string id) => new(id, new[] { command, jabCommand }, new[] { move, jab },
            movement, damageRules: rules);
        return new BattleSimulation(new FightRoster(Build("P1"), Build("P2")));
    }

    private static BattleState Duel(int distance, bool mirrored = false)
    {
        int x = mirrored ? distance / 2 : -distance / 2;
        return new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, x, mirrored ? Facing.Left : Facing.Right) with
                { DamageScript = DamageScriptState.FromSeed(1, 0) },
            FighterState.Create(1, 1000, -x, mirrored ? Facing.Right : Facing.Left),
        }, BattleState.CreateDefault().StateVersion);
    }

    private static BattleState Cast(BattleSimulation simulation, BattleState state, bool both)
    {
        state = simulation.Step(state, new FrameInput(0, InputBits.LightKick, both ? InputBits.LightKick : InputBits.None)).State;
        for (int frame = 1; frame <= 15; frame++)
        {
            Assert.Empty(state.Projectiles);
            state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
        }
        return state;
    }
}
