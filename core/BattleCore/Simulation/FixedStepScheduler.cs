namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// 固定步长逻辑帧调度器 —— **纯 C#，零 Godot 依赖，可独立单元测试**。
///
/// 解决的问题（阶段 01 实测出的缺陷）：
/// 1. <b>精度漂移</b>：1/60 秒 = 16666.666... 微秒，无法用整数微秒精确表示。
///    旧实现用 <c>(int)(delta*1e6)</c> 截断 + 整数间隔 <c>1000000/60</c>，
///    截断误差与间隔误差在不同渲染帧率下彼此抵消或叠加，
///    导致"逻辑帧总数随渲染帧率变化"（实测 240FPS 跑 600 秒少 5 帧）。
/// 2. <b>时间源换成秒（double）累加</b>：把每次渲染的真实 delta（秒）累加到
///    double 累加器，再用 <see cref="FixedStepSeconds"/> 作为步长判定。
/// 3. <b>帧积压上限</b>：卡顿后若一次性补上所有欠帧会造成死亡螺旋。
///    超出 <see cref="MaxStepsPerAdvance"/> 的欠帧计入 <see cref="DroppedStepCount"/>。
/// 4. <b>显式暂停</b>：<see cref="IsPaused"/> 为 true 时 <see cref="Advance"/>
///    不推进任何逻辑帧，也不累积欠帧时间。
///
/// <para>
/// <b>阶段 02 补修（帧同步）—— 记账职责分离：</b>
/// </para>
/// <para>
/// 旧实现中 <see cref="Advance"/> 在返回预算前就把 <see cref="TotalSteps"/> 加好，
/// 一旦调用方执行中途抛异常，调度器计数就跑在权威状态前面，留下半提交状态。现在：
/// </para>
/// <list type="bullet">
/// <item><see cref="Advance"/> 只<b>消耗时间预算</b>并返回「允许推进几帧」，
///       它<b>不</b>增加 <see cref="TotalSteps"/>。</item>
/// <item>调用方每真正提交一帧，必须调用 <see cref="CommitStep"/> 记账。</item>
/// <item><see cref="BattleSession"/> 是唯一合法调用方，保证「提交一帧 → 记账一次」严格配对。</item>
/// </list>
/// <para>
/// <b>为什么不回滚累加器：</b>异常后把已消耗的步长退回去，会让同一段真实时间在下一帧
/// 被重复计费，从而在长时间跑动中引入新漂移。丢弃预算只会让逻辑时间略微落后
/// （最多 <see cref="MaxStepsPerAdvance"/> 帧），且可由 <see cref="DroppedStepCount"/> 观测。
/// </para>
///
/// <para>
/// <b>阶段 02 补修第二轮 —— 数值健壮性：</b>
/// </para>
/// <list type="bullet">
/// <item><b>超大 delta 分块</b>：单次巨大的 delta（如真实时间被挂起数分钟）若直接
///       用「循环减步长」处理，一方面循环次数可能是天文数字，另一方面连续减法会
///       累积浮点残差导致少算 1 帧。<see cref="Advance"/> 因此先用
///       <see cref="double.Truncate"/> 整除算出「整帧数」与「余数」，
///       只在余数上做循环，把复杂度从 O(帧数) 降到常数级。</item>
/// <item><b>溢出保护</b>：整帧数在转为 <c>long</c> 前必须夹紧到安全范围，
///       避免 <c>(long)</c> 对超出范围的 <c>double</c> 产生未定义结果
///       （实测会得到 <see cref="long.MaxValue"/>，进而让丢弃计数溢出为负、
///       累加器变成 NaN）。</item>
/// </list>
///
/// 精度策略说明：
/// 调度器只决定"该推进几帧"，不参与任何战斗数值计算。核心权威状态全部为 int。
/// </summary>
public sealed class FixedStepScheduler
{
    /// <summary>标准逻辑帧率（Hz）。</summary>
    public const int DefaultTicksPerSecond = 60;

    /// <summary>
    /// 单个逻辑帧的时长（秒）。使用 double 除法而非整数微秒，
    /// 避免 16666us 与真实 16666.666...us 的系统性偏差。
    /// </summary>
    public static readonly double FixedStepSeconds = 1.0 / DefaultTicksPerSecond;

    /// <summary>
    /// 单次 <see cref="Advance"/> 最多推进的逻辑帧数（默认 = 1 秒的量）。
    /// 防止卡顿后一次性补帧过多。超出部分记录到 <see cref="DroppedStepCount"/>。
    /// </summary>
    public int MaxStepsPerAdvance { get; }

