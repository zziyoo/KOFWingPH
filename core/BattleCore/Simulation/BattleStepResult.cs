namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// 单次 <c>Step()</c> 的结果。不可变。
/// 同时返回“推进后的新状态”和“本帧发生了什么”的描述，便于表现层与测试消费。
/// </summary>
public sealed record BattleStepResult
{
    /// <summary>推进前的逻辑帧号。</summary>
    public int FrameBefore { get; }

    /// <summary>推进后的逻辑帧号。</summary>
    public int FrameAfter { get; }

    /// <summary>本帧实际提交的输入。</summary>
    public Input.FrameInput Input { get; }

    /// <summary>推进后的权威状态。与传入状态不是同一实例。</summary>
    public State.BattleState State { get; }

    public BattleStepResult(int frameBefore, int frameAfter, Input.FrameInput input, State.BattleState state)
    {
        FrameBefore = frameBefore;
        FrameAfter = frameAfter;
        Input = input;
        State = state;
    }

    /// <summary>本次 Step 是否恰好推进了一个逻辑帧。</summary>
    public bool AdvancedExactlyOneFrame => FrameAfter == FrameBefore + 1;
}
