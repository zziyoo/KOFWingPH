using System.Text.Json;
using System.Text.Json.Nodes;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 09D（D113–D115）：<b>JSON 角色配置加载、与 DemoRoster 等价性、错误处理与数据驱动战斗</b>。
///
/// <para>测试用 JSON 由 csproj 复制到输出目录 <c>data/fighters/</c>；加载器本身只接受字符串/流，
/// 不依赖 Godot 或文件系统。</para>
/// </summary>
public class FighterDataTests
{
    private static string Json(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "fighters", name + ".json"));

    private static string Source(string name) => $"fighters/{name}.json";

    private static FighterCatalog JsonCatalog() => FighterDataLoader.LoadCatalog(new[]
    {
        new FighterDataDocument(Source("Kai"), Json("Kai")),
        new FighterDataDocument(Source("Rei"), Json("Rei")),
    });

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    /// <summary>把配置的全部战斗相关字段摊平成可逐项比较的文本行。</summary>
    private static List<string> Describe(FighterDefinition d)
    {
        var lines = new List<string> { $"fighterId={d.FighterId}" };
        MovementDefinition m = d.Movement;
        lines.Add($"movement=({m.WalkForwardSpeed.RawValue},{m.WalkBackwardSpeed.RawValue}," +
            $"{m.PreJumpFrames},{m.ShortHopInitialVelocity.RawValue}," +
            $"{m.HighJumpInitialVelocity.RawValue},{m.JumpGravity.RawValue}," +
            $"{m.HorizontalJumpSpeed.RawValue})");

        foreach (Commands.CommandDefinition c in d.Commands)
        {
            lines.Add($"cmd={c.CommandId}|{string.Join(",", c.DirectionSequence)}|" +
                $"{(int)c.RequiredButtons}|{c.MaxSequenceFrames}|{c.ButtonToleranceFrames}|" +
                $"{c.Priority}|{c.BufferLifetimeFrames}|{c.ChargeHoldFrames}|" +
                $"{c.ChargeReleaseToleranceFrames}");
        }

        foreach (MoveDefinition mv in d.Moves)
        {
            lines.Add($"move={mv.MoveId}|{mv.CommandId}|{mv.StartupFrames}|{mv.ActiveFrames}|" +
                $"{mv.RecoveryFrames}|{(int)mv.AllowedFrom}");
            foreach (AttackDefinition a in mv.Attacks)
                lines.Add($"  atk={AttackLine(a)}");
            foreach (CancelDefinition c in mv.Cancels)
                lines.Add($"  cancel={c.TargetMoveId}|{c.WindowFirstFrame}|{c.WindowLastFrame}|" +
                    $"{(int)c.Phases}|{c.RequiresHitConfirm}|{c.RequiresBlockConfirm}|{c.AllowWhiff}");
            foreach (ThrowDefinition t in mv.Throws)
                lines.Add($"  throw={t.ThrowId}|{t.Range.RawValue}|{t.FirstActiveFrame}|" +
                    $"{t.LastActiveFrame}|{t.Damage}|{t.KnockdownFrames}|{t.WakeupFrames}|" +
                    $"{t.PushDistance.RawValue}|{t.HitstopFrames}|{(int)t.TargetStates}");
            foreach (ProjectileDefinition p in mv.Projectiles)
                lines.Add($"  proj={p.ProjectileTypeId}|{AttackLine(p.Hit)}|" +
                    $"{p.SpeedX.RawValue}|{p.SpeedY.RawValue}|{p.LifetimeFrames}|" +
                    $"{p.MaxAlive}|{p.SpawnAtActiveFrame}");
        }
        return lines;
    }

    private static string AttackLine(AttackDefinition a) =>
        $"{a.OffsetForward.RawValue}|{a.OffsetUp.RawValue}|{a.HalfWidth.RawValue}|" +
        $"{a.HalfHeight.RawValue}|{a.FirstActiveFrame}|{a.LastActiveFrame}|{(int)a.Height}|" +
        $"{a.Damage}|{a.HitstunFrames}|{a.BlockstunFrames}|{a.GroundKnockback.RawValue}|" +
        $"{a.GroundKnockbackFriction.RawValue}|{a.BlockKnockback.RawValue}|" +
        $"{a.LaunchVelocityX.RawValue}|{a.LaunchVelocityY.RawValue}|" +
        $"{a.KnockdownFrames}|{a.WakeupFrames}|{a.HitstopFrames}";

    /// <summary>直接构造「本帧已处于 Active」的权威状态，绕过指令识别来验证招式数据。</summary>
    private static BattleState ActiveState(string moveId, int phaseRemaining,
        int p1X = -30, int p2X = 30, string? p2MoveId = null, int p2Phase = 0)
    {
        FighterState p1 = FighterState.Create(0, 1000, p1X, Facing.Right) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = moveId,
            PhaseRemainingFrames = phaseRemaining,
            MoveStartedFrame = 0,
        };
        FighterState p2 = FighterState.Create(1, 1000, p2X, Facing.Left);
        if (p2MoveId is not null)
        {
            p2 = p2 with
            {
                LogicState = FighterLogicState.Active,
                MoveId = p2MoveId,
                PhaseRemainingFrames = p2Phase,
                MoveStartedFrame = 0,
            };
        }
        return new BattleState(0, new[] { p1, p2 }, BattleState.CreateDefault().StateVersion);
    }

    // ─────────────────────────────────────────────────────────────
    // 1. 加载与 DemoRoster 逐字段等价
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void KaiJson_IsEquivalentToDemoRoster()
    {
        FighterDefinition fromJson = FighterDataLoader.Load(Json("Kai"), Source("Kai"));
        Assert.Equal(Describe(DemoRoster.CreateKai()), Describe(fromJson));
    }

    [Fact]
    public void ReiJson_IsEquivalentToDemoRoster()
    {
        FighterDefinition fromJson = FighterDataLoader.Load(Json("Rei"), Source("Rei"));
        Assert.Equal(Describe(DemoRoster.CreateRei()), Describe(fromJson));
    }

    [Fact]
    public void SameJson_LoadedTwice_IsIdentical()
    {
        Assert.Equal(
            Describe(FighterDataLoader.Load(Json("Kai"), Source("Kai"))),
            Describe(FighterDataLoader.Load(Json("Kai"), Source("Kai"))));
    }

    [Fact]
    public void Catalog_HasBothFightersWithSourcePaths()
    {
        FighterCatalog catalog = JsonCatalog();
        Assert.Equal(new[] { "Kai", "Rei" }, catalog.Entries.Select(e => e.FighterId).ToArray());
        Assert.All(catalog.Entries, e => Assert.Contains(".json", e.Source));
        Assert.NotNull(catalog.CreateRoster("Kai", "Rei"));
        Assert.NotNull(catalog.CreateRoster("Kai", "Kai")); // 镜像
    }

    // ─────────────────────────────────────────────────────────────
    // 2. JSON 驱动的战斗（四类能力全部走既有 BattleCore）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LoadedDefinition_NormalAttack_DealsJsonDamageAndHitstop()
    {
        var sim = new BattleSimulation(JsonCatalog().CreateRoster("Kai", "Rei"));
        BattleStepResult r = sim.Step(
            ActiveState("StandLightPunch", 2), FrameInput.Empty(0));

        Assert.Equal(970, r.State.Fighter(1).Health);       // JSON damage 30
        Assert.Equal(4, r.State.Fighter(1).HitstopRemainingFrames); // JSON hitstop 4
        Assert.Equal(4, r.State.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(12, r.State.Fighter(1).StunRemainingFrames);
    }

    [Fact]
    public void LoadedDefinition_236A_DealsJsonDamage()
    {
        var sim = new BattleSimulation(JsonCatalog().CreateRoster("Kai", "Rei"));
        BattleStepResult r = sim.Step(
            ActiveState("FireballPrototype", 3), FrameInput.Empty(0));
        Assert.Equal(950, r.State.Fighter(1).Health);
    }

    [Fact]
    public void LoadedDefinition_623A_LaunchesWithJsonPlan()
    {
        var sim = new BattleSimulation(JsonCatalog().CreateRoster("Kai", "Rei"));
        BattleStepResult r = sim.Step(
            ActiveState("DragonPunchPrototype", 5), FrameInput.Empty(0));

        Assert.Equal(FighterLogicState.AirHitstun, r.State.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.FromInt(6), r.State.Fighter(1).VelocityY);
        Assert.Equal(12, r.State.Fighter(1).LandingKnockdownFrames);
        Assert.Equal(8, r.State.Fighter(1).LandingWakeupFrames);
    }

    [Fact]
    public void LoadedDefinition_214B_SweepsWithLowLaunch()
    {
        var sim = new BattleSimulation(JsonCatalog().CreateRoster("Rei", "Kai"));
        BattleStepResult r = sim.Step(
            ActiveState("SweepPrototype", 4), FrameInput.Empty(0));

        Assert.Equal(FighterLogicState.AirHitstun, r.State.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.FromFraction(5, 2), r.State.Fighter(1).VelocityY);
        Assert.Equal(960, r.State.Fighter(1).Health);
    }

    [Fact]
    public void LoadedDefinition_CancelWindow_UsesJsonDefinition()
    {
        FighterCatalog catalog = JsonCatalog();
        FighterState fighter = FighterState.Create(0, 1000, -30, Facing.Right) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = "StandLightPunch",
            PhaseRemainingFrames = 1,
            MoveStartedFrame = 0,
            MoveElapsedFrames = 5,
        };
        Assert.True(FighterStateMachine.IsInCancelWindow(fighter, catalog.Get("Kai").Definition));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThrowTech_OptionalJsonFlagIsExplicitAndTyped(bool techable)
    {
        JsonNode root = JsonNode.Parse(Json("Kai"))!;
        var move = root["moves"]!.AsArray().Single(m => m!["moveId"]!.GetValue<string>() == "StandingThrow")!;
        var definition = move["throws"]![0]!;
        Assert.True(FighterDataLoader.Load(root.ToJsonString()).Moves.Single(m => m.MoveId == "StandingThrow").Throws[0].Techable);
        definition["techable"] = techable;
        Assert.Equal(techable, FighterDataLoader.Load(root.ToJsonString()).Moves.Single(m => m.MoveId == "StandingThrow").Throws[0].Techable);
        definition["techable"] = "false";
        Assert.Throws<FighterDataException>(() => FighterDataLoader.Load(root.ToJsonString()));
    }

    [Fact]
    public void LoadedDefinition_Throw_UsesJsonDefinition()
    {
        var sim = new BattleSimulation(JsonCatalog().CreateRoster("Kai", "Rei"));
        BattleStepResult r = sim.Step(
            ActiveState("StandingThrow", 2), FrameInput.Empty(0));

        Assert.NotNull(r.State.PendingThrow);
        Assert.Equal(1000, r.State.Fighter(1).Health);
        r = sim.Step(r.State, FrameInput.Empty(r.State.Frame));
        r = sim.Step(r.State, FrameInput.Empty(r.State.Frame));
        Assert.Equal(940, r.State.Fighter(1).Health);   // 投技伤害 60
        Assert.Equal(FighterLogicState.Knockdown, r.State.Fighter(1).LogicState);
        Assert.Single(r.Events, e => e.Type == BattleEventType.ThrowHit);
    }

    [Fact]
    public void LoadedDefinition_Projectile_SpawnsJsonEntity()
    {
        var sim = new BattleSimulation(JsonCatalog().CreateRoster("Kai", "Rei"));
        // Startup(1) → 本帧切换到 Active 首帧（elapsed 0），D99 规定该帧只建立实体。
        BattleState initial = new(0, new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right) with
            {
                LogicState = FighterLogicState.Startup,
                MoveId = "Fireball",
                PhaseRemainingFrames = 1,
                MoveStartedFrame = 0,
            },
            FighterState.Create(1, 1000, 30, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);
        BattleStepResult r = sim.Step(initial, FrameInput.Empty(0));

        ProjectileState p = Assert.Single(r.State.Projectiles);
        Assert.Equal("Fireball", p.TypeId);
        Assert.Equal(FixedPoint.FromInt(6), p.VelocityX);
        Assert.Equal(90, p.RemainingLifetime);
    }

    [Fact]
    public void LoadedDefinition_StrikeClash_StillWorks()
    {
        var sim = new BattleSimulation(JsonCatalog().CreateRoster("Kai", "Kai"));
        // 中距离：攻击框重叠但打不到身体。
        BattleState state = ActiveState("StandLightPunch", 2,
            p1X: -60, p2X: 60, p2MoveId: "StandLightPunch", p2Phase: 2);
        BattleStepResult r = sim.Step(state, FrameInput.Empty(0));

        Assert.Single(r.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(1000, r.State.Fighter(0).Health);
        Assert.Equal(1000, r.State.Fighter(1).Health);
    }

    [Fact]
    public void SameJsonAndSameInputLog_ProducesIdenticalBattle()
    {
        static (BattleState State, List<int> Events) Run()
        {
            var sim = new BattleSimulation(JsonCatalog().CreateRoster("Kai", "Rei"));
            BattleState state = new(0, new[]
            {
                FighterState.Create(0, 1000, -30, Facing.Right),
                FighterState.Create(1, 1000, 30, Facing.Left),
            }, BattleState.CreateDefault().StateVersion);
            var ids = new List<int>();
            for (int f = 0; f < 200; f++)
            {
                InputBits p1 = f % 17 == 0 ? InputBits.LightPunch : InputBits.None;
                InputBits p2 = f % 29 == 0 ? InputBits.LightKick : InputBits.None;
                BattleStepResult r = sim.Step(state, F(f, p1, p2));
                state = r.State;
                ids.AddRange(r.Events.Select(e => e.EventId));
            }
            return (state, ids);
        }

        (BattleState a, List<int> aIds) = Run();
        (BattleState b, List<int> bIds) = Run();
        Assert.Equal(a.Fighter(0), b.Fighter(0));
        Assert.Equal(a.Fighter(1), b.Fighter(1));
        Assert.Equal(a.Projectiles, b.Projectiles);
        Assert.Equal(a.Round, b.Round);
        Assert.Equal(aIds, bIds);
    }

    [Fact]
    public void ModifiedJsonDamage_ChangesOnlyThatMove_WithoutRecompiling()
    {
        string original = Json("Kai");
        string modified = Mutate(original, o => o["moves"]![0]!["attacks"]![0]!["damage"] = 77);
        Assert.NotEqual(original, modified);

        FighterDefinition fromJson = FighterDataLoader.Load(modified, "fighters/Kai.modified.json");
        Assert.Equal(77, fromJson.Moves[0].Attacks[0].Damage);
        Assert.Equal(50, fromJson.Moves[1].Attacks[0].Damage); // 其他招式不受影响
        List<string> expected = Describe(DemoRoster.CreateKai());
        List<string> actual = Describe(fromJson);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(1, expected.Zip(actual).Count(pair => pair.First != pair.Second)); // 仅一行变化

        var sim = new BattleSimulation(new FightRoster(fromJson, DemoRoster.CreateRei()));
        BattleStepResult r = sim.Step(ActiveState("StandLightPunch", 2), FrameInput.Empty(0));
        Assert.Equal(923, r.State.Fighter(1).Health); // 1000 - 77
    }

    [Fact]
    public void FullMatch_WithJsonCatalog_IsPlayable()
    {
        FighterCatalog catalog = JsonCatalog();
        var match = new MatchSession(MatchRules.CreateDefault(),
            (p1, p2) => catalog.CreateRoster(p1 ?? "Kai", p2 ?? "Rei"),
            rules => new BattleState(0, new[]
            {
                FighterState.Create(0, 1000, -30, Facing.Right) with { Health = 30 },
                FighterState.Create(1, 1000, 30, Facing.Left) with { Health = 30 },
            }, BattleState.CreateDefault().StateVersion,
                round: null, projectiles: null, nextProjectileId: 1,
                roundTimerRemainingFrames: rules.RoundTimerFrames));
        match.SelectFighters("Kai", "Rei");
        match.StartMatch();

        match.StepExact(4, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));
        Assert.Equal(1, match.State.P1Wins);
        match.StartNextRound();
        match.StepExact(4, f => f == 0 ? F(f, InputBits.LightPunch) : FrameInput.Empty(f));

        Assert.Equal(MatchPhase.MatchFinished, match.State.Phase);
        Assert.Equal(MatchOutcome.P1Win, match.State.Outcome);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 错误处理（D114）
    // ─────────────────────────────────────────────────────────────

    private static JsonObject Root(string json) => JsonNode.Parse(json)!.AsObject();

    /// <summary>用 DOM 精确修改某个字段，避免脆弱的字符串替换。</summary>
    private static string Mutate(string json, Action<JsonObject> mutate)
    {
        JsonObject root = Root(json);
        mutate(root);
        return root.ToJsonString();
    }

    private static FighterDataException ExpectFailure(string json)
    {
        FighterDataException ex = Assert.Throws<FighterDataException>(() =>
            FighterDataLoader.Load(json, "fighters/Test.json"));
        Assert.Contains("fighters/Test.json", ex.Message);
        Assert.False(string.IsNullOrWhiteSpace(ex.FieldPath));
        Assert.False(string.IsNullOrWhiteSpace(ex.Reason));
        return ex;
    }

    [Fact]
    public void MalformedJson_AndUnsupportedVersion_AreRejected()
    {
        ExpectFailure("{ not json");
        FighterDataException version = ExpectFailure(
            Mutate(Json("Kai"), o => o["schemaVersion"] = 2));
        Assert.Contains("schemaVersion", version.FieldPath);
        FighterDataException missing = ExpectFailure(
            Mutate(Json("Kai"), o => o.Remove("schemaVersion")));
        Assert.Contains("schemaVersion", missing.FieldPath);
    }

    [Fact]
    public void MissingFighterId_AndNullArrays_AreRejected()
    {
        ExpectFailure(Mutate(Json("Kai"), o => o["fighterId"] = ""));
        ExpectFailure(Mutate(Json("Kai"), o => o.Remove("fighterId")));
        FighterDataException nullCommands = ExpectFailure(
            Mutate(Json("Kai"), o => o["commands"] = null));
        Assert.Contains("commands", nullCommands.FieldPath);
        FighterDataException nullMoves = ExpectFailure(
            Mutate(Json("Kai"), o => o["moves"] = null));
        Assert.Contains("moves", nullMoves.FieldPath);
        FighterDataException nullAttacks = ExpectFailure(
            Mutate(Json("Kai"), o => o["moves"]![0]!["attacks"] = null));
        Assert.Contains("attacks", nullAttacks.FieldPath);
    }

    [Fact]
    public void DuplicateIds_AndMissingReferences_AreRejected()
    {
        FighterDataException dupCommand = ExpectFailure(Mutate(Json("Kai"),
            o => o["commands"]![1]!["commandId"] = "236A"));
        Assert.Contains("<definition>", dupCommand.FieldPath);

        FighterDataException dupMove = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![4]!["moveId"] = "StandLightPunch"));
        Assert.Contains("<definition>", dupMove.FieldPath);

        FighterDataException badCommandRef = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![0]!["commandId"] = "NOPE"));
        Assert.Contains("<definition>", badCommandRef.FieldPath);

        FighterDataException badCancelRef = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![0]!["cancels"]![0]!["targetMoveId"] = "NOPE"));
        Assert.Contains("<definition>", badCancelRef.FieldPath);
    }

    [Fact]
    public void InvalidEnums_AndButtonBits_AreRejected()
    {
        FighterDataException badDirection = ExpectFailure(Mutate(Json("Kai"),
            o => o["commands"]![0]!["directions"]![1] = "Sideways"));
        Assert.Contains("directions", badDirection.FieldPath);

        FighterDataException badButton = ExpectFailure(Mutate(Json("Kai"),
            o => o["commands"]![4]!["buttons"]![0] = "Left"));
        Assert.Contains("buttons", badButton.FieldPath);

        FighterDataException badHeight = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![2]!["attacks"]![0]!["height"] = "Sideways"));
        Assert.Contains("height", badHeight.FieldPath);

        FighterDataException badTargetStates = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![3]!["throws"]![0]!["targetStates"] = "Nope"));
        Assert.Contains("targetStates", badTargetStates.FieldPath);
    }

    [Fact]
    public void IllegalNumbers_AreRejected_WithFieldPaths()
    {
        FighterDataException negativeDamage = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![0]!["attacks"]![0]!["damage"] = -5));
        Assert.Contains("moves[0].attacks[0]", negativeDamage.FieldPath);
        Assert.Contains("damage", negativeDamage.Message);

        FighterDataException badHitstop = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![0]!["attacks"]![0]!["hitstopFrames"] = 31));
        Assert.Contains("moves[0].attacks[0]", badHitstop.FieldPath);
        Assert.Contains("hitstopFrames", badHitstop.Message);

        FighterDataException zeroDenominator = ExpectFailure(Mutate(Json("Kai"),
            o => o["movement"]!["walkBackwardSpeed"]!["denominator"] = 0));
        Assert.Contains("denominator", zeroDenominator.FieldPath);

        FighterDataException negativeDenominator = ExpectFailure(Mutate(Json("Kai"),
            o => o["movement"]!["walkBackwardSpeed"]!["denominator"] = -2));
        Assert.Contains("分母必须为正", negativeDenominator.Reason);

        ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![0]!["startupFrames"] = 0));

        FighterDataException nullSpeed = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![4]!["projectiles"]![0]!["speedX"] = null));
        Assert.Contains("speedX", nullSpeed.FieldPath);
    }

    [Fact]
    public void NullRequiredField_IsNotSilentlyDefaulted()
    {
        FighterDataException ex = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![0]!["attacks"]![0]!["damage"] = null));
        Assert.Contains("必填", ex.Reason);
        Assert.Contains("damage", ex.FieldPath);
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        FighterDataException ex = ExpectFailure(Mutate(Json("Kai"),
            o => o["typoField"] = 1));
        Assert.Contains("JSON 格式错误", ex.Reason);
    }

    [Fact]
    public void IllegalAttackWindow_AndProjectile_AreRejected()
    {
        // 5A 的 ActiveFrames = 2，显式 lastActiveFrame = 2 越界。
        FighterDataException badWindow = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![0]!["attacks"]![0]!["lastActiveFrame"] = 2));
        Assert.Contains("moves[0]", badWindow.FieldPath);

        FighterDataException badSpeed = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![4]!["projectiles"]![0]!["speedX"]!["numerator"] = 0));
        Assert.Contains("moves[4].projectiles[0]", badSpeed.FieldPath);
        Assert.Contains("speedX", badSpeed.Message);

        FighterDataException illegalThrow = ExpectFailure(Mutate(Json("Kai"),
            o => o["moves"]![3]!["throws"]![0]!["knockdownFrames"] = 0));
        Assert.Contains("throws[0]", illegalThrow.FieldPath);
    }

    [Fact]
    public void IllegalFractionRange_IsRejected()
    {
        FighterDataException ex = ExpectFailure(Mutate(Json("Kai"),
            o => o["movement"]!["walkForwardSpeed"]!["numerator"] = long.MaxValue));
        Assert.Contains("walkForwardSpeed", ex.FieldPath);
    }

    [Fact]
    public void LoadFromStream_IsEquivalentToString()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Json("Rei")));
        Assert.Equal(
            Describe(FighterDataLoader.Load(Json("Rei"), Source("Rei"))),
            Describe(FighterDataLoader.Load(stream, Source("Rei"))));
    }

    [Fact]
    public void Loader_DoesNotDependOnGodot()
    {
        // 纯 C# 程序集可在无 Godot 环境下加载；若引用了 Godot，本测试程序集无法编译。
        var assembly = typeof(FighterDataLoader).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(),
            a => a.Name is not null && a.Name.StartsWith("Godot", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateFighterIdInCatalog_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => FighterDataLoader.LoadCatalog(new[]
        {
            new FighterDataDocument(Source("Kai"), Json("Kai")),
            new FighterDataDocument("fighters/Kai.copy.json", Json("Kai")),
        }));
    }
}
