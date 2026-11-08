using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Team;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 10B（D123/D124）：<b>队伍选择模型</b>与<b>输入链路诊断</b>——两者都是纯 C#，
/// 表现层只负责把按键转成方法调用、把结果画出来。
/// </summary>
public class TeamSelectionTests
{
    private static TeamSelectionModel Model(int teamSize = TeamDefinition.TeamSize) =>
        new(FighterCatalog.CreateDemo(), teamSize);

    [Fact]
    public void DefaultLineup_UsesFirstCharacterForP1_AndSecondForP2()
    {
        TeamSelectionModel model = Model();

        Assert.Equal(new[] { "Kai", "Kai", "Kai" }, model.SelectedIds(TeamSelectionModel.Player1));
        Assert.Equal(new[] { "Rei", "Rei", "Rei" }, model.SelectedIds(TeamSelectionModel.Player2));
        Assert.Equal(0, model.P1Cursor);
        Assert.Equal(0, model.P2Cursor);
        Assert.False(model.P1Ready);
        Assert.False(model.BothReady);
    }

    [Fact]
    public void CursorWrapsInBothDirections()
    {
        TeamSelectionModel model = Model();

        model.MoveCursor(TeamSelectionModel.Player1, -1);
        Assert.Equal(2, model.P1Cursor);
        model.MoveCursor(TeamSelectionModel.Player1, +1);
        Assert.Equal(0, model.P1Cursor);

        model.MoveCursor(TeamSelectionModel.Player2, +2);
        Assert.Equal(2, model.P2Cursor);
    }

    [Fact]
    public void CyclingCharacter_WrapsAndClearsReady()
    {
        TeamSelectionModel model = Model();
        model.ToggleReady(TeamSelectionModel.Player1);
        Assert.True(model.P1Ready);

        model.CycleCharacter(TeamSelectionModel.Player1, +1);
        Assert.Equal("Rei", model.SelectedIds(TeamSelectionModel.Player1)[0]);
        Assert.False(model.P1Ready); // 改阵容即取消确认

        model.CycleCharacter(TeamSelectionModel.Player1, -1);
        Assert.Equal("Kai", model.SelectedIds(TeamSelectionModel.Player1)[0]);
        model.CycleCharacter(TeamSelectionModel.Player1, -1);
        Assert.Equal("Rei", model.SelectedIds(TeamSelectionModel.Player1)[0]); // 回绕
    }

    [Fact]
    public void DuplicateCharacters_AreAllowedAcrossSlots()
    {
        TeamSelectionModel model = Model();
        // 三个槽位全部保持 Kai。
        TeamDefinition team = model.BuildTeam(TeamSelectionModel.Player1, "P1");

        Assert.Equal(3, team.Count);
        Assert.Equal(new[] { "Kai", "Kai", "Kai" }, team.FighterIds);
        Assert.All(Enumerable.Range(0, 3), i =>
            Assert.Equal(1000, team.MemberAt(i).MaxHealth));
    }

    [Fact]
    public void BuildTeam_UsesCatalogMaxHealth_AndPerSlotCharacters()
    {
        FighterCatalog catalog = new(new[]
        {
            new FighterCatalogEntry("Kai", "Kai", "test", DemoCombat.Kai(), maxHealth: 1000),
            new FighterCatalogEntry("Rei", "Rei", "test", DemoCombat.Rei(), maxHealth: 800),
        });
        var model = new TeamSelectionModel(catalog);

        model.CycleCharacter(TeamSelectionModel.Player2, 0); // 无变化但验证调用安全
        model.MoveCursor(TeamSelectionModel.Player2, +1);
        model.CycleCharacter(TeamSelectionModel.Player2, -1); // 槽位 1 改为 Kai

        TeamDefinition team = model.BuildTeam(TeamSelectionModel.Player2, "P2");
        Assert.Equal(800, team.MemberAt(0).MaxHealth);
        Assert.Equal(1000, team.MemberAt(1).MaxHealth);
    }

    [Fact]
    public void Reset_RestoresDefaultLineupAndClearsReady()
    {
        TeamSelectionModel model = Model();
        model.CycleCharacter(TeamSelectionModel.Player1, +1);
        model.MoveCursor(TeamSelectionModel.Player2, +2);
        model.ToggleReady(TeamSelectionModel.Player1);
        model.ToggleReady(TeamSelectionModel.Player2);
        Assert.True(model.BothReady);

        model.Reset();

        Assert.Equal(new[] { "Kai", "Kai", "Kai" }, model.SelectedIds(TeamSelectionModel.Player1));
        Assert.Equal(new[] { "Rei", "Rei", "Rei" }, model.SelectedIds(TeamSelectionModel.Player2));
        Assert.Equal(0, model.P1Cursor);
        Assert.Equal(0, model.P2Cursor);
        Assert.False(model.BothReady);
    }

