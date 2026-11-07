using NewKOF.BattleCore.Input;

namespace NewKOF.BattleCore.Commands;

/// <summary>
/// 默认指令表（阶段 03B-1）。全部通过 <see cref="CommandDefinition"/> 数据描述，
/// 识别器核心不认识任何具体招式。
///
/// <para><b>键位约定</b>（与 HUD 的 A/B/C/D 一致）：
/// A = 轻拳、B = 重拳、C = 轻脚、D = 重脚。</para>
///
/// <para><b>默认规则</b>：序列跨度 20 帧（≈333ms）、按键容差 3 帧、结果保留 8 帧。
/// 623 的优先级高于 236：两者在同一帧都可成立时（例如 →↓↘→ + A），
/// 选择升龙而不是波动。</para>
/// </summary>
public static class CommandCatalog
{
    /// <summary>↓↘→ + A（波动指令）。</summary>
    public const string QuarterCircleForwardLightPunch = "236A";

    /// <summary>→↓↘ + A（升龙指令）。</summary>
    public const string DragonPunchLightPunch = "623A";

    /// <summary>↓↙← + B（后半圆指令）。</summary>
    public const string QuarterCircleBackHeavyPunch = "214B";

    /// <summary>→→（双击前方向）。</summary>
    public const string DoubleTapForward = "66";

    /// <summary>构造默认指令表；每次返回新实例，调用方可自行增删。</summary>
    public static IReadOnlyList<CommandDefinition> CreateDefault() => new[]
    {
        new CommandDefinition(
            QuarterCircleForwardLightPunch,
            new[]
            {
                CommandDirection.Down,
                CommandDirection.DownForward,
                CommandDirection.Forward,
            },
            InputBits.LightPunch,
            maxSequenceFrames: 20,
            buttonToleranceFrames: 3,
            priority: 10,
            bufferLifetimeFrames: 8),

        new CommandDefinition(
            DragonPunchLightPunch,
            new[]
            {
                CommandDirection.Forward,
                CommandDirection.Down,
                CommandDirection.DownForward,
            },
            InputBits.LightPunch,
            maxSequenceFrames: 20,
            buttonToleranceFrames: 3,
            priority: 20,
            bufferLifetimeFrames: 8),

        new CommandDefinition(
            QuarterCircleBackHeavyPunch,
            new[]
            {
                CommandDirection.Down,
                CommandDirection.DownBack,
                CommandDirection.Back,
            },
            InputBits.HeavyPunch,
            maxSequenceFrames: 20,
            buttonToleranceFrames: 3,
            priority: 10,
            bufferLifetimeFrames: 8),

        // 双击前：前 → 中立 → 前。中立是独立条目，因此「按住前不放」不会误触发。
        new CommandDefinition(
            DoubleTapForward,
            new[]
            {
                CommandDirection.Forward,
                CommandDirection.Neutral,
                CommandDirection.Forward,
            },
            InputBits.None,
            maxSequenceFrames: 18,
            buttonToleranceFrames: 0,
            priority: 0,
            bufferLifetimeFrames: 8),
    };
}
