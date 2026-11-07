using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 阶段 04 的<b>演示角色配置</b>（测试与 Godot 验收共用）。
///
/// <para>
/// 数值只是原型帧数据，不代表最终拳皇角色。两名角色刻意展示 B4 的全部要点：</para>
/// <list type="bullet">
/// <item>Kai 与 Rei 拥有<b>不同</b>的指令集合与招式表；</item>
/// <item>双方都有 <c>236A</c> 指令，但映射到<b>不同</b>的 MoveId 与不同阶段帧数
///       （同一 CommandId、不同招式）；</item>
/// <item>Kai 独有 <c>623A</c> 与普通按键招式 <c>5A</c>（站立无方向 + 轻拳，
///       用于验证非复杂指令的状态切换）；Rei 独有 <c>214B</c>。</item>
/// </list>
/// </summary>
public static class DemoRoster
{
    /// <summary>Kai 的 236A（波动原型）。</summary>
    public const string FireballPrototype = "FireballPrototype";

    /// <summary>Kai 的 623A（升龙原型）。</summary>
    public const string DragonPunchPrototype = "DragonPunchPrototype";

    /// <summary>Kai 的站立轻拳（5A：无方向 + A）。</summary>
    public const string StandLightPunch = "StandLightPunch";

    /// <summary>Rei 的 236A（同指令不同招式）。</summary>
    public const string BackDraft = "BackDraft";

    /// <summary>Rei 的 214B（扫腿原型）。</summary>
    public const string SweepPrototype = "SweepPrototype";

    /// <summary>角色甲「Kai」的配置。</summary>
    public static FighterDefinition CreateKai() => new(
        "Kai",
        new[]
        {
            new CommandDefinition(
                "236A",
                new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
                InputBits.LightPunch,
                maxSequenceFrames: 20, buttonToleranceFrames: 3, priority: 10),
            new CommandDefinition(
                "623A",
                new[] { CommandDirection.Forward, CommandDirection.Down, CommandDirection.DownForward },
                InputBits.LightPunch,
                maxSequenceFrames: 20, buttonToleranceFrames: 3, priority: 20),
            // 5A：站立轻拳 = 裸按键指令（D56，空方向序列 + A）——不要求任何方向，
            // 站立不动按 A 即触发（按住不重复、旧边沿不复活）。
            // 任何带方向的其他指令（如 236A）同帧竞争时按优先级胜出。
            new CommandDefinition(
                "5A",
                Array.Empty<CommandDirection>(),
                InputBits.LightPunch,
                priority: 5),
        },
        new[]
        {
            new MoveDefinition(FireballPrototype, "236A",
                startupFrames: 4, activeFrames: 3, recoveryFrames: 8),
            new MoveDefinition(DragonPunchPrototype, "623A",
                startupFrames: 3, activeFrames: 5, recoveryFrames: 10),
            new MoveDefinition(StandLightPunch, "5A",
                startupFrames: 3, activeFrames: 2, recoveryFrames: 6,
                allowedFrom: MoveAllowedStates.Standing),
        });

    /// <summary>角色乙「Rei」的配置。</summary>
    public static FighterDefinition CreateRei() => new(
        "Rei",
        new[]
        {
            new CommandDefinition(
                "236A",
                new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
                InputBits.LightPunch,
                maxSequenceFrames: 20, buttonToleranceFrames: 3, priority: 10),
            new CommandDefinition(
                "214B",
                new[] { CommandDirection.Down, CommandDirection.DownBack, CommandDirection.Back },
                InputBits.HeavyPunch,
                maxSequenceFrames: 20, buttonToleranceFrames: 3, priority: 10),
        },
        new[]
        {
            // 与 Kai 的 236A 同指令、不同招式、不同阶段帧数（B4.5）。
            new MoveDefinition(BackDraft, "236A",
                startupFrames: 5, activeFrames: 2, recoveryFrames: 7),
            new MoveDefinition(SweepPrototype, "214B",
                startupFrames: 3, activeFrames: 4, recoveryFrames: 9),
        });

    /// <summary>默认演示对局：Kai（P1）对 Rei（P2）。</summary>
    public static FightRoster CreateKaiVsRei() => new(CreateKai(), CreateRei());
}
