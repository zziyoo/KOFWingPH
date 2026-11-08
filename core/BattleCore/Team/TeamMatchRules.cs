using NewKOF.BattleCore;

namespace NewKOF.BattleCore.Team;

/// <summary>
/// <b>队伍赛规则</b>（阶段 10A，D120）：只描述一场「交锋（bout）」的计时规则。
/// 队伍人数由 <see cref="TeamDefinition"/> 决定，本类不重复假定 3 人。
///
/// <para>与 1v1 的 <c>MatchRules</c> 刻意分离：1v1 是「先胜 N 回合」，
/// 队伍赛是「按序接替直到一方耗尽」，两套语义混在一起会让计分与终局互相污染。</para>
/// </summary>
public sealed class TeamMatchRules : IEquatable<TeamMatchRules>
{
    public const int MaxRoundTimeSeconds = 999;

    /// <param name="roundTimeSeconds">每场交锋的计时秒数（1..999；按 60Hz 换算成逻辑帧）。</param>
    /// <param name="enableTimeout">是否启用计时与 Time Over 裁决；false = 不启用时钟。</param>
    public TeamMatchRules(int roundTimeSeconds = 99, bool enableTimeout = true)
    {
        if (roundTimeSeconds < 1 || roundTimeSeconds > MaxRoundTimeSeconds)
            throw new ArgumentOutOfRangeException(nameof(roundTimeSeconds), roundTimeSeconds,
                $"交锋计时秒数必须在 1..{MaxRoundTimeSeconds} 内。");

        RoundTimeSeconds = roundTimeSeconds;
        EnableTimeout = enableTimeout;
    }

    /// <summary>每场交锋的计时秒数。</summary>
    public int RoundTimeSeconds { get; }

    /// <summary>是否启用交锋计时与 Time Over 裁决。</summary>
    public bool EnableTimeout { get; }

    /// <summary>每场交锋的逻辑帧数（未启用计时为 0）。</summary>
    public int BoutTimerFrames =>
        EnableTimeout ? RoundTimeSeconds * BattleCoreInfo.LogicFrameRate : 0;

    /// <summary>默认演示规则：99 秒、启用超时。</summary>
    public static TeamMatchRules CreateDefault() => new();

    public bool Equals(TeamMatchRules? other) =>
        other is not null
        && RoundTimeSeconds == other.RoundTimeSeconds
        && EnableTimeout == other.EnableTimeout;

    public override bool Equals(object? obj) => obj is TeamMatchRules other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RoundTimeSeconds, EnableTimeout);

    public override string ToString() =>
        $"{(EnableTimeout ? $"{RoundTimeSeconds}s" : "no-timer")}";
}
