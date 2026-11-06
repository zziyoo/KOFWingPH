namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// 固定步长逻辑帧调度器 —— **纯 C#，零 Godot 依赖，可独立单元测试**。
///
/// 解决的问题（阶段 02 实测出的阶段 01 缺陷）：
/// 1. <b>精度漂移</b>：1/60 秒 = 16666.666... 微秒，无法用整数微秒精确表示。
///    旧实现用 <c>(int)(delta*1e6)</c> 截断 + 整数间隔 <c>1000000/60</c>，
///    截断误差与间隔误差在不同渲染帧率下彼此抵消或叠加，
///    导致"逻辑帧总数随渲染帧率变化"（实测 240FPS 跑 600 秒少 5 帧）。
/// 2. <b>时间源换成秒（double）累加</b>：本类不使用"每帧微秒整数累加再整除"，
///    而是把每次渲染的真实 delta（秒）累加到 double 累加器，再用
///    <see cref="FixedStepSeconds"/> 作为步长判定。误差只来自 double 的
///    53 位尾数（约 1e-16 秒量级），且<b>不随渲染帧率系统性累积</b>。
/// 3. <b>帧积压上限</b>：卡顿/断点/调试暂停后，若一次性补上所有欠帧会造成
///    "补帧越补越慢"的死亡螺旋。本类提供 <see cref="MaxStepsPerAdvance"/>，
///    超出上限的欠帧被计入 <see cref="DroppedStepCount"/> 并<b>显式暴露</b>，
///    绝不静默丢弃。
/// 4. <b>显式暂停</b>：<see cref="IsPaused"/> 为 true 时 <see cref="Advance"/>
///    不推进任何逻辑帧，也不累积欠帧时间。
///
/// 精度策略说明：
/// 使用 double 秒而非整数微秒，是因为 double 能精确表示 1/60 的整数倍序列
/// presentable 的误差远小于整数微秒截断。核心权威状态本身仍然全部为 int，
/// 调度器只决定"该推进几帧"，不参与任何战斗数值计算。
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

    /// <summary>累计推进的逻辑帧总数。单调递增。</summary>
    public long TotalSteps { get; private set; }

    /// <summary>因超过 <see cref="MaxStepsPerAdvance"/> 而未被推进的欠帧数（累计）。</summary>
    public long DroppedStepCount { get; private set; }

    /// <summary>当前累加器余量（秒），范围 [0, <see cref="FixedStepSeconds"/>)。</summary>
    public double AccumulatedSeconds => _accumulator;

    /// <summary>是否暂停。暂停期间 Advance 不推进、不累积。</summary>
    public bool IsPaused { get; private set; }

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
    /// 用一帧渲染的真实耗时（秒）推进调度器，返回本帧应当推进的逻辑帧数。
    /// </summary>
    /// <param name="deltaSeconds">本次渲染帧的耗时（秒）。必须为有限非负数。</param>
    /// <returns>本帧应执行的逻辑帧步数，范围 [0, <see cref="MaxStepsPerAdvance"/>]。</returns>
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

        // 浮点安全余量：避免 0.9999999 个步长因舍入被判为不足一帧。
        double epsilon = FixedStepSeconds * 1e-9;

        int steps = 0;
        while (_accumulator + epsilon >= FixedStepSeconds)
        {
            if (steps >= MaxStepsPerAdvance)
            {
                // 明确记录被丢弃的欠帧数，绝不静默吞掉。
                // 把累加器收敛到上限内，避免下一帧继续无限积压。
                long pending = (long)Math.Floor((_accumulator + epsilon) / FixedStepSeconds);
                if (pending > 0)
                {
                    DroppedStepCount += pending;
                    _accumulator -= pending * FixedStepSeconds;
                    if (_accumulator < 0) _accumulator = 0;
                }
                break;
            }

            _accumulator -= FixedStepSeconds;
            _accumulator = Math.Max(0, _accumulator);
            steps++;
            TotalSteps++;
        }

        return steps;
    }
}
