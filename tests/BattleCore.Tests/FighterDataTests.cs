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

    [Theory]
    [InlineData(-1, false)]
    [InlineData(26, false)]
    [InlineData(27, true)]
    [InlineData(31, true)]
    public void AuthoredMeleeTripPreservesReactionAndGroundTechWindow(int techAtFallTick, bool groundTech)
    {
        string json = Mutate(Json("Kai"), fighter =>
        {
            JsonNode hit = fighter["moves"]![0]!["attacks"]![0]!;
            hit["launchVelocityX"] = JsonNode.Parse("""{"numerator":2,"denominator":1}""");
            hit["launchVelocityY"] = JsonNode.Parse("""{"numerator":8,"denominator":1}""");
            hit["knockdownFrames"] = 13;
            hit["wakeupFrames"] = 25;
            hit["hitstopFrames"] = 3;
            hit["launchGravity"] = JsonNode.Parse("""{"numerator":1,"denominator":2}""");
            hit["launchReaction"] = JsonNode.Parse("""
                {"startInFall":true,"suppressOwnerProjectilesDuringFall":true,
                 "impactFrames":4,"riseFrames":8,"lieImpactFrames":3,
                 "lieDownFrames":13,"wakeupFrames":25,"transitionGravitySteps":2,
                 "bounceHorizontalRetention":{"numerator":3,"denominator":4},
                 "bounceVerticalVelocity":{"numerator":3,"denominator":1},
                 "bounceGravity":{"numerator":1,"denominator":2},
                 "groundTech":{"inputBufferFrames":5,"durationFrames":28,"slideFrames":16,
                   "initialSpeed":{"numerator":8,"denominator":1},
                   "velocityRetention":{"numerator":85,"denominator":100}}}
                """);
            JsonNode box = JsonNode.Parse("""
                {"minX":{"numerator":-4,"denominator":1},"maxX":{"numerator":4,"denominator":1},
                 "minY":{"numerator":12,"denominator":1},"maxY":{"numerator":30,"denominator":1}}
                """)!;
            JsonNode idle = JsonNode.Parse("""
                {"minX":{"numerator":-30,"denominator":1},"maxX":{"numerator":30,"denominator":1},
                 "minY":{"numerator":0,"denominator":1},"maxY":{"numerator":120,"denominator":1}}
                """)!;
            fighter["groundHurtboxes"] = new JsonObject
            {
                ["standing"] = new JsonArray(idle.DeepClone()),
                ["crouching"] = new JsonArray(idle),
                ["tripFall"] = new JsonArray(new JsonObject { ["frames"] = 1, ["boxes"] = new JsonArray(box) }),
            };
        });
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var sim = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = ActiveState("StandLightPunch", 2, p1X: -30, p2X: 30);
        BattleStepResult contact = sim.Step(state, FrameInput.Empty(state.Frame));
        Assert.Single(contact.Events, e => e.Type == BattleEventType.Hit);
        state = contact.State;
        for (int freeze = 0; freeze < 3; freeze++)
        {
            FighterState target = state.Fighter(1);
            Assert.Equal(LaunchReactionPhase.Shake, target.LaunchReaction!.Value.Phase);
            Assert.True(target.LaunchReaction.Value.RestrictsAirFollowupFrom(0));
            Assert.False(target.LaunchReaction.Value.RestrictsAirFollowupFrom(1));
            Assert.Equal(FixedPoint.Zero, target.PositionY);
            Assert.Equal(30, Assert.Single(FighterBoxes.GetHurtboxes(target, fighter)).MaxY);
            state = sim.Step(state, FrameInput.Empty(state.Frame)).State;
        }

        BattleState saved = state;
        state = sim.Step(state, FrameInput.Empty(state.Frame)).State;
        Assert.Equal(state.Fighters, sim.Step(saved, FrameInput.Empty(saved.Frame)).State.Fighters);
        FighterState launched = state.Fighter(1);
        Assert.Equal(LaunchReactionPhase.Fall, launched.LaunchReaction!.Value.Phase);
        Assert.True(launched.LaunchReaction.Value.RestrictsAirFollowupFrom(0));
        Assert.Equal(FixedPoint.FromFraction(15, 2), launched.PositionY);
        Assert.Equal(32, launched.PositionX);
        Assert.Equal(FixedPoint.FromFraction(75, 2), Assert.Single(FighterBoxes.GetHurtboxes(launched, fighter)).MaxY);
        bool performedTech = false;
        while (state.Fighter(1).LaunchReaction is { } reaction)
        {
            Assert.InRange(state.Frame, 1, 100);
            InputBits defense = reaction.Phase == LaunchReactionPhase.Fall && reaction.Ticks == techAtFallTick
                ? InputBits.LightPunch | InputBits.LightKick : InputBits.None;
            FrameInput input = F(state.Frame, InputBits.None, defense);
            BattleStepResult next = sim.Step(state, input);
            Assert.Equal(next.State.Fighters, sim.Step(state, input).State.Fighters);
            state = next.State;
            if (state.Fighter(1).LaunchReaction is { Phase: LaunchReactionPhase.Impact or LaunchReactionPhase.Tech } grounded)
                Assert.False(grounded.RestrictsAirFollowupFrom(0));
            if (state.Fighter(1).LaunchReaction is { Phase: LaunchReactionPhase.Tech, Ticks: 1 })
            {
                performedTech = true;
                Assert.Equal(100, state.Fighter(1).PositionX);
                Assert.Equal(8, state.Fighter(1).VelocityX);
            }
        }
        Assert.Equal(groundTech, performedTech);
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AuthoredHeavyReactionHoldsDuringHitPauseAndUsesDefenderTimeline(bool mirror, bool crouching)
    {
        string attackerJson = Mutate(Json("Kai"), o =>
        {
            JsonNode strike = o["moves"]![0]!["attacks"]![0]!;
            strike["groundHitReaction"] = "Heavy";
            strike["hitstunFrames"] = 5;
            strike["hitstopFrames"] = 3;
        });
        static object Number(int value) => new { numerator = value, denominator = 1 };
        static object Box(int left, int right, int top) =>
            new { minX = Number(left), maxX = Number(right), minY = Number(0), maxY = Number(top) };
        string defenderJson = Mutate(Json("Kai"), o =>
        {
            o["groundHurtboxes"] = JsonSerializer.SerializeToNode(new
            {
                standing = new[] { Box(-30, 30, 120) },
                crouching = new[] { Box(-30, 30, 70) },
                crouchRise = new[] { new { frames = 4, boxes = new[] { Box(-30, 30, 82) } } },
                heavyHitstun = new[] { new { frames = 2, boxes = new[] { Box(-19, 11, 104) } },
                    new { frames = 3, boxes = new[] { Box(-45, 7, 72) } } },
                crouchingHeavyHitstun = new[] { new { frames = 5, boxes = new[] { Box(-17, 12, 68) } } },
            });
        });
        FighterDefinition attacker = FighterDataLoader.Load(attackerJson);
        FighterDefinition defender = FighterDataLoader.Load(defenderJson);
        var simulation = new BattleSimulation(new FightRoster(attacker, defender));
        BattleState state = ActiveState("StandLightPunch", 2, p1X: mirror ? 30 : -30, p2X: mirror ? -30 : 30);
        state = new BattleState(0, [
            state.Fighter(0) with { Facing = mirror ? Facing.Left : Facing.Right },
            state.Fighter(1) with { Facing = mirror ? Facing.Right : Facing.Left,
                LogicState = crouching ? FighterLogicState.Crouching : FighterLogicState.Standing }], state.StateVersion);

        for (int frame = 0; frame <= 9; frame++)
        {
            FrameInput input = F(frame, InputBits.None, crouching && frame == 0 ? InputBits.Down : InputBits.None);
            BattleStepResult first = simulation.Step(state, input);
            Assert.Equal(first.State.Fighters, simulation.Step(state, input).State.Fighters);
            if (frame == 0)
                Assert.Single(first.Events, e => e.Type == BattleEventType.Hit);
            state = first.State;
            FighterState target = state.Fighter(1);
            FixedRect box = Assert.Single(FighterBoxes.GetHurtboxes(target, defender));
            int height = crouching ? (frame < 8 ? 68 : frame == 8 ? 70 : 82) : (frame <= 4 ? 104 : frame <= 7 ? 72 : 120);
            int right = crouching ? (frame < 8 ? mirror ? 12 : 17 : 30)
                : (frame <= 4 ? mirror ? 11 : 19 : frame <= 7 ? mirror ? 7 : 45 : 30);
            Assert.Equal(FixedPoint.FromInt(height), box.MaxY);
            Assert.Equal(FixedPoint.FromInt(right), box.MaxX - target.PositionX);
        }
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
    }

    [Fact]
    public void AuthoredProjectileGroundReactionFreezesAndClearsOnControlReturn()
    {
        static object Number(int value) => new { numerator = value, denominator = 1 };
        static object Box(int top) => new { minX = Number(-30), maxX = Number(30), minY = Number(0), maxY = Number(top) };
        string json = Mutate(Json("Kai"), o =>
        {
            JsonNode projectile = o["moves"]!.AsArray().SelectMany(m => m!["projectiles"]!.AsArray()).First()!;
            projectile["hit"]!["groundHitReaction"] = "HeavyLow";
            o["groundHurtboxes"] = JsonSerializer.SerializeToNode(new
            {
                standing = new[] { Box(120) },
                crouching = new[] { Box(70) },
                lowHeavyHitstun = new[] { new { frames = 120, boxes = new[] { Box(96) } } },
            });
        });
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var sim = new BattleSimulation(new FightRoster(fighter, fighter));
        MoveDefinition source = Assert.Single(fighter.Moves, move => move.Projectiles.Count != 0);
        ProjectileDefinition projectile = Assert.Single(source.Projectiles);
        BattleState state = BattleState.CreateDefault();
        var entity = new ProjectileState(1, 0, projectile.ProjectileTypeId, source.MoveId,
            state.Fighter(1).PositionX, 40, 0, 0, 4);
        state = new BattleState(0, state.Fighters, state.StateVersion, projectiles: [entity], nextProjectileId: 2);
        BattleStepResult first = sim.Step(state, F(0, InputBits.None));
        Assert.Single(first.Events, e => e.Type == BattleEventType.ProjectileHit);
        FighterState target = first.State.Fighter(1);
        Assert.Equal(FixedPoint.FromInt(96), Assert.Single(FighterBoxes.GetHurtboxes(target, fighter)).MaxY);
        state = first.State;
        for (int frame = 1; frame <= target.HitstopRemainingFrames + target.StunRemainingFrames; frame++)
        {
            BattleState snapshot = state;
            state = sim.Step(snapshot, F(frame, InputBits.None)).State;
            Assert.Equal(state.Fighters, sim.Step(snapshot, F(frame, InputBits.None)).State.Fighters);
            if (frame <= target.HitstopRemainingFrames)
                Assert.Equal(target.GroundReaction, state.Fighter(1).GroundReaction);
        }
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
        Assert.Equal(default, state.Fighter(1).GroundReaction);
    }

    [Fact]
    public void AuthoredGuardHurtboxStaysCrouchedThroughFreezeAfterRelease()
    {
        static object Number(int value) => new { numerator = value, denominator = 1 };
        static object Box(int top) => new { minX = Number(-30), maxX = Number(30), minY = Number(0), maxY = Number(top) };
        string defenderJson = Mutate(Json("Kai"), o => o["groundHurtboxes"] = JsonSerializer.SerializeToNode(new
        {
            standing = new[] { Box(120) }, crouching = new[] { Box(70) },
            standingGuard = new[] { Box(95) }, crouchingGuard = new[] { Box(65) },
            standingGuardReady = new[] { Box(96) }, crouchingGuardReady = new[] { Box(66) },
        }));
        FighterDefinition attacker = FighterDataLoader.Load(Json("Kai"));
        FighterDefinition defender = FighterDataLoader.Load(defenderJson);
        var sim = new BattleSimulation(new FightRoster(attacker, defender));
        BattleState state = ActiveState("StandLightPunch", 2, p1X: -30, p2X: 30);
        BattleStepResult first = sim.Step(state, F(0, InputBits.None, InputBits.Down | InputBits.Right));
        Assert.Single(first.Events, e => e.Type == BattleEventType.Blocked);
        Assert.Equal(FixedPoint.FromInt(65), Assert.Single(FighterBoxes.GetHurtboxes(first.State.Fighter(1), defender)).MaxY);
        state = sim.Step(first.State, F(1, InputBits.None)).State;
        Assert.Equal(FixedPoint.FromInt(65), Assert.Single(FighterBoxes.GetHurtboxes(state.Fighter(1), defender)).MaxY);
    }

    [Fact]
    public void Reentry_ExplicitSelfCancelStartsNewInstanceAndCanHitAgain()
    {
        string json = Mutate(Json("Kai"), o =>
        {
            JsonNode jab = o["moves"]![0]!;
            jab["cancels"] = JsonNode.Parse("""
                [{"targetMoveId":"StandLightPunch","windowFirstFrame":6,"windowLastFrame":10,
                  "phases":"Recovery","requiresHitConfirm":false,"requiresBlockConfirm":false,
                  "allowWhiff":true,"allowSameMove":true,"standingInputOnly":true,"maximumInputAge":0}]
                """);
        });
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var sim = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = new(0, new[] { FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left) }, BattleState.CreateDefault().StateVersion);
        var events = new List<BattleEvent>();
        for (int frame = 0; frame <= 17; frame++)
        {
            BattleStepResult step = sim.Step(state, F(frame, frame is 0 or 10 ? InputBits.LightPunch : InputBits.None));
            state = step.State;
            events.AddRange(step.Events);
            if (frame == 10)
            {
                FighterState attacker = state.Fighter(0);
                Assert.Equal(FighterLogicState.Startup, attacker.LogicState);
                Assert.Equal(10, attacker.MoveStartedFrame);
                Assert.Equal(0, attacker.MoveElapsedFrames);
                Assert.Equal(-1, attacker.HitConfirmFrame);
                Assert.Equal(-1, attacker.BlockConfirmFrame);
                Assert.Empty(FighterBoxes.GetActiveHitboxes(attacker, fighter));
            }
        }
        Assert.Equal(new[] { 0, 10 }, events.Where(evt => evt.Type == BattleEventType.MoveStarted).Select(evt => evt.Frame));
        Assert.Equal(new[] { 3, 13 }, events.Where(evt => evt.Type == BattleEventType.Hit).Select(evt => evt.Frame));
        Assert.Equal(10, state.Fighter(1).LastHitAttackStartFrame);
    }

    [Fact]
    public void DynamicDamage_LoadsOptInRulesAndUsesTheActiveTickRoll()
    {
        string json = Mutate(Json("Kai"), o =>
        {
            o["damageRules"] = JsonNode.Parse("""
                {"baseScale":{"numerator":1923,"denominator":200},
                 "attackMultiplier":{"numerator":4,"denominator":5},
                 "defenseMultiplier":{"numerator":1,"denominator":1},
                 "hitstunDefenseMultiplier":{"numerator":97,"denominator":100},
                 "varianceMaximum":2,"counterGraceTicks":2}
                """);
            JsonNode attack = o["moves"]![0]!["attacks"]![0]!;
            attack["damage"] = 0;
            attack["dynamicDamage"] = JsonNode.Parse("""{"normalBase":9,"counterBase":9,"useContactDivisor":false}""");
        });
        FighterDefinition attacker = FighterDataLoader.Load(json);
        var sim = new BattleSimulation(new FightRoster(attacker, FighterDataLoader.Load(Json("Kai"))));
        BattleState initial = ActiveState("StandLightPunch", 2, p1X: -30, p2X: 30);

        // Seed 1337 → Park–Miller 22470959 → range bucket 0 → floor(9 × 9.615) × .8 → 69.
        BattleStepResult step = sim.Step(initial, F(0, InputBits.None));
        Assert.Equal(69, Assert.Single(step.Events, evt => evt.Type == BattleEventType.Hit).Amount);
        Assert.Equal(931, step.State.Fighter(1).Health);
        Assert.Equal(step.State.Fighters, sim.Step(initial, F(0, InputBits.None)).State.Fighters);
    }

    [Theory]
    [InlineData(1u, 0, 69)]
    [InlineData(3u, 1, 61)]
    [InlineData(7u, 2, 54)]
    public void DynamicDamage_SeedsProduceLegalDistinctDamage(uint seed, int expectedVariance, int expectedDamage)
    {
        var simulation = DynamicSimulation();
        BattleState state = DynamicInitial(seed);
        state = simulation.Step(state, F(0, InputBits.LightPunch)).State;
        BattleStepResult step = simulation.Step(state, F(1, InputBits.None));
        Assert.Equal(expectedVariance, step.State.Fighter(0).DamageScript.Variance);
        Assert.Equal(expectedDamage, Assert.Single(step.Events, e => e.Type == BattleEventType.Hit).Amount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DynamicDamage_HitstopDivisorAndReplayFollowCommittedContact(bool firstMisses, bool guard)
    {
        var simulation = DynamicSimulation();
        BattleState initial = DynamicInitial(1337, firstMisses ? 100 : 60);
        var steps = new List<BattleStepResult>();
        var inputs = new List<FrameInput>();
        var contacts = new List<BattleEvent>();
        BattleState state = initial;
        for (int frame = 0; frame < 28; frame++)
        {
            inputs.Add(F(frame, frame == 0 ? InputBits.LightPunch : InputBits.None,
                guard && frame == 1 ? InputBits.Right : InputBits.None));
            BattleStepResult step = simulation.Step(state, inputs[^1]);
            contacts.AddRange(step.Events.Where(e => e.Type is BattleEventType.Hit or BattleEventType.Blocked));
            steps.Add(step);
            state = step.State;
        }
        Assert.Equal(firstMisses ? new[] { 4 } : new[] { 1, 7 }, contacts.Select(e => e.Frame));
        Assert.All(contacts, contact => Assert.Equal(guard ? BattleEventType.Blocked : BattleEventType.Hit, contact.Type));
        Assert.Equal(1, steps[0].State.Fighter(0).DamageScript.ContactDivisor);
        if (!firstMisses)
        {
            DamageScriptState hit = steps[1].State.Fighter(0).DamageScript;
            Assert.Equal(3, hit.Pause);
            Assert.Equal(hit.RandomState, steps[4].State.Fighter(0).DamageScript.RandomState);
            Assert.NotEqual(steps[1].State.Fighter(1).DamageScript.RandomState,
                steps[2].State.Fighter(1).DamageScript.RandomState); // Victim shake does not pause the script.
            Assert.Equal(2, steps[5].State.Fighter(0).DamageScript.ContactDivisor);
            Assert.Equal(2, steps[7].State.Fighter(0).DamageScript.ContactDivisor);
            if (!guard)
                Assert.Contains(contacts[1].Amount, new[] { 22, 29 });
        }
        else
        {
            Assert.Equal(1, steps[4].State.Fighter(0).DamageScript.ContactDivisor);
            Assert.Contains(contacts[0].Amount, new[] { 54, 61, 69 });
        }
        Assert.Equal(1, steps[^1].State.Fighter(0).DamageScript.ContactDivisor);

        BattleState replay = steps[1].State;
        for (int frame = 2; frame < steps.Count; frame++)
        {
            BattleStepResult step = simulation.Step(replay, inputs[frame]);
            Assert.Equal(steps[frame].State.Fighters, step.State.Fighters);
            Assert.Equal(steps[frame].Events, step.Events);
            replay = step.State;
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void DynamicDamage_RecoveryCarryExpiresOnThirdNonAttackTick(int attackAt, bool expectedCounter)
    {
        var simulation = DynamicSimulation();
        BattleState initial = DynamicInitial(1);
        FighterState target = initial.Fighter(1) with
        {
            LogicState = FighterLogicState.Recovery, PhaseRemainingFrames = 12,
            MoveId = "Heavy", MoveStartedFrame = 0, MoveElapsedFrames = 8,
        };
        FighterState source = initial.Fighter(0) with
        {
            DamageScript = DamageScriptState.FromSeed(1, 0) with { CounterAge = 1 },
        };
        BattleState state = new(1, new[] { source, target }, initial.StateVersion);
        for (int offset = 0; offset <= attackAt; offset++)
        {
            if (offset == attackAt)
                state = new BattleState(state.Frame, new[] { state.Fighter(0) with
                {
                    LogicState = FighterLogicState.Startup, PhaseRemainingFrames = 1,
                    MoveId = "Heavy", MoveStartedFrame = state.Frame - 1,
                }, state.Fighter(1) }, state.StateVersion);
            BattleStepResult result = simulation.Step(state, F(state.Frame, InputBits.None));
            state = result.State;
            if (offset != attackAt)
                continue;
            int age = state.Fighter(0).DamageScript.CounterAge;
            int variance = state.Fighter(0).DamageScript.Variance;
            int basis = expectedCounter ? 20 : 16;
            int expected = ((basis - variance) * 1923 / 200 * 4 + 2) / 5;
            Assert.Equal(expectedCounter ? attackAt + 2 : 0, age);
            Assert.Equal(expected, Assert.Single(result.Events, e => e.Type == BattleEventType.Hit).Amount);
        }
    }

    [Theory]
    [InlineData(60, false)]
    [InlineData(60, true)]
    [InlineData(100, false)]
    public void DynamicDamage_TradesAndKnockoutUseBothPreSettlementSnapshots(int distance, bool knockout)
    {
        var simulation = DynamicSimulation();
        BattleState state = DynamicInitial(1337, distance, knockout ? 1 : 1000);
        state = simulation.Step(state, F(0, InputBits.LightPunch, InputBits.HeavyPunch)).State;
        BattleStepResult step = simulation.Step(state, F(1, InputBits.None));
        BattleEvent[] hits = step.Events.Where(e => e.Type == BattleEventType.Hit).ToArray();
        Assert.Equal(distance == 60 ? 2 : 1, hits.Length);
        Assert.Equal(1, step.State.Fighter(0).DamageScript.CounterAge + (knockout ? 1 : 0));
        Assert.Equal(1, step.State.Fighter(1).DamageScript.CounterAge + (knockout ? 1 : 0));
        Assert.Null(step.State.Fighter(0).MoveId);
        Assert.Empty(FighterBoxes.GetActiveHitboxes(step.State.Fighter(0), simulation.Roster.Player1));
        Assert.Equal(0, step.State.Fighter(0).DamageScript.Pause);
        if (distance == 60)
            Assert.Equal(0, step.State.Fighter(1).DamageScript.Pause);
        if (knockout)
        {
            Assert.Contains(step.Events, evt => evt.Type == BattleEventType.DoubleKnockout);
            var next = simulation.Step(step.State, F(2, InputBits.LightPunch)).State;
            Assert.Equal(step.State.Fighters, next.Fighters);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DynamicDamage_MirroringKeepsRoundingAndAttackStream(int seat)
    {
        var simulation = DynamicSimulation();
        BattleState initial = DynamicInitial(3);
        FighterState[] fighters = initial.Fighters.ToArray();
        fighters[seat] = fighters[seat] with { DamageScript = DamageScriptState.FromSeed(3, 0) };
        BattleState state = new(0, fighters, initial.StateVersion);
        state = simulation.Step(state, F(0, seat == 0 ? InputBits.LightPunch : InputBits.None,
            seat == 1 ? InputBits.LightPunch : InputBits.None)).State;
        BattleStepResult step = simulation.Step(state, F(1, InputBits.None));
        BattleEvent hit = Assert.Single(step.Events, e => e.Type == BattleEventType.Hit);
        Assert.Equal(seat, hit.SourceFighterId);
        Assert.Equal(61, hit.Amount);
        Assert.Equal(847425747u, step.State.Fighter(seat).DamageScript.RandomState);
    }

    [Theory]
    [InlineData(1, 2, 1, 1, 2)] // 3 × .5 rounds away from zero.
    [InlineData(1, 2, 1, 10, 1)] // Nonzero damage under one applies the native minimum.
    [InlineData(0, 1, 1, 1, 0)]
    [InlineData(2, 1, 3, 2, 9)]
    public void DynamicDamage_MultipliesByBothFightersRulesBeforeRounding(int attackNumerator, int attackDenominator,
        int defenseNumerator, int defenseDenominator, int expected)
    {
        var baseSimulation = DynamicSimulation();
        FighterDefinition source = baseSimulation.Roster.Player1;
        FighterDefinition WithRules(string id, DamageRatio attack, DamageRatio defense) => new(id, source.Commands,
            source.Moves.Select(m => new MoveDefinition(m.MoveId, m.CommandId, m.StartupFrames, m.ActiveFrames,
                m.RecoveryFrames, attacks: m.Attacks.Select(a => new AttackDefinition(a.OffsetForward, a.OffsetUp,
                    a.HalfWidth, a.HalfHeight, firstActiveFrame: a.FirstActiveFrame, lastActiveFrame: a.LastActiveFrame,
                    damage: 0, contactGroup: a.ContactGroup, dynamicDamage: new DynamicAttackDamage(3, 3, false))).ToArray())).ToArray(),
            damageRules: new DynamicDamageRules(DamageRatio.One, attack, defense, DamageRatio.One, 0, 2));
        var sim = new BattleSimulation(new FightRoster(
            WithRules("Source", new DamageRatio(attackNumerator, attackDenominator), DamageRatio.One),
            WithRules("Target", DamageRatio.One, new DamageRatio(defenseNumerator, defenseDenominator))));
        BattleState state = sim.Step(DynamicInitial(1), F(0, InputBits.LightPunch)).State;
        Assert.Equal(expected, Assert.Single(sim.Step(state, F(1, InputBits.None)).Events, e => e.Type == BattleEventType.Hit).Amount);
    }

    private static BattleState DynamicInitial(uint seed, int distance = 60, int health = 1000) => new(0, new[]
    {
        FighterState.Create(0, health, -distance / 2, Facing.Right) with { DamageScript = DamageScriptState.FromSeed(seed, 0) },
        FighterState.Create(1, health, distance / 2, Facing.Left) with { DamageScript = DamageScriptState.FromSeed(seed, 1) },
    }, BattleState.CreateDefault().StateVersion);

    private static BattleSimulation DynamicSimulation()
    {
        var commands = new[]
        {
            new Commands.CommandDefinition("5A", Array.Empty<Commands.CommandDirection>(), InputBits.LightPunch),
            new Commands.CommandDefinition("5B", Array.Empty<Commands.CommandDirection>(), InputBits.HeavyPunch),
        };
        var rules = new DynamicDamageRules(new DamageRatio(1923, 200), new DamageRatio(4, 5), DamageRatio.One,
            new DamageRatio(97, 100), 2, 2);
        AttackDefinition Strike(int end, int group, int first = 0, int last = 0, int normal = 9, int counter = 9) =>
            new(offsetForward: FixedPoint.FromFraction(end, 2), offsetUp: 45,
                halfWidth: FixedPoint.FromFraction(end, 2), halfHeight: 40,
                firstActiveFrame: first, lastActiveFrame: last, damage: 0, hitstunFrames: 10, blockstunFrames: 10,
                hitstopByOutcome: new HitstopByOutcome(new(3, 5), new(3, 3)), contactGroup: group,
                dynamicDamage: new DynamicAttackDamage(normal, counter, group > 0));
        var fighter = new FighterDefinition("DynamicProbe", commands, new[]
        {
            new MoveDefinition("Double", "5A", 1, 4, 4, attacks: new[] { Strike(60, 1), Strike(90, 2, 3, 3) }),
            new MoveDefinition("Heavy", "5B", 1, 1, 20, attacks: new[] { Strike(90, 0, normal: 16, counter: 20) }),
        }, damageRules: rules);
        return new BattleSimulation(new FightRoster(fighter, fighter));
    }

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

    [Theory]
    [InlineData(false, false, 2, 5)]
    [InlineData(true, true, 3, 1)]
    public void MeleeHitstop_SelectsOutcomeAndFreezesEachSeatIndependently(
        bool blocked, bool mirrored, int attackerPause, int defenderPause)
    {
        string json = Mutate(Json("Kai"), o =>
        {
            JsonNode jab = o["moves"]!.AsArray().Single(m => (string?)m!["moveId"] == "StandLightPunch")!;
            jab["attacks"]![0]!["hitstopByOutcome"] = JsonNode.Parse("""
                {"hit":{"attackerFrames":2,"defenderFrames":5},
                 "block":{"attackerFrames":3,"defenderFrames":1}}
                """);
        });
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var sim = new BattleSimulation(new FightRoster(fighter, fighter));
        int attacker = mirrored ? 1 : 0;
        int defender = 1 - attacker;
        BattleState state = new(0, new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);
        InputBits back = !blocked ? InputBits.None : mirrored ? InputBits.Left : InputBits.Right;
        FrameInput Input(InputBits button) => mirrored ? F(state.Frame, back, button) : F(state.Frame, button, back);
        state = sim.Step(state, Input(InputBits.LightPunch)).State;

        BattleStepResult contact;
        do
        {
            contact = sim.Step(state, Input(InputBits.None));
            state = contact.State;
        } while (state.Frame < 8 && state.Fighter(defender).StunRemainingFrames == 0);

        Assert.Contains(contact.Events, e => e.Type == (blocked ? BattleEventType.Blocked : BattleEventType.Hit));
        Assert.Equal(attackerPause, state.Fighter(attacker).HitstopRemainingFrames);
        Assert.Equal(defenderPause, state.Fighter(defender).HitstopRemainingFrames);
        int elapsed = state.Fighter(attacker).MoveElapsedFrames;
        int stun = state.Fighter(defender).StunRemainingFrames;
        int health = state.Fighter(defender).Health;
        for (int tick = 1; tick <= Math.Max(attackerPause, defenderPause) + 1; tick++)
        {
            BattleStepResult next = sim.Step(state, Input(InputBits.None));
            state = next.State;
            Assert.Equal(Math.Max(0, attackerPause - tick), state.Fighter(attacker).HitstopRemainingFrames);
            Assert.Equal(Math.Max(0, defenderPause - tick), state.Fighter(defender).HitstopRemainingFrames);
            Assert.Equal(elapsed + Math.Max(0, tick - attackerPause), state.Fighter(attacker).MoveElapsedFrames);
            Assert.Equal(stun - Math.Max(0, tick - defenderPause), state.Fighter(defender).StunRemainingFrames);
            Assert.Equal(health, state.Fighter(defender).Health);
            Assert.DoesNotContain(next.Events, e => e.Type is BattleEventType.Hit or BattleEventType.Blocked);
        }
    }

    [Theory]
    [InlineData("{\"hit\":{\"attackerFrames\":0,\"defenderFrames\":0}}", ".block")]
    [InlineData("{\"hit\":{\"attackerFrames\":0}}", ".hit.defenderFrames")]
    [InlineData("{\"hit\":{\"attackerFrames\":31,\"defenderFrames\":0}}", ".hit.attackerFrames")]
    public void MeleeHitstop_RejectsMissingOrOutOfRangeFields(string value, string path)
    {
        string json = Mutate(Json("Kai"), o =>
            o["moves"]![0]!["attacks"]![0]!["hitstopByOutcome"] = JsonNode.Parse(value));

        Assert.EndsWith(".hitstopByOutcome" + path, ExpectFailure(json).FieldPath);
    }

    [Theory]
    [InlineData(false, false, 13, 11, 3156914)]
    [InlineData(true, false, 11, 10, 3073532)]
    [InlineData(true, true, 11, 10, 2789212)]
    public void GroundReaction_MatchesMeasuredSlideAfterIndependentPause(bool blocked, bool crouch, int pause,
        int stun, int expectedDisplacement)
    {
        string json = Mutate(Json("Kai"), o =>
        {
            o["movement"]!["groundReaction"] = JsonNode.Parse("""
                {"velocityRetention":{"numerator":17,"denominator":20},
                 "lastHitStepScale":{"numerator":3,"denominator":5},
                 "crouchGuardVelocityRetention":{"numerator":41,"denominator":50}}
                """);
            JsonNode jab = o["moves"]!.AsArray().Single(m => (string?)m!["moveId"] == "StandLightPunch")!;
            JsonNode attack = jab["attacks"]![0]!;
            attack["groundKnockback"] = JsonNode.Parse("""{"numerator":6,"denominator":1}""");
            attack["blockKnockback"] = attack["groundKnockback"]!.DeepClone();
            attack["hitstunFrames"] = 11;
            attack["blockstunFrames"] = 10;
            attack["hitstopByOutcome"] = JsonNode.Parse("""
                {"hit":{"attackerFrames":11,"defenderFrames":13},
                 "block":{"attackerFrames":11,"defenderFrames":11}}
                """);
        });
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var sim = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = new(0, new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        for (int tick = 0; tick <= 3; tick++)
            state = sim.Step(state, F(state.Frame, tick == 0 ? InputBits.LightPunch : InputBits.None,
                blocked && tick == 3 ? InputBits.Right : InputBits.None)).State;

        Assert.Equal(stun, state.Fighter(1).StunRemainingFrames);
        Assert.Equal(pause, state.Fighter(1).HitstopRemainingFrames);
        FixedPoint contactX = state.Fighter(1).PositionX;
        for (int tick = 0; tick < pause; tick++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            Assert.Equal(contactX, state.Fighter(1).PositionX);
        }
        FixedPoint speed = 6;
        FixedPoint expectedX = contactX;
        for (int tick = 1; tick < stun; tick++)
        {
            bool holdCrouch = crouch && tick <= 6;
            state = sim.Step(state, F(state.Frame, InputBits.None,
                holdCrouch ? InputBits.Down | InputBits.Right : InputBits.None)).State;
            expectedX += !blocked && tick == stun - 1 ? speed * FixedPoint.FromFraction(3, 5) : speed;
            Assert.Equal(expectedX, state.Fighter(1).PositionX);
            speed *= holdCrouch ? FixedPoint.FromFraction(41, 50) : FixedPoint.FromFraction(17, 20);
        }
        state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).VelocityX);
        Assert.InRange((state.Fighter(1).PositionX - contactX).RawValue,
            FixedPoint.FromFraction(expectedDisplacement - 1000, 100000).RawValue,
            FixedPoint.FromFraction(expectedDisplacement + 1000, 100000).RawValue);
    }

    [Fact]
    public void StandingHurtboxOnCrouchEntry_UsesCurrentInputForGuardResult()
    {
        string json = Mutate(Json("Kai"), o => o["groundHurtboxes"] = JsonNode.Parse("""
            {"standingOnCrouchEntry":true,
             "standing":[{"minX":{"numerator":-20,"denominator":1},"minY":{"numerator":1,"denominator":1},
                           "maxX":{"numerator":19,"denominator":1},"maxY":{"numerator":88,"denominator":1}}],
             "crouching":[{"minX":{"numerator":-20,"denominator":1},"minY":{"numerator":1,"denominator":1},
                            "maxX":{"numerator":19,"denominator":1},"maxY":{"numerator":72,"denominator":1}}]}
            """));
        FighterDefinition defender = FighterDataLoader.Load(json);
        var attacker = new FighterDefinition("Probe", defender.Commands, new[]
        {
            new MoveDefinition("Probe", "5A", 1, 2, 1, attacks: new[]
            {
                new AttackDefinition(31, 80, 27, 7, damage: 7, blockstunFrames: 10),
            }),
        });
        var sim = new BattleSimulation(new FightRoster(attacker, defender));
        BattleState state = new(0, new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 30, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);
        InputBits downBack = InputBits.Down | InputBits.Right;
        BattleState standing = sim.Step(state, F(0, InputBits.LightPunch)).State;
        BattleState crouched = sim.Step(state, F(0, InputBits.LightPunch, downBack)).State;

        BattleStepResult late = sim.Step(standing, F(1, InputBits.None, downBack));
        BattleStepResult early = sim.Step(crouched, F(1, InputBits.None, downBack));
        Assert.Contains(late.Events, e => e.Type == BattleEventType.Blocked);
        Assert.Equal(FighterLogicState.Blockstun, late.State.Fighter(1).LogicState);
        Assert.DoesNotContain(early.Events, e => e.Type is BattleEventType.Blocked or BattleEventType.Hit);
        Assert.Equal(FighterLogicState.Crouching, early.State.Fighter(1).LogicState);
        Assert.Equal(1000, late.State.Fighter(1).Health);
        Assert.Equal(1000, early.State.Fighter(1).Health);
    }

    [Theory]
    [InlineData(0, 77, false, true)]
    [InlineData(0, 78, false, false)]
    [InlineData(0, 78, true, true)]
    [InlineData(0, 79, true, false)]
    [InlineData(1, 77, false, true)]
    [InlineData(1, 78, false, false)]
    [InlineData(1, 78, true, true)]
    [InlineData(1, 79, true, false)]
    public void GroundHurtboxes_UseSourceRectanglesAndHonorTheAttacksEdgeRule(
        int seat, int distance, bool contactOnEdge, bool hits)
    {
        string json = Mutate(Json("Kai"), o =>
        {
            o["groundHurtboxes"] = JsonNode.Parse("""
                {"standing":[{"minX":{"numerator":-20,"denominator":1},"minY":{"numerator":1,"denominator":1},
                              "maxX":{"numerator":19,"denominator":1},"maxY":{"numerator":88,"denominator":1}},
                             {"minX":{"numerator":-8,"denominator":1},"minY":{"numerator":81,"denominator":1},
                              "maxX":{"numerator":15,"denominator":1},"maxY":{"numerator":104,"denominator":1}}],
                 "crouching":[{"minX":{"numerator":-20,"denominator":1},"minY":{"numerator":1,"denominator":1},
                               "maxX":{"numerator":19,"denominator":1},"maxY":{"numerator":72,"denominator":1}}]}
                """);
        });
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var attackerDefinition = new FighterDefinition("BoxProbe", fighter.Commands, new[]
        {
            new MoveDefinition("Probe", "5A", 1, 1, 1, attacks: new[]
            {
                new AttackDefinition(FixedPoint.FromFraction(63, 2), FixedPoint.FromFraction(161, 2),
                    FixedPoint.FromFraction(55, 2), FixedPoint.FromFraction(15, 2), contactOnEdge: contactOnEdge),
            }),
        });
        FighterState attacker = FighterState.Create(seat, 1000, 0, seat == 0 ? Facing.Right : Facing.Left) with
        {
            LogicState = FighterLogicState.Active, MoveId = "Probe", PhaseRemainingFrames = 1, MoveStartedFrame = 0,
        };
        FighterState target = FighterState.Create(1 - seat, 1000, seat == 0 ? distance : -distance,
            seat == 0 ? Facing.Left : Facing.Right);
        IReadOnlyList<AttackContact> Detect(FighterState defender) => seat == 0
            ? HitDetection.Detect(attacker, defender, attackerDefinition, fighter)
            : HitDetection.Detect(defender, attacker, fighter, attackerDefinition);
        Assert.Equal(hits ? 1 : 0, Detect(target).Count);
        Assert.Empty(Detect(target with { LogicState = FighterLogicState.Crouching }));
    }

    [Fact]
    public void GroundHurtboxes_RejectExtremeLocalCoordinatesBeforeMirroring()
    {
        string json = Mutate(Json("Kai"), o => o["groundHurtboxes"] = JsonNode.Parse("""
            {"standing":[{"minX":{"numerator":-140737488355328,"denominator":1},
                           "minY":{"numerator":0,"denominator":1},
                           "maxX":{"numerator":1,"denominator":1},"maxY":{"numerator":1,"denominator":1}}],
             "crouching":[{"minX":{"numerator":0,"denominator":1},"minY":{"numerator":0,"denominator":1},
                            "maxX":{"numerator":1,"denominator":1},"maxY":{"numerator":1,"denominator":1}}]}
            """));

        Assert.Contains("groundHurtboxes", ExpectFailure(json).FieldPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundHitstunSourceRectanglesRejectPrematureWaveContact(bool mirrored)
    {
        const string box = """
            [{"minX":{"numerator":-20,"denominator":1},"minY":{"numerator":1,"denominator":1},
              "maxX":{"numerator":19,"denominator":1},"maxY":{"numerator":80,"denominator":1}}]
            """;
        FighterDefinition Load(bool configured) => FighterDataLoader.Load(Mutate(Json("Kai"), data =>
        {
            var ground = new JsonObject { ["standing"] = JsonNode.Parse(box), ["crouching"] = JsonNode.Parse(box) };
            if (configured)
            {
                ground["hitstun"] = JsonNode.Parse(box);
                ground["hitstunRecoveryFrames"] = 6;
            }
            data["groundHurtboxes"] = ground;
        }));

        int direction = mirrored ? -1 : 1;
        var target = FighterState.Create(1, 1000, 0, mirrored ? Facing.Right : Facing.Left) with
        {
            LogicState = FighterLogicState.Hitstun,
            StunRemainingFrames = 3,
            PositionX = direction * FixedPoint.FromFraction(948852, 10000),
        };
        FixedRect wave = mirrored ? new FixedRect(-66, -35, 1, 64) : new FixedRect(35, 66, 1, 64);
        FighterDefinition legacy = Load(false);
        Assert.Contains(FighterBoxes.GetHurtboxes(target, legacy), wave.IntersectsOrTouches);
        FighterDefinition configured = Load(true);
        FixedRect source = Assert.Single(FighterBoxes.GetHurtboxes(target, configured));
        Assert.False(wave.IntersectsOrTouches(source));
        FixedPoint facingEdge = mirrored ? -source.MaxX : source.MinX;
        Assert.Equal(direction * target.PositionX - 19, facingEdge);
        Assert.Contains(FighterBoxes.GetHurtboxes(target with { StunRemainingFrames = 7 }, configured), wave.IntersectsOrTouches);
        Assert.Contains(FighterBoxes.GetHurtboxes(target with { HitstopRemainingFrames = 1 }, configured), wave.IntersectsOrTouches);
    }

    [Theory]
    [InlineData(0, 128, false)]
    [InlineData(0, 129, true)]
    [InlineData(0, 130, true)]
    [InlineData(1, 128, false)]
    [InlineData(1, 129, true)]
    [InlineData(1, 130, true)]
    public void MoveMinimumStartDistance_GatesTheCommandFromEitherSeat(int seat, int twiceDistance, bool starts)
    {
        string json = Mutate(Json("Kai"), o => o["moves"]!.AsArray()
            .Single(m => (string?)m!["moveId"] == "StandLightPunch")!["minimumStartDistance"] =
                JsonNode.Parse("""{"numerator":4194305,"denominator":65536}"""));
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState initial = new(0, new[]
        {
            FighterState.Create(0, 1000, -30, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with { PositionX = FixedPoint.FromFraction(twiceDistance, 2) - 30 },
        }, BattleState.CreateDefault().StateVersion);

        BattleStepResult result = simulation.Step(initial, F(0,
            seat == 0 ? InputBits.LightPunch : InputBits.None,
            seat == 1 ? InputBits.LightPunch : InputBits.None));

        Assert.Equal(starts ? "StandLightPunch" : null, result.State.Fighter(seat).MoveId);
        Assert.Equal(starts, result.Events.Any(e => e.Type == BattleEventType.MoveStarted));
    }

    [Fact]
    public void MoveMinimumStartDistance_DoesNotOverflowBeforeStageClampsPositions()
    {
        var simulation = new BattleSimulation();
        BattleState initial = new(0, new[]
        {
            FighterState.Create(0, 1000, 0, Facing.Right) with { PositionX = FixedPoint.MinValue },
            FighterState.Create(1, 1000, 0, Facing.Left) with { PositionX = FixedPoint.MaxValue },
        }, BattleState.CreateDefault().StateVersion);

        BattleState next = simulation.Step(initial, F(0, InputBits.None)).State;

        Assert.Equal(simulation.Stage.MinX, next.Fighter(0).PositionX);
        Assert.Equal(simulation.Stage.MaxX, next.Fighter(1).PositionX);
    }

    [Theory]
    [InlineData(0, false, 184, true)]
    [InlineData(1, false, 185, false)]
    [InlineData(1, true, 256, true)]
    [InlineData(0, true, 257, false)]
    public void CloseNormalSelection_UsesBothFacingBodyWidthsAtTheInputTick(
        int seat, bool heavy, int quarters, bool near)
    {
        string json = Mutate(Json("Kai"), o =>
        {
            o["groundBodyWidths"] = JsonNode.Parse("""
                {"front":{"numerator":15,"denominator":1},"back":{"numerator":16,"denominator":1}}
                """);
            string command = heavy ? "5B" : "5A";
            JsonArray moves = o["moves"]!.AsArray();
            JsonNode far = moves.Single(m => (string?)m!["commandId"] == "5A")!;
            if (heavy)
            {
                o["commands"]!.AsArray().Add(JsonNode.Parse("""
                    {"commandId":"5B","directions":[],"buttons":["HeavyPunch"],"maxSequenceFrames":1,
                     "buttonToleranceFrames":0,"priority":0,"bufferLifetimeFrames":1,"chargeHoldFrames":0,
                     "chargeReleaseToleranceFrames":0}
                    """));
                far["commandId"] = command;
            }
            JsonNode close = far.DeepClone();
            close["moveId"] = "Close";
            close["maximumStartBodyDistance"] = new JsonObject { ["numerator"] = heavy ? 34 : 16, ["denominator"] = 1 };
            moves.Insert(0, close);
        });
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var sim = new BattleSimulation(new FightRoster(fighter, fighter));
        FixedPoint half = FixedPoint.FromFraction(quarters, 8);
        BattleState initial = new(0, new[]
        {
            FighterState.Create(0, 1000, 0, Facing.Right) with { PositionX = -half },
            FighterState.Create(1, 1000, 0, Facing.Left) with { PositionX = half },
        }, BattleState.CreateDefault().StateVersion);
        InputBits punch = heavy ? InputBits.HeavyPunch : InputBits.LightPunch;

        BattleState started = sim.Step(initial, F(0, seat == 0 ? punch : InputBits.None,
            seat == 1 ? punch : InputBits.None)).State;
        Assert.Equal(near ? "Close" : "StandLightPunch", started.Fighter(seat).MoveId);
        Assert.Equal(half, started.Fighter(1).PositionX);

        BattleState during = sim.Step(started, F(1, seat == 1 ? InputBits.Left : InputBits.None,
            seat == 0 ? InputBits.Right : InputBits.None)).State;
        Assert.True(during.Fighter(1).PositionX - during.Fighter(0).PositionX > half + half);
        Assert.Equal(started.Fighter(seat).MoveId, during.Fighter(seat).MoveId);
        Assert.Equal(0, during.Fighter(seat).MoveStartedFrame);
    }

    [Theory]
    [InlineData(Facing.Left, false, 0, true)]
    [InlineData(Facing.Left, false, 1, false)]
    [InlineData(Facing.Right, false, 1, true)]
    [InlineData(Facing.Left, true, 0, false)]
    public void CloseNormalSelection_UsesOpponentsFacingEdgeAndAirWidth(
        Facing opponentFacing, bool airborne, int distanceBeyond46Raw, bool near)
    {
        var command = new Commands.CommandDefinition("5A", Array.Empty<Commands.CommandDirection>(), InputBits.LightPunch);
        var attacker = new FighterDefinition("Attacker", new[] { command }, new[]
        {
            new MoveDefinition("Near", "5A", 3, 4, 5, maximumStartBodyDistance: 16),
            new MoveDefinition("Far", "5A", 3, 4, 5),
        }, groundBodyWidths: new GroundBodyWidths(15, 16));
        var opponent = new FighterDefinition("Opponent", Array.Empty<Commands.CommandDefinition>(), Array.Empty<MoveDefinition>(),
            groundBodyWidths: new GroundBodyWidths(15, 16), airBodyWidths: new GroundBodyWidths(11, 11));
        var sim = new BattleSimulation(new FightRoster(attacker, opponent));
        var target = FighterState.Create(1, 1000, 46, opponentFacing) with
        {
            PositionX = FixedPoint.FromRaw(FixedPoint.FromInt(46).RawValue + distanceBeyond46Raw),
            LogicState = airborne ? FighterLogicState.Airborne : FighterLogicState.Standing,
            PositionY = airborne ? 50 : 0,
            JumpKind = airborne ? JumpKind.HighJump : JumpKind.None,
        };
        var initial = new BattleState(0, new[] { FighterState.Create(0, 1000, 0, Facing.Right), target },
            BattleState.CreateDefault().StateVersion);

        Assert.Equal(near ? "Near" : "Far", sim.Step(initial, F(0, InputBits.LightPunch)).State.Fighter(0).MoveId);
    }

    [Theory]
    [InlineData(Facing.Right, -20, 19)]
    [InlineData(Facing.Left, -19, 20)]
    public void NeutralJumpHurtboxes_MirrorWithoutReplacingMovingOrUnconfiguredAirHurtboxes(
        Facing facing, int left, int right)
    {
        string json = Mutate(Json("Kai"), document => document["neutralAirHurtboxes"] = JsonNode.Parse("""
            [{"minX":{"numerator":-20,"denominator":1},"minY":{"numerator":33,"denominator":1},
              "maxX":{"numerator":19,"denominator":1},"maxY":{"numerator":96,"denominator":1}}]
            """));
        FighterDefinition definition = FighterDataLoader.Load(json);
        var neutral = FighterState.Create(0, 1000, 0, facing) with
        {
            LogicState = FighterLogicState.Airborne, PositionY = 60, JumpKind = JumpKind.HighJump,
        };
        Assert.Equal(new FixedRect(left, right, 93, 156), Assert.Single(FighterBoxes.GetHurtboxes(neutral, definition)));
        Assert.Equal(FighterBoxes.GetHurtbox(neutral), Assert.Single(FighterBoxes.GetHurtboxes(neutral,
            FighterDataLoader.Load(Json("Kai")))));
        var moving = neutral with { VelocityX = 3 };
        Assert.Equal(FighterBoxes.GetHurtbox(moving), Assert.Single(FighterBoxes.GetHurtboxes(moving, definition)));
    }

    [Fact]
    public void MoveHurtboxTimeline_UsesAuthoritativeElapsedAndMirrors()
    {
        string json = Mutate(Json("Kai"), o => o["moves"]!.AsArray()
            .Single(m => (string?)m!["moveId"] == "StandLightPunch")!["hurtboxTimeline"] =
                JsonNode.Parse("""
                [{"frames":3,"boxes":[{"minX":{"numerator":-4,"denominator":1},"minY":{"numerator":60,"denominator":1},
                                       "maxX":{"numerator":11,"denominator":1},"maxY":{"numerator":90,"denominator":1}}]},
                 {"frames":8,"boxes":[{"minX":{"numerator":-4,"denominator":1},"minY":{"numerator":60,"denominator":1},
                                       "maxX":{"numerator":40,"denominator":1},"maxY":{"numerator":90,"denominator":1}}]}]
                """));
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = BattleState.CreateDefault();
        for (int tick = 0; tick <= 11; tick++)
        {
            InputBits input = tick == 0 ? InputBits.LightPunch : InputBits.None;
            state = simulation.Step(state, F(tick, input, input)).State;
            for (int seat = 0; seat < 2; seat++)
            {
                FighterState current = state.Fighter(seat);
                FixedRect box = Assert.Single(FighterBoxes.GetHurtboxes(current, fighter));
                if (tick == 11)
                {
                    Assert.Null(current.MoveId);
                    Assert.Equal(FighterBoxes.GetHurtbox(current), box);
                    continue;
                }
                int forward = tick < 3 ? 11 : 40;
                Assert.Equal(seat == 0 ? new FixedRect(-124, -120 + forward, 60, 90)
                    : new FixedRect(120 - forward, 124, 60, 90), box);
            }
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"frames\":0,\"boxes\":[]}]")]
    [InlineData("[{\"frames\":1,\"boxes\":[]}]")]
    public void MoveHurtboxTimeline_RejectsInvalidCoverage(string value)
    {
        string json = Mutate(Json("Kai"), o =>
            o["moves"]![0]!["hurtboxTimeline"] = JsonNode.Parse(value));

        Assert.Contains("moves[0]", ExpectFailure(json).FieldPath);
    }

    [Theory]
    [InlineData(FighterLogicState.Standing, 30)]
    [InlineData(FighterLogicState.Startup, 54)]
    [InlineData(FighterLogicState.Active, 54)]
    [InlineData(FighterLogicState.Recovery, 30)]
    public void MeleeCounterDamage_DependsOnDefendersAttackPhase(FighterLogicState defendingPhase, int expected)
    {
        string json = Mutate(Json("Kai"), o => o["moves"]!.AsArray()
            .Single(m => (string?)m!["moveId"] == "StandLightPunch")!["attacks"]![0]!["counterDamage"] = 54);
        FighterDefinition attacker = FighterDataLoader.Load(json);
        var defender = new FighterDefinition("Defender", new[]
        {
            new Commands.CommandDefinition("5A", Array.Empty<Commands.CommandDirection>(), InputBits.LightPunch),
        }, new[] { new MoveDefinition("Probe", "5A", 4, 4, 4) });
        var simulation = new BattleSimulation(new FightRoster(attacker, defender));
        BattleState initial = ActiveState("StandLightPunch", 2, p1X: -30, p2X: 30);
        FighterState target = initial.Fighter(1);
        if (defendingPhase != FighterLogicState.Standing)
            target = target with { LogicState = defendingPhase, MoveId = "Probe", PhaseRemainingFrames = 3, MoveStartedFrame = 0 };

        BattleStepResult result = simulation.Step(new BattleState(0,
            new[] { initial.Fighter(0), target }, initial.StateVersion), F(0, InputBits.None));

        Assert.Equal(expected, Assert.Single(result.Events, e => e.Type == BattleEventType.Hit).Amount);
        Assert.Equal(1000 - expected, result.State.Fighter(1).Health);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(7)]
    public void MultipleMovesReplayTradeWithoutInheritingAnAttackInstance(int heavyActive)
    {
        var commands = new[]
        {
            new Commands.CommandDefinition("5A", Array.Empty<Commands.CommandDirection>(), InputBits.LightPunch,
                bufferLifetimeFrames: 0),
            new Commands.CommandDefinition("5B", Array.Empty<Commands.CommandDirection>(), InputBits.HeavyPunch,
                bufferLifetimeFrames: 0),
        };
        var hurt = new GroundHurtboxes(new[] { new FixedRect(-20, 19, 1, 88) },
            new[] { new FixedRect(-20, 19, 1, 72) });
        AttackDefinition Attack(int offset, int damage, int counter, int stun) => new(offset, 76, 27, 8,
            damage: damage, counterDamage: counter, hitstunFrames: stun, hitstopByOutcome: new(new(11, 13), new(11, 11)));
        var fighter = new FighterDefinition("TestNormals", commands, new[]
        {
            new MoveDefinition("Light", "5A", 3, 4, 5, MoveAllowedStates.Standing, new[] { Attack(31, 46, 54, 11) }),
            new MoveDefinition("Heavy", "5B", 5, heavyActive, 25 - heavyActive, MoveAllowedStates.Standing,
                new[] { Attack(40, 115, 154, 19) }, minimumStartDistance: 65),
        }, groundHurtboxes: hurt);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState initial = new(0, new[]
        {
            FighterState.Create(0, 1000, -36, Facing.Right),
            FighterState.Create(1, 1000, 36, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        FrameInput[] inputs = Enumerable.Range(0, 66).Select(i => F(i,
            i == 0 ? InputBits.HeavyPunch : i == 38 ? InputBits.LightPunch : InputBits.None,
            i == 2 ? InputBits.LightPunch : InputBits.None)).ToArray();
        BattleState current = initial;
        var committed = new List<BattleStepResult>();
        foreach (FrameInput input in inputs)
        {
            BattleStepResult step = simulation.Step(current, input);
            committed.Add(step);
            current = step.State;
        }

        Assert.Equal("Heavy", committed[0].State.Fighter(0).MoveId);
        Assert.Equal("Light", committed[2].State.Fighter(1).MoveId);
        Assert.Equal(new[] { 54, 154 }, committed[5].Events.Where(e => e.Type == BattleEventType.Hit).Select(e => e.Amount));
        Assert.All(committed[5].State.Fighters, f =>
        {
            Assert.Null(f.MoveId);
            Assert.Equal(0, f.MoveElapsedFrames);
            Assert.Equal(13, f.HitstopRemainingFrames);
        });
        Assert.Equal("Light", committed[38].State.Fighter(0).MoveId);
        Assert.Equal(46, Assert.Single(committed[41].Events, e => e.Type == BattleEventType.Hit).Amount);
        Assert.Equal(800, current.Fighter(1).Health);

        BattleState replay = committed[4].State;
        for (int i = 5; i < inputs.Length; i++)
        {
            BattleStepResult step = simulation.Step(replay, inputs[i]);
            Assert.Equal(committed[i].Events, step.Events);
            Assert.Equal(committed[i].State.Fighters, step.State.Fighters);
            replay = step.State;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupedMeleeContact_ResolvesOncePerPulseAndRestartsForNextMove(bool guard)
    {
        string json = Mutate(Json("Kai"), document =>
        {
            var move = document["moves"]!.AsArray()
                .Single(value => (string?)value!["moveId"] == "StandLightPunch")!;
            move["activeFrames"] = 3;
            var attacks = move["attacks"]!.AsArray();
            var first = attacks[0]!;
            first["firstActiveFrame"] = 0;
            first["lastActiveFrame"] = 1;
            first["contactGroup"] = 1;
            first["groundKnockback"]!["numerator"] = 0;
            first["blockKnockback"]!["numerator"] = 0;
            first["groundKnockbackFriction"]!["numerator"] = 0;
            var second = first.DeepClone();
            second["firstActiveFrame"] = 2;
            second["lastActiveFrame"] = 2;
            second["contactGroup"] = 2;
            second["damageAfterPriorContact"] = 9;
            attacks.Add(second);
        });
        FighterDefinition fighter = FighterDataLoader.Load(json);
        var simulation = new BattleSimulation(new FightRoster(fighter, fighter));
        BattleState state = ActiveState("StandLightPunch", 3, p1X: -30, p2X: 30);
        var contacts = new List<BattleEvent>();
        var steps = new List<BattleStepResult>();
        var inputs = new List<FrameInput>();
        for (int i = 0; i < 60; i++)
        {
            InputBits p1 = i == 40 ? InputBits.LightPunch : InputBits.None;
            InputBits p2 = guard && i is 0 or 43 ? InputBits.Right : InputBits.None;
            inputs.Add(F(i, p1, p2));
            BattleStepResult step = simulation.Step(state, inputs[^1]);
            steps.Add(step);
            contacts.AddRange(step.Events.Where(e => e.Type is BattleEventType.Hit or BattleEventType.Blocked));
            state = step.State;
        }
        Assert.Equal(4, contacts.Count);
        Assert.All(contacts, contact => Assert.Equal(guard ? BattleEventType.Blocked : BattleEventType.Hit, contact.Type));
        if (!guard)
            Assert.Equal(new[] { 30, 9, 30, 9 }, contacts.Select(contact => contact.Amount));

        BattleState replay = steps[0].State;
        Assert.Equal(1u, replay.Fighter(1).LastHitContactGroups);
        for (int i = 1; i < steps.Count; i++)
        {
            BattleStepResult step = simulation.Step(replay, inputs[i]);
            Assert.Equal(steps[i].Events, step.Events);
            Assert.Equal(steps[i].State.Fighters, step.State.Fighters);
            replay = step.State;
        }
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
