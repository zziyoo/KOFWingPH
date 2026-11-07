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
/// <item><b>输入与战斗状态一致提交</b>：使用 <see cref="IInputSource"/> 时，
///       顺序恒为「准备输入 → 模拟出候选状态 → 提交输入 → 落权威状态 → 帧记账」。
///       <b>候选状态在输入提交成功之前不会赋给 <c>CurrentState</c></b>，
///       因此模拟失败或提交失败都不会让状态/计数跑在输入前面。失败后这次准备的结果
///       被直接丢弃：不写输入历史、不消费按下边沿，重试同一帧仍是同一输入。
///       普通 <see cref="Func{T,TResult}"/> 输入源无法回滚，故要求其对同一帧号幂等。</item>
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
    private readonly Action<FrameInput, BattleState>? _frameCommitted;

    /// <summary>逻辑帧调度器。</summary>
    public FixedStepScheduler Scheduler { get; }

    /// <summary>当前权威状态。外部只读；更新只能经由 <see cref="Advance"/> 或 <see cref="StepExact"/>。</summary>
    public BattleState CurrentState { get; private set; }

    /// <summary>
    /// 会话是否处于<b>故障封闭</b>状态。当提交后观察者（<see cref="_frameCommitted"/>）
    /// 抛异常时置为 <see langword="true"/>：该帧的战斗状态、输入历史、记账均已落定，
    /// 但<b>派生的指令识别状态可能没有完成该帧的观察</b>（观察者在识别器完整消费该帧
    /// 之前抛出）。此时不得继续正常模拟——继续推进会把下一帧误当成识别器的下一个历史帧，
    /// 造成派生状态静默漏帧。
    /// </summary>
    public bool IsFaulted { get; private set; }

    /// <summary>进入 <see cref="IsFaulted"/> 时的帧号（未故障时为 -1）。</summary>
    public int FaultedFrame { get; private set; } = -1;

    /// <param name="initialState">初始状态；null 表示 <see cref="BattleState.CreateDefault"/>。</param>
    /// <param name="scheduler">调度器；null 表示默认调度器。</param>
    /// <param name="frameCommitted">
    /// 可选提交观察者：每个逻辑帧<b>完整提交之后</b>（状态、输入历史、记账均已落定）调用一次。
    /// 入参为该帧输入与该帧<b>开始之前</b>的权威状态（<c>stateBefore</c>），
    /// 其中 <c>Facing</c> 是「本帧输入被消费时的朝向」——供指令识别等只读派生层使用。
    /// <b>不得抛异常</b>；若抛出，异常照常向上传播（已提交的帧与记账保持一致，不会回滚）。
    /// </param>
    public BattleSession(
        BattleState? initialState = null,
        FixedStepScheduler? scheduler = null,
        Action<FrameInput, BattleState>? frameCommitted = null)
    {
        CurrentState = initialState ?? BattleState.CreateDefault();
        Scheduler = scheduler ?? new FixedStepScheduler();
        _initialFrame = CurrentState.Frame;
        _frameCommitted = frameCommitted;
    }

    /// <summary>
    /// 用渲染帧耗时推进，返回本次<b>实际提交</b>的逻辑帧数。
    ///
    /// <para>
    /// 输入按"即将推进的帧号"提供，保证输入帧号与状态帧号严格对应。
    /// 两种输入来源二选一：
    /// <see cref="IInputSource"/>（可两阶段提交，推荐）或普通委托（无回滚能力）。
    /// </para>
    /// </summary>
    /// <param name="deltaSeconds">渲染帧耗时（秒）。</param>
    /// <param name="inputProvider">
    /// 普通输入委托：入参为即将推进的逻辑帧号，返回该帧输入。
    /// <b>契约：必须对同一帧号返回相同输入（幂等）</b>——模拟失败后会话会以同一帧号
    /// 再次调用它；会话无法回滚该委托的副作用，故不适合有状态的输入源。
    /// 传 null 表示双方无输入。
    /// </param>
    /// <param name="inputSource">
    /// 两阶段输入源（如 <see cref="InputBuffer"/>）。模拟成功后才提交输入，
    /// 失败时不会留下「输入已提交、战斗帧未提交」的幻影帧。与 <paramref name="inputProvider"/> 互斥。
    /// </param>
    /// <returns>本次实际提交的逻辑帧数（0 到 <see cref="FixedStepScheduler.MaxStepsPerAdvance"/>）。</returns>
    /// <exception cref="ArgumentException">同时传入两种输入来源。</exception>
    /// <exception cref="InvalidOperationException">
    /// 输入帧号与当前逻辑帧不一致。
    /// </exception>
    /// <remarks>
    /// 若输入来源或 <c>Step()</c> 抛出异常：
    /// 已提交的帧保留、未提交的帧不计入 <c>TotalSteps</c>，
    /// 剩余时间预算记为丢弃，异常原样向上抛出。详见类型注释的「异常策略」。
    /// </remarks>
    public int Advance(
        double deltaSeconds,
        Func<int, FrameInput>? inputProvider = null,
        IInputSource? inputSource = null)
    {
        ThrowIfFaulted();
        RejectTwoInputSources(inputProvider, inputSource);

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

                // 帧开始时的权威状态：含「当帧开始时的朝向」，供指令识别按正确朝向解析。
                // 输入在本帧开始被消费，因此朝向语义必须取帧开始（而不是帧结束）状态。
                BattleState stateBefore = CurrentState;

                FrameInput input = ResolveInput(frame, inputProvider, inputSource);

                // 防御：输入提供者返回的帧号必须与当前帧一致，否则立即失败而不是静默错帧。
                if (input.Frame != frame)
                {
                    throw new InvalidOperationException(
                        $"输入提供者返回帧号({input.Frame})与当前逻辑帧({frame})不一致。");
                }

                // 先算出候选状态但**不赋值**：Step() 或 Commit() 抛异常时，
                // 权威状态必须原地不动，否则会破坏 Frame - 初始帧号 == TotalSteps。
                BattleState nextState = _simulation.Step(CurrentState, input).State;

                // 模拟成功后才提交输入（写历史 + 消费边沿）。
                // Commit 失败时 nextState 被丢弃，状态与计数都不前进。
                inputSource?.Commit(input);

                CurrentState = nextState;

                // 只有真正提交后才记账，保证 TotalSteps 不会跑在状态前面。
                Scheduler.CommitStep();
                committed++;

                // 本帧已完整提交：通知只读观察者（如指令识别）。
                // 传入「帧开始状态」，其 Facing 是本帧输入被消费时的朝向。
                // 观察者异常单独捕获：战斗状态/记账已落定，但派生状态（识别器）可能
                // 未完成本帧观察 → 置故障封闭后照常向上抛，不吞异常。
                try
                {
                    _frameCommitted?.Invoke(input, stateBefore);
                }
                catch
                {
                    MarkFaulted(input.Frame);
                    throw;
                }
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
    /// <param name="inputProvider">同 <see cref="Advance"/>（幂等契约）。</param>
    /// <param name="inputSource">同 <see cref="Advance"/>；模拟成功后才提交输入。</param>
    /// <exception cref="ArgumentOutOfRangeException">count 为负。</exception>
    /// <exception cref="ArgumentException">同时传入两种输入来源。</exception>
    public void StepExact(
        int count,
        Func<int, FrameInput>? inputProvider = null,
        IInputSource? inputSource = null)
    {
        ThrowIfFaulted();

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "步数不能为负。");

        RejectTwoInputSources(inputProvider, inputSource);

        for (int i = 0; i < count; i++)
        {
            int frame = CurrentState.Frame;

            // 帧开始状态：供指令识别按「当帧开始朝向」解析（与 Advance 一致）。
            BattleState stateBefore = CurrentState;

            FrameInput input = ResolveInput(frame, inputProvider, inputSource);

            if (input.Frame != frame)
            {
                throw new InvalidOperationException(
                    $"输入提供者返回帧号({input.Frame})与当前逻辑帧({frame})不一致。");
            }

            // 与 Advance 共用同一提交顺序：候选状态 → 提交输入 → 落状态 → 记账。
            BattleState nextState = _simulation.Step(CurrentState, input).State;
            inputSource?.Commit(input);
            CurrentState = nextState;
            Scheduler.CommitStep();

            // 与 Advance 相同：完整提交后才通知观察者，传帧开始状态。
            // 观察者异常置故障封闭后照常向上抛。
            try
            {
                _frameCommitted?.Invoke(input, stateBefore);
            }
            catch
            {
                MarkFaulted(input.Frame);
                throw;
            }
        }
    }

    /// <summary>按优先级取输入：两阶段输入源 &gt; 普通委托 &gt; 空输入。</summary>
    private static FrameInput ResolveInput(
        int frame,
        Func<int, FrameInput>? inputProvider,
        IInputSource? inputSource)
    {
        if (inputSource is not null)
            return inputSource.Prepare(frame);

        return inputProvider is null ? FrameInput.Empty(frame) : inputProvider(frame);
    }

    private static void RejectTwoInputSources(Func<int, FrameInput>? inputProvider, IInputSource? inputSource)
    {
        if (inputProvider is not null && inputSource is not null)
            throw new ArgumentException(
                "inputProvider 与 inputSource 只能二选一：前者无法回滚，后者支持两阶段提交。");
    }

    /// <summary>
    /// 校验核心不变量是否成立：<c>CurrentState.Frame - 初始帧号 == Scheduler.TotalSteps</c>。
    /// 供测试与调试断言使用，不改变任何状态。
    /// </summary>
    public bool IsFrameSyncIntact => CurrentState.Frame - _initialFrame == Scheduler.TotalSteps;

    /// <summary>
    /// 清除故障封闭状态，允许继续模拟。调用方必须在<b>恢复派生状态与战斗状态的一致性</b>
    /// 之后才能调用——例如重建指令识别器，或把识别器重新同步到已提交帧。
    /// 本方法不负责自动重放漏帧：如何补上那帧的派生观察由调用方决定，
    /// 会话只保证恢复后从正确的下一帧继续，不再被故障锁死。
    /// </summary>
    public void Recover()
    {
        IsFaulted = false;
        FaultedFrame = -1;
    }

    /// <summary>在 Faulted 状态下拒绝继续模拟，防止派生状态静默漏帧。</summary>
    private void ThrowIfFaulted()
    {
        if (IsFaulted)
        {
            throw new InvalidOperationException(
                $"会话已进入故障封闭状态（帧 {FaultedFrame} 提交后观察者抛异常）。" +
                "必须先恢复派生状态的一致性并调用 Recover()，之后才能继续模拟。");
        }
    }

    /// <summary>记录观察者故障：进入故障封闭，标记故障帧。</summary>
    private void MarkFaulted(int frame)
    {
        IsFaulted = true;
        FaultedFrame = frame;
    }
}
