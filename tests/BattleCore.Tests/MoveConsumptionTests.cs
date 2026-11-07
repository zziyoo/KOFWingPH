using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 04：指令缓冲消费契约（B5/D60）。
///
/// <para>规则：识别、缓冲、消费、动作状态转换是四个不同概念——
/// 每条识别结果最多成功消费一次；过期不能消费；不属于当前角色的指令不会被错误消费；
/// 失败的消费请求不破坏其他有效结果；不能行动时符合缓冲规则的指令保留到有效期结束；
/// 多条有效结果的选择顺序稳定（缓冲顺序 = 识别帧升序）；消费状态随权威快照携带，
/// 与渲染帧率无关。</para>
/// </summary>
public class MoveConsumptionTests
{
    private static readonly FighterDefinition Kai = DemoRoster.CreateKai();
    private static readonly FighterDefinition Rei = DemoRoster.CreateRei();

    /// <summary>逐帧喂入 236A（↓0-1 ↘2-3 →+A4），返回识别状态（结果识别于帧 4）。</summary>
    private static RecognitionState Recognize236A(FighterDefinition definition, Facing facing = Facing.Right)
    {
        InputBits[] log =
        {
            InputBits.Down, InputBits.Down,
            DownOrDownForward(facing), DownOrDownForward(facing),
            ForwardPlusA(facing),
        };
        RecognitionState state = RecognitionState.Empty;
        for (int f = 0; f < log.Length; f++)
            state = state.Advance(log[f], facing, f, 0, definition.Commands);
        return state;
    }

    private static InputBits DownOrDownForward(Facing facing) => facing == Facing.Right
        ? InputBits.Down | InputBits.Right
        : InputBits.Down | InputBits.Left;

    private static InputBits ForwardPlusA(Facing facing) => facing == Facing.Right
        ? InputBits.Right | InputBits.LightPunch
        : InputBits.Left | InputBits.LightPunch;

    private static RelativeInput Neutral() =>
        new(Forward: false, Back: false, Up: false, Down: false, Attacks: InputBits.None);

    // ─────────────────────────────────────────────────────────────
    // 一次消费 / 重复消费
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ConsumeOnce_SecondConsumeFails()
    {
        RecognitionState recognition = Recognize236A(Kai);

        // 第一次消费成功（进入 Startup）。
        var first = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, Kai, 5);
        Assert.NotNull(first.ConsumedMatch);

