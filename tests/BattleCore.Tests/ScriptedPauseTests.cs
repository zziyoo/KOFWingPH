using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public sealed class ScriptedPauseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonContactStrikeOverlapUsesTheConfiguredClashRule(bool enabled)
    {
        var command = new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch);
        var punch = new MoveDefinition("Punch", "5A", 1, 3, 4, attacks: new[]
        {
            new AttackDefinition(49, 35, 9, 10, 0, 2, AttackHeight.Mid, 20, 5, 4),
        });
        var roster = new FightRoster(new FighterDefinition("A", new[] { command }, new[] { punch }, allowStrikeClash: enabled),
            new FighterDefinition("B", new[] { command }, new[] { punch }, allowStrikeClash: enabled));
        var simulation = new BattleSimulation(roster);
        var initial = new BattleState(0,
            new[] { FighterState.Create(0, 1000, -50, Facing.Right), FighterState.Create(1, 1000, 50, Facing.Left) },
            BattleState.CreateDefault().StateVersion);
        var entered = simulation.Step(initial, new FrameInput(0, InputBits.LightPunch, InputBits.LightPunch));
        var active = simulation.Step(entered.State, new FrameInput(1, InputBits.None, InputBits.None));
        Assert.Empty(active.Contacts);
        Assert.Equal(enabled, active.Events.Any(e => e.Type == BattleEventType.StrikeClash));
        Assert.All(active.State.Fighters, fighter => Assert.Equal(enabled ? FighterLogicState.Recovery : FighterLogicState.Active,
            fighter.LogicState));
    }

    [Fact]
    public void PauseStartsAfterTargetShakeAndFreezesWorldWithoutRepeatingContact()
    {
        var (simulation, state) = Setup();
        var frozenFrames = new List<int>();
        int contacts = 0;
        for (int frame = 0; frame < 50; frame++)
        {
            var next = simulation.Step(state, new FrameInput(frame,
                frame == 0 ? InputBits.LightPunch : InputBits.None, InputBits.None));
            contacts += next.Events.Count(e => e.Type == BattleEventType.ProjectileHit);
            if (state.ScriptedPause.IsActive)
            {
                frozenFrames.Add(frame);
                Assert.Equal(0, state.Fighter(1).HitstopRemainingFrames);
                Assert.Empty(next.Events);
                Assert.Equal(state.Projectiles, next.State.Projectiles);
                Assert.Equal(state.RoundTimerRemainingFrames, next.State.RoundTimerRemainingFrames);
                for (int seat = 0; seat < 2; seat++)
                {
                    FighterState prior = state.Fighter(seat);
                    Assert.Equal(prior with { Recognition = next.State.Fighter(seat).Recognition }, next.State.Fighter(seat));
                    Assert.Equal(frame, next.State.Fighter(seat).RecognitionState.LastObservedFrame);
                }

                BattleStepResult replay = simulation.Step(state, next.Input);
                Assert.Equal(next.State.ScriptedPause, replay.State.ScriptedPause);
                Assert.Equal(next.State.Fighters, replay.State.Fighters);
                Assert.Equal(next.State.Projectiles, replay.State.Projectiles);
                Assert.Equal(next.Events, replay.Events);
            }
            state = next.State;
        }

        Assert.Equal(1, contacts);
        // Contact is frame 2, shake runs on 3 and 4, and the first eligible even owner time is 6.
        Assert.Equal(Enumerable.Range(0, 12).Select(i => 7 + 3 * i), frozenFrames);
        Assert.Equal(62, state.RoundTimerRemainingFrames);
        Assert.False(state.ScriptedPause.IsActive);
    }

    [Fact]
    public void GuardChipNeverRequestsScriptedPause()
    {
        var (simulation, state) = Setup();
        int blocks = 0;
        for (int frame = 0; frame < 12; frame++)
        {
            var step = simulation.Step(state, new FrameInput(frame,
                frame == 0 ? InputBits.LightPunch : InputBits.None, InputBits.Right));
            blocks += step.Events.Count(e => e.Type == BattleEventType.ProjectileBlocked);
            state = step.State;
            Assert.False(state.ScriptedPause.IsActive);
        }
        Assert.Equal(1, blocks);
        Assert.Equal(991, state.Fighter(1).Health);
    }

    private static (BattleSimulation Simulation, BattleState State) Setup()
    {
        AttackDefinition hit = new(0, 30, 25, 20, 0, 0, AttackHeight.Mid, 76, 5, 4,
            hitstopByOutcome: new HitstopByOutcome(new HitstopPair(0, 2), new HitstopPair(0, 1)));
        var timeline = new ProjectileTimeline(4, new[]
        {
            new ProjectileHitSpan(1, 2, new FixedRect(0, 80, 10, 50), hit, guardDamage: 9),
        }, requestPauseOnHit: true);
        var wave = new ProjectileDefinition("WaveEntity",
            new AttackDefinition(0, 0, 1, 1, 0, 0, AttackHeight.Mid, 0, 1, 1),
            speedX: 2, lifetimeFrames: 25, timeline: timeline);
        var definition = new FighterDefinition("PauseProbe",
            new[] { new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch) },
            new[] { new MoveDefinition("Wave", "5A", 1, 1, 58, projectiles: new[] { wave }) },
            damageRules: new DynamicDamageRules(DamageRatio.One, DamageRatio.One, DamageRatio.One, DamageRatio.One, 2, 2));
        var simulation = new BattleSimulation(new FightRoster(definition, FightRoster.CreateEmpty().Player2));
        return (simulation, new BattleState(0,
            new[] { FighterState.Create(0, 1000, -40, Facing.Right), FighterState.Create(1, 1000, 40, Facing.Left) },
            BattleState.CreateDefault().StateVersion, roundTimerRemainingFrames: 100));
    }
}
