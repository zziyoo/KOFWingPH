using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 28：Terry State 300（前＋轻拳）的真实命令接入——根组合或 Helper 的入口判据、
/// 保留指令绑定真实招式的消费迁移、各来源取消窗口与出边，全部对齐已验收的原版轨迹。
/// </summary>
public sealed class ForwardLightPunchTests
{
    private const string Light = "Light";
    private const string Heavy = "Heavy";
    private const string BackKnuckle = "BackKnuckle";
    private const string Wave = "Wave";

    private static readonly CommandBufferPolicy LatchPolicy = new(true, 4, 1, 1, helperRunsDuringGlobalPause: true);

    private static CommandDefinition ForwardLightPunch(int priority = 4) => new(
        "ForwardLightPunch", [CommandDirection.Forward], InputBits.LightPunch,
        maxSequenceFrames: 1, buttonToleranceFrames: 0, priority: priority, bufferLifetimeFrames: 0,
        bufferPolicy: LatchPolicy, reserveGroundInput: true, reserveOnEdgeDirection: true);

    private static MoveDefinition BackKnuckleMove(string commandId = "ForwardLightPunch", bool withWave = true) => new(
        BackKnuckle, commandId, 11, 9, 27, attacks: Attack(),
        cancels: withWave
            ? [new CancelDefinition(Wave, 18, 19, MoveCancelPhases.Active,
                requiresHitConfirm: true, requiresBlockConfirm: true)]
            : []);

    private static IReadOnlyList<AttackDefinition> Attack() =>
    [
        new(FixedPoint.FromInt(59), FixedPoint.FromInt(76), FixedPoint.FromInt(23), FixedPoint.FromInt(15),
            0, 2, AttackHeight.High, hitstopFrames: 13, contactGroup: 1),
        new(FixedPoint.FromInt(67), FixedPoint.FromInt(72), FixedPoint.FromInt(31), FixedPoint.FromInt(15),
            6, 8, AttackHeight.Low, hitstopFrames: 13, contactGroup: 2),
    ];

    private static FighterDefinition Fighter() => new("Test",
    [
        new("5A", [], InputBits.LightPunch, priority: 2, bufferLifetimeFrames: 2),
        new("5B", [], InputBits.HeavyPunch, priority: 1, bufferLifetimeFrames: 2),
        new("236A", [CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward],
            InputBits.LightPunch, maxSequenceFrames: 16, buttonToleranceFrames: 0, priority: 8,
            bufferLifetimeFrames: 0, matchHeldMotion: true, matchCompletedMotion: true,
            bufferPolicy: new CommandBufferPolicy(true, 12, 1, 4)),
        ForwardLightPunch(),
        new("ReservedRisingUpper", [CommandDirection.DownForward], InputBits.HeavyPunch,
            buttonToleranceFrames: 0, priority: 3, bufferLifetimeFrames: 0,
            bufferPolicy: LatchPolicy, reserveGroundInput: true, reserveOnEdgeDirection: true),
        new("5C", [], InputBits.LightKick, priority: 0, bufferLifetimeFrames: 2),
        new("2D", [], InputBits.HeavyKick, priority: -2, bufferLifetimeFrames: 2),
        new("2C", [], InputBits.LightKick, priority: -1, bufferLifetimeFrames: 2),
    ],
    [
        new(Light, "5A", 3, 4, 5, MoveAllowedStates.Standing),
        new(Heavy, "5B", 3, 10, 15),
        new(Wave, "236A", 15, 2, 35),
        BackKnuckleMove(),
        new("LowKick", "5C", 5, 3, 11, maximumStartBodyDistance: FixedPoint.FromInt(28),
            cancels: [new CancelDefinition(Wave, 5, 8, MoveCancelPhases.Active, allowWhiff: true)],
            reservedCancels:
            [
                new ReservedCancelDefinition("ForwardLightPunch", 5, 8, MoveCancelPhases.Active),
                new ReservedCancelDefinition("ReservedRisingUpper", 5, 8, MoveCancelPhases.Active),
            ]),
        new("LowKickFar", "5C", 5, 3, 11),
        new("CrouchLight", "5A", 3, 2, 8, MoveAllowedStates.Crouching,
            reservedCancels: [new ReservedCancelDefinition("ForwardLightPunch", 4, 4, MoveCancelPhases.Active)]),
        new("Sweep", "2D", 10, 8, 22, MoveAllowedStates.Crouching,
            reservedCancels: [new ReservedCancelDefinition("ForwardLightPunch", 11, 17, MoveCancelPhases.Active)]),
        new("CrouchKick", "2C", 3, 3, 8, MoveAllowedStates.Crouching,
            cancels: [new CancelDefinition(Light, 6, 13, MoveCancelPhases.Active | MoveCancelPhases.Recovery,
                allowWhiff: true)]),
    ], groundBodyWidths: new GroundBodyWidths(15, 16), useCommandPriority: true);

