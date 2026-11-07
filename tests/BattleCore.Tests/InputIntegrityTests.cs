using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 03A 专项补修：输入所有权、两阶段提交与历史容量。
///
/// 前半部分是对**修复前**缺陷的复现（历史可被外部改写、输入先于战斗帧提交
/// 导致「幻影输入」与重试错帧），修复后必须全部通过。
/// </summary>
public class InputIntegrityTests
{
    private const double Step = 1.0 / 60.0;

    private static FighterState[] TwoFighters() => new[]
    {
        FighterState.Create(0, 1000, -120, Facing.Right),
        FighterState.Create(1, 1000, 120, Facing.Left),
    };

    /// <summary>构造一个会让 <c>Step()</c> 必然失败的状态：帧号已到 int 上限，+1 溢出为负。</summary>
    private static BattleSession OverflowingSession() =>
        new(new BattleState(int.MaxValue, TwoFighters(), stateVersion: 1));

    /// <summary>在第一次 <see cref="Prepare"/> 抛异常的输入源，用于验证失败后的恢复。</summary>
    private sealed class FailOnceSource : IInputSource
    {
        private readonly InputBuffer _inner;
        private int _remainingFailures;

        public FailOnceSource(InputBuffer inner, int failures)
        {
            _inner = inner;
            _remainingFailures = failures;
        }

        public FrameInput Prepare(int frame)
        {
            if (_remainingFailures > 0)
            {
                _remainingFailures--;
                throw new InvalidOperationException("输入准备故障");
            }

            return _inner.Prepare(frame);
        }

        public void Commit(FrameInput input) => _inner.Commit(input);
    }

    /// <summary>
    /// 在指定帧的 <see cref="Commit"/> 抛异常的输入源（只失败一次）。
    /// 用于构造「<c>Step()</c> 已成功算出候选状态，但输入提交失败」的路径。
    /// 它自身不做任何写入，符合 <see cref="IInputSource.Commit"/> 的无副作用失败契约。
    /// </summary>
    private sealed class CommitFailSource : IInputSource
    {
        private readonly InputBuffer _inner;
        private readonly int _failOnFrame;
        private bool _failed;

        public CommitFailSource(InputBuffer inner, int failOnFrame)
        {
            _inner = inner;
            _failOnFrame = failOnFrame;
        }

        public FrameInput Prepare(int frame) => _inner.Prepare(frame);

        public void Commit(FrameInput input)
        {
            if (!_failed && input.Frame == _failOnFrame)
            {
                _failed = true;
                throw new InvalidOperationException("输入提交故障");
            }

            _inner.Commit(input);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 缺陷 1：History 只读是假的（返回的仍是 List<T>）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void History_IsNotBackedByMutableList()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        buffer.Take(0);

        // 强制转换必须失败：外部不能拿到内部可变列表
        Assert.Null(buffer.History as List<FrameInput>);
    }

    [Fact]
    public void History_CannotBeMutatedThroughIListInterface()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        buffer.Take(0);
        buffer.Sample(InputBits.None, InputBits.None);
        buffer.Take(1);

        var mutable = (IList<FrameInput>)buffer.History;

        Assert.True(mutable.IsReadOnly, "History 必须向调用方报告为只读");
        Assert.Throws<NotSupportedException>(() => mutable.Add(FrameInput.Empty(2)));
        Assert.Throws<NotSupportedException>(() => mutable.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => mutable.Clear());

        Assert.Equal(2, buffer.History.Count);
        Assert.Equal(1, buffer.LastInput!.Value.Frame);
    }

    [Fact]
    public void CommittedHistory_IsNotAffectedByLaterSamples()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        buffer.Take(0);

        buffer.Sample(InputBits.None, InputBits.None);
        buffer.Take(1);

