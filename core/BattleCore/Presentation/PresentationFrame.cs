using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Presentation;

/// <summary>
/// <b>一个已提交逻辑帧的表现快照</b>（阶段 10 最终补修，D127）。
///
/// <para><b>为什么需要它</b>：<c>LastEvents</c> 只代表<b>最近一次</b>逻辑帧的事件。
/// 低渲染帧率下 <c>Advance()</c> 一次可能提交多个逻辑帧；若命中发生在较早的帧、
/// 后续帧又没有事件，表现层只会读到最后的空列表——伤害已经结算，火花却丢了。
/// 事件位置同理：用最终状态取坐标会把火花画在击退之后的位置上。</para>
///
/// <para><b>唯一数据来源</b>：本类型只由比赛层在「帧完整提交之后」创建
/// （<see cref="Match.MatchSession"/> / <see cref="Team.TeamMatchSession"/>），
/// 携带该帧<b>提交前后</b>的权威状态与该帧<b>全部</b>权威事件。
/// 它不参与任何判定，也不允许被表现层写回——单向数据流：
/// <c>权威战斗状态 → 逐帧表现快照 → 动画与特效</c>。</para>
///
/// <para><b>帧号语义</b>：<see cref="LogicFrame"/> 是<b>被推进的那一帧</b>
/// （= <c>StateBefore.Frame</c> = 该帧事件的 <see cref="BattleEvent.Frame"/>），
/// 而 <c>StateAfter.Frame == LogicFrame + 1</c>。事件发生时的角色坐标取
/// <see cref="StateAfter"/>：该帧的移动与结算（含投技位移）已经应用，
/// 正是这一帧画面上角色所在的位置；后续帧的击退不会回流到本快照。</para>
/// </summary>
public readonly record struct PresentationFrame
{
    /// <summary>回合号（1v1）或交锋号（3v3）；与 <see cref="BattleEvent.EventId"/> 组成事件唯一身份。</summary>
    public int Scope { get; }

    /// <summary>被推进的逻辑帧号（= 帧开始状态帧号 = 该帧事件的 Frame）。</summary>
    public int LogicFrame { get; }

    /// <summary>该帧开始前的权威状态（含「本帧生效中的 Hitstop 剩余帧数」，供表现时钟判定冻结）。</summary>
    public BattleState StateBefore { get; }

    /// <summary>该帧提交后的权威状态（事件发生位置的唯一依据）。</summary>
    public BattleState StateAfter { get; }

    /// <summary>该帧的<b>全部</b>权威事件（只读、帧内顺序固定；无事件时为空列表，绝不为 null）。</summary>
    public IReadOnlyList<BattleEvent> Events { get; }

    /// <summary>实体移除前采集的位置，按事件身份关联，仅供表现。</summary>
    public IReadOnlyList<EventAnchor> EventAnchors { get; }

    /// <summary>该帧是否产生了事件。</summary>
    public bool HasEvents => Events.Count > 0;

    /// <param name="scope">回合号 / 交锋号，必须 ≥ 1。</param>
    /// <param name="logicFrame">被推进的逻辑帧号，必须 ≥ 0。</param>
    /// <param name="stateBefore">帧开始状态；<c>Frame</c> 必须等于 <paramref name="logicFrame"/>。</param>
    /// <param name="stateAfter">帧提交后状态；<c>Frame</c> 必须等于 <paramref name="logicFrame"/> + 1。</param>
    /// <param name="events">该帧事件；null 视为空。每个事件的 <c>Frame</c> 必须等于 <paramref name="logicFrame"/>。</param>
    /// <exception cref="ArgumentNullException">状态为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">scope / logicFrame 越界。</exception>
    /// <exception cref="ArgumentException">帧号或事件帧号与快照不一致（装配错误，立即失败而不是静默接受）。</exception>
    public PresentationFrame(
        int scope,
        int logicFrame,
        BattleState stateBefore,
        BattleState stateAfter,
        IReadOnlyList<BattleEvent>? events = null,
        IReadOnlyList<EventAnchor>? eventAnchors = null)
    {
        ArgumentNullException.ThrowIfNull(stateBefore);
        ArgumentNullException.ThrowIfNull(stateAfter);
        if (scope < 1)
            throw new ArgumentOutOfRangeException(nameof(scope), scope,
                "回合/交锋号从 1 开始（事件身份必须可区分不同回合的同号 EventId）。");
        if (logicFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(logicFrame), logicFrame, "逻辑帧号不能为负。");
        if (stateBefore.Frame != logicFrame)
            throw new ArgumentException(
                $"帧开始状态的 Frame({stateBefore.Frame}) 与快照逻辑帧({logicFrame}) 不一致。",
                nameof(stateBefore));
        if (stateAfter.Frame != logicFrame + 1)
            throw new ArgumentException(
                $"帧提交后状态的 Frame({stateAfter.Frame}) 必须是逻辑帧 + 1（{logicFrame + 1}）。",
                nameof(stateAfter));

        IReadOnlyList<BattleEvent> list = events ?? Array.Empty<BattleEvent>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Frame != logicFrame)
                throw new ArgumentException(
                    $"第 {i} 条事件的 Frame({list[i].Frame}) 与快照逻辑帧({logicFrame}) 不一致" +
                    "（事件必须来自同一逻辑帧的权威结算）。",
                    nameof(events));
        }

        EventAnchors = Array.AsReadOnly((eventAnchors ?? Array.Empty<EventAnchor>()).ToArray());
        Scope = scope;
        LogicFrame = logicFrame;
        StateBefore = stateBefore;
        StateAfter = stateAfter;
        Events = list;
    }
}
