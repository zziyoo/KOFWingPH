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
/// 不变量（Invariant）：
/// - <see cref="CurrentState"/>.Frame 恒等于 <see cref="FixedStepScheduler.TotalSteps"/>
///   加上初始帧号。初始帧号由 <see cref="BattleState.CreateDefault"/> 决定（0）。
/// - 每次推进前，喂给 <c>Step()</c> 的 <see cref="FrameInput.Frame"/> 恒等于当前状态帧号。
/// - 不推进时（暂停/欠帧不足）绝不调用 <c>Step()</c>。
/// </summary>
public sealed class BattleSession
{
    private readonly BattleSimulation _simulation = new();

    /// <summary>逻辑帧调度器。</summary>
    public FixedStepScheduler Scheduler { get; }

    /// <summary>当前权威状态。外部只读；更新只能经由 <see cref="Advance"/>。</summary>
    public BattleState CurrentState { get; private set; }

    public BattleSession(BattleState? initialState = null, FixedStepScheduler? scheduler = null)
    {
        CurrentState = initialState ?? BattleState.CreateDefault();
        Scheduler = scheduler ?? new FixedStepScheduler();
    }

    /// <summary>
    /// 用渲染帧耗时推进，并返回本次实际推进后的状态。
    /// 输入由 <paramref name="inputProvider"/> 按"即将推进的帧号"提供，
    /// 从而保证输入帧号与状态帧号严格对应。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧耗时（秒）。</param>
    /// <param name="inputProvider">
    /// 输入提供者：入参为即将推进的逻辑帧号，返回该帧输入。
    /// 传 null 表示双方无输入。
    /// </param>
    /// <returns>本次推进的逻辑帧数。</returns>
    public int Advance(double deltaSeconds, Func<int, FrameInput>? inputProvider = null)
    {
        int steps = Scheduler.Advance(deltaSeconds);

        for (int i = 0; i < steps; i++)
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
        }

        return steps;
    }

    /// <summary>显式推进指定帧数（测试与工具使用），不受时间调度影响。</summary>
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
            CurrentState = _simulation.Step(CurrentState, input).State;
        }
    }
}
