using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 01 的最小验收测试。
/// 全部为纯逻辑测试，不创建窗口、不依赖 Godot、不读写文件。
/// </summary>
public class BattleSimulationTests
{
    private static BattleSimulation NewSim() => new();

    [Fact]
    public void Step_AdvancesExactlyOneLogicFrame()
    {
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();
        Assert.Equal(0, state.Frame);

        BattleStepResult r1 = sim.Step(state, FrameInput.Empty(0));
        Assert.Equal(1, r1.State.Frame);
        Assert.True(r1.AdvancedExactlyOneFrame);

        BattleStepResult r2 = sim.Step(r1.State, FrameInput.Empty(1));
        Assert.Equal(2, r2.State.Frame);
        Assert.True(r2.AdvancedExactlyOneFrame);
    }

    [Fact]
    public void Step_ManyTimes_ReachesExactFrameCount()
    {
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();

        for (int i = 0; i < 600; i++)
            state = sim.Step(state, FrameInput.Empty(i)).State;

        Assert.Equal(600, state.Frame);
    }

    [Fact]
    public void Step_DoesNotMutateInputState()
    {
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();
        FighterState p1Before = state.Fighter(0);
        FighterState p2Before = state.Fighter(1);

        BattleStepResult result = sim.Step(state, FrameInput.Empty(0));

        Assert.Equal(0, state.Frame);                       // 原状态帧号未变
        Assert.NotSame(state, result.State);                // 返回新实例
        Assert.Equal(p1Before, result.State.Fighter(0));
        Assert.Equal(p2Before, result.State.Fighter(1));
    }

    [Fact]
    public void Step_SameInitialStateAndInputs_ProduceIdenticalResults()
    {
        var simA = NewSim();
        var simB = NewSim();

        BattleState a = BattleState.CreateDefault();
        BattleState b = BattleState.CreateDefault();

        var inputs = new[]
        {
            FrameInput.Empty(0),
            new FrameInput(1, InputBits.Right | InputBits.LightPunch, InputBits.Left),
            new FrameInput(2, InputBits.Up, InputBits.Down | InputBits.HeavyKick),
        };

        foreach (FrameInput input in inputs)
        {
            a = simA.Step(a, input).State;
        }

        // 用第二个独立实例重放同一输入序列
        var simB2 = NewSim();
        b = BattleState.CreateDefault();
        foreach (FrameInput input in inputs)
        {
            b = simB2.Step(b, input).State;
        }

        Assert.Equal(a.Frame, b.Frame);
        Assert.Equal(a.StateVersion, b.StateVersion);
        for (int i = 0; i < a.Fighters.Count; i++)
            Assert.Equal(a.Fighter(i), b.Fighter(i));
    }

    [Fact]
    public void Step_RejectsMismatchedInputFrame()
    {
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault(); // Frame = 0

        var ex = Assert.Throws<InvalidOperationException>(
            () => sim.Step(state, FrameInput.Empty(5)));
        Assert.Contains("不一致", ex.Message);
    }

    [Fact]
    public void Step_RecordsInputInResult()
    {
        var sim = NewSim();
        BattleState state = BattleState.CreateDefault();
        var input = new FrameInput(0, InputBits.Left, InputBits.Right);

        BattleStepResult result = sim.Step(state, input);

        Assert.Equal(input, result.Input);
        Assert.Equal(0, result.FrameBefore);
        Assert.Equal(1, result.FrameAfter);
    }

    [Fact]
    public void BattleState_RejectsWrongFighterCount()
    {
        Assert.Throws<ArgumentException>(() =>
            new BattleState(0, new[] { FighterState.Create(0, 1000, -120, Facing.Right) }, 1));
    }

    [Fact]
    public void BattleState_IsReadOnlyAfterConstruction()
    {
        var fighters = new[]
        {
            FighterState.Create(0, 1000, -120, Facing.Right),
            FighterState.Create(1, 1000, 120, Facing.Left),
        };
        var state = new BattleState(0, fighters, 1);

        // 修改源数组不应影响已构造状态（构造时已复制）。
        fighters[0] = FighterState.Create(0, 1, -999, Facing.Left);

        Assert.Equal(1000, state.Fighter(0).MaxHealth);
        Assert.Equal(-120, state.Fighter(0).PositionX);
    }
}
