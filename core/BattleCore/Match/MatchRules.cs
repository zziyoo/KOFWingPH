namespace NewKOF.BattleCore.Match;

/// <summary>
/// 一场比赛的<b>权威规则</b>（阶段 09A，D104）。纯 C# 数据，构造期校验、不可变。
///
/// <para>当前是 1v1「先胜 N 回合」的原型比赛模式，<b>不是</b>拳皇正式的 3v3 队伍赛。
/// 规则只存在于 BattleCore：Godot 不得用临时变量决定胜负或计时长度。</para>
///
/// <para><b>默认演示规则</b>：先取得 2 个回合胜场者赢得比赛、最多 5 回合、每回合 99 秒
/// （99 × 60 = 5940 逻辑帧）、启用超时裁决、允许单回合平局（DoubleKO / TimeOverDraw
/// 不加胜场但计入已进行回合数）。</para>
/// </summary>
public sealed class MatchRules
{
    /// <summary>回合数硬上限（防止规则配置导致比赛无限进行）。</summary>
    public const int MaxRoundsAllowed = 15;

    /// <summary>单回合计时上限（秒）。</summary>
    public const int MaxRoundTimeSeconds = 999;

    /// <param name="roundsToWin">先取得多少个回合胜场即赢得比赛（≥ 1）。</param>
    /// <param name="maxRounds">最多进行多少个回合（≥ RoundsToWin，≤ MaxRoundsAllowed）。</param>
    /// <param name="roundTimeSeconds">单回合计时秒数（1..999；按 60Hz 换算成逻辑帧）。</param>
    /// <param name="enableTimeout">是否启用回合计时与 Time Over 裁决；false = 不启用时钟。</param>
    /// <exception cref="ArgumentOutOfRangeException">任何数值越界或组合非法。</exception>
    public MatchRules(
        int roundsToWin = 2,
        int maxRounds = 5,
        int roundTimeSeconds = 99,
        bool enableTimeout = true)
    {
        if (roundsToWin < 1 || roundsToWin > MaxRoundsAllowed)
            throw new ArgumentOutOfRangeException(nameof(roundsToWin), roundsToWin,
                $"夺冠所需胜场必须在 1..{MaxRoundsAllowed} 内。");
        if (maxRounds < roundsToWin || maxRounds > MaxRoundsAllowed)
            throw new ArgumentOutOfRangeException(nameof(maxRounds), maxRounds,
                $"最大回合数必须 ≥ RoundsToWin({roundsToWin}) 且 ≤ {MaxRoundsAllowed}。");
        if (roundTimeSeconds < 1 || roundTimeSeconds > MaxRoundTimeSeconds)
            throw new ArgumentOutOfRangeException(nameof(roundTimeSeconds), roundTimeSeconds,
                $"回合计时秒数必须在 1..{MaxRoundTimeSeconds} 内。");

        RoundsToWin = roundsToWin;
        MaxRounds = maxRounds;
        RoundTimeSeconds = roundTimeSeconds;
        EnableTimeout = enableTimeout;
    }

    /// <summary>先取得多少个回合胜场即赢得比赛。</summary>
    public int RoundsToWin { get; }

    /// <summary>最多进行的回合数；达到后按累计胜场决定比赛结果（相同则平局）。</summary>
    public int MaxRounds { get; }

    /// <summary>单回合计时秒数。</summary>
    public int RoundTimeSeconds { get; }

    /// <summary>是否启用回合计时与 Time Over 裁决。</summary>
    public bool EnableTimeout { get; }

    /// <summary>
    /// 单回合初始计时逻辑帧数 = 秒数 × 60（<see cref="BattleCoreInfo.LogicFrameRate"/>）。
    /// 未启用超时时为 0，表示「不维护权威时钟」。
    /// </summary>
    public int RoundTimerFrames => EnableTimeout
        ? RoundTimeSeconds * BattleCoreInfo.LogicFrameRate
        : 0;

    /// <summary>默认演示规则（2 胜 / 最多 5 回合 / 99 秒 / 启用超时）。</summary>
    public static MatchRules CreateDefault() => new();
}
