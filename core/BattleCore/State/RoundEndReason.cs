namespace NewKOF.BattleCore.State;

/// <summary>
/// 回合<b>结束原因</b>（阶段 09A 引入，09B 接入内核结算）。
///
/// <para>与 <see cref="RoundOutcome"/> 正交：Outcome 回答「谁赢了」，Reason 回答「为什么结束」。
/// 这样 09B 的 Time Over 胜利不会被伪装成 Knockout 事件，D89 原有的 KO / DoubleKO 语义
/// 也不需要改写（旧枚举值 0..3 保持原义）。</para>
/// </summary>
public enum RoundEndReason
{
    /// <summary>回合尚未结束（仅 <see cref="RoundPhase.InProgress"/> 合法）。</summary>
    None = 0,

    /// <summary>一方体力归零（D89 KO）。</summary>
    Knockout = 1,

    /// <summary>双方同帧体力归零（D89 DoubleKO）。</summary>
    DoubleKO = 2,

    /// <summary>时间耗尽后按剩余体力比例裁决（阶段 09B）。</summary>
    TimeOver = 3,

    /// <summary>时间耗尽且双方体力比例完全相同（阶段 09B）。</summary>
    TimeOverDraw = 4,
}
