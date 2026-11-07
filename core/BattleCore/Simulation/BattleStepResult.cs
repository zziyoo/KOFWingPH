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

    /// <summary>
    /// 本帧的攻击接触候选（阶段 06A，D76：几何层「攻击框 ∩ 受击框」）。
    /// 只读；无接触时为共享空列表。06A 仅作确定性诊断输出——
    /// 命中/防御/伤害结算是 06B 的权威规则，重复命中抑制属于 06C。
    /// </summary>
    public IReadOnlyList<Combat.AttackContact> Contacts { get; }

    /// <summary>
    /// 本帧的权威战斗事件（阶段 06C，D82/D83）。只读；无事件时为共享空列表。
    /// 由 Step 的权威结算产生、与 State 同生同灭——重放同一输入必得相同事件序列；
    /// BattleSession 在帧完整提交后才转交观察者（D83）。
    /// </summary>
    public IReadOnlyList<BattleEvent> Events { get; }

    public BattleStepResult(
        int frameBefore,
        int frameAfter,
        Input.FrameInput input,
        State.BattleState state,
        IReadOnlyList<Combat.AttackContact>? contacts = null,
        IReadOnlyList<BattleEvent>? events = null)
    {
        FrameBefore = frameBefore;
        FrameAfter = frameAfter;
        Input = input;
        State = state;
        Contacts = contacts ?? Array.Empty<Combat.AttackContact>();
        Events = events ?? Array.Empty<BattleEvent>();
    }

    /// <summary>本次 Step 是否恰好推进了一个逻辑帧。</summary>
    public bool AdvancedExactlyOneFrame => FrameAfter == FrameBefore + 1;
}
