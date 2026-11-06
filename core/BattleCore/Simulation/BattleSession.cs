using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// 把「调度器 → 模拟 → 状态」绑定在一起的会话对象。
///
/// 存在意义：
/// <see cref="FixedStepScheduler"/> 只回答"该推进几帧"，而"推到哪一帧、用哪一帧的输入"
/// 必须与 <see cref="BattleState.Frame"/> 严格一致。若把这个配对逻辑散落在表现层，
/// 容易出现"输入用了旧帧号"或"状态推进了但输入没跟上"的错误。
/// 本类把这条不变量收进内核，并可被纯 C# 测试覆盖。
///
/// <para>
/// <b>不变量（Invariant）—— 阶段 02 补修后：</b>
/// </para>
/// <list type="number">
/// <item><c>CurrentState.Frame == Scheduler.TotalSteps</c>（初始帧号为 0 时）。
///       两条推进路径（<see cref="Advance"/> 与 <see cref="StepExact"/>）
///       都必须维持该等式。</item>
/// <item>每次推进前，喂给 <c>Step()</c> 的 <see cref="FrameInput.Frame"/> 恒等于当前状态帧号。</item>
/// <item><b>提交一帧 → 记账一次</b>：只有 <c>Step()</c> 成功返回后才调用
///       <see cref="FixedStepScheduler.CommitStep"/>。任何异常都不会让
///       <c>TotalSteps</c> 超过真实提交的帧数。</item>
/// <item>不推进时（暂停 / 欠帧不足）绝不调用 <c>Step()</c>。</item>
/// </list>
///
/// <para>
/// <b>异常策略（阶段 02 补修明确）：失败即终止本次调度批次，不回滚、不重试、不忽略。</b>
/// </para>
/// <list type="bullet">
/// <item>异常会原样向上抛出（<b>绝不 catch 后吞掉</b>）。</item>
/// <item>本批次中已成功提交的帧<b>保留</b>；未提交的帧<b>不计入</b> <c>TotalSteps</c>。</item>
/// <item>已消耗但未提交的时间预算记为 <see cref="FixedStepScheduler.RecordDroppedSteps"/>，
///       使 <c>TotalSteps + DroppedStepCount</c> 仍能解释投入的时间。</item>
/// <item>会话<b>不会</b>停在"半提交"状态：<c>CurrentState.Frame == Scheduler.TotalSteps</c>
///       依然成立，调用方可以安全地继续推进或选择终止。</item>
/// <item>是否继续由调用方决定：表现层通常应暂停并报告错误，而不是静默继续。</item>
/// </list>
/// </summary>
public sealed class BattleSession
{
    private readonly BattleSimulation _simulation = new();
    private readonly int _initialFrame;

    /// <summary>逻辑帧调度器。</summary>
    public FixedStepScheduler Scheduler { get; }

    /// <summary>当前权威状态。外部只读；更新只能经由 <see cref="Advance"/> 或 <see cref="StepExact"/>。</summary>
    public BattleState CurrentState { get; private set; }

    public BattleSession(BattleState? initialState = null, FixedStepScheduler? scheduler = null)
    {
        CurrentState = initialState ?? BattleState.CreateDefault();
        Scheduler = scheduler ?? new FixedStepScheduler();
        _initialFrame = CurrentState.Frame;
    }