    private static RecognitionState Advance(FighterDefinition fighter, IEnumerable<InputBits> inputs,
        int first = 0, Facing facing = Facing.Right, bool frozen = false, RecognitionState? prior = null,
        bool globalPaused = false)
    {
        RecognitionState state = prior ?? RecognitionState.Empty;
        int frame = first;
        foreach (InputBits bits in inputs)
            state = state.Advance(bits, facing, frame++, 0, fighter.Commands,
                useCommandPriority: fighter.UseCommandPriority, inHitPause: frozen, globalPaused: globalPaused);
        return state;
    }

    private static FighterActionTick TickIdle(FighterDefinition fighter, RecognitionState recognition,
        int frame, InputBits input, FighterLogicState state = FighterLogicState.Standing)
    {
        return FighterStateMachine.Tick(state, null, 0,
            DirectionResolver.Resolve(input, Facing.Right), recognition, fighter, frame,
            opponentDistance: FixedPoint.FromInt(60), opponentBodyDistance: FixedPoint.FromInt(29),
            opponentGroundedNonHitstun: true);
    }

    private static FighterActionTick TickCancel(FighterDefinition fighter, RecognitionState recognition,
        int frame, InputBits input, int elapsed, string source = "LowKick",
        FighterLogicState state = FighterLogicState.Active, int phaseRemaining = 3)
    {
        return FighterStateMachine.Tick(state, source, phaseRemaining,
            DirectionResolver.Resolve(input, Facing.Right), recognition, fighter, frame,
            moveStartedFrame: frame - elapsed, moveElapsedFrames: elapsed,
            opponentDistance: FixedPoint.FromInt(44), opponentBodyDistance: FixedPoint.FromInt(13),
            opponentGroundedNonHitstun: true);
    }

