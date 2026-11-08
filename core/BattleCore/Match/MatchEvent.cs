using NewKOF.BattleCore.Simulation;

namespace NewKOF.BattleCore.Match;

/// <summary>
/// <b>比赛级事件身份</b>（阶段 09A，D104）：<c>(RoundNumber, EventId)</c> 组合。
///
/// <para><see cref="BattleEvent.EventId"/> = <c>Frame × 16 + seq</c> 只在<b>单个回合内</b>唯一
/// （D82）。方案一让每个回合拥有独立的 <c>BattleSession</c>、Frame 从 0 开始，因此不同回合
/// 的 EventId <b>会重复</b>。跨回合消费者必须用本组合身份去重，绝不能把裸 EventId
/// 当作全比赛唯一键。</para>
/// </summary>
public readonly record struct MatchEventId(int RoundNumber, int EventId)
{
    /// <summary>可读的唯一键（诊断/日志用，不参与权威判定）。</summary>
    public override string ToString() => $"R{RoundNumber}:{EventId}";
}

/// <summary>一条带回合身份的战斗事件（由比赛层在帧提交后包装，表现层只读）。</summary>
public readonly record struct MatchEvent(int RoundNumber, BattleEvent RoundEvent)
{
    /// <summary>跨回合唯一的事件身份。</summary>
    public MatchEventId Identity => new(RoundNumber, RoundEvent.EventId);

    /// <summary>事件类型（便于表现层直接筛选）。</summary>
    public BattleEventType Type => RoundEvent.Type;
}
