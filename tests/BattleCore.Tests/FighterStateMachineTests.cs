using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 04：角色动作状态机的纯函数测试（B2/B8/B9）。
///
/// <para>直接驱动 <see cref="FighterStateMachine.Tick"/>：显式构造状态与解析输入，
/// 不经过会话。合法转换图唯一：
/// 待机 ↔ 蹲、待机 → Startup → Active → Recovery → 待机。</para>
/// </summary>
public class FighterStateMachineTests
{
    private static readonly FighterDefinition Kai = DemoRoster.CreateKai();

    private static RelativeInput Resolved(bool down = false, bool up = false) =>
        new(Forward: false, Back: false, Up: up, Down: down, Attacks: InputBits.None);

    /// <summary>推进识别状态到「缓冲中有一条 FireballPrototype(236A) 结果、识别帧为 frame」。</summary>
    private static RecognitionState RecognitionWithFireball(int frame)
    {
        // 逐帧喂入 236A 手势：↓(0-1) ↘(2-3) →+A(4)。识别在帧 4 完成。
        InputBits[] log =
        {
            InputBits.Down, InputBits.Down,
            InputBits.Down | InputBits.Right, InputBits.Down | InputBits.Right,
            InputBits.Right | InputBits.LightPunch,
        };
        RecognitionState state = RecognitionState.Empty;
        for (int f = 0; f <= frame; f++)
            state = state.Advance(log[f], Facing.Right, f, 0, Kai.Commands);
        return state;
    }

    // ─────────────────────────────────────────────────────────────
    // 待机与蹲下（B9）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Idle_NoDown_StaysStanding()
    {
        var tick = FighterStateMachine.Tick(
            FighterLogicState.Standing, null, 0, Resolved(), RecognitionState.Empty, Kai, 0);

        Assert.Equal(FighterLogicState.Standing, tick.State);
        Assert.Null(tick.MoveId);
        Assert.Equal(0, tick.PhaseRemainingFrames);
        Assert.Null(tick.ConsumedMatch);
    }

    [Fact]
    public void Idle_Down_EntersCrouching_ReleaseReturnsStanding()
    {
        var crouch = FighterStateMachine.Tick(
            FighterLogicState.Standing, null, 0, Resolved(down: true), RecognitionState.Empty, Kai, 0);
        Assert.Equal(FighterLogicState.Crouching, crouch.State);

        var stand = FighterStateMachine.Tick(
            FighterLogicState.Crouching, null, 0, Resolved(), RecognitionState.Empty, Kai, 1);
        Assert.Equal(FighterLogicState.Standing, stand.State);
    }

    [Fact]
    public void Idle_DownAndUp_StandsBecauseUpPriority()
    {
        // SOCD 上优先已在 Resolve 处理；这里直接给「上优先后不为下」的解析结果。
        var tick = FighterStateMachine.Tick(
            FighterLogicState.Crouching, null, 0, Resolved(up: true), RecognitionState.Empty, Kai, 0);
        Assert.Equal(FighterLogicState.Standing, tick.State);
    }

    // ─────────────────────────────────────────────────────────────
    // 阶段推进（B8）：按配置帧数准确切换
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Startup_CountsDownThenActivates()
    {
        // FireballPrototype: Startup 4 / Active 3 / Recovery 8。
        var t1 = FighterStateMachine.Tick(FighterLogicState.Startup,
            DemoRoster.FireballPrototype, 4, Resolved(), RecognitionState.Empty, Kai, 0);
        Assert.Equal((FighterLogicState.Startup, 3), (t1.State, t1.PhaseRemainingFrames));

        var t2 = FighterStateMachine.Tick(FighterLogicState.Startup,
            DemoRoster.FireballPrototype, 1, Resolved(), RecognitionState.Empty, Kai, 0);
        Assert.Equal(FighterLogicState.Active, t2.State);
        Assert.Equal(3, t2.PhaseRemainingFrames); // ActiveFrames
        Assert.Equal(DemoRoster.FireballPrototype, t2.MoveId);
    }

    [Fact]
    public void Active_CountsDownThenRecovers()
    {
        var tick = FighterStateMachine.Tick(FighterLogicState.Active,
            DemoRoster.FireballPrototype, 1, Resolved(), RecognitionState.Empty, Kai, 0);
        Assert.Equal(FighterLogicState.Recovery, tick.State);
        Assert.Equal(8, tick.PhaseRemainingFrames); // RecoveryFrames
    }

    [Fact]
    public void Recovery_Completes_BackToStanding()
    {
        var tick = FighterStateMachine.Tick(FighterLogicState.Recovery,
            DemoRoster.FireballPrototype, 1, Resolved(), RecognitionState.Empty, Kai, 0);
        Assert.Equal(FighterLogicState.Standing, tick.State);
        Assert.Null(tick.MoveId);
        Assert.Equal(0, tick.PhaseRemainingFrames);
    }

