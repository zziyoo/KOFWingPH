using NewKOF.BattleCore.Simulation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// Godot 层集成契约测试（任务 2 的诚实边界说明）。
///
/// ⚠️ 重要声明：
/// 纯 C# 测试工程**无法实例化** Godot 节点（<c>BattleScene : Node2D</c> 需要
/// Godot 运行时与 ScriptHost）。因此下面的测试**不是**"Godot 调度已通过测试"，
/// 而是验证 <c>BattleScene._Process</c> 唯一依赖的那个内核行为契约：
///
///   <c>BattleScene._Process(delta)</c> 的实现是：
///       <c>int steps = _session.Advance(delta, inputProvider: null);</c>
///       若 steps &gt; 0 则同步画面。
///
/// 所以只要 <see cref="BattleSession.Advance(double, Func{int, NewKOF.BattleCore.Input.FrameInput}?)"/>
/// 在任意 delta 序列下满足下面的契约，Godot 层的帧号推进就是正确的
/// （Godot 层没有其它时间逻辑，已核对源码）。
///
/// Godot 层的真实运行验证由 <c>docs/PROGRESS.md</c> 记录的
/// "HUD 帧号实测 + 图形窗口启动" 完成，不在此处伪造。
/// </summary>
public class GodotIntegrationContractTests
{
    /// <summary>
    /// 契约 1：<c>BattleScene</c> 传入的 delta 序列（任意帧率、任意抖动）
    /// 都不能让逻辑帧号出现"跳号"或"倒退"。
    /// </summary>
    [Fact]
    public void Session_UnderIrregularDeltas_NeverSkipsOrRegressesFrames()
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 5));
        int previousFrame = session.CurrentState.Frame;

        // 模拟真实渲染抖动：帧耗时在 4ms ~ 40ms 间随机
        var rng = new Random(12345);
        for (int i = 0; i < 5000; i++)
        {
            double delta = 0.004 + rng.NextDouble() * 0.036;
            session.Advance(delta);

            int frame = session.CurrentState.Frame;
            Assert.True(frame >= previousFrame, $"帧号倒退：{previousFrame} -> {frame}");
            Assert.True(frame - previousFrame <= 5,
                $"单次推进超过补帧上限：{previousFrame} -> {frame}");
            previousFrame = frame;
        }
    }

    /// <summary>
    /// 契约 2：两次渲染之间推进的帧号差值，必须等于
    /// <see cref="FixedStepScheduler.Advance"/> 的返回值。
    /// 这正是 <c>BattleScene</c> 判断"是否需要重绘"的依据。
    /// </summary>
    [Fact]
    public void AdvanceReturnValue_EqualsFrameDelta()
    {
        var session = new BattleSession();

        for (int i = 0; i < 500; i++)
        {
            int before = session.CurrentState.Frame;
            int steps = session.Advance(1.0 / 60.0);
            int after = session.CurrentState.Frame;

            Assert.Equal(steps, after - before);
        }
    }

    /// <summary>
    /// 契约 3：<c>BattleScene</c> 不在 <c>_Process</c> 里写权威状态。
    /// 内核层面可验证的部分：状态只能由 Advance 改变，
    /// 外部无法通过公开 API 篡改 <see cref="BattleSession.CurrentState"/>。
    /// </summary>
    [Fact]
    public void CurrentState_CanOnlyChangeThroughAdvance()
    {
        var session = new BattleSession();
        var snapshot = session.CurrentState;

        // CurrentState 是只读属性。注意：BattleSession 不是 record，
        // CurrentState 声明为 { get; private set; }，因此公开 setter 必须不存在。
        var prop = typeof(BattleSession).GetProperty(nameof(BattleSession.CurrentState));
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);

        // private set 会产生非 public 的 setter，但外部不可调用。
        // 这里确认 setter（若存在）不是 public。
        var setter = prop.GetSetMethod(nonPublic: false);
        Assert.Null(setter);

        Assert.Same(snapshot, session.CurrentState);
    }

    /// <summary>
    /// 契约 4：长时间运行（模拟 10 分钟）帧号无漂移。
    /// </summary>
    [Theory]
    [InlineData(60.0)]
    [InlineData(144.0)]
    [InlineData(240.0)]
    public void TenMinuteRun_HasZeroDrift(double fps)
    {
        var session = new BattleSession(scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1000));
        int renders = (int)Math.Round(fps * 600.0);
        double delta = 1.0 / fps;

        for (int i = 0; i < renders; i++)
            session.Advance(delta);

        Assert.Equal(36000, session.CurrentState.Frame);
        Assert.Equal(0, session.Scheduler.DroppedStepCount);
    }
}