    /// <summary>
    /// 用渲染帧耗时推进，返回本次<b>实际提交</b>的逻辑帧数。
    ///
    /// <para>
    /// 输入由 <paramref name="inputProvider"/> 按"即将推进的帧号"提供，
    /// 从而保证输入帧号与状态帧号严格对应。
    /// </para>
    /// </summary>
    /// <param name="deltaSeconds">渲染帧耗时（秒）。</param>
    /// <param name="inputProvider">
    /// 输入提供者：入参为即将推进的逻辑帧号，返回该帧输入。
    /// 传 null 表示双方无输入。
    /// </param>
    /// <returns>本次实际提交的逻辑帧数（0 到 <see cref="FixedStepScheduler.MaxStepsPerAdvance"/>）。</returns>
    /// <exception cref="InvalidOperationException">
    /// 输入提供者返回的帧号与当前逻辑帧不一致。
    /// </exception>
    /// <remarks>
    /// 若 <paramref name="inputProvider"/> 或 <c>Step()</c> 抛出异常：
    /// 已提交的帧保留、未提交的帧不计入 <c>TotalSteps</c>，
    /// 剩余时间预算记为丢弃，异常原样向上抛出。详见类型注释的「异常策略」。
    /// </remarks>
    public int Advance(double deltaSeconds, Func<int, FrameInput>? inputProvider = null)
    {
        // 只取「允许推进几帧」的时间预算，此处不增加 TotalSteps。
        int budget = Scheduler.Advance(deltaSeconds);
        if (budget <= 0)
            return 0;

        int committed = 0;
        try
        {
            for (int i = 0; i < budget; i++)
            {
                int frame = CurrentState.Frame;
                FrameInput input = inputProvider is null
                    ? FrameInput.Empty(frame)
                    : inputProvider(frame);

                // 防御：输入提供者返回的帧号必须与当前帧一致，否则立即失败而不是静默错帧。
                if (input.Frame != frame)
                {
                    throw new InvalidOperationException(
                        $"输入提供者返回帧号({input.Frame})与当前逻辑帧({frame})不一致。");
                }

                CurrentState = _simulation.Step(CurrentState, input).State;

                // 只有真正提交后才记账，保证 TotalSteps 不会跑在状态前面。
                Scheduler.CommitStep();
                committed++;
            }
        }
        catch
        {
            // 未提交的剩余预算记为丢弃：这部分真实时间已经过去但不会被模拟。
            // 不吞异常、不回滚累加器（回滚会导致同一段时间被重复计费）。
            int uncommitted = budget - committed;
            if (uncommitted > 0)
                Scheduler.RecordDroppedSteps(uncommitted);

            throw;
        }

        return committed;
    }

    /// <summary>
    /// 显式推进指定帧数（测试、调试与工具使用），不受时间调度影响。
    ///
    /// <para>
    /// <b>与 <see cref="Advance"/> 共存的契约：</b>
    /// 两条路径共用同一个 <see cref="FixedStepScheduler.TotalSteps"/> 记账，
    /// 因此可以任意交替使用，帧号关系始终满足
    /// <c>CurrentState.Frame == Scheduler.TotalSteps</c>。
    /// </para>
    /// <para>
    /// <b>暂停下的行为：</b>暂停只阻止「时间驱动」推进（<see cref="Advance"/>）。
    /// 显式单步是显式请求，<b>在暂停状态下依然生效</b>，这是调试逐帧检查所需的语义。
    /// </para>
    /// </summary>
    /// <param name="count">要推进的帧数，必须非负。</param>
    /// <param name="inputProvider">同 <see cref="Advance"/>。</param>
    /// <exception cref="ArgumentOutOfRangeException">count 为负。</exception>
    public void StepExact(int count, Func<int, FrameInput>? inputProvider = null)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "步数不能为负。");

        for (int i = 0; i < count; i++)
        {
            int frame = CurrentState.Frame;
            FrameInput input = inputProvider is null
                ? FrameInput.Empty(frame)
                : inputProvider(frame);

            if (input.Frame != frame)
            {
                throw new InvalidOperationException(
                    $"输入提供者返回帧号({input.Frame})与当前逻辑帧({frame})不一致。");
            }

            CurrentState = _simulation.Step(CurrentState, input).State;

            // 与 Advance 共用同一记账路径，保证两种推进方式不会错位。
            Scheduler.CommitStep();
        }
    }

    /// <summary>
    /// 校验核心不变量是否成立：<c>CurrentState.Frame - 初始帧号 == Scheduler.TotalSteps</c>。
    /// 供测试与调试断言使用，不改变任何状态。
    /// </summary>
    public bool IsFrameSyncIntact => CurrentState.Frame - _initialFrame == Scheduler.TotalSteps;
}