    [Fact]
    public void Recovery_WithDownHeld_GoesStraightToCrouching()
    {
        // 收招完成同帧可执行待机更新（B9）：按住下 → 直接蹲下。
        var tick = FighterStateMachine.Tick(FighterLogicState.Recovery,
            DemoRoster.FireballPrototype, 1, Resolved(down: true), RecognitionState.Empty, Kai, 0);
        Assert.Equal(FighterLogicState.Crouching, tick.State);
        Assert.Null(tick.MoveId);
    }

    // ─────────────────────────────────────────────────────────────
    // 动作期间不允许的状态跳转 / 损坏状态立即失败
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MovePhases_NeverLeaveTheMoveWithoutCompleting()
    {
        // 动作阶段推进绝不会直接产生待机（必须经 Recovery 完成），
        // 也不会在 Startup 直接跳到 Recovery。
        foreach (var (state, remaining, expectedNext, expectedRemaining) in new[]
        {
            (FighterLogicState.Startup, 4, FighterLogicState.Startup, 3),
            (FighterLogicState.Active, 3, FighterLogicState.Active, 2),
            (FighterLogicState.Recovery, 8, FighterLogicState.Recovery, 7),
        })
        {
            var tick = FighterStateMachine.Tick(state, DemoRoster.FireballPrototype,
                remaining, Resolved(), RecognitionState.Empty, Kai, 0);
            Assert.Equal(expectedNext, tick.State);
            Assert.Equal(expectedRemaining, tick.PhaseRemainingFrames);
        }
    }

    [Fact]
    public void Tick_MissingMoveIdInMoveState_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => FighterStateMachine.Tick(
            FighterLogicState.Startup, null, 2, Resolved(), RecognitionState.Empty, Kai, 0));
    }

    [Fact]
    public void Tick_UnknownMoveId_Throws()
    {
        // 配置与状态不匹配（状态携带未配置的招式）→ 立即失败而非静默修正。
        Assert.Throws<InvalidOperationException>(() => FighterStateMachine.Tick(
            FighterLogicState.Startup, "NoSuchMove", 2, Resolved(), RecognitionState.Empty, Kai, 0));
    }

    // ─────────────────────────────────────────────────────────────
    // 消费入口（详细契约见 MoveConsumptionTests，这里验证状态机集成）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Idle_WithValidBufferedCommand_StartsMove()
    {
        RecognitionState recognition = RecognitionWithFireball(frame: 4);

        var tick = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            Resolved(), recognition, Kai, 5);

        Assert.Equal(FighterLogicState.Startup, tick.State);
        Assert.Equal(DemoRoster.FireballPrototype, tick.MoveId);
        Assert.Equal(4, tick.PhaseRemainingFrames); // StartupFrames
        Assert.NotNull(tick.ConsumedMatch);
        Assert.Equal("236A", tick.ConsumedMatch!.CommandId);
        // 消费标记已应用：新识别状态里该结果为已消费。
        Assert.True(tick.Recognition.Buffer.Single(r => r.Match.RecognizedFrame == 4).Consumed);
    }

    [Fact]
    public void InMove_BufferedCommand_IsNotConsumed()
    {
        // 动作期间不允许重复启动（B8）：缓冲有效也不消费，原样保留。
        RecognitionState recognition = RecognitionWithFireball(frame: 4);

        var tick = FighterStateMachine.Tick(FighterLogicState.Startup,
            DemoRoster.FireballPrototype, 2, Resolved(), recognition, Kai, 5);

        Assert.Equal(FighterLogicState.Startup, tick.State);
        Assert.Null(tick.ConsumedMatch);
        Assert.False(tick.Recognition.Buffer[0].Consumed); // 识别状态未被改动。
    }

    [Fact]
    public void RecoveryCompletion_ConsumesStillValidCommandSameFrame()
    {
        // B5：恢复到可行动状态时，同帧消费仍有效的指令。
        // 用长缓冲时效的指令表构造：帧 4 识别 236A（时效 60 帧），收招在帧 6 结束。
        var longLifetimeCommands = new[]
        {
            new CommandDefinition("236A",
                new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
                InputBits.LightPunch, priority: 10, bufferLifetimeFrames: 60),
        };
        var definition = new FighterDefinition("Kai", longLifetimeCommands,
            new[] { new MoveDefinition(DemoRoster.FireballPrototype, "236A", 4, 3, 8) });

        InputBits[] log =
        {
            InputBits.Down, InputBits.Down,
            InputBits.Down | InputBits.Right, InputBits.Down | InputBits.Right,
            InputBits.Right | InputBits.LightPunch,
            InputBits.None, InputBits.None,
        };
        RecognitionState recognition = RecognitionState.Empty;
        for (int f = 0; f < log.Length; f++)
            recognition = recognition.Advance(log[f], Facing.Right, f, 0, definition.Commands);

        var tick = FighterStateMachine.Tick(FighterLogicState.Recovery,
            DemoRoster.FireballPrototype, 1, Resolved(), recognition, definition, 7);

        // 收招完成 → 同帧消费缓冲中的 236A → 再次进入 Startup。
        Assert.Equal(FighterLogicState.Startup, tick.State);
        Assert.NotNull(tick.ConsumedMatch);
        Assert.Equal(4, tick.ConsumedMatch!.RecognizedFrame);
    }
}