    [Fact]
    public void IllegalPlayerOrSlot_IsRejected()
    {
        TeamSelectionModel model = Model();
        Assert.Throws<ArgumentOutOfRangeException>(() => model.MoveCursor(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.CharacterAt(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.CharacterAt(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TeamSelectionModel(
            FighterCatalog.CreateDemo(), teamSize: 0));
    }

    [Fact]
    public void DescribeLine_MarksTheEditingSlot()
    {
        TeamSelectionModel model = Model();
        model.MoveCursor(TeamSelectionModel.Player1, +1);

        string line = model.DescribeLine(TeamSelectionModel.Player1);
        Assert.Contains("[Kai]", line);
        Assert.StartsWith("Kai > ", line);
    }

    [Fact]
    public void TeamSize_IsConfigurable_NotHardcodedToThree()
    {
        TeamSelectionModel model = Model(teamSize: 2);
        Assert.Equal(2, model.TeamSize);
        Assert.Equal(2, model.BuildTeam(TeamSelectionModel.Player1, "P1").Count);
    }

    // ─────────────────────────────────────────────────────────────
    // InputTrace / TracingInputSource
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void InputTrace_IsDisabledByDefault()
    {
        var trace = new InputTrace();
        trace.Record("key", "J down");
        Assert.Equal(0, trace.Count);
        Assert.Equal(string.Empty, trace.Dump());
    }

    [Fact]
    public void InputTrace_KeepsOrderAndDropsOldest()
    {
        var trace = new InputTrace(capacity: 3) { Enabled = true };
        trace.Record("key", "a");
        trace.Record("sample", "b");
        trace.Record("prepare", "c");
        trace.Record("commit", "d");

        Assert.Equal(3, trace.Count);
        Assert.Equal(4, trace.TotalRecorded);
        string[] lines = trace.Lines.ToArray();
        Assert.Contains("b", lines[0]);
        Assert.Contains("d", lines[2]);
        Assert.DoesNotContain("key: a", trace.Dump());
        Assert.Equal(3, trace.Dump().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void InputTrace_DescribeBitsAndInput()
    {
        Assert.Equal("-", InputTrace.DescribeBits(InputBits.None));
        Assert.Equal("A", InputTrace.DescribeBits(InputBits.LightPunch));
        Assert.Equal("LD", InputTrace.DescribeBits(InputBits.Left | InputBits.HeavyKick));
        Assert.Equal("f7 P1=A P2=-",
            InputTrace.DescribeInput(new FrameInput(7, InputBits.LightPunch, InputBits.None)));
    }

    private sealed class StubInput : IInputSource
    {
        public List<int> Prepared { get; } = new();
        public List<int> Committed { get; } = new();

        public FrameInput Prepare(int frame)
        {
            Prepared.Add(frame);
            return new FrameInput(frame, InputBits.LightPunch, InputBits.None);
        }

        public void Commit(FrameInput input) => Committed.Add(input.Frame);
    }

    [Fact]
    public void TracingInputSource_ForwardsAndRecords()
    {
        var trace = new InputTrace { Enabled = true };
        var inner = new StubInput();
        var source = new TracingInputSource(inner, trace);

        FrameInput prepared = source.Prepare(3);
        source.Commit(prepared);

        Assert.Equal(new[] { 3 }, inner.Prepared);
        Assert.Equal(new[] { 3 }, inner.Committed);
        Assert.Same(inner, source.Inner);

        string[] lines = trace.Lines.ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains("prepare", lines[0]);
        Assert.Contains("commit", lines[1]);
        Assert.Contains("P1=A", lines[0]);
    }
}

/// <summary>测试用权威角色配置（与 DemoRoster 同源，单独取出便于自定义满血值）。</summary>
internal static class DemoCombat
{
    public static NewKOF.BattleCore.Combat.FighterDefinition Kai() =>
        NewKOF.BattleCore.Combat.DemoRoster.CreateKai();

    public static NewKOF.BattleCore.Combat.FighterDefinition Rei() =>
        NewKOF.BattleCore.Combat.DemoRoster.CreateRei();
}
