using NewKOF.BattleCore.Simulation;

namespace NewKOF.BattleCore.Team;

/// <summary>
/// <b>队伍赛事件身份</b>（阶段 10A，D120）：<c>(BoutNumber, EventId)</c>。
///
/// <para><see cref="BattleEvent.EventId"/> = <c>Frame × 16 + seq</c> 只在<b>单场交锋内</b>唯一
/// （D82）。每场交锋都重建 <c>BattleSession</c>、Frame 从 0 开始，因此不同交锋的 EventId
/// <b>会重复</b>。跨交锋消费者必须用本组合身份去重，不能把裸 EventId 当全场唯一键。</para>
/// </summary>
public readonly record struct TeamMatchEventId(int BoutNumber, int EventId)
{
    /// <summary>可读唯一键（诊断/日志用，不参与权威判定）。</summary>
    public override string ToString() => $"B{BoutNumber}:{EventId}";
}

/// <summary>一条带交锋身份的队伍赛事件（由比赛层在帧提交后包装，表现层只读）。</summary>
public readonly record struct TeamMatchEvent(int BoutNumber, BattleEvent BoutEvent)
{
    /// <summary>跨交锋唯一的事件身份。</summary>
    public TeamMatchEventId Identity => new(BoutNumber, BoutEvent.EventId);

    /// <summary>事件类型（便于表现层直接筛选）。</summary>
    public BattleEventType Type => BoutEvent.Type;
}