    [Fact]
    public void ForwardWithLightPunchStartsBackKnuckleFromIdle()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.Right | InputBits.LightPunch]);
        FighterActionTick action = TickIdle(fighter, state, 0, InputBits.Right | InputBits.LightPunch);
        Assert.Equal(BackKnuckle, action.MoveId);
        Assert.Equal(11, action.PhaseRemainingFrames);
        Assert.Equal(0, action.MoveElapsedFrames);
        Assert.NotNull(action.ConsumedMatch);
    }

    [Theory]
    [InlineData(InputBits.None, Light)]
    [InlineData(InputBits.Down | InputBits.Right, "CrouchLight")]
    public void RootDisjunctRequiresForwardWithoutDown(InputBits direction, string fallback)
    {
        FighterDefinition fighter = Fighter();
        InputBits bits = direction | InputBits.LightPunch;
        RecognitionState state = Advance(fighter, [bits]);
        FighterActionTick action = TickIdle(fighter, state, 0, bits,
            direction.HasFlag(InputBits.Down) ? FighterLogicState.Crouching : FighterLogicState.Standing);
        Assert.Equal(fallback, action.MoveId);
    }

    [Fact]
    public void HelperLatchEntersWithoutDirectionOrButtonAtConsumption()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter,
            [InputBits.Right | InputBits.LightPunch, InputBits.None, InputBits.None]);
        FighterActionTick action = TickIdle(fighter, state, 2, InputBits.None);
        Assert.Equal(BackKnuckle, action.MoveId);
    }

    [Fact]
    public void HelperLatchIgnoresHeldDownAtConsumption()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter,
            [InputBits.Right | InputBits.LightPunch, InputBits.Down]);
        FighterActionTick action = TickIdle(fighter, state, 1, InputBits.Down, FighterLogicState.Crouching);
        Assert.Equal(BackKnuckle, action.MoveId);
    }

    [Fact]
    public void HelperLatchExpiresAfterThreeTicks()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter,
            [InputBits.Right | InputBits.LightPunch, InputBits.None, InputBits.None, InputBits.None,
             InputBits.None]);
        FighterActionTick action = TickIdle(fighter, state, 4, InputBits.None);
        Assert.Null(action.MoveId);
    }

    [Fact]
    public void DelayedDirectionDoesNotReviveTheRootEdge()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.LightPunch, InputBits.Right]);
        FighterActionTick first = TickIdle(fighter, state, 0, InputBits.LightPunch);
        Assert.Equal(Light, first.MoveId);
    }

    [Fact]
    public void DeferredPlaceholderStillConsumesWithoutAMove()
    {
        CommandDefinition risingUpper = new("ReservedRisingUpper", [CommandDirection.DownForward],
            InputBits.HeavyPunch, buttonToleranceFrames: 0, priority: 3, bufferLifetimeFrames: 0,
            bufferPolicy: LatchPolicy, reserveGroundInput: true, reserveOnEdgeDirection: true);
        FighterDefinition fighter = new("Test",
            [ForwardLightPunch(), risingUpper, new("5A", [], InputBits.LightPunch, priority: 2)],
            [BackKnuckleMove(withWave: false), new(Light, "5A", 3, 4, 5, MoveAllowedStates.Standing)],
            useCommandPriority: true);
        RecognitionState state = Advance(fighter,
            [InputBits.Down | InputBits.Right | InputBits.HeavyPunch, InputBits.None]);
        FighterActionTick action = TickIdle(fighter, state, 1, InputBits.None);
        Assert.Null(action.MoveId);
        Assert.Null(action.ConsumedMatch);
        Assert.Contains(action.Recognition.Buffer, result => result.Consumed &&
            result.Match.CommandId == "ReservedRisingUpper");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void LightKickWindowEntersBackKnuckle(int tick)
    {
        FighterDefinition fighter = Fighter();
        InputBits[] inputs = [InputBits.LightKick, .. Enumerable.Repeat(InputBits.None, tick - 1),
            InputBits.Right | InputBits.LightPunch];
        RecognitionState state = Advance(fighter, inputs);
        FighterActionTick action = TickCancel(fighter, state, tick, InputBits.Right | InputBits.LightPunch, tick);
        Assert.Equal(BackKnuckle, action.MoveId);
        Assert.Equal(0, action.MoveElapsedFrames);
    }

    [Fact]
    public void LightKickWindowRejectsLateInputWithoutAChain()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.LightKick, .. Enumerable.Repeat(InputBits.None, 8),
            InputBits.Right | InputBits.LightPunch]);
        FighterActionTick action = TickCancel(fighter, state, 9, InputBits.Right | InputBits.LightPunch, 9,
            state: FighterLogicState.Recovery, phaseRemaining: 10);
        Assert.Null(action.ConsumedMatch);
        Assert.NotEqual(Wave, action.MoveId);
        Assert.NotEqual(BackKnuckle, action.MoveId);
    }

    [Fact]
    public void LightKickWindowRejectsLateInputDuringRecoveryToo()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.LightKick, .. Enumerable.Repeat(InputBits.None, 8),
            InputBits.Right | InputBits.LightPunch]);
        FighterActionTick action = TickCancel(fighter, state, 10, InputBits.Right | InputBits.LightPunch, 9,
            state: FighterLogicState.Recovery, phaseRemaining: 10);
        Assert.NotEqual(Wave, action.MoveId);
        Assert.NotEqual(BackKnuckle, action.MoveId);
        Assert.Null(action.ConsumedMatch);
    }

    [Fact]
    public void CrouchLightPunchWindowIsASingleTickAndRequiresReleasingDown()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.Down | InputBits.LightPunch,
            .. Enumerable.Repeat(InputBits.Down, 3), InputBits.Right | InputBits.LightPunch]);
        FighterActionTick action = TickCancel(fighter, state, 4, InputBits.Right | InputBits.LightPunch, 4,
            source: "CrouchLight");
        Assert.Equal(BackKnuckle, action.MoveId);
    }

    [Fact]
    public void SweepWindowEntersBackKnuckle()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.Down | InputBits.HeavyKick,
            .. Enumerable.Repeat(InputBits.Down, 10), InputBits.Right | InputBits.LightPunch]);
        FighterActionTick action = TickCancel(fighter, state, 11, InputBits.Right | InputBits.LightPunch, 11,
            source: "Sweep");
        Assert.Equal(BackKnuckle, action.MoveId);
    }

    [Fact]
    public void CrouchLightKickHasNoBackKnuckleWindowAndChainsOrdinary()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.Down | InputBits.LightKick,
            .. Enumerable.Repeat(InputBits.Down, 5), InputBits.Right | InputBits.LightPunch]);
        FighterActionTick action = TickCancel(fighter, state, 6, InputBits.Right | InputBits.LightPunch, 6,
            source: "CrouchKick");
        Assert.Equal(Light, action.MoveId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FrozenInputCommitsAtTheFirstThawTick(bool splitOrder)
    {
        FighterDefinition fighter = Fighter();
        InputBits[] frozenInputs = splitOrder
            ? [InputBits.LightKick, InputBits.Right, .. Enumerable.Repeat(InputBits.None, 7), InputBits.LightPunch]
            : [InputBits.LightKick, InputBits.LightPunch, .. Enumerable.Repeat(InputBits.None, 7), InputBits.Right];
        RecognitionState state = Advance(fighter, frozenInputs, frozen: true);
        FighterActionTick action = TickCancel(fighter, state, 9, InputBits.Right, 5);
        Assert.Equal(BackKnuckle, action.MoveId);
    }

    [Fact]
    public void BackKnuckleCancelsIntoTheWaveOnlyAfterContact()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter,
            [InputBits.Right | InputBits.LightPunch, InputBits.None, InputBits.Down,
             InputBits.Down | InputBits.Right, InputBits.Right, InputBits.Right | InputBits.LightPunch]);
        FighterActionTick whiff = FighterStateMachine.Tick(FighterLogicState.Active, BackKnuckle, 3,
            DirectionResolver.Resolve(InputBits.Right, Facing.Right), state, fighter, 18,
            moveStartedFrame: 0, moveElapsedFrames: 18,
            opponentDistance: FixedPoint.FromInt(240), opponentBodyDistance: FixedPoint.FromInt(209),
            opponentGroundedNonHitstun: true);
        Assert.NotEqual(Wave, whiff.MoveId);
        Assert.Null(whiff.ConsumedMatch);
        FighterActionTick contact = FighterStateMachine.Tick(FighterLogicState.Active, BackKnuckle, 3,
            DirectionResolver.Resolve(InputBits.Right, Facing.Right), state, fighter, 18,
            moveStartedFrame: 0, moveElapsedFrames: 18, hitConfirmFrame: 16,
            opponentDistance: FixedPoint.FromInt(60), opponentBodyDistance: FixedPoint.FromInt(29),
            opponentGroundedNonHitstun: true);
        Assert.Equal(Wave, contact.MoveId);
        Assert.Equal(0, contact.MoveElapsedFrames);
    }

    [Fact]
    public void BackKnuckleDoesNotCancelIntoItselfInsideTheContactWindow()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.Right | InputBits.LightPunch]);
        FighterActionTick action = FighterStateMachine.Tick(FighterLogicState.Active, BackKnuckle, 3,
            DirectionResolver.Resolve(InputBits.Right, Facing.Right), state, fighter, 18,
            moveStartedFrame: 0, moveElapsedFrames: 18, hitConfirmFrame: 16,
            opponentDistance: FixedPoint.FromInt(60), opponentBodyDistance: FixedPoint.FromInt(29),
            opponentGroundedNonHitstun: true);
        Assert.Equal(BackKnuckle, action.MoveId);
        Assert.Equal(18, action.MoveElapsedFrames);
        Assert.Null(action.ConsumedMatch);
    }

    [Fact]
    public void NaturalRecoveryTickRestartsBackKnuckleOnAFreshEdge()
    {
        FighterDefinition fighter = Fighter();
        RecognitionState state = Advance(fighter, [InputBits.Right | InputBits.LightPunch]);
        FighterActionTick action = FighterStateMachine.Tick(FighterLogicState.Recovery, BackKnuckle, 1,
            DirectionResolver.Resolve(InputBits.Right | InputBits.LightPunch, Facing.Right), state, fighter, 51,
            moveStartedFrame: 4, moveElapsedFrames: 46,
            opponentDistance: FixedPoint.FromInt(240), opponentBodyDistance: FixedPoint.FromInt(209),
            opponentGroundedNonHitstun: true);
        Assert.Equal(BackKnuckle, action.MoveId);
        Assert.Equal(0, action.MoveElapsedFrames);
    }

    [Fact]
    public void SecondContactGroupSettlesAgainAgainstTheSameTarget()
    {
        AttackDefinition Group(int first, int last, int group) =>
            new(FixedPoint.FromInt(30), FixedPoint.FromInt(50), FixedPoint.FromInt(10), FixedPoint.FromInt(10),
                first, last, hitstunFrames: 19, blockstunFrames: 18, hitstopFrames: 2, contactGroup: group);
        MoveDefinition move = new(BackKnuckle, "ForwardLightPunch", 11, 9, 27,
            attacks: [Group(0, 2, 1), Group(6, 8, 2)]);
        FighterDefinition fighter = new("Duelist", [ForwardLightPunch()], [move],
            groundHurtboxes: new GroundHurtboxes([new FixedRect(-19, 19, 1, 88)],
                [new FixedRect(-19, 19, 1, 72)]),
            groundBodyWidths: new GroundBodyWidths(10, 10), useCommandPriority: true);
        BattleSimulation simulation = new(new FightRoster(fighter, fighter));
        BattleState state = new(0,
            [FighterState.Create(0, 1000, -15, Facing.Right) with { DamageScript = DamageScriptState.FromSeed(1337, 0) },
             FighterState.Create(1, 1000, 15, Facing.Left) with { DamageScript = DamageScriptState.FromSeed(1337, 1) }],
            BattleState.CreateDefault().StateVersion);
        int stuns = 0;
        int moves = 0;
        int contacts = 0;
        int attackActive = 0;
        var frames = new List<string>();
        for (int frame = 0; frame < 50; frame++)
        {
            InputBits bits = frame == 0 ? InputBits.Right | InputBits.LightPunch : InputBits.None;
            BattleStepResult step = simulation.Step(state, new FrameInput(frame, bits, InputBits.None));
            stuns += step.Events.Count(entry => entry.Type == BattleEventType.HitstunStarted);
            moves += step.Events.Count(entry => entry.Type == BattleEventType.MoveStarted &&
                entry.MoveId == BackKnuckle);
            contacts += step.Events.Count(entry => entry.Type == BattleEventType.Hit);
            attackActive += step.Events.Count(entry => entry.Type == BattleEventType.AttackActive);
            if (step.State.Fighter(0).MoveId == BackKnuckle && step.State.Fighter(0).MoveElapsedFrames is >= 10 and <= 14)
                frames.Add($"f{frame} e{step.State.Fighter(0).MoveElapsedFrames} " +
                    $"x{step.State.Fighter(0).PositionX} ev{step.Events.Count}");
            state = step.State;
        }

        Assert.Equal(1, moves);
        Assert.True(stuns == 2, $"stuns={stuns} contacts={contacts} attackActive={attackActive} " +
            string.Join(";", frames));
    }
}
