using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Numerics;
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
            // 5A：站立轻拳——短前向攻击框（Active 全程生效），Mid，随 06B 接入伤害。
            // 07B（D91）：命中确认后可取消到 236A（演示数据，窗口 = 自发动帧起算的 3..10 帧，
            // 覆盖 Active 与大部分 Recovery；确认必须来自更早帧）。
            new MoveDefinition(StandLightPunch, "5A",
                startupFrames: 3, activeFrames: 2, recoveryFrames: 6,
                allowedFrom: MoveAllowedStates.Standing,
                attacks: new[]
                {
                    new AttackDefinition(
                        offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(70),
                        halfWidth: FixedPoint.FromInt(20), halfHeight: FixedPoint.FromInt(20),
                        height: AttackHeight.Mid, damage: 30, hitstunFrames: 12, blockstunFrames: 8),
                },
                cancels: new[]
                {
                    new CancelDefinition(FireballPrototype,
                        windowFirstFrame: 3, windowLastFrame: 10,
                        MoveCancelPhases.Active | MoveCancelPhases.Recovery,
                        requiresHitConfirm: true),
                }),
            new MoveDefinition(FireballPrototype, "236A",
                startupFrames: 4, activeFrames: 3, recoveryFrames: 8,
                attacks: new[]
                {
                    new AttackDefinition(
                        offsetForward: FixedPoint.FromInt(50), offsetUp: FixedPoint.FromInt(60),
                        halfWidth: FixedPoint.FromInt(25), halfHeight: FixedPoint.FromInt(25),
                        height: AttackHeight.Mid, damage: 50, hitstunFrames: 16, blockstunFrames: 10),
                }),
            new MoveDefinition(DragonPunchPrototype, "623A",
                startupFrames: 3, activeFrames: 5, recoveryFrames: 10,
                attacks: new[]
                {
                    // 升龙：攻击框偏上（High，蹲防失败）。
                    new AttackDefinition(
                        offsetForward: FixedPoint.FromInt(35), offsetUp: FixedPoint.FromInt(95),
                        halfWidth: FixedPoint.FromInt(22), halfHeight: FixedPoint.FromInt(30),
                        height: AttackHeight.High, damage: 60, hitstunFrames: 18, blockstunFrames: 12),
                }),
        },
        // 运动配置（D64/D68）：Kai——前行 2.0 / 后撤 1.5 单位每帧；预备跳 3 帧；
        // 小跳初速 4.0（腾空 16 帧、最高 14）、大跳初速 6.5（腾空 26 帧、最高 39）、
        // 重力 0.5、水平跳速 3.0。
        movement: new MovementDefinition(
            walkForwardSpeed: FixedPoint.FromFraction(2, 1),
            walkBackwardSpeed: FixedPoint.FromFraction(3, 2),
            preJumpFrames: FixedPoint.FromInt(3),
            shortHopInitialVelocity: FixedPoint.FromFraction(4, 1),
            highJumpInitialVelocity: FixedPoint.FromFraction(13, 2),
            jumpGravity: FixedPoint.FromFraction(1, 2),
            horizontalJumpSpeed: FixedPoint.FromInt(3)));

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
                startupFrames: 5, activeFrames: 2, recoveryFrames: 7,
                attacks: new[]
                {
                    new AttackDefinition(
                        offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(60),
                        halfWidth: FixedPoint.FromInt(24), halfHeight: FixedPoint.FromInt(24),
                        height: AttackHeight.Mid, damage: 45, hitstunFrames: 15, blockstunFrames: 10),
                }),
            new MoveDefinition(SweepPrototype, "214B",
                startupFrames: 3, activeFrames: 4, recoveryFrames: 9,
                attacks: new[]
                {
                    // 扫腿：低位攻击框（Low，站防失败）。
                    new AttackDefinition(
                        offsetForward: FixedPoint.FromInt(45), offsetUp: FixedPoint.FromInt(20),
                        halfWidth: FixedPoint.FromInt(30), halfHeight: FixedPoint.FromInt(18),
                        height: AttackHeight.Low, damage: 40, hitstunFrames: 14, blockstunFrames: 10),
                }),
        },
        // 运动配置（D64/D68）：Rei 与 Kai 刻意不同——证明 P1/P2 可携带不同运动数据：
        // 前 1.75 / 后 1.25 单位每帧；预备跳 4 帧；小跳初速 3.5、大跳初速 6.0、
        // 重力 0.5、水平跳速 2.5。
        movement: new MovementDefinition(
            walkForwardSpeed: FixedPoint.FromFraction(7, 4),
            walkBackwardSpeed: FixedPoint.FromFraction(5, 4),
            preJumpFrames: FixedPoint.FromInt(4),
            shortHopInitialVelocity: FixedPoint.FromFraction(7, 2),
            highJumpInitialVelocity: FixedPoint.FromInt(6),
            jumpGravity: FixedPoint.FromFraction(1, 2),
            horizontalJumpSpeed: FixedPoint.FromFraction(5, 2)));

    /// <summary>默认演示对局：Kai（P1）对 Rei（P2）。</summary>
    public static FightRoster CreateKaiVsRei() => new(CreateKai(), CreateRei());
}
