using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 03A：输入缓冲与 <see cref="BattleSession"/> 的集成测试。
///
/// 证明「渲染帧采样 → 逻辑帧输入」这条链路在各种调度形态下都正确：
/// 零帧、单帧、多帧批次、暂停、丢弃预算、不同渲染帧率。
/// </summary>
public class InputPipelineTests
{
    private const double Step = 1.0 / 60.0;

    [Fact]
    public void Advance_FeedsProviderWithTheFrameBeingSimulated()
    {
        var buffer = new InputBuffer();
        var session = new BattleSession();
        buffer.Sample(InputBits.Right, InputBits.None);

        session.Advance(Step, buffer.Take);

        Assert.Equal(1, session.CurrentState.Frame);
        Assert.Single(buffer.History);
        Assert.Equal(0, buffer.History[0].Frame);
        Assert.Equal(InputBits.Right, buffer.History[0].Player1);
    }

    [Fact]
    public void MultiFrameBatch_ConsumesExactlyOneInputPerLogicFrame()
    {
        var buffer = new InputBuffer();
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 3));

        // 一个渲染帧内「按下又松开」，然后一次推进 3 个逻辑帧。
        buffer.Sample(InputBits.LightPunch, InputBits.None);
        buffer.Sample(InputBits.None, InputBits.None);

        int steps = session.Advance(3 * Step, buffer.Take);

        Assert.Equal(3, steps);
        Assert.Equal(3, buffer.History.Count);

        // 边沿只落在第 0 帧；第 1、2 帧不得重复消费。
        Assert.Equal(InputBits.LightPunch, buffer.History[0].Player1);
        Assert.Equal(InputBits.None, buffer.History[1].Player1);
        Assert.Equal(InputBits.None, buffer.History[2].Player1);
    }

    [Fact]
    public void ZeroLogicFrames_KeepsPendingEdgeForNextFrame()
    {
        var buffer = new InputBuffer();
        var session = new BattleSession();
        buffer.Sample(InputBits.HeavyPunch, InputBits.None);

        // 1ms 远小于一个逻辑步长：本渲染帧推进 0 帧，不得产出输入。
        int steps = session.Advance(0.001, buffer.Take);
        Assert.Equal(0, steps);
        Assert.Empty(buffer.History);

        // 补足一个步长后，边沿仍然在，落在第 0 帧。
        session.Advance(Step, buffer.Take);
        Assert.Single(buffer.History);
        Assert.Equal(InputBits.HeavyPunch, buffer.History[0].Player1);
    }

    [Fact]
    public void Pause_ProducesNoInputFrames_AndPreservesPendingEdge()
    {
        var buffer = new InputBuffer();
        var session = new BattleSession();
        session.Scheduler.Pause();

        buffer.Sample(InputBits.Down, InputBits.None);
        Assert.Equal(0, session.Advance(1.0, buffer.Take));
        Assert.Empty(buffer.History);

        session.Scheduler.Resume();
        session.Advance(Step, buffer.Take);

        // 暂停期间按下的键不会被吞：恢复后第一帧带上它。
        Assert.Single(buffer.History);
        Assert.Equal(InputBits.Down, buffer.History[0].Player1);
    }

    [Fact]
    public void DroppedBudget_DoesNotEmitExtraInputFrames()
    {
        // 上限 5 帧：1 秒预算只执行 5 帧，其余记为丢弃。
        var buffer = new InputBuffer();
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 5));
        buffer.Sample(InputBits.Right, InputBits.Left);

        int steps = session.Advance(1.0, buffer.Take);

        Assert.Equal(5, steps);
        Assert.True(session.Scheduler.DroppedStepCount > 0);

        // 被丢弃的时间不产生逻辑帧，也就不产生输入帧。
        Assert.Equal(5, buffer.History.Count);
        for (int i = 0; i < buffer.History.Count; i++)
            Assert.Equal(i, buffer.History[i].Frame);
    }

    [Fact]
    public void InputHistoryFrames_AlwaysMatchStateFrames()
    {
        var buffer = new InputBuffer();
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 5));
        var rng = new Random(20261107);

        for (int i = 0; i < 500; i++)
        {
            if (i % 7 == 0)
                buffer.Sample(InputBits.Right, InputBits.None);
            else if (i % 11 == 0)
                buffer.Sample(InputBits.None, InputBits.None);

            session.Advance(0.004 + rng.NextDouble() * 0.036, buffer.Take);
        }

        Assert.Equal(session.CurrentState.Frame, buffer.History.Count);
        for (int i = 0; i < buffer.History.Count; i++)
            Assert.Equal(i, buffer.History[i].Frame);

        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void FixedInputLog_ProducesIdenticalRunAtAnyRenderRate()
    {
        // 固定的逐逻辑帧输入日志（600 帧 = 10 秒）。
        var log = new FrameInput[600];
        for (int frame = 0; frame < log.Length; frame++)
        {
            InputBits p1 = (frame / 30) % 2 == 0 ? InputBits.Right : InputBits.Left;
            InputBits p2 = frame % 45 == 0 ? InputBits.LightKick : InputBits.None;
            log[frame] = new FrameInput(frame, p1, p2);
        }

        BattleState? reference = null;

        foreach (double fps in new[] { 30.0, 60.0, 144.0, 240.0 })
        {
            var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1000));
            var seen = new List<FrameInput>();
            int renders = (int)Math.Round(fps * 10.0);

            for (int i = 0; i < renders; i++)
            {
                // 输入按「即将推进的逻辑帧号」查表，渲染帧率只影响「一次推进几帧」。
                session.Advance(1.0 / fps, frame =>
                {
                    FrameInput input = log[frame];
                    seen.Add(input);
                    return input;
                });
            }

            Assert.Equal(600, session.CurrentState.Frame);
            Assert.Equal(600, seen.Count);
            for (int i = 0; i < 600; i++)
                Assert.Equal(log[i], seen[i]);

            reference ??= session.CurrentState;
            Assert.Equal(reference.Frame, session.CurrentState.Frame);
            for (int p = 0; p < 2; p++)
                Assert.Equal(reference.Fighter(p), session.CurrentState.Fighter(p));
        }
    }
}
