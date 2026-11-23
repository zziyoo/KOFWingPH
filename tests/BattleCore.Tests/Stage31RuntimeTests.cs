using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class Stage31RuntimeTests
{
    [Fact]
    public void ReversalFreezesAndDisarmsOnlyTheMatchedHelperEntity()
    {
        var tackle = new MoveDefinition("Tackle", "Tackle", 1, 8, 4,
            reversals: [new MoveReversal(2, 4,
                [new FixedRect(-10, 20, 25, 55)], sourceHitstop: 3, targetHitstop: 3)]);
        AttackDefinition hit = ProjectileHit();
        var wave = new MoveDefinition("Wave", "Wave", 1, 2, 4,
            projectiles: [new ProjectileDefinition("HelperWave", hit, FixedPoint.One,
                lifetimeFrames: 12, timeline: new ProjectileTimeline(8,
                    [new ProjectileHitSpan(1, 8, new FixedRect(-5, 5, -5, 5), hit)]))]);
        var commands = new[] { new CommandDefinition("Tackle", [], InputBits.HeavyPunch),
            new CommandDefinition("Wave", [], InputBits.LightPunch) };
        var counter = new FighterDefinition("Counter", commands, [tackle]);
        var owner = new FighterDefinition("Owner", commands, [wave]);
        var simulation = new BattleSimulation(new FightRoster(counter, owner));

        FighterState counterState = FighterState.Create(0, 1000, 0, Facing.Right) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = "Tackle",
            MoveStartedFrame = 0,
            MoveElapsedFrames = 1,
            PhaseRemainingFrames = 7,
        };
        FighterState ownerState = FighterState.Create(1, 1000, 100, Facing.Left);
        var helper = new ProjectileState(1, 1, "HelperWave", "Wave",
            PositionX: 5, PositionY: 40, VelocityX: -1, VelocityY: 0,
            RemainingLifetime: 10, SpawnFacing: Facing.Left);
        var before = new BattleState(0, [counterState, ownerState], BattleState.CurrentStateVersion,
            projectiles: [helper], nextProjectileId: 2);

        BattleStepResult reversed = simulation.Step(before, FrameInput.Empty(0));
        BattleEvent reversalEvent = Assert.Single(reversed.Events, e => e.Type.ToString() == "ProjectileReversed");
        Assert.Equal(1, reversalEvent.ProjectileId);
        Assert.Equal((1000, 1000), (reversed.State.Fighter(0).Health, reversed.State.Fighter(1).Health));
        Assert.Equal(3, reversed.State.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(0, reversed.State.Fighter(1).HitstopRemainingFrames);
        ProjectileState frozen = Assert.Single(reversed.State.Projectiles);
        FixedPoint freezePosition = frozen.PositionX;
        int freezeLife = frozen.RemainingLifetime;
        int tackleTick = reversed.State.Fighter(0).MoveElapsedFrames;

        BattleState state = reversed.State;
        for (int remaining = 2; remaining >= 0; remaining--)
        {
            state = simulation.Step(state, FrameInput.Empty(state.Frame)).State;
            ProjectileState entity = Assert.Single(state.Projectiles);
            Assert.Equal((freezePosition, freezeLife), (entity.PositionX, entity.RemainingLifetime));
            Assert.Equal(tackleTick, state.Fighter(0).MoveElapsedFrames);
            Assert.Equal(0, state.Fighter(1).HitstopRemainingFrames);
        }

        BattleStepResult resumed = simulation.Step(state, FrameInput.Empty(state.Frame));
        ProjectileState moving = Assert.Single(resumed.State.Projectiles);
        Assert.Equal(freezePosition - 1, moving.PositionX);
        Assert.Equal(freezeLife - 1, moving.RemainingLifetime);
        Assert.DoesNotContain(resumed.Events, e => e.Type is BattleEventType.ProjectileHit or BattleEventType.ProjectileBlocked);
        Assert.Equal(resumed.State.Projectiles.ToArray(), simulation.Step(state, FrameInput.Empty(state.Frame)).State.Projectiles.ToArray());
    }

    [Theory]
    [InlineData(false, FighterLogicState.AirBlockstun)]
    [InlineData(true, FighterLogicState.AirHitstun)]
    public void AuthoredMoveAboveGroundUsesAirGuardEligibility(bool groundGuardOnly, FighterLogicState expected)
    {
        AttackDefinition hit = ProjectileHit(height: AttackHeight.Mid, groundGuardOnly: groundGuardOnly);
        var projectileMove = new MoveDefinition("Wave", "Wave", 1, 2, 4,
            projectiles: [new ProjectileDefinition("WaveEntity", hit, FixedPoint.One,
                lifetimeFrames: 12, timeline: new ProjectileTimeline(8,
                    [new ProjectileHitSpan(1, 8, new FixedRect(-5, 5, -5, 5), hit)]))]);
        var targetMove = new MoveDefinition("AirMove", "AirMove", 1, 10, 1,
            airMotion: new MoveAirMotion(0, 8, FixedPoint.Zero, FixedPoint.FromInt(8),
                FixedPoint.FromFraction(1, 2), FixedPoint.One));
        var commands = new[] { new CommandDefinition("Wave", [], InputBits.LightPunch),
            new CommandDefinition("AirMove", [], InputBits.HeavyPunch) };
        var attacker = new FighterDefinition("Attacker", commands, [projectileMove]);
        var target = new FighterDefinition("Target", commands, [targetMove]);
        var simulation = new BattleSimulation(new FightRoster(attacker, target));

        FighterState targetState = FighterState.Create(1, 1000, 20, Facing.Left) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = "AirMove",
            MoveStartedFrame = 0,
            MoveElapsedFrames = 1,
            PhaseRemainingFrames = 9,
            PositionY = 20,
            VelocityY = 8,
            AuthoredAirMotion = true,
        };
        var projectile = new ProjectileState(1, 0, "WaveEntity", "Wave", 20, 25, 0, 0, 10,
            SpawnFacing: Facing.Right);
        var before = new BattleState(0,
            [FighterState.Create(0, 1000, -100, Facing.Right), targetState],
            BattleState.CurrentStateVersion, projectiles: [projectile], nextProjectileId: 2);

        BattleStepResult result = simulation.Step(before,
            new FrameInput(0, InputBits.None, InputBits.Right));

        Assert.Contains(result.Events, e => e.Type == (expected == FighterLogicState.AirBlockstun
            ? BattleEventType.ProjectileBlocked : BattleEventType.ProjectileHit));
        Assert.Equal(expected, result.State.Fighter(1).LogicState);
        Assert.Equal(expected == FighterLogicState.AirBlockstun ? 1000 : 1000 - hit.Damage,
            result.State.Fighter(1).Health);
    }

    [Fact]
    public void DynamicGuardDamageAppliesChipWithoutConsumingAttackVariance()
    {
        var attack = new AttackDefinition(offsetForward: 20, offsetUp: 40, halfWidth: 30, halfHeight: 30,
            firstActiveFrame: 0, lastActiveFrame: 0, height: AttackHeight.High, damage: 0,
            hitstunFrames: 12, blockstunFrames: 12, hitstopFrames: 0, contactGroup: 1,
            dynamicDamage: new DynamicAttackDamage(4, 7, false),
            dynamicGuardDamage: new DynamicAttackDamage(1, 1, false, useVariance: false));
        var move = new MoveDefinition("Tackle", "Tackle", 1, 1, 1, attacks: [attack]);
        var command = new CommandDefinition("Tackle", [], InputBits.HeavyPunch);
        var rules = new DynamicDamageRules(new DamageRatio(1923, 200), new DamageRatio(4, 5),
            DamageRatio.One, new DamageRatio(97, 100), varianceMaximum: 2, counterGraceTicks: 2);
        var attacker = new FighterDefinition("Terry", [command], [move], damageRules: rules);
        var defender = new FighterDefinition("Opponent", [command], [move], damageRules: rules);
        var simulation = new BattleSimulation(new FightRoster(attacker, defender));

        BattleState state = new BattleState(0,
            [FighterState.Create(0, 1000, 0, Facing.Right), FighterState.Create(1, 1000, 40, Facing.Left)],
            BattleState.CurrentStateVersion);
        BattleStepResult? blocked = null;
        for (int frame = 0; frame < 8 && blocked is null; frame++)
        {
            BattleStepResult step = simulation.Step(state,
                new FrameInput(frame, frame == 0 ? InputBits.HeavyPunch : InputBits.None, InputBits.Right));
            state = step.State;
            if (step.Events.Any(evt => evt.Type == BattleEventType.Blocked)) blocked = step;
        }

        Assert.NotNull(blocked);
        Assert.Equal(7, Assert.Single(blocked!.Events, evt => evt.Type == BattleEventType.Blocked).Amount);
        Assert.Equal(993, blocked.State.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Blockstun, blocked.State.Fighter(1).LogicState);
    }

    private static AttackDefinition ProjectileHit(AttackHeight height = AttackHeight.Mid, bool groundGuardOnly = false) =>
        new(offsetForward: 0, offsetUp: 0, halfWidth: 5, halfHeight: 5, height: height,
            damage: 10, hitstunFrames: 8, blockstunFrames: 8, hitstopFrames: 0,
            groundGuardOnly: groundGuardOnly);
}
