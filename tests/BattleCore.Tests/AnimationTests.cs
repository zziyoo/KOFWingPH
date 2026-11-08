using System.Text.Json.Nodes;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 10C（D125）：<b>状态驱动的动画管线</b>——表现配置加载、状态→动画映射、
/// 有效动作帧、Hitstop 冻结、缺失资源降级，以及「动画绝不写回权威状态」。
/// </summary>
public class AnimationTests
{
    private static string Json(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "presentation", $"{name}.json"));

    private static AnimationSet Load(string name) =>
        PresentationDataLoader.Load(Json(name), $"presentation/{name}.json");

    private static FighterState Fighter(
        FighterLogicState state = FighterLogicState.Standing,
        string? moveId = null,
        int elapsed = 0,
        int velocityX = 0,
        int positionY = 0,
        int hitstop = 0,
        Facing facing = Facing.Right)
    {
        FighterState fighter = FighterState.Create(0, 1000, 0, facing);
        if (state != FighterLogicState.Standing || moveId is not null)
        {
            fighter = fighter with
            {
                LogicState = state,
                MoveId = moveId,
                MoveStartedFrame = 0,
                MoveElapsedFrames = elapsed,
                PhaseRemainingFrames = 1,
            };
        }

        return fighter with
        {
            VelocityX = FixedPoint.FromInt(velocityX),
            PositionY = FixedPoint.FromInt(positionY),
            HitstopRemainingFrames = hitstop,
        };
    }

    [Fact]
    public void PresentationConfig_LoadsBothFighters()
    {
        AnimationSet kai = Load("Kai");
        AnimationSet rei = Load("Rei");

        Assert.Equal("Kai", kai.FighterId);
        Assert.Equal("Rei", rei.FighterId);
        Assert.True(kai.Clips.Count >= 10);
        Assert.Contains("idle", kai.Clips.Select(c => c.Name));
        Assert.Equal("idle", kai.States["Standing"]);
        Assert.Equal("walk", kai.States["Walking"]);
        Assert.Equal("atk_active", kai.Moves["StandLightPunch"].Active);
    }

    [Fact]
    public void Standing_PlaysIdle()
    {
        AnimationSet set = Load("Kai");
        ResolvedPose pose = AnimationResolver.Resolve(Fighter(), set, visualFrame: 0);

        Assert.Equal("idle", pose.ClipName);
        Assert.Equal(0, pose.FrameIndex);
        Assert.Equal(120, pose.Frame.Height);
    }

    [Fact]
    public void Idle_AdvancesWithVisualClock_AndLoops()
    {
        AnimationSet set = Load("Kai");
        FighterState idle = Fighter();

        // 每帧停留 8 个逻辑帧，两帧循环。
        Assert.Equal(0, AnimationResolver.Resolve(idle, set, 0).FrameIndex);
        Assert.Equal(0, AnimationResolver.Resolve(idle, set, 7).FrameIndex);
        Assert.Equal(1, AnimationResolver.Resolve(idle, set, 8).FrameIndex);
        Assert.Equal(0, AnimationResolver.Resolve(idle, set, 16).FrameIndex);
    }

    [Fact]
    public void Walking_OverridesStanding_WhenGroundedAndMoving()
    {
        AnimationSet set = Load("Kai");

        Assert.Equal("walk",
            AnimationResolver.ResolveClipName(Fighter(velocityX: 3), set));
        Assert.Equal("idle",
            AnimationResolver.ResolveClipName(Fighter(velocityX: 0), set));
        // 空中即使有水平速度也不算 Walking。
        Assert.Equal("jump",
            AnimationResolver.ResolveClipName(
                Fighter(state: FighterLogicState.Airborne, velocityX: 3, positionY: 40), set));
    }

    [Fact]
    public void JumpAndFall_MapToJumpClip()
    {
        AnimationSet set = Load("Kai");
        Assert.Equal("jump", AnimationResolver.ResolveClipName(
            Fighter(state: FighterLogicState.PreJump), set));
        Assert.Equal("jump", AnimationResolver.ResolveClipName(
            Fighter(state: FighterLogicState.Airborne, positionY: 80), set));
    }

    [Fact]
    public void MovePhases_UseMoveSpecificClips_AndMoveElapsedFrames()
    {
        AnimationSet set = Load("Kai");

        var startup = Fighter(FighterLogicState.Startup, "StandLightPunch", elapsed: 1);
        var active = Fighter(FighterLogicState.Active, "StandLightPunch", elapsed: 2);
        var recovery = Fighter(FighterLogicState.Recovery, "StandLightPunch", elapsed: 5);

        Assert.Equal("atk_startup", AnimationResolver.ResolveClipName(startup, set));
        Assert.Equal("atk_active", AnimationResolver.ResolveClipName(active, set));
        Assert.Equal("atk_recovery", AnimationResolver.ResolveClipName(recovery, set));

        // Active 段 2 帧、每帧 3 逻辑帧：elapsed 0..2 → 0，3..5 → 1（不循环，夹在末帧）。
        Assert.Equal(0, AnimationResolver.Resolve(startup with { MoveElapsedFrames = 0 }, set, 0).FrameIndex);
        Assert.Equal(0, AnimationResolver.Resolve(active with { MoveElapsedFrames = 2 }, set, 0).FrameIndex);
        Assert.Equal(1, AnimationResolver.Resolve(active with { MoveElapsedFrames = 3 }, set, 0).FrameIndex);
        Assert.Equal(1, AnimationResolver.Resolve(active with { MoveElapsedFrames = 99 }, set, 0).FrameIndex);
    }

    [Fact]
    public void HeavyMoves_UseHeavyClips()
    {
        AnimationSet set = Load("Kai");
        Assert.Equal("heavy_active", AnimationResolver.ResolveClipName(
            Fighter(FighterLogicState.Active, "DragonPunchPrototype", elapsed: 1), set));
    }

    [Theory]
    [InlineData(FighterLogicState.Hitstun, "hurt")]
    [InlineData(FighterLogicState.Blockstun, "guard")]
    [InlineData(FighterLogicState.AirHitstun, "air_hurt")]
    [InlineData(FighterLogicState.Knockdown, "knockdown")]
    [InlineData(FighterLogicState.Wakeup, "wakeup")]
    [InlineData(FighterLogicState.Crouching, "crouch")]
    public void ReactionStates_MapToTheirClips(FighterLogicState state, string expected)
    {
        AnimationSet set = Load("Kai");
        Assert.Equal(expected, AnimationResolver.ResolveClipName(Fighter(state), set));
    }

    [Fact]
    public void Hitstop_FreezesTheVisualClock()
    {
        var clock = new FighterVisualClock();

        // f0..f4 正常推进（非冻结）。
        Assert.Equal(0, clock.Sync(0, FighterLogicState.Hitstun, null, 0));
        Assert.Equal(4, clock.Sync(4, FighterLogicState.Hitstun, null, 0));

        // f5..f8 处于 Hitstop(4)：画面必须停在 4。
        for (int frame = 5; frame <= 8; frame++)
            Assert.Equal(4, clock.Sync(frame, FighterLogicState.Hitstun, null, hitstopRemaining: 4));

        // 冻结解除后继续推进：冻结的 4 帧不计入动画时间，只补上解除后的 1 帧。
        Assert.Equal(5, clock.Sync(9, FighterLogicState.Hitstun, null, 0));
    }

    [Fact]
    public void VisualClock_IsIdempotentPerLogicFrame()
    {
        var clock = new FighterVisualClock();
        clock.Sync(0, FighterLogicState.Standing, null, 0);
        clock.Sync(3, FighterLogicState.Standing, null, 0);

        // 同一逻辑帧被渲染多次（高刷新率）不得推进动画。
        Assert.Equal(3, clock.Sync(3, FighterLogicState.Standing, null, 0));
        Assert.Equal(3, clock.Sync(3, FighterLogicState.Standing, null, 0));
        Assert.Equal(4, clock.Sync(4, FighterLogicState.Standing, null, 0));
    }

    [Fact]
    public void VisualClock_ResetsOnStateOrMoveChange()
    {
        var clock = new FighterVisualClock();
        clock.Sync(0, FighterLogicState.Standing, null, 0);
        Assert.Equal(10, clock.Sync(10, FighterLogicState.Standing, null, 0));

        // 状态切换 → 新动画从第 0 帧开始。
        Assert.Equal(0, clock.Sync(10, FighterLogicState.Crouching, null, 0));
        Assert.Equal(0, clock.Sync(10, FighterLogicState.Startup, "StandLightPunch", 0));
        // 换招（同一状态下不同 MoveId）也算切换。
        Assert.Equal(0, clock.Sync(12, FighterLogicState.Startup, "FireballPrototype", 0));
    }

    [Fact]
    public void MissingMoveConfig_FallsBackToPhaseStateClip()
    {
        AnimationSet set = Load("Kai");
        // "UnknownMove" 不在配置里：用「该逻辑阶段」的通用动画，而不是崩溃或退回 idle。
        Assert.Equal("atk_startup", AnimationResolver.ResolveClipName(
            Fighter(FighterLogicState.Startup, "UnknownMove"), set));
        Assert.Equal("atk_active", AnimationResolver.ResolveClipName(
            Fighter(FighterLogicState.Active, "UnknownMove"), set));
        Assert.Equal("atk_recovery", AnimationResolver.ResolveClipName(
            Fighter(FighterLogicState.Recovery, "UnknownMove"), set));
    }

    [Fact]
    public void MissingState_FallsBackToIdle()
    {
        var set = new AnimationSet("Test",
            new[] { new AnimationClip("idle", new[] { new PoseFrame(10, 10, 0, 0, 0, "#FFFFFF") }, true) },
            new Dictionary<string, string> { ["Standing"] = "idle" });

        // Blockstun 未配置 → 兜底 idle。
        Assert.Equal("idle", AnimationResolver.ResolveClipName(Fighter(FighterLogicState.Blockstun), set));
    }

    [Fact]
    public void FallbackSet_CoversEveryLogicState()
    {
        AnimationSet fallback = AnimationSet.CreateFallback("Ghost");

        foreach (FighterLogicState state in Enum.GetValues<FighterLogicState>())
        {
            string clip = AnimationResolver.ResolveClipName(Fighter(state), fallback);
            Assert.NotNull(fallback.FindClip(clip));
        }
        Assert.Equal("idle", fallback.FallbackClip.Name);
    }

    [Fact]
    public void Animation_DoesNotMutateAuthoritativeState()
    {
        AnimationSet set = Load("Kai");
        FighterState fighter = Fighter(FighterLogicState.Active, "StandLightPunch", elapsed: 2);
        FighterState before = fighter;

        AnimationResolver.Resolve(fighter, set, 12);
        AnimationResolver.ResolveClipName(fighter, set);

        Assert.Equal(before, fighter); // readonly record struct：逐位相等
    }

    [Fact]
    public void TwoClocks_DoNotShareProgress()
    {
        var clockA = new FighterVisualClock();
        var clockB = new FighterVisualClock();

        clockA.Sync(0, FighterLogicState.Standing, null, 0);
        clockA.Sync(5, FighterLogicState.Standing, null, 0);
        clockB.Sync(0, FighterLogicState.Standing, null, 0);

        Assert.Equal(5, clockA.VisualFrame);
        Assert.Equal(0, clockB.VisualFrame);
    }

    // ─────────────────────────────────────────────────────────────
    // 配置错误处理
    // ─────────────────────────────────────────────────────────────

    private static string Mutate(string json, Action<JsonObject> mutate)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        mutate(root);
        return root.ToJsonString();
    }

    private static PresentationDataException ExpectFailure(string json)
    {
        PresentationDataException ex = Assert.Throws<PresentationDataException>(() =>
            PresentationDataLoader.Load(json, "presentation/Test.json"));
        Assert.Contains("presentation/Test.json", ex.Message);
        Assert.False(string.IsNullOrWhiteSpace(ex.FieldPath));
        Assert.False(string.IsNullOrWhiteSpace(ex.Reason));
        return ex;
    }

    [Fact]
    public void UnsupportedSchemaVersion_AndMissingFields_AreRejected()
    {
        ExpectFailure(Mutate(Json("Kai"), o => o["schemaVersion"] = 2));
        ExpectFailure(Mutate(Json("Kai"), o => o.Remove("schemaVersion")));
        ExpectFailure(Mutate(Json("Kai"), o => o["fighterId"] = ""));
        ExpectFailure(Mutate(Json("Kai"), o => o["animations"] = null));
        ExpectFailure(Mutate(Json("Kai"), o => o["states"] = null));
    }

    [Fact]
    public void MalformedJson_AndUnknownFields_AreRejected()
    {
        ExpectFailure("{ not json");
        PresentationDataException unknown = ExpectFailure(
            Mutate(Json("Kai"), o => o["typoField"] = 1));
        Assert.Contains("JSON 格式错误", unknown.Reason);
    }

    [Fact]
    public void DuplicateAnimationNames_AndBadReferences_AreRejected()
    {
        PresentationDataException duplicate = ExpectFailure(
            Mutate(Json("Kai"), o => o["animations"]![1]!["name"] = "idle"));
        Assert.Contains("animations[1]", duplicate.FieldPath);

        ExpectFailure(Mutate(Json("Kai"), o => o["states"]!["Standing"] = "nope"));
        ExpectFailure(Mutate(Json("Kai"), o => o["moves"]!["StandLightPunch"]!["active"] = "nope"));
        ExpectFailure(Mutate(Json("Kai"), o => o["moves"]!["StandLightPunch"]!["startup"] = null));
    }

    [Fact]
    public void IllegalFrameValues_AreRejected()
    {
        ExpectFailure(Mutate(Json("Kai"), o => o["animations"]![0]!["frames"] = null));
        ExpectFailure(Mutate(Json("Kai"), o => o["animations"]![0]!["frames"] = new JsonArray()));
        ExpectFailure(Mutate(Json("Kai"), o => o["animations"]![0]!["frames"]![0]!["width"] = 0));
        ExpectFailure(Mutate(Json("Kai"), o => o["animations"]![0]!["frames"]![0]!["height"] = 999));
        ExpectFailure(Mutate(Json("Kai"), o => o["animations"]![0]!["frames"]![0]!["color"] = "red"));
        ExpectFailure(Mutate(Json("Kai"), o => o["animations"]![0]!["frames"]![0]!["rotation"] = 181));
        ExpectFailure(Mutate(Json("Kai"), o => o["animations"]![0]!["framesPerStep"] = 0));
    }

    [Fact]
    public void LoadCatalog_RejectsDuplicateFighterId()
    {
        var documents = new[]
        {
            new PresentationDataDocument("presentation/Kai.json", Json("Kai")),
            new PresentationDataDocument("presentation/Kai.copy.json", Json("Kai")),
        };
        Assert.Throws<ArgumentException>(() => PresentationDataLoader.LoadCatalog(documents));
    }

    [Fact]
    public void LoadFromStream_MatchesString()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Json("Rei")));
        AnimationSet fromStream = PresentationDataLoader.Load(stream, "presentation/Rei.json");
        AnimationSet fromString = PresentationDataLoader.Load(Json("Rei"), "presentation/Rei.json");

        Assert.Equal(fromString.FighterId, fromStream.FighterId);
        Assert.Equal(fromString.Clips.Count, fromStream.Clips.Count);
        Assert.Equal(fromString.Moves["Fireball"].Active, fromStream.Moves["Fireball"].Active);
    }
}