        // 第二次对同一状态再消费：结果已标记消费，不会再发动。
        var second = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), first.Recognition, Kai, 6);
        Assert.Null(second.ConsumedMatch);
        Assert.Equal(FighterLogicState.Standing, second.State);

        // 直接对已消费结果调用消费 API → 显式失败。
        Assert.Throws<InvalidOperationException>(
            () => recognition.WithResultConsumed(4).WithResultConsumed(4));
    }

    [Fact]
    public void WithResultConsumed_UnknownFrame_Throws()
    {
        RecognitionState recognition = Recognize236A(Kai);

        Assert.Throws<InvalidOperationException>(() => recognition.WithResultConsumed(999));
    }

    // ─────────────────────────────────────────────────────────────
    // 过期不能消费
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ExpiredResult_CannotBeConsumed()
    {
        // 默认缓冲时效 8 帧：帧 4 识别 → 帧 13 已过期（4+8=12 含此帧，帧 13 移除）。
        RecognitionState recognition = Recognize236A(Kai);
        for (int f = 5; f <= 13; f++)
            recognition = recognition.Advance(InputBits.None, Facing.Right, f, 0, Kai.Commands);

        Assert.Empty(recognition.Buffer);

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, Kai, 14);
        Assert.Null(tick.ConsumedMatch);
        Assert.Equal(FighterLogicState.Standing, tick.State);
    }

    [Fact]
    public void ResultSurvivesUntilExpiry_ThenCannotFire()
    {
        // 不行动时符合缓冲规则的指令保留到有效期结束（B5）：
        // 帧 4 识别 → 帧 12 仍可消费（ExpiresAtFrame=12 含此帧）。
        RecognitionState recognition = Recognize236A(Kai);
        for (int f = 5; f <= 12; f++)
            recognition = recognition.Advance(InputBits.None, Facing.Right, f, 0, Kai.Commands);

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, Kai, 12);
        Assert.NotNull(tick.ConsumedMatch);
    }

    // ─────────────────────────────────────────────────────────────
    // 无映射 / 失败不破坏其他结果 / 选择顺序
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void UnmappedCommand_StaysInBuffer_DoesNotBlockLaterMappedCommand()
    {
        // 指令表中 623A 无对应招式（Kai 有招式；这里构造「有指令无招式」的角色），
        // 623A 先识别（帧 4），5A 后识别（帧 5）→ 消费跳过前者、消费后者。
        var commands = new[]
        {
            new CommandDefinition("623A",
                new[] { CommandDirection.Forward, CommandDirection.Down, CommandDirection.DownForward },
                InputBits.LightPunch, priority: 20),
            new CommandDefinition("5A",
                new[] { CommandDirection.Neutral },
                InputBits.LightPunch, priority: 5, bufferLifetimeFrames: 30),
        };
        var definition = new FighterDefinition(
            "NoMoveFor623", commands,
            new[] { new MoveDefinition("Jab", "5A", 3, 2, 6) });

        // 帧 0-2：→ ↓ ↘+A（623A 手势，帧 2 完成）；帧 3 松开；帧 4 中立+A（5A）。
        RecognitionState recognition = RecognitionState.Empty
            .Advance(InputBits.Right, Facing.Right, 0, 0, commands)
            .Advance(InputBits.Down, Facing.Right, 1, 0, commands)
            .Advance(InputBits.Down | InputBits.Right | InputBits.LightPunch, Facing.Right, 2, 0, commands)
            .Advance(InputBits.None, Facing.Right, 3, 0, commands)
            .Advance(InputBits.LightPunch, Facing.Right, 4, 0, commands);

        // 缓冲里应有 623A（帧 2，无招式映射）与 5A（帧 4，映射 Jab）。
        Assert.Equal(2, recognition.Buffer.Count);
        Assert.Equal("623A", recognition.Buffer[0].Match.CommandId);
        Assert.Equal("5A", recognition.Buffer[1].Match.CommandId);

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, definition, 5);

        // 选择顺序 = 缓冲顺序（识别帧升序）：623A 无映射被跳过且保留，5A 被消费。
        Assert.NotNull(tick.ConsumedMatch);
        Assert.Equal("5A", tick.ConsumedMatch!.CommandId);
        Assert.Equal("Jab", tick.MoveId);
        Assert.False(tick.Recognition.Buffer[0].Consumed); // 623A 原样保留。
        Assert.True(tick.Recognition.Buffer[1].Consumed);
    }

    [Fact]
    public void SelectionOrder_IsFifoByRecognitionFrame()
    {
        // 缓冲里有两条可消费结果（识别帧 4 与 5）→ 稳定选择较早的一条。
        var commands = new[]
        {
            new CommandDefinition("5A",
                new[] { CommandDirection.Neutral },
                InputBits.LightPunch, priority: 5, bufferLifetimeFrames: 30),
        };
        var definition = new FighterDefinition("Kai", commands,
            new[] { new MoveDefinition("Jab", "5A", 3, 2, 6) });

        // 帧 3 与帧 6 各完成一次独立的 5A 手势（中间用方向变化分隔两次手势，
        // 否则 D43 手势消费会正确地阻止同手势第二次匹配）。
        RecognitionState recognition = RecognitionState.Empty
            .Advance(InputBits.LightPunch, Facing.Right, 3, 0, commands)  // 5A@3
            .Advance(InputBits.Down, Facing.Right, 4, 0, commands)        // 分隔
            .Advance(InputBits.None, Facing.Right, 5, 0, commands)        // Neutral@5
            .Advance(InputBits.LightPunch, Facing.Right, 6, 0, commands); // 5A@6

        Assert.Equal(2, recognition.Buffer.Count);

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, definition, 7);

        Assert.Equal(3, tick.ConsumedMatch!.RecognizedFrame); // 先识别者先消费。
    }

    // ─────────────────────────────────────────────────────────────
    // 角色归属 / 同指令不同招式 / 状态门控
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SameCommandId_DifferentMovePerCharacter()
    {
        // 同一条 236A 手势：Kai 消费 → FireballPrototype(4/3/8)；Rei 消费 → BackDraft(5/2/7)。
        var kaiTick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), Recognize236A(Kai), Kai, 5);
        var reiTick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), Recognize236A(Rei), Rei, 5);

        Assert.Equal(DemoRoster.FireballPrototype, kaiTick.MoveId);
        Assert.Equal(4, kaiTick.PhaseRemainingFrames);
        Assert.Equal(DemoRoster.BackDraft, reiTick.MoveId);
        Assert.Equal(5, reiTick.PhaseRemainingFrames);
    }

    [Fact]
    public void FacingLeft_RecognitionUsesPerFrameFacing()
    {
        // P2（朝左）做 236A：相对方向按当帧朝向解析，识别与消费不受朝向影响。
        var reiTick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), Recognize236A(Rei, Facing.Left), Rei, 5);

        Assert.Equal(DemoRoster.BackDraft, reiTick.MoveId);
    }

    [Fact]
    public void MoveAllowedOnlyFromCrouching_WaitsForCrouch()
    {
        // 招式只允许蹲姿发动：站立时有效结果保留；蹲下当帧消费（B5）。
        var commands = new[]
        {
            new CommandDefinition("5A",
                new[] { CommandDirection.Neutral },
                InputBits.LightPunch, priority: 5, bufferLifetimeFrames: 30),
        };
        var definition = new FighterDefinition("Kai", commands,
            new[]
            {
                new MoveDefinition("CrouchJab", "5A", 3, 2, 6,
                    allowedFrom: MoveAllowedStates.Crouching),
            });

        RecognitionState recognition = RecognitionState.Empty
            .Advance(InputBits.LightPunch, Facing.Right, 3, 0, commands);

        // 站立：不消费。
        var standingTick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, definition, 4);
        Assert.Null(standingTick.ConsumedMatch);
        Assert.False(standingTick.Recognition.Buffer[0].Consumed);

        // 蹲下：同帧消费。
        var crouchTick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            new RelativeInput(false, false, false, true, InputBits.None), recognition, definition, 4);
        Assert.Equal(FighterLogicState.Startup, crouchTick.State);
        Assert.Equal("CrouchJab", crouchTick.MoveId);
        Assert.True(crouchTick.Recognition.Buffer[0].Consumed);
    }

    // ─────────────────────────────────────────────────────────────
    // 不出现幽灵动作 / 消费与动作状态是不同概念
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void NoBufferedResult_NoMoveStarts()
    {
        // LastMatch 有值 ≠ 可发动：消费只来自未消费的缓冲结果。
        RecognitionState recognition = Recognize236A(Kai);
        Assert.NotNull(recognition.Last);       // 识别发生过。
        var consumed = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, Kai, 5);
        Assert.NotNull(consumed.ConsumedMatch);

        // 消费后再过 20 帧（结果过期、Last 仍在）→ 空缓冲，不再发动。
        RecognitionState later = consumed.Recognition;
        for (int f = 5; f <= 26; f++)
            later = later.Advance(InputBits.None, Facing.Right, f, 0, Kai.Commands);
        Assert.NotNull(later.Last);
        Assert.Empty(later.Buffer);

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), later, Kai, 27);
        Assert.Null(tick.ConsumedMatch);
        Assert.Equal(FighterLogicState.Standing, tick.State);
    }

    [Fact]
    public void PureTickFunctions_DontMutateInputs()
    {
        RecognitionState recognition = Recognize236A(Kai);
        var before = recognition;

        FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, Kai, 5);

        // 纯函数：传入的识别状态原样不变（旧快照安全）。
        Assert.Equal(before, recognition);
    }

    // ─────────────────────────────────────────────────────────────
    // 裸按键指令（D56）：站立不动按 A → 5A（不依赖方向历史新鲜度）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void BareButtonCommand_FiresOnPressEdge_AfterLongIdle()
    {
        // 长时间无输入（中立条目从帧 0 开始）后按 A：裸按键指令仍应触发——
        // 这是它与 [Neutral] 序列指令的关键区别（后者要求中立「新进入」）。
        RecognitionState recognition = RecognitionState.Empty;
        for (int f = 0; f <= 30; f++)
        {
            recognition = recognition.Advance(
                f == 30 ? InputBits.LightPunch : InputBits.None, Facing.Right, f, 0, Kai.Commands);
        }

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, Kai, 31);

        Assert.Equal(DemoRoster.StandLightPunch, tick.MoveId);
        Assert.Equal(FighterLogicState.Startup, tick.State);
        Assert.Equal(3, tick.PhaseRemainingFrames);
    }

    [Fact]
    public void BareButtonCommand_HeldButton_DoesNotRetrigger()
    {
        // 按住 A：一次边沿一次识别（D41）；缓冲过期后不再发动。
        RecognitionState recognition = RecognitionState.Empty
            .Advance(InputBits.LightPunch, Facing.Right, 0, 0, Kai.Commands)
            .Advance(InputBits.LightPunch, Facing.Right, 1, 0, Kai.Commands);

        // 帧 0 识别（5A），帧 1 按住不重复（无新边沿）→ 缓冲里只有帧 0 一条。
        var result = Assert.Single(recognition.Buffer);
        Assert.Equal("5A", result.Match.CommandId);
        Assert.True(result.Consumed is false);

        for (int f = 2; f <= 20; f++)
            recognition = recognition.Advance(InputBits.LightPunch, Facing.Right, f, 0, Kai.Commands);

        // 8 帧缓冲时效后过期，按住期间没有任何新识别。
        Assert.Empty(recognition.Buffer);

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, Kai, 21);
        Assert.Null(tick.ConsumedMatch);
        Assert.Equal(FighterLogicState.Standing, tick.State);
    }

    [Fact]
    public void BareButtonCommand_YieldsToHigherPriorityCommand_SameFrame()
    {
        // 同一 A 边沿同时完成 236A（优先级 10）与 5A（优先级 5）→ 236A 胜出，
        // 5A 的边沿也被消费，不产生幽灵 5A。
        RecognitionState recognition = RecognitionState.Empty
            .Advance(InputBits.Down, Facing.Right, 0, 0, Kai.Commands)
            .Advance(InputBits.Down | InputBits.Right, Facing.Right, 1, 0, Kai.Commands)
            .Advance(InputBits.Right | InputBits.LightPunch, Facing.Right, 2, 0, Kai.Commands);

        var match = Assert.Single(recognition.Buffer);
        Assert.Equal("236A", match.Match.CommandId);

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Neutral(), recognition, Kai, 3);
        Assert.Equal(DemoRoster.FireballPrototype, tick.MoveId);
    }

    // ─────────────────────────────────────────────────────────────
    // 集成：消费经 BattleSimulation.Step 驱动，与渲染帧率无关
    // ─────────────────────────────────────────────────────────────

    private static Dictionary<int, FrameInput> Log236A(int completionFrame)
    {
        // ↓(c-4..c-3) ↘(c-2..c-1) →+A(c)。
        var log = new Dictionary<int, FrameInput>();
        for (int f = 0; f <= completionFrame; f++)
        {
            InputBits p1 = f switch
            {
                var x when x <= completionFrame - 2 => InputBits.Down,
                var x when x <= completionFrame - 1 => InputBits.Down | InputBits.Right,
                _ => InputBits.Right | InputBits.LightPunch,
            };
            log[f] = new FrameInput(f, p1, InputBits.None);
        }

        return log;
    }

    private static BattleState RunInputs(BattleSimulation simulation, Dictionary<int, FrameInput> log, int untilFrame)
    {
        BattleState state = BattleState.CreateDefault();
        for (int f = 0; f <= untilFrame; f++)
            state = simulation.Step(state, log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f)).State;
        return state;
    }

    [Fact]
    public void Step_DrivesConsumptionExactlyOnce_RegardlessOfStepCadence()
    {
        // 同一条输入日志：一次推进到底 vs 分两次推进（模拟不同渲染节奏），
        // 动作状态一致且只发动一次（无重复消费）。
        var log = Log236A(completionFrame: 10);

        var simA = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        BattleState a = RunInputs(simA, log, untilFrame: 13);

        var simB = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        BattleState b = BattleState.CreateDefault();
        foreach (int f in Enumerable.Range(0, 8))
            b = simB.Step(b, log[f]).State;
        for (int f = 8; f <= 13; f++)
        {
            b = simB.Step(b,
                log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f)).State;
        }

        // P1（Kai）：帧 10 识别并消费 → state(11) 进入 Startup(4)，state(14) 剩 1。
        Assert.Equal(FighterLogicState.Startup, a.Fighter(0).LogicState);
        Assert.Equal(1, a.Fighter(0).PhaseRemainingFrames);
        Assert.Equal(DemoRoster.FireballPrototype, a.Fighter(0).MoveId);
        Assert.True(a.Fighter(0).RecognitionState.Buffer.Single(r => r.Match.RecognizedFrame == 10).Consumed);

        Assert.Equal(a.Fighter(0).LogicState, b.Fighter(0).LogicState);
        Assert.Equal(a.Fighter(0).PhaseRemainingFrames, b.Fighter(0).PhaseRemainingFrames);
    }
}
