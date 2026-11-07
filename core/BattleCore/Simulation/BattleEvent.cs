namespace NewKOF.BattleCore.Simulation;

/// <summary>战斗事件类型（阶段 06C，D82）。</summary>
public enum BattleEventType
{
    /// <summary>一名角色消费指令并发动招式（进入 Startup）。</summary>
    MoveStarted = 0,

    /// <summary>攻击实例进入生效帧（该帧存在至少一个 Active 攻击框；每次发动只发一次）。</summary>
    AttackActive = 1,

    /// <summary>命中：Amount = 实扣伤害（已应用 max(0, …) 钳制）。DamageApplied 并入本类型。</summary>
    Hit = 2,

    /// <summary>被防御：不掉血，Amount = 防御硬直帧数。</summary>
    Blocked = 3,

    /// <summary>受击硬直开始：Amount = 硬直帧数。</summary>
    HitstunStarted = 4,

    /// <summary>防御硬直开始：Amount = 硬直帧数。</summary>
    BlockstunStarted = 5,
}

/// <summary>
/// 一条确定性战斗事件（阶段 06C，D82）。不可变值类型。
///
/// <para><b>EventId（D82）</b>：= Frame × 16 + 帧内序号（0 起始）——同初态同输入必得
/// 相同 EventId 与事件顺序；绝不使用 Guid/时钟/随机数/渲染帧号。
/// 每帧事件数硬上限 16（超出显式抛异常，防静默碰撞）。</para>
///
/// <para><b>帧内排序（D82）</b>：按权威阶段固定——① MoveStarted（攻击者 ID 升序）；
/// ② AttackActive（攻击者 ID 升序）；③ 逐被击者（ID 升序）：Hit → HitstunStarted
/// 或 Blocked → BlockstunStarted。</para>
///
/// <para><b>与状态提交的关系（D83）</b>：事件由 <c>BattleSimulation.Step</c> 的权威结算
/// 产生、随 <see cref="BattleStepResult"/> 返回；<c>BattleSession</c> 在该帧<b>完整提交后</b>
/// 才转交观察者——Step 失败或输入提交失败时该帧事件整体不存在，不存在「已发布但未提交」。
/// 事件不参与权威判定（不决定伤害是否发生），Godot 只消费。</para>
/// </summary>
public readonly record struct BattleEvent(
    int Frame,
    int EventId,
    BattleEventType Type,
    int SourceFighterId,
    int TargetFighterId,
    string? MoveId,
    int Amount)
{
    /// <summary>每帧事件数上限（EventId 帧内 4 位空间）。</summary>
    public const int MaxEventsPerFrame = 16;
}