    /// <summary>
    /// 累计<b>已提交</b>的逻辑帧总数。单调递增。
    /// 只在 <see cref="CommitStep"/> 中增加，因此恒等于真实执行过的模拟帧数。
    /// </summary>
    public long TotalSteps { get; private set; }

    /// <summary>
    /// 累计被舍弃的<b>模拟时间预算</b>（以逻辑帧为单位）。
    ///
    /// <para>
    /// <b>语义（阶段 02 补修明确）：本计数记录的是「因超过
    /// <see cref="MaxStepsPerAdvance"/> 或因执行异常而被丢弃的时间预算」，
    /// 不代表已经执行过的逻辑帧。</b>真实执行帧数见 <see cref="TotalSteps"/>。
    /// </para>
    /// <para>
    /// 满足 <c>TotalSteps + DroppedStepCount ≈ 累计投入时间 / FixedStepSeconds</c>
    /// （浮点舍入范围内）。
    /// </para>
    /// </summary>
    public long DroppedStepCount { get; private set; }

    /// <summary>当前累加器余量（秒），范围 [0, <see cref="FixedStepSeconds"/>)。</summary>
    public double AccumulatedSeconds => _accumulator;

    /// <summary>是否暂停。暂停期间 Advance 不推进、不累积。</summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// 整帧数转 long 的安全上界。远小于 <see cref="long.MaxValue"/>，
    /// 保证后续 <c>DroppedStepCount += pending</c> 不会溢出。
    /// </summary>
    private const double MaxSafeWholeSteps = 1e15;

    private double _accumulator;

