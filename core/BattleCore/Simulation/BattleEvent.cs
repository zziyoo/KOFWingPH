namespace NewKOF.BattleCore.Simulation;

/// <summary>战斗事件类型（阶段 06C，D82）。</summary>
public enum BattleEventType
{
    /// <summary>一名角色消费指令并发动招式（进入 Startup）。</summary>
    MoveStarted = 0,

    /// <summary>
    /// 攻击实例进入生效帧：<b>本帧确实有攻击框生效</b>（结算前视图，同帧被打断也照发）。
    /// 每次发动只发一次——多攻击定义错开生效窗口时，取首个进入生效窗口的帧。
    /// Amount = 0。
    /// </summary>
    AttackActive = 1,

    /// <summary>命中：Amount = 实扣伤害（已应用 max(0, …) 钳制）。DamageApplied 并入本类型。</summary>
    Hit = 2,

    /// <summary>被防御：不掉血，Amount = 防御硬直帧数。</summary>
    Blocked = 3,

    /// <summary>受击硬直开始：Amount = 硬直帧数。</summary>
    HitstunStarted = 4,

    /// <summary>防御硬直开始：Amount = 硬直帧数。</summary>
    BlockstunStarted = 5,

    /// <summary>
    /// 单方 KO（阶段 07A，D89）：Source = 胜者 ID，Target = 被击倒者 ID，Amount = 0。
    /// 每个回合至多一次，与 <see cref="RoundEnded"/> 同帧产生。
    /// </summary>
    Knockout = 6,

    /// <summary>
    /// 双方同帧归零（阶段 07A，D89）：Source = 0，Target = 1（双方均被击倒），Amount = 0。
    /// 与 <see cref="Knockout"/> 互斥——双 KO 不会产生 Knockout 事件。
    /// </summary>
    DoubleKnockout = 7,

    /// <summary>
    /// 回合结束（阶段 07A，D89）：Amount = <see cref="State.RoundOutcome"/> 的整数值
    /// （1 = P1Win、2 = P2Win、3 = DoubleKO），是判断胜负的唯一事件依据。
    /// Source = 胜者 ID（DoubleKO 时为 -1，无单一胜者），Target = -1。
    /// 每个回合恰好一次，只在结束时的那一帧出现。
    /// </summary>
    RoundEnded = 8,

    /// <summary>
    /// 投技实例进入生效帧（阶段 08B，D97）：Source = 投技者，Target = 目标，
    /// MoveId = 招式 ID，Amount = 0。每次发动只发一次（与 <see cref="AttackActive"/> 同构）。
    /// 它的存在本身就是「投技挥空/未命中」的确定性证据——命中另有 <see cref="ThrowHit"/>。
    /// </summary>
    ThrowAttempt = 9,

    /// <summary>
    /// 投技抓取成功（阶段 08B，D97）：Source = 投技者，Target = 被投者，
    /// MoveId = 招式 ID，Amount = <b>实扣伤害</b>（与 <see cref="Hit"/> 同语义）。
    /// </summary>
    ThrowHit = 10,

    /// <summary>
    /// 双方同帧有效投技互相冲突（阶段 08B，D97）：<b>两边都不成立</b>、都不造成伤害。
    /// Source = 0，Target = 1，MoveId = null，Amount = 0。每帧至多一次。
    /// </summary>
    ThrowClash = 11,

    /// <summary>
    /// 攻击相杀（阶段 08C，D98）：双方攻击框正面积重叠、且双方都<b>没有</b>打中对方受击框
    /// （攻击在空中对撞）。Source = 0，Target = 1，MoveId = null，Amount = 0。每帧至多一次。
    /// 两边攻击都中断进入 Recovery，不产生伤害。
    /// </summary>
    StrikeClash = 12,
}

/// <summary>
/// 一条确定性战斗事件（阶段 06C，D82）。不可变值类型。
///
/// <para><b>EventId（D82）</b>：= Frame × 16 + 帧内序号（0 起始）——同初态同输入必得
/// 相同 EventId 与事件顺序；绝不使用 Guid/时钟/随机数/渲染帧号。
/// 每帧事件数硬上限 16（超出显式抛异常，防静默碰撞）；帧号上限见
/// <see cref="MaxFrameForEventIds"/>（int 空间约 25.9 天连续对局，超出同样显式失败）。</para>
///
/// <para><b>帧内排序（D82）</b>：按权威阶段固定——① MoveStarted（攻击者 ID 升序）；
/// ② AttackActive（攻击者 ID 升序）；③ 逐被击者（ID 升序）：Hit → HitstunStarted
/// 或 Blocked → BlockstunStarted；④ 回合终局（D89）：Knockout / DoubleKnockout → RoundEnded。</para>
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

    /// <summary>
    /// EventId 可用的最大逻辑帧号：<c>MaxFrameForEventIds × 16 + 15 ≤ int.MaxValue</c>。
    ///
    /// <para>int 空间在 60Hz 下可覆盖约 <b>25.9 天</b>的连续对局（134,217,727 帧），
    /// 单场对局远不会触达；超出时 <c>BattleSimulation</c> 显式抛异常，
    /// 绝不静默回绕产生与历史帧重复的 EventId。若未来需要跨场次长期累积 ID，
    /// 应整体升级为 long（含公开接口与测试），而不是只改一个字段。</para>
    /// </summary>
    public const int MaxFrameForEventIds = int.MaxValue / MaxEventsPerFrame;
}
