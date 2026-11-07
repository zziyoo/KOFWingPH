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

    // ─────────────────────────────────────────────────────────────
    // 阶段 03B-1 补修：提交后观察者（frameCommitted）异常安全
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FrameCommittedObserver_Throwing_KeepsFrameAccountedAndStateAdvanced()
    {
        // 观察者在第 2 帧抛异常：该帧必须已经记账、状态已经推进，
        // 输入历史与状态对齐，异常照常向上传播（不回滚）。
        var buffer = new InputBuffer();
        var observedFrames = new List<int>();
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (input, state) =>
            {
                observedFrames.Add(input.Frame);
                if (input.Frame == 2)
                    throw new InvalidOperationException("观察者故障");
            });

        buffer.Sample(InputBits.Right, InputBits.None);

        Assert.Throws<InvalidOperationException>(() => session.StepExact(5, inputSource: buffer));

        // 帧 0/1/2 已提交（观察者在帧 2 抛），帧 3/4 未提交。
        Assert.Equal(3, session.Scheduler.TotalSteps);
        Assert.Equal(3, session.CurrentState.Frame);
        Assert.Equal(3, observedFrames.Count);
        Assert.Equal(new[] { 0, 1, 2 }, observedFrames);
        Assert.True(buffer.IsAlignedWith(session.CurrentState.Frame));
    }

    [Fact]
    public void FrameCommittedObserver_Throwing_UncommittedBudgetIsRecordedAsDropped()
    {
        // 通过 Advance 推进：观察者抛异常后，本批次未提交的剩余预算应记入丢弃。
        var buffer = new InputBuffer();
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (input, state) => throw new InvalidOperationException("观察者故障"));

        buffer.Sample(InputBits.Right, InputBits.None);

        // 单次 Advance 的预算至少 1 帧；观察者在首帧即抛。
        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: buffer));

        // 已提交 1 帧（观察者抛在提交之后），剩余预算记为丢弃。
        Assert.Equal(1, session.Scheduler.TotalSteps);
        Assert.Equal(1, session.CurrentState.Frame);
        Assert.True(session.Scheduler.DroppedStepCount >= 0);
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void FrameCommittedObserver_Throwing_ThenRecoverContinuesWithCorrectFrame()
    {
        // 观察者抛一次后会话进入故障封闭；调用方恢复派生状态一致性后 Recover()，
        // 后续帧从正确的连续帧号继续，无跳变/漏帧。
        var buffer = new InputBuffer();
        var failOnce = true;
        var recognized = new List<int>();

        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (input, state) =>
            {
                if (failOnce)
                {
                    failOnce = false;
                    throw new InvalidOperationException("一次性故障");
                }

                recognized.Add(input.Frame);
            });

        buffer.Sample(InputBits.Right, InputBits.None);

        // 第一次推进：帧 0 触发一次性故障。
        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: buffer));
        Assert.True(session.IsFaulted);
        Assert.Equal(0, session.FaultedFrame);

        // 未恢复前直接继续被拒绝。
        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: buffer));

        // 显式恢复后，从帧 1 继续。
        session.Recover();
        Assert.False(session.IsFaulted);

        buffer.Sample(InputBits.Right, InputBits.None);
        session.Advance(Step, inputSource: buffer);

        Assert.Equal(1, recognized[0]);
        Assert.True(session.IsFrameSyncIntact);
    }

    // ─────────────────────────────────────────────────────────────
    // 阶段 03B-1 最终补修：P2 观察者异常后的失败封闭（Faulted）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ObserverFault_FirstObservationThrows_EntersFaultedAndKeepsFrameAccounted()
    {
        var buffer = new InputBuffer();
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (_, _) => throw new InvalidOperationException("首次观察故障"));

        buffer.Sample(InputBits.Right, InputBits.None);

        // 首帧提交后观察者立即抛：该帧战斗状态与记账已落定，但会话进入故障封闭。
        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: buffer));

        Assert.True(session.IsFaulted);
        Assert.Equal(0, session.FaultedFrame);
        Assert.Equal(1, session.CurrentState.Frame);      // 帧 0 已提交
        Assert.Equal(1, session.Scheduler.TotalSteps);    // 记账一致
        Assert.True(session.IsFrameSyncIntact);
        Assert.True(buffer.IsAlignedWith(session.CurrentState.Frame));
    }

    [Fact]
    public void ObserverFault_MidObservationThrows_StateAndAccountingStaySynchronized()
    {
        var buffer = new InputBuffer();
        var observed = new List<int>();
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (input, _) =>
            {
                observed.Add(input.Frame);
                if (input.Frame == 2)
                    throw new InvalidOperationException("中途观察故障");
            });

        buffer.Sample(InputBits.Right, InputBits.None);

        Assert.Throws<InvalidOperationException>(() => session.StepExact(5, inputSource: buffer));

        // 帧 0/1/2 已提交（帧 2 观察者抛），帧 3/4 未提交。
        Assert.True(session.IsFaulted);
        Assert.Equal(2, session.FaultedFrame);
        Assert.Equal(3, session.CurrentState.Frame);
        Assert.Equal(3, session.Scheduler.TotalSteps);
        Assert.Equal(new[] { 0, 1, 2 }, observed);
        Assert.True(session.IsFrameSyncIntact);
        Assert.True(buffer.IsAlignedWith(session.CurrentState.Frame));
    }

    [Fact]
    public void ObserverFault_ContinuingWithoutRecover_IsRejectedByBothPaths()
    {
        var buffer = new InputBuffer();
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (_, _) => throw new InvalidOperationException("观察故障"));

        buffer.Sample(InputBits.Right, InputBits.None);

        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: buffer));
        Assert.True(session.IsFaulted);

        // Advance 与 StepExact 都拒绝在未恢复时继续。
        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: buffer));
        Assert.Throws<InvalidOperationException>(() => session.StepExact(1, inputSource: buffer));

        // 状态与记账未被拒绝操作推进。
        Assert.Equal(1, session.CurrentState.Frame);
        Assert.Equal(1, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void ObserverFault_AdvanceUncommittedBudget_IsRecordedAsDropped()
    {
        // 观察者在首帧抛：本批次其余预算（>=1 帧）未提交，记为丢弃。
        var buffer = new InputBuffer();
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (_, _) => throw new InvalidOperationException("观察故障"));

        buffer.Sample(InputBits.Right, InputBits.None);

        long droppedBefore = session.Scheduler.DroppedStepCount;
        Assert.Throws<InvalidOperationException>(() => session.Advance(4 * Step, inputSource: buffer));

        Assert.True(session.IsFaulted);
        Assert.Equal(1, session.CurrentState.Frame);
        Assert.True(session.Scheduler.DroppedStepCount > droppedBefore);
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void ObserverFault_RecoverThenContinue_DoesNotSkipFrames()
    {
        var buffer = new InputBuffer();
        var recognized = new List<int>();
        var shouldThrow = true;
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (input, _) =>
            {
                if (shouldThrow && input.Frame == 0)
                {
                    shouldThrow = false;
                    throw new InvalidOperationException("观察故障");
                }

                recognized.Add(input.Frame);
            });

        buffer.Sample(InputBits.Right, InputBits.None);
        Assert.Throws<InvalidOperationException>(() => session.Advance(Step, inputSource: buffer));
        Assert.True(session.IsFaulted);

        session.Recover();
        Assert.False(session.IsFaulted);
        Assert.Equal(-1, session.FaultedFrame);

        buffer.Sample(InputBits.Right, InputBits.None);
        session.StepExact(2, inputSource: buffer);

        // 恢复后从帧 1 开始，无跳变（帧 1、2 连续）。
        Assert.Equal(new[] { 1, 2 }, recognized);
        Assert.Equal(3, session.CurrentState.Frame);
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void ObserverFault_StepOrCommitFailure_DoesNotTriggerFaulted()
    {
        // 只有「观察者」异常才触发故障封闭；Step/Commit 的失败不应误标 Faulted。
        var session = OverflowingSession();  // Step 会在首帧因帧号溢出失败
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);

        Assert.ThrowsAny<Exception>(() => session.Advance(Step, inputSource: buffer));

        Assert.False(session.IsFaulted);
    }

    [Fact]
    public void NormalObserver_DoesNotFaultAndSimulationProceedsNormally()
    {
        var buffer = new InputBuffer();
        var recognized = new List<int>();
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10),
            frameCommitted: (input, _) => recognized.Add(input.Frame));

        buffer.Sample(InputBits.Right, InputBits.None);
        session.StepExact(5, inputSource: buffer);

        Assert.False(session.IsFaulted);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, recognized);
        Assert.Equal(5, session.CurrentState.Frame);
        Assert.True(session.IsFrameSyncIntact);
    }
}