    public FixedStepScheduler(int maxStepsPerAdvance = DefaultTicksPerSecond, bool paused = false)
    {
        if (maxStepsPerAdvance <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxStepsPerAdvance), maxStepsPerAdvance,
                "单次最多推进帧数必须为正。");
        MaxStepsPerAdvance = maxStepsPerAdvance;
        IsPaused = paused;
    }

    /// <summary>暂停逻辑帧推进。可重复调用。</summary>
    public void Pause() => IsPaused = true;

    /// <summary>恢复逻辑帧推进。恢复后不会补上暂停期间的时间。</summary>
    public void Resume() => IsPaused = false;

    /// <summary>
    /// 用一帧渲染的真实耗时（秒）推进时间预算，返回本帧<b>允许</b>推进的逻辑帧数。
    ///
    /// <para>
    /// <b>注意：本方法不增加 <see cref="TotalSteps"/>。</b>
    /// 调用方必须为返回的每一帧实际提交调用一次 <see cref="CommitStep"/>。
    /// </para>
    /// </summary>
    /// <param name="deltaSeconds">本次渲染帧的耗时（秒）。必须为有限非负数。</param>
    /// <returns>本帧允许执行的逻辑帧步数，范围 [0, <see cref="MaxStepsPerAdvance"/>]。</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// deltaSeconds 为 NaN、无穷大或负数。
    /// </exception>
    public int Advance(double deltaSeconds)
    {
        if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds,
                "渲染帧耗时必须是有限数值。");
        if (deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds,
                "渲染帧耗时不能为负。");

        if (IsPaused)
            return 0;

        _accumulator += deltaSeconds;
        if (double.IsInfinity(_accumulator))
        {
            // 累加器自身溢出（极端连续大 delta）：钳制到安全值，
            // 超出部分按丢弃预算记账，绝不产生 NaN。
            double clamped = MaxSafeWholeSteps * FixedStepSeconds;
            double overflowFrames = double.IsInfinity(_accumulator)
                ? MaxSafeWholeSteps
                : (_accumulator - clamped) / FixedStepSeconds;
            long dropped = ToSafeLong(overflowFrames);
            if (dropped > 0)
                SafeAddDropped(dropped);
            _accumulator = clamped;
        }

        // 浮点安全余量：避免 0.9999999 个步长因舍入被判为不足一帧。
        double epsilon = FixedStepSeconds * 1e-9;

        // ── 用整除一次算出整帧数与余数，避免「循环减步长」的 O(帧数) 复杂度
        //    和连续减法累积的浮点残差（实测单次 3600s 会少算 1 帧）。
        double wholeAndFraction = (_accumulator + epsilon) / FixedStepSeconds;

        // 夹紧到安全范围后再转 long，避免 (long) 对超大 double 的未定义结果。
        double safeWhole = wholeAndFraction > MaxSafeWholeSteps
            ? double.PositiveInfinity
            : Math.Floor(wholeAndFraction);

        long availableWhole;
        if (double.IsInfinity(safeWhole) || double.IsNaN(safeWhole))
        {
            // 帧数超出可表示范围：全部按丢弃处理，只保留最大允许执行的量。
            availableWhole = long.MaxValue / 4;
        }
        else
        {
            availableWhole = ToSafeLong(safeWhole);
        }

        if (availableWhole < 0)
            availableWhole = 0;

        int steps = (int)Math.Min(availableWhole, MaxStepsPerAdvance);

        if (steps > 0)
        {
            // 精确扣除 steps 个步长；用乘法而非循环减法，减少浮点残差累积。
            _accumulator -= steps * FixedStepSeconds;
            if (_accumulator < 0) _accumulator = 0;

            // 若扣除后仍剩余整帧（说明超过上限），把剩余整帧记为丢弃预算。
            long remaining = availableWhole - steps;
            if (remaining > 0)
            {
                SafeAddDropped(remaining);
                // 只保留不足一帧的余数，防止下一帧继续无限积压。
                double leftover = _accumulator - remaining * FixedStepSeconds;
                _accumulator = leftover > 0 ? leftover : 0;
            }
        }

        // 最终保证累加器落在合法区间 [0, FixedStepSeconds)。
        if (double.IsNaN(_accumulator) || _accumulator < 0)
            _accumulator = 0;

        // 若余量已接近或超过一个完整步长（超大 delta 的浮点残留），
        // 收敛到 0 并把该整帧记为丢弃，避免它在下一帧被重复计入。
        if (_accumulator >= FixedStepSeconds * (1 - 1e-6))
        {
            if (_accumulator >= FixedStepSeconds)
                SafeAddDropped(1);
            _accumulator = 0;
        }

        return steps;
    }

    /// <summary>
    /// 为一帧<b>已成功提交</b>的模拟记账，<see cref="TotalSteps"/> 加一。
    /// 只应由 <see cref="BattleSession"/> 在 <c>Step()</c> 返回后调用。
    /// </summary>
    /// <remarks>
    /// <b>internal 而非 public</b>：若外部程序集能直接调用本方法，
    /// 就能让 <see cref="TotalSteps"/> 跑到权威状态前面，破坏帧同步不变量。
    /// 权限收窄到程序集内部 + 测试程序集（<c>InternalsVisibleTo</c>）。
    /// </remarks>
    /// <param name="count">本次提交的帧数，默认 1。</param>
    /// <exception cref="ArgumentOutOfRangeException">count 为负数。</exception>
    internal void CommitStep(int count = 1)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "提交帧数不能为负。");
        TotalSteps += count;
    }

    /// <summary>
    /// 把已消耗但未提交的<b>时间预算</b>记为丢弃，用于执行异常后的显式记账。
    ///
    /// <para>
    /// 语义：这部分时间已经过去但不会被模拟，属于「丢失的模拟时间」，
    /// 与 <see cref="TotalSteps"/> 无关。累加器本身<b>不回滚</b>——
    /// 回滚会让同一段真实时间被重复计费，引入新的漂移。
    /// </para>
    /// </summary>
    /// <param name="count">要记为丢弃的帧数预算，必须非负。</param>
    /// <exception cref="ArgumentOutOfRangeException">count 为负数。</exception>
    /// <remarks>
    /// <b>internal 而非 public</b>：与 <see cref="CommitStep"/> 同理，
    /// 外部可调用即可随意伪造丢弃计数，让时间预算账目失去可信度。
    /// </remarks>
    internal void RecordDroppedSteps(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "丢弃帧数不能为负。");
        SafeAddDropped(count);
    }

    /// <summary>把 double 安全转成 long，超出范围时饱和而非产生未定义值。</summary>
    private static long ToSafeLong(double value)
    {
        if (double.IsNaN(value))
            return 0;
        if (value >= MaxSafeWholeSteps)
            return (long)MaxSafeWholeSteps;
        if (value <= 0)
            return 0;
        return (long)value;
    }

    /// <summary>饱和加法，防止 DroppedStepCount 溢出为负。</summary>
    private void SafeAddDropped(long amount)
    {
        if (amount <= 0)
            return;
        if (DroppedStepCount > long.MaxValue - amount)
            DroppedStepCount = long.MaxValue;
        else
            DroppedStepCount += amount;
    }
}
