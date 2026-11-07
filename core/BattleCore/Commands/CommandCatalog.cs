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

    /// <summary>[4]6A（后蓄前 + 轻拳）。</summary>
    public const string ChargeBackForwardLightPunch = "46A";

    /// <summary>[2]8B（下蓄上 + 重拳）。</summary>
    public const string ChargeDownUpHeavyPunch = "28B";

    /// <summary>[4]6C（后蓄前 + 轻脚）。</summary>
    public const string ChargeBackForwardLightKick = "46C";

    /// <summary>↓↘→↓↘→ + A（双重波动 / 超必杀）。</summary>
    public const string DoubleQuarterCircleForwardLightPunch = "236236A";

    /// <summary>↓↙←↓↙← + B（双重反波动 / 超必杀）。</summary>
    public const string DoubleQuarterCircleBackHeavyPunch = "214214B";

    /// <summary>→↓↘→↓↘ + A（双重升龙 / 超必杀）。</summary>
    public const string DoubleDragonPunchLightPunch = "623623A";

    /// <summary>蓄力指令默认保持帧数（60Hz 下 30 帧 ≈ 0.5 秒）。</summary>
    public const int DefaultChargeHoldFrames = 30;

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

        // ── 蓄力指令（03B-2）────────────────────────────────────
        // [4]6A：后蓄 DefaultChargeHoldFrames 帧 → 前 + A。
        // 蓄力只认纯方向（斜方向打断蓄力）。序列跨度上限放宽，容纳蓄力保持时长。
        new CommandDefinition(
            ChargeBackForwardLightPunch,
            new[]
            {
                CommandDirection.Back,
                CommandDirection.Forward,
            },
            InputBits.LightPunch,
            maxSequenceFrames: 60,
            buttonToleranceFrames: 3,
            priority: 30,
            bufferLifetimeFrames: 8,
            chargeHoldFrames: DefaultChargeHoldFrames),

        // [2]8B：下蓄 → 上 + B。
        new CommandDefinition(
            ChargeDownUpHeavyPunch,
            new[]
            {
                CommandDirection.Down,
                CommandDirection.Up,
            },
            InputBits.HeavyPunch,
            maxSequenceFrames: 60,
            buttonToleranceFrames: 3,
            priority: 30,
            bufferLifetimeFrames: 8,
            chargeHoldFrames: DefaultChargeHoldFrames),

        // [4]6C：与 [4]6A 同模板，不同攻击键。
        new CommandDefinition(
            ChargeBackForwardLightKick,
            new[]
            {
                CommandDirection.Back,
                CommandDirection.Forward,
            },
            InputBits.LightKick,
            maxSequenceFrames: 60,
            buttonToleranceFrames: 3,
            priority: 30,
            bufferLifetimeFrames: 8,
            chargeHoldFrames: DefaultChargeHoldFrames),

        // ── 双重方向 / 超必杀指令（03B-2）──────────────────────
        // 236236A：↓↘→↓↘→ + A。优先级高于单段 236A，序列长度也作为次级键。
        new CommandDefinition(
            DoubleQuarterCircleForwardLightPunch,
            new[]
            {
                CommandDirection.Down,
                CommandDirection.DownForward,
                CommandDirection.Forward,
                CommandDirection.Down,
                CommandDirection.DownForward,
                CommandDirection.Forward,
            },
            InputBits.LightPunch,
            maxSequenceFrames: 40,
            buttonToleranceFrames: 3,
            priority: 15,
            bufferLifetimeFrames: 8),

        // 214214B：↓↙←↓↙← + B。
        new CommandDefinition(
            DoubleQuarterCircleBackHeavyPunch,
            new[]
            {
                CommandDirection.Down,
                CommandDirection.DownBack,
                CommandDirection.Back,
                CommandDirection.Down,
                CommandDirection.DownBack,
                CommandDirection.Back,
            },
            InputBits.HeavyPunch,
            maxSequenceFrames: 40,
            buttonToleranceFrames: 3,
            priority: 15,
            bufferLifetimeFrames: 8),

        // 623623A：→↓↘→↓↘ + A。
        new CommandDefinition(
            DoubleDragonPunchLightPunch,
            new[]
            {
                CommandDirection.Forward,
                CommandDirection.Down,
                CommandDirection.DownForward,
                CommandDirection.Forward,
                CommandDirection.Down,
                CommandDirection.DownForward,
            },
            InputBits.LightPunch,
            maxSequenceFrames: 40,
            buttonToleranceFrames: 3,
            priority: 25,
            bufferLifetimeFrames: 8),
    };
}