        // 历史是不可变值快照：后续采样不得改写第 0 帧
        Assert.Equal(InputBits.Right, buffer.History[0].Player1);
    }

    // ─────────────────────────────────────────────────────────────
    // 缺陷 2：Take() 先提交输入，Step() 之后才可能失败
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FailedStep_LeavesNoPhantomHistoryEntry()
    {
        BattleSession session = OverflowingSession();
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);

        Assert.ThrowsAny<Exception>(() => session.Advance(Step, inputSource: buffer));

        // 战斗帧没有提交，输入历史也不得出现「已提交」的帧
        Assert.Equal(int.MaxValue, session.CurrentState.Frame);
        Assert.Empty(buffer.History);
    }

    [Fact]
    public void FailedStep_RetrySameFrame_UsesSameInputAndStillFailsCleanly()
    {
        BattleSession session = OverflowingSession();
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.LightPunch, InputBits.None);

        Assert.ThrowsAny<Exception>(() => session.Advance(Step, inputSource: buffer));

        // 重试同一逻辑帧：不得报「输入帧号不连续」，而应重新尝试模拟并再次失败
        var second = Record.Exception(() => session.Advance(Step, inputSource: buffer));

        Assert.NotNull(second);
        Assert.DoesNotContain("连续", second!.Message);
        Assert.Empty(buffer.History);
        Assert.Equal(int.MaxValue, session.CurrentState.Frame);
    }

    // ─────────────────────────────────────────────────────────────
    // 两阶段提交：Prepare / Commit 语义
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Prepare_DoesNotCommitHistoryOrConsumePendingEdge()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);

        FrameInput prepared = buffer.Prepare(0);

        Assert.Equal(InputBits.Right, prepared.Player1);
        Assert.Empty(buffer.History);
        Assert.Null(buffer.LastInput);

        // 同一帧再次准备必须得到同一输入（失败重试依赖这条幂等性）
        Assert.Equal(prepared, buffer.Prepare(0));
    }

    [Fact]
    public void Commit_WritesHistoryOnce_AndConsumesEdge()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);

        FrameInput input = buffer.Prepare(0);
        buffer.Commit(input);

        Assert.Single(buffer.History);
        Assert.Equal(input, buffer.History[0]);

        // 边沿已消费：松开后下一帧就是空输入
        buffer.Sample(InputBits.None, InputBits.None);
        Assert.Equal(InputBits.None, buffer.Prepare(1).Player1);
    }

    [Fact]
    public void Commit_RejectsInputFromAnotherFrame()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        buffer.Prepare(0);

        Assert.Throws<InvalidOperationException>(() => buffer.Commit(FrameInput.Empty(5)));
        Assert.Empty(buffer.History);
    }

    // ─────────────────────────────────────────────────────────────
    // 多帧批次中的失败与恢复
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SimulationFailure_MidBatch_KeepsOnlyCommittedFrames()
    {
        // 起始帧 = int.MaxValue - 1：第 0 帧能成功，第 1 帧必失败（帧号 +1 溢出）。
        var start = new BattleState(int.MaxValue - 1, TwoFighters(), stateVersion: 1);
        var session = new BattleSession(start, new FixedStepScheduler(maxStepsPerAdvance: 3));
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);

        Assert.ThrowsAny<Exception>(() => session.Advance(2 * Step, inputSource: buffer));

        // 已完成的帧保留：状态、记账、输入历史三者一致
        Assert.Equal(int.MaxValue, session.CurrentState.Frame);
        Assert.Equal(1, session.Scheduler.TotalSteps);
        Assert.Single(buffer.History);
        Assert.Equal(int.MaxValue - 1, buffer.History[0].Frame);
        Assert.True(buffer.IsAlignedWith(session.CurrentState.Frame));
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void PendingEdgeSurvivesPrepareFailure_AndIsConsumedExactlyOnce()
    {
        var session = new BattleSession();
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.HeavyKick, InputBits.None);
        buffer.Sample(InputBits.None, InputBits.None); // 点击：按下后立即松开

        var source = new FailOnceSource(buffer, failures: 1);

        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: source));
        Assert.Empty(buffer.History);
        Assert.Equal(0, session.CurrentState.Frame);

        // 失败后重试：边沿仍在，且只落在第 0 帧一次。
        session.Advance(Step, inputSource: source);
        Assert.Single(buffer.History);
        Assert.Equal(InputBits.HeavyKick, buffer.History[0].Player1);

        session.Advance(Step, inputSource: source);
        Assert.Equal(InputBits.None, buffer.History[1].Player1);
    }

    [Fact]
    public void DelegateProvider_IsReinvokedWithSameFrame_AfterFailure()
    {
        // 普通委托无法回滚：契约是「同一帧号必须返回同一输入」。
        var session = new BattleSession();
        var frames = new List<int>();
        int attempt = 0;

        FrameInput Provider(int frame)
        {
            frames.Add(frame);
            if (attempt++ == 0)
                throw new InvalidOperationException("模拟前故障");
            return FrameInput.Empty(frame);
        }

        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, Provider));
        Assert.Equal(0, session.CurrentState.Frame);

        session.Advance(Step, Provider);

        Assert.Equal(1, session.CurrentState.Frame);
        Assert.Equal(new[] { 0, 0 }, frames);
    }

    // ─────────────────────────────────────────────────────────────
    // 单步 / 暂停 / 混用
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StepExact_WithInputSource_CommitsHistoryAndStaysAligned()
    {
        var session = new BattleSession();
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);

        session.StepExact(3, inputSource: buffer);

        Assert.Equal(3, session.CurrentState.Frame);
        Assert.Equal(3, buffer.History.Count);
        Assert.True(buffer.IsAlignedWith(3));
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void Pause_ThenStepExact_ProducesNoGhostInputFrames()
    {
        var session = new BattleSession();
        var buffer = new InputBuffer();
        session.Scheduler.Pause();
        buffer.Sample(InputBits.Down, InputBits.None);

        // 暂停：时间驱动不推进，也不得产生输入帧
        Assert.Equal(0, session.Advance(1.0, inputSource: buffer));
        Assert.Empty(buffer.History);

        // 暂停下显式单步仍生效，且输入与帧严格一一对应
        session.StepExact(2, inputSource: buffer);

        Assert.Equal(2, buffer.History.Count);
        Assert.Equal(2, session.CurrentState.Frame);
        Assert.Equal(InputBits.Down, buffer.History[0].Player1);
        Assert.Equal(InputBits.Down, buffer.History[1].Player1);
        Assert.True(buffer.IsAlignedWith(2));
    }

    [Fact]
    public void Advance_RejectsBothInputSources()
    {
        var session = new BattleSession();
        var buffer = new InputBuffer();

        Assert.Throws<ArgumentException>(() =>
            session.Advance(Step, frame => FrameInput.Empty(frame), buffer));
    }

    [Fact]
    public void NonZeroInitialFrame_HistoryStartsAtThatFrame()
    {
        var start = new BattleState(frame: 100, TwoFighters(), stateVersion: 1);
        var session = new BattleSession(start);
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);

        session.Advance(Step, inputSource: buffer);
        session.Advance(Step, inputSource: buffer);

        Assert.Equal(102, session.CurrentState.Frame);
        Assert.Equal(2, buffer.History.Count);
        Assert.Equal(100, buffer.History[0].Frame);
        Assert.True(buffer.IsAlignedWith(102));
    }

    // ─────────────────────────────────────────────────────────────
    // P1：Commit 失败不得推进战斗状态、不得记账
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Advance_CommitThrows_DoesNotAdvanceStateOrAccount()
    {
        var session = new BattleSession();
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        var source = new CommitFailSource(buffer, failOnFrame: 0);

        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: source));

        // Step() 已经算出候选状态，但提交失败：权威状态、帧计数、输入历史都不得前进
        Assert.Equal(0, session.CurrentState.Frame);
        Assert.Equal(0, session.Scheduler.TotalSteps);
        Assert.True(session.IsFrameSyncIntact);
        Assert.Empty(buffer.History);

        // 时间预算已被消耗但未执行 → 记为丢弃，账目仍可解释
        Assert.Equal(1, session.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void StepExact_CommitThrows_DoesNotAdvanceStateOrAccount()
    {
        var session = new BattleSession();
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Down, InputBits.None);
        var source = new CommitFailSource(buffer, failOnFrame: 0);

        Assert.Throws<InvalidOperationException>(() => session.StepExact(3, inputSource: source));

        Assert.Equal(0, session.CurrentState.Frame);
        Assert.Equal(0, session.Scheduler.TotalSteps);
        Assert.True(session.IsFrameSyncIntact);
        Assert.Empty(buffer.History);

        // 显式单步没有时间预算，不应产生丢弃计数
        Assert.Equal(0, session.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void MultiFrameBatch_CommitFailureAtSecondFrame_KeepsOnlyCommittedFrames()
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 3));
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        var source = new CommitFailSource(buffer, failOnFrame: 1);

        Assert.Throws<InvalidOperationException>(() => session.Advance(2 * Step, inputSource: source));

        // 第 0 帧已完整提交（状态 + 记账 + 输入），第 1 帧三者都不得留下
        Assert.Equal(1, session.CurrentState.Frame);
        Assert.Equal(1, session.Scheduler.TotalSteps);
        Assert.Single(buffer.History);
        Assert.Equal(0, buffer.History[0].Frame);
        Assert.True(buffer.IsAlignedWith(session.CurrentState.Frame));
        Assert.True(session.IsFrameSyncIntact);
        Assert.Equal(1, session.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void CommitFailure_ThenRetry_CommitsEdgeExactlyOnce()
    {
        var session = new BattleSession();
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.HeavyKick, InputBits.None);
        buffer.Sample(InputBits.None, InputBits.None); // 点击
        var source = new CommitFailSource(buffer, failOnFrame: 0);

        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: source));
        Assert.Equal(0, session.CurrentState.Frame);
        Assert.Empty(buffer.History);

        // 重试：边沿仍在，落在第 0 帧且只落一次
        session.Advance(Step, inputSource: source);
        session.Advance(Step, inputSource: source);

        Assert.Equal(2, session.CurrentState.Frame);
        Assert.Equal(2, buffer.History.Count);
        Assert.Equal(InputBits.HeavyKick, buffer.History[0].Player1);
        Assert.Equal(InputBits.None, buffer.History[1].Player1);
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void InputBufferCommit_RejectsMismatchedFrame_WithoutSideEffects()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        FrameInput prepared = buffer.Prepare(0);

        Assert.Throws<InvalidOperationException>(() => buffer.Commit(FrameInput.Empty(5)));

        // 校验失败：历史与 pending 均未被改动
        Assert.Empty(buffer.History);
        Assert.Equal(0, buffer.CoalescedPressCount);
        Assert.Equal(prepared, buffer.Prepare(0));
    }

    [Fact]
    public void InputBufferCommit_RejectsStaleInput_WithoutSideEffects()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        FrameInput prepared = buffer.Prepare(0);

        // Prepare 之后采样被改动，旧输入不再等于当前状态
        buffer.Sample(InputBits.Left, InputBits.Right);
        Assert.Throws<InvalidOperationException>(() => buffer.Commit(prepared));

        Assert.Empty(buffer.History);

        // 边沿未被消费：原 Right 边沿 + 新的 Left 都在
        FrameInput replay = buffer.Prepare(0);
        Assert.Equal(InputBits.Right | InputBits.Left, replay.Player1);
    }

    // ─────────────────────────────────────────────────────────────
    // 历史容量（P2）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void History_IsCappedAtConfiguredCapacity()
    {
        var buffer = new InputBuffer(historyCapacity: 10);
        for (int frame = 0; frame < 25; frame++)
            buffer.Take(frame);

        Assert.Equal(10, buffer.History.Count);
        Assert.Equal(15, buffer.History[0].Frame);
        Assert.Equal(24, buffer.LastInput!.Value.Frame);
        Assert.True(buffer.IsHistoryTruncated);
    }

    [Fact]
    public void DefaultCapacity_BoundsHistoryOnLongRun()
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 4));
        var buffer = new InputBuffer();

        for (int i = 0; i < 2000; i++)
            session.Advance(Step, inputSource: buffer);

        Assert.Equal(2000, session.CurrentState.Frame);
        Assert.Equal(InputBuffer.DefaultCapacity, buffer.History.Count);
        Assert.True(buffer.IsHistoryTruncated);
        Assert.True(buffer.IsAlignedWith(session.CurrentState.Frame));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputBuffer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputBuffer(-1));
    }
}
