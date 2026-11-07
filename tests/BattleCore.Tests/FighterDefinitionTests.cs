using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 04：角色配置与招式定义的构造期校验（B4.7 —— 未知指令、重复 ID 等明确处理）
/// 与不可变性（B4.6 —— 配置不可被外部意外修改）。
/// </summary>
public class FighterDefinitionTests
{
    private static CommandDefinition Command236A() => new(
        "236A",
        new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
        InputBits.LightPunch);

    private static CommandDefinition Command623A() => new(
        "623A",
        new[] { CommandDirection.Forward, CommandDirection.Down, CommandDirection.DownForward },
        InputBits.LightPunch);

    // ─────────────────────────────────────────────────────────────
    // MoveDefinition 校验
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 3, 8)]
    [InlineData(4, 0, 8)]
    [InlineData(4, 3, 0)]
    [InlineData(-1, 3, 8)]
    public void MoveDefinition_RejectsNonPositivePhaseFrames(int startup, int active, int recovery)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveDefinition(
            "M", "236A", startup, active, recovery));
    }

    [Fact]
    public void MoveDefinition_RejectsEmptyAllowedStates()
    {
        Assert.Throws<ArgumentException>(() => new MoveDefinition(
            "M", "236A", 4, 3, 8, MoveAllowedStates.None));
    }

    [Fact]
    public void MoveDefinition_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => new MoveDefinition("", "236A", 4, 3, 8));
        Assert.Throws<ArgumentException>(() => new MoveDefinition("M", " ", 4, 3, 8));
    }

    // ─────────────────────────────────────────────────────────────
    // 裸按键指令（D56：空方向序列 + 攻击键）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void BareButtonCommand_WithButton_IsValid()
    {
        var command = new CommandDefinition(
            "5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5);

        Assert.Empty(command.DirectionSequence);
        Assert.Equal(InputBits.LightPunch, command.RequiredButtons);
    }

    [Fact]
    public void BareButtonCommand_WithoutButton_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new CommandDefinition(
            "Bad", Array.Empty<CommandDirection>(), InputBits.None));
    }

    [Fact]
    public void BareButtonCommand_WithCharge_IsRejected()
    {
        // 蓄力指令至少需要 2 个方向元素（D52）——空序列 + 蓄力无意义。
        Assert.Throws<ArgumentException>(() => new CommandDefinition(
            "Bad", Array.Empty<CommandDirection>(), InputBits.LightPunch, chargeHoldFrames: 10));
    }

    // ─────────────────────────────────────────────────────────────
    // FighterDefinition 校验
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FighterDefinition_RejectsDuplicateMoveId()
    {
        Assert.Throws<ArgumentException>(() => new FighterDefinition(
            "Kai",
            new[] { Command236A() },
            new[]
            {
                new MoveDefinition("Fireball", "236A", 4, 3, 8),
                new MoveDefinition("Fireball", "236A", 5, 2, 7),
            }));
    }

    [Fact]
    public void FighterDefinition_RejectsMoveReferencingUnknownCommand()
    {
        // 招式引用了指令表中不存在的 CommandId → 构造期失败（B4.7）。
        Assert.Throws<ArgumentException>(() => new FighterDefinition(
            "Kai",
            new[] { Command236A() },
            new[] { new MoveDefinition("DragonPunch", "623A", 3, 5, 10) }));
    }

    [Fact]
    public void FighterDefinition_RejectsDuplicateCommandId()
    {
        // 同一角色内同一指令映射到多个招式会产生歧义 → 构造期失败。
        // （跨角色允许同名指令，见 MoveConsumptionTests。）
        Assert.Throws<ArgumentException>(() => new FighterDefinition(
            "Kai",
            new[] { Command236A(), Command236A() },
            new[] { new MoveDefinition("Fireball", "236A", 4, 3, 8) }));
    }

    [Fact]
    public void FighterDefinition_RejectsEmptyFighterId()
    {
        Assert.Throws<ArgumentException>(() => new FighterDefinition(
            " ", new[] { Command236A() }, Array.Empty<MoveDefinition>()));
    }

    [Fact]
    public void FighterDefinition_AllowsCommandWithoutMove()
    {
        // 指令表中有 236A 但没有任何招式引用它 —— 合法（识别仍发生，只是不触发动作）。
        var definition = new FighterDefinition(
            "Kai", new[] { Command236A() }, Array.Empty<MoveDefinition>());

        Assert.Null(definition.FindMove("236A"));
        Assert.Single(definition.Commands);
        Assert.Empty(definition.Moves);
    }

    // ─────────────────────────────────────────────────────────────
    // 不可变性与查找
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FighterDefinition_DefensiveCopy_ExternalMutationHasNoEffect()
    {
        var commands = new[] { Command236A() };
        var moves = new List<MoveDefinition> { new("Fireball", "236A", 4, 3, 8) };

        var definition = new FighterDefinition("Kai", commands, moves);

        // 外部修改源集合不得影响配置（B4.6）。
        commands[0] = Command623A();
        moves.Clear();
        moves.Add(new MoveDefinition("Other", "623A", 1, 1, 1));

        Assert.Equal("236A", definition.Commands[0].CommandId);
        Assert.Single(definition.Moves);
        Assert.Equal("Fireball", definition.Moves[0].MoveId);
    }

    [Fact]
    public void FindMove_Resolution()
    {
        var definition = DemoRoster.CreateKai();

        Assert.NotNull(definition.FindMove("236A"));
        Assert.Equal(DemoRoster.FireballPrototype, definition.FindMove("236A")!.MoveId);
        Assert.Equal(DemoRoster.DragonPunchPrototype, definition.FindMove("623A")!.MoveId);
        Assert.Equal(DemoRoster.StandLightPunch, definition.FindMove("5A")!.MoveId);
        Assert.Null(definition.FindMove("214B")); // Kai 没有这条指令对应的招式。

        Assert.Equal(DemoRoster.FireballPrototype, definition.FindMoveById(DemoRoster.FireballPrototype)!.MoveId);
        Assert.Null(definition.FindMoveById("Nope"));
    }

    // ─────────────────────────────────────────────────────────────
    // FightRoster
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FightRoster_RejectsNullFighter()
    {
        var kai = DemoRoster.CreateKai();
        Assert.Throws<ArgumentNullException>(() => new FightRoster(null!, kai));
        Assert.Throws<ArgumentNullException>(() => new FightRoster(kai, null!));
    }

    [Fact]
    public void FightRoster_CreateEmpty_HasNoCommandsOrMoves()
    {
        var roster = FightRoster.CreateEmpty();

        Assert.Empty(roster.Fighter(0).Commands);
        Assert.Empty(roster.Fighter(0).Moves);
        Assert.Empty(roster.Fighter(1).Commands);
    }

    [Fact]
    public void DemoRoster_SameCommandId_DifferentMoves()
    {
        // B4.5：两名角色都有 236A 指令，但映射到不同 MoveId 与不同阶段帧数。
        var kai = DemoRoster.CreateKai();
        var rei = DemoRoster.CreateRei();

        MoveDefinition kaiMove = kai.FindMove("236A")!;
        MoveDefinition reiMove = rei.FindMove("236A")!;

        Assert.NotEqual(kaiMove.MoveId, reiMove.MoveId);
        Assert.NotEqual(kaiMove.StartupFrames, reiMove.StartupFrames);
        Assert.NotEqual(kaiMove.TotalFrames, reiMove.TotalFrames);

        // 指令集差异：623A 只属于 Kai；214B 只属于 Rei。
        Assert.NotNull(kai.FindMove("623A"));
        Assert.Null(rei.FindMove("623A"));
        Assert.NotNull(rei.FindMove("214B"));
        Assert.Null(kai.FindMove("214B"));
    }
}
