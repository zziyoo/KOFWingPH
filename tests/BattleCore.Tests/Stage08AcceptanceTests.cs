using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 08 最终专项补修（三）：跨系统验收——
/// 冻结 + D73 推挤在<b>不同渲染帧率</b>与 <c>StepExact</c>/<c>Advance</c> 两条路径下逐位一致，
/// 并做含「相杀 + Hitstop」的 10000 帧确定性长跑（事件身份与 EventId 唯一性）。
/// </summary>
public class Stage08AcceptanceTests
{
    private const string PokeA = "PokeA";
    private const string PokeB = "PokeB";

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    private static BattleSimulation ClashSim(int hitstop = 3)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        AttackDefinition Box() => new(
            offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
            halfWidth: FixedPoint.FromInt(20), halfHeight: FixedPoint.FromInt(20),
            firstActiveFrame: 0, lastActiveFrame: int.MaxValue,
            height: AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10,
            hitstopFrames: hitstop);

        FighterDefinition Build(string id, string moveId) => new(id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[] { new MoveDefinition(moveId, "5A", 1, 3, 4, attacks: new[] { Box() }) },
            movement);

        return new BattleSimulation(new FightRoster(
            Build("Kai", PokeA), Build("Rei", PokeB)));
    }

    /// <summary>P1 处于 3 帧 Hitstop、P2 正常；初始间距正好 = 60（双方合法站位）。</summary>
    private static BattleState FreezeDuel() => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, 0, Facing.Right) with { HitstopRemainingFrames = 3 },
            FighterState.Create(1, 1000, 60, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion);

    private static FixedStepScheduler Scheduler() => new(maxStepsPerAdvance: 8);

    [Fact]
    public void DifferentRenderRates_AgreeUnderFrozenSeparation()
    {
        // 60fps：每批 1 逻辑帧 × 10；30fps：每批 2 逻辑帧 × 5 —— 总逻辑帧数相同。
        static BattleState Run(double delta, int batches)
        {
            var session = new BattleSession(
                initialState: FreezeDuel(),
                simulation: new BattleSimulation(),
                scheduler: Scheduler());
            for (int i = 0; i < batches; i++)
                session.Advance(delta, inputProvider: f => F(f, InputBits.None, InputBits.Left));
            Assert.True(session.IsFrameSyncIntact);
            return session.CurrentState;
        }

        BattleState at60 = Run(FixedStepScheduler.FixedStepSeconds, 10);
        BattleState at30 = Run(FixedStepScheduler.FixedStepSeconds * 2, 5);

        Assert.Equal(at60.Frame, at30.Frame);
        Assert.Equal(at60.Fighter(0), at30.Fighter(0));
        Assert.Equal(at60.Fighter(1), at30.Fighter(1));
        Assert.Equal(at60.Round, at30.Round);
        Assert.Equal(0, at60.Fighter(0).HitstopRemainingFrames); // 停顿已走完
    }

    [Fact]
    public void StepExactAndAdvance_AgreeUnderFrozenSeparation()
    {
        var exact = new BattleSession(
            initialState: FreezeDuel(),
            simulation: new BattleSimulation(),
            scheduler: Scheduler());
        var scheduled = new BattleSession(
            initialState: FreezeDuel(),
            simulation: new BattleSimulation(),
            scheduler: Scheduler());

        exact.StepExact(20, inputProvider: f => F(f, InputBits.None, InputBits.Left));
        for (int i = 0; i < 20; i++)
            scheduled.Advance(FixedStepScheduler.FixedStepSeconds,
                inputProvider: f => F(f, InputBits.None, InputBits.Left));

        Assert.True(exact.IsFrameSyncIntact);
        Assert.True(scheduled.IsFrameSyncIntact);
        Assert.Equal(exact.CurrentState.Frame, scheduled.CurrentState.Frame);
        Assert.Equal(exact.CurrentState.Fighter(0), scheduled.CurrentState.Fighter(0));
        Assert.Equal(exact.CurrentState.Fighter(1), scheduled.CurrentState.Fighter(1));
    }

    [Fact]
    public void LongRun_10000Frames_ClashAndHitstop_IsDeterministic()
    {
        static (BattleState State, List<int> EventIds) Run()
        {
            var sim = ClashSim(hitstop: 3);
            BattleState state = new(0, new[]
            {
                FighterState.Create(0, 1000, -60, Facing.Right),
                FighterState.Create(1, 1000, 60, Facing.Left),
            }, BattleState.CreateDefault().StateVersion);
            var ids = new List<int>();

            for (int f = 0; f < 10000; f++)
            {
                InputBits press = f % 97 == 0 ? InputBits.LightPunch : InputBits.None;
                BattleStepResult r = sim.Step(state, F(f, press, press));
                state = r.State;
                ids.AddRange(r.Events.Select(e => e.EventId));
            }

            return (state, ids);
        }

        (BattleState a, List<int> aIds) = Run();
        (BattleState b, List<int> bIds) = Run();

        Assert.Equal(a.Fighter(0), b.Fighter(0));
        Assert.Equal(a.Fighter(1), b.Fighter(1));
        Assert.Equal(a.Projectiles, b.Projectiles);
        Assert.Equal(a.NextProjectileId, b.NextProjectileId);
        Assert.Equal(a.Round, b.Round);
        Assert.Equal(aIds, bIds);
        Assert.Equal(aIds.Count, aIds.Distinct().Count()); // EventId 全局唯一，无回绕
        Assert.NotEmpty(aIds);
    }
}
