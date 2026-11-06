using System.Reflection;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 02 补修的「额外核对」测试：
/// <list type="bullet">
/// <item><c>DroppedStepCount</c> 语义 = 被舍弃的<b>时间预算</b>，不是已执行的逻辑帧。</item>
/// <item>超大但有限的 delta 不会造成计数溢出或长时间卡死。</item>
/// <item>核心帧同步不变量 <see cref="BattleSession.IsFrameSyncIntact"/> 在各种路径下成立。</item>
/// </list>
/// </summary>
public class SchedulerAccountingTests
{
    [Fact]
    public void DroppedStepCount_RepresentsDiscardedTimeBudget_NotExecutedFrames()
    {
        // 单次 1 秒、上限 5 帧：执行 5 帧，其余 ~55 帧的时间预算被丢弃。
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 5);
        int executed = scheduler.Advance(1.0);
        scheduler.CommitStep(executed);

        Assert.Equal(5, executed);
        Assert.Equal(5, scheduler.TotalSteps);          // 真实执行帧数
        Assert.True(scheduler.DroppedStepCount >= 54);   // 被舍弃的时间预算

        // 语义区分：丢弃数不计入已执行帧数
        Assert.NotEqual(scheduler.TotalSteps, scheduler.DroppedStepCount);

        // 时间预算守恒：已执行 + 丢弃 ≈ 总投入时间 / 步长（1 秒 = 60 帧）
        long accounted = scheduler.TotalSteps + scheduler.DroppedStepCount;
        Assert.InRange(accounted, 59, 60);
    }

    [Fact]
    public void DroppedStepCount_IsZero_WhenNoBacklogOrFailure()
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 10));
        for (int i = 0; i < 600; i++)
            session.Advance(FixedStepScheduler.FixedStepSeconds);

        Assert.Equal(600, session.CurrentState.Frame);
        Assert.Equal(600, session.Scheduler.TotalSteps);
        Assert.Equal(0, session.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void DroppedStepCount_IncludesUncommittedBudgetAfterException()
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 4));

        Assert.Throws<InvalidOperationException>(() =>
            session.Advance(4 * FixedStepScheduler.FixedStepSeconds, frame =>
                frame == 1
                    ? throw new InvalidOperationException("第二帧故障")
                    : FrameInput.Empty(frame)));

        // 第 0 帧提交，第 1~3 帧的预算被记为丢弃
        Assert.Equal(1, session.Scheduler.TotalSteps);
        Assert.Equal(3, session.Scheduler.DroppedStepCount);
    }

    [Fact]
    public void Advance_VeryLargeFiniteDelta_DoesNotOverflow()
    {
        // 上限很小，避免真的循环几百万次；重点是超大 delta 不产生异常/溢出。
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 8);

        int steps = scheduler.Advance(double.MaxValue / 4.0);
        scheduler.CommitStep(steps);

        Assert.Equal(8, steps);
        Assert.Equal(8, scheduler.TotalSteps);
        Assert.True(scheduler.DroppedStepCount > 0);
        // 累加器必须被收敛回合法范围，不能残留巨大值
        Assert.True(scheduler.AccumulatedSeconds >= 0);
        Assert.True(scheduler.AccumulatedSeconds < FixedStepScheduler.FixedStepSeconds);
    }

    [Fact]
    public void Advance_LargeButReasonableDelta_OneHour_DoesNotOverflow()
    {
        // 1 小时 = 3600 秒 = 216000 帧；上限放大以便全部执行。
        var scheduler = new FixedStepScheduler(maxStepsPerAdvance: 300_000);
        int steps = scheduler.Advance(3600.0);
        scheduler.CommitStep(steps);

        Assert.Equal(216000, steps);
        Assert.Equal(216000, scheduler.TotalSteps);
        Assert.Equal(0, scheduler.DroppedStepCount);
    }

    [Fact]
    public void LongRunningSimulation_DoesNotOverflowCounters()
    {
        // 模拟 24 小时 @ 60Hz = 5,184,000 帧，验证 long 计数安全。
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1000));
        const int rendersPerSecond = 60;
        const int seconds = 3600; // 先用 1 小时做实际规模的循环（24 小时太慢）
        for (int i = 0; i < rendersPerSecond * seconds; i++)
            session.Advance(1.0 / rendersPerSecond);

        Assert.Equal(216_000, session.CurrentState.Frame);
        Assert.Equal(216_000, session.Scheduler.TotalSteps);
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void IsFrameSyncIntact_TrueForAllNormalPaths()
    {
        var session = new BattleSession();
        Assert.True(session.IsFrameSyncIntact);

        session.StepExact(7);
        Assert.True(session.IsFrameSyncIntact);

        session.Advance(FixedStepScheduler.FixedStepSeconds);
        Assert.True(session.IsFrameSyncIntact);

        session.Scheduler.Pause();
        session.Advance(FixedStepScheduler.FixedStepSeconds);
        Assert.True(session.IsFrameSyncIntact);

        session.StepExact(3);
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void IsFrameSyncIntact_StillTrueAfterException()
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 3));

        Assert.Throws<InvalidOperationException>(() =>
            session.Advance(3 * FixedStepScheduler.FixedStepSeconds, frame =>
                frame == 1 ? throw new InvalidOperationException("故障") : FrameInput.Empty(frame)));

        // 关键：异常后不变量依然成立，不存在半提交状态
        Assert.True(session.IsFrameSyncIntact);
        Assert.Equal(session.CurrentState.Frame, session.Scheduler.TotalSteps);
    }

    [Fact]
    public void IsFrameSyncIntact_WithNonZeroInitialFrame()
    {
        // 初始帧号非 0 时，不变量按「初始帧号 + TotalSteps」计算
        var custom = new State.BattleState(
            frame: 100,
            fighters: new[]
            {
                State.FighterState.Create(0, 1000, -120, State.Facing.Right),
                State.FighterState.Create(1, 1000, 120, State.Facing.Left),
            },
            stateVersion: 1);

        var session = new BattleSession(initialState: custom);
        Assert.True(session.IsFrameSyncIntact);

        session.StepExact(5);
        Assert.Equal(105, session.CurrentState.Frame);
        Assert.Equal(5, session.Scheduler.TotalSteps);
        Assert.True(session.IsFrameSyncIntact);
    }

    [Fact]
    public void CommitStep_RejectsNegative()
    {
        var scheduler = new FixedStepScheduler();
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.CommitStep(-1));
    }

    [Fact]
    public void RecordDroppedSteps_RejectsNegative()
    {
        var scheduler = new FixedStepScheduler();
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.RecordDroppedSteps(-1));
    }

    [Fact]
    public void Advance_ReturnValueIsBudget_NotAutoCommitted()
    {
        // 明确契约：Advance 返回预算但不再自行记账。
        var scheduler = new FixedStepScheduler();
        int budget = scheduler.Advance(2 * FixedStepScheduler.FixedStepSeconds);

        Assert.Equal(2, budget);
        Assert.Equal(0, scheduler.TotalSteps); // 未提交前不计

        scheduler.CommitStep(budget);
        Assert.Equal(2, scheduler.TotalSteps);
    }

    /// <summary>
    /// 记账方法不得出现在公开 API 上。
    ///
    /// 若外部程序集能调用 <c>CommitStep()</c> / <c>RecordDroppedSteps()</c>，
    /// 就能让 <c>TotalSteps</c> 跑在权威状态前面（或伪造丢弃预算），
    /// 直接破坏 <see cref="BattleSession.IsFrameSyncIntact"/> 所表达的不变量。
    /// 源码中它们已改为 <c>internal</c>，这里用反射把该约束固定下来，
    /// 防止后续有人为了方便又改回 public。
    /// </summary>
    [Fact]
    public void AccountingMutators_AreNotPartOfPublicApi()
    {
        Type type = typeof(FixedStepScheduler);

        Assert.Null(type.GetMethod("CommitStep", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(type.GetMethod("RecordDroppedSteps", BindingFlags.Public | BindingFlags.Instance));

        // 仍应存在 internal 版本，供 BattleSession 与测试使用。
        Assert.NotNull(type.GetMethod("CommitStep", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("RecordDroppedSteps", BindingFlags.NonPublic | BindingFlags.Instance));
    }
}
