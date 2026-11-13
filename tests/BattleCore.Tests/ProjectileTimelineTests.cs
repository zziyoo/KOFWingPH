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
    public void ScriptedLaunchMatchesBothAirArcsAndRecovery(bool mirrored)
    {
        var simulation = CreateSimulation(scriptedLaunch: true);
        BattleState state = Cast(simulation, Duel(60, mirrored), both: false);
        BattleState Step() => state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
        FighterState target = Step().Fighter(1);
        Assert.Equal(LaunchReactionPhase.Shake, target.LaunchReaction!.Value.Phase);
        for (int frame = 0; frame < 13; frame++)
        {
            target = Step().Fighter(1);
            Assert.Equal(LaunchReactionPhase.Shake, target.LaunchReaction!.Value.Phase);
            Assert.Equal(FixedPoint.Zero, target.PositionY);
        }

        var samples = new Dictionary<int, FighterState>();
        for (int frame = 1; frame <= 81; frame++)
            samples[frame] = Step().Fighter(1);

        int direction = mirrored ? -1 : 1;
        void Verify(int frame, LaunchReactionPhase phase, int ticks, FixedPoint x, FixedPoint y,
            FixedPoint vy, bool groundCrossed = false)
        {
            FighterState sample = samples[frame];
            LaunchReactionState reaction = sample.LaunchReaction!.Value;
            Assert.Equal((phase, ticks, groundCrossed), (reaction.Phase, reaction.Ticks, reaction.GroundCrossed));
            Assert.Equal((x * direction, y, vy), (sample.PositionX, sample.PositionY, sample.VelocityY));
        }

        Verify(1, LaunchReactionPhase.Lift, 1, FixedPoint.FromFraction(69, 2), FixedPoint.FromFraction(13, 2),
            FixedPoint.FromFraction(13, 2));
        Verify(2, LaunchReactionPhase.Rise, 1, 39, 12, FixedPoint.FromFraction(11, 2));
        Verify(9, LaunchReactionPhase.Rise, 8, FixedPoint.FromFraction(141, 2), FixedPoint.FromFraction(73, 2), 2);
        Verify(10, LaunchReactionPhase.Fall, 1, 75, 38, FixedPoint.FromFraction(3, 2));
        Verify(26, LaunchReactionPhase.Fall, 17, 147, 0, FixedPoint.FromFraction(-13, 2), groundCrossed: true);
        Verify(27, LaunchReactionPhase.Impact, 1, 147, 0, 0);
        Verify(30, LaunchReactionPhase.Impact, 4, 147, 0, 0);
        Verify(31, LaunchReactionPhase.Bounce, 1, 147, 0, 3);
        Verify(32, LaunchReactionPhase.Bounce, 2, FixedPoint.FromFraction(1203, 8),
            FixedPoint.FromFraction(5, 2), FixedPoint.FromFraction(5, 2));
        FixedPoint restingX = FixedPoint.FromFraction(1473, 8);
        Verify(42, LaunchReactionPhase.Bounce, 12, restingX, 0, FixedPoint.FromFraction(-5, 2), groundCrossed: true);
        Verify(43, LaunchReactionPhase.LyingImpact, 1, restingX, 0, 0);
        Verify(45, LaunchReactionPhase.LyingImpact, 3, restingX, 0, 0);
        Verify(46, LaunchReactionPhase.Lying, 1, restingX, 0, 0);
        Verify(55, LaunchReactionPhase.Lying, 10, restingX, 0, 0);
        Verify(56, LaunchReactionPhase.Wakeup, 1, restingX, 0, 0);
        Verify(80, LaunchReactionPhase.Wakeup, 25, restingX, 0, 0);
        Assert.Equal(FighterLogicState.Standing, samples[81].LogicState);
        Assert.Null(samples[81].LaunchReaction);
        Assert.Equal(restingX * direction, samples[81].PositionX);
    }

    [Theory]
    [InlineData(26)]
    [InlineData(42)]
    public void NonLaunchingStrikeOnGroundCrossingKeepsReactionValid(int crossingFrame)
    {
        var simulation = CreateSimulation(scriptedLaunch: true, farJab: true);
        BattleState state = Cast(simulation, Duel(60), both: false);
        state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
        for (int frame = 0; frame < 13 + crossingFrame - 2; frame++)
            state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
        state = simulation.Step(state, new FrameInput(state.Frame, InputBits.LightPunch, InputBits.None)).State;
        BattleStepResult struck = simulation.Step(state, FrameInput.Empty(state.Frame));
        Assert.Contains(struck.Events, e => e.Type == BattleEventType.Hit);
        FighterState defender = struck.State.Fighter(1);
        Assert.Equal(FighterLogicState.AirHitstun, defender.LogicState);
        Assert.True(defender.LaunchReaction!.Value.GroundCrossed);
        Assert.Equal(FixedPoint.Zero, defender.PositionY);
        Assert.True(defender.VelocityY < FixedPoint.Zero);

        BattleState landed = simulation.Step(struck.State, FrameInput.Empty(struck.State.Frame)).State;
        Assert.Equal(FighterLogicState.Knockdown, landed.Fighter(1).LogicState);
        Assert.Equal(crossingFrame == 26 ? LaunchReactionPhase.Impact : LaunchReactionPhase.LyingImpact,
            landed.Fighter(1).LaunchReaction!.Value.Phase);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(24, true)]
    public void ScriptedWakeupProtectsStrikesThroughTheControlReturnTick(int wakeupTick, bool exits)
    {
        var simulation = CreateSimulation(scriptedLaunch: true, farJab: true);
        BattleState state = Cast(simulation, Duel(60), both: false);
        while (state.Fighter(1).LaunchReaction is not { Phase: LaunchReactionPhase.Wakeup } reaction ||
               reaction.Ticks < wakeupTick)
        {
            Assert.InRange(state.Frame, 0, 110);
            state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
        }

        int health = state.Fighter(1).Health;
        state = simulation.Step(state, new FrameInput(state.Frame, InputBits.LightPunch, InputBits.None)).State;
        BattleStepResult protectedFrame = simulation.Step(state, FrameInput.Empty(state.Frame));
        Assert.DoesNotContain(protectedFrame.Events, e => e.Type == BattleEventType.Hit);
        Assert.Equal(health, protectedFrame.State.Fighter(1).Health);
        Assert.Equal(exits ? FighterLogicState.Standing : FighterLogicState.Wakeup, protectedFrame.State.Fighter(1).LogicState);
        if (exits)
        {
            BattleStepResult after = simulation.Step(protectedFrame.State, FrameInput.Empty(protectedFrame.State.Frame));
            Assert.Contains(after.Events, e => e.Type == BattleEventType.Hit);
            Assert.Equal(health - 10, after.State.Fighter(1).Health);
        }
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

    private static BattleSimulation CreateSimulation(bool varyDamage = false, bool scriptedLaunch = false, bool farJab = false)
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
        var reaction = scriptedLaunch ? new LaunchReactionProfile(4, FixedPoint.FromFraction(3, 4),
            FixedPoint.FromFraction(7, 2), FixedPoint.FromFraction(1, 2), 13, 25,
            riseFrames: 8, lieImpactFrames: 3, transitionGravitySteps: 2) : null;
        var timeline = new ProjectileTimeline(11, new[]
        {
            new ProjectileHitSpan(1, 8, new FixedRect(16, 79, 1, 48), Hit(true), 9, damage,
                launchGravity: FixedPoint.FromFraction(1, 2), launchReaction: reaction),
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
            attacks: new[] { farJab ? new AttackDefinition(160, 20, 60, 20, damage: 10, hitstunFrames: 10)
                : new AttackDefinition(45, 60, 30, 30, damage: 10, hitstunFrames: 10) });
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
