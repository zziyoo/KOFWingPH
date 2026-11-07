using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 04 集成测试（B6/B8）：通过 <see cref="BattleSimulation"/> 与
/// <see cref="BattleSession"/> 驱动完整链路
/// 「输入 → 指令识别 → 缓冲 → 消费 → 状态机推进」，覆盖确定性、快照重放、
/// 帧率一致、暂停、帧同步与观察者故障封闭。
/// </summary>
public class Phase04IntegrationTests
{
    private static readonly FightRoster Roster = DemoRoster.CreateKaiVsRei();

    private static BattleSimulation NewSim() => new(Roster);

    /// <summary>构造 236A 输入日志：↓(c-4..c-3) ↘(c-2..c-1) →+A(c)（P1，朝右）。</summary>
    private static Dictionary<int, FrameInput> Log236A(int completionFrame, InputBits p2 = InputBits.None)    {
        var log = new Dictionary<int, FrameInput>();
        for (int f = 0; f <= completionFrame; f++)
        {
            InputBits p1 = f switch
            {
                var x when x <= completionFrame - 2 => InputBits.Down,
                var x when x <= completionFrame - 1 => InputBits.Down | InputBits.Right,
                _ => InputBits.Right | InputBits.LightPunch,
            };
            log[f] = new FrameInput(f, p1, p2);
        }

        return log;
    }

    private static BattleState Run(BattleSimulation sim, Dictionary<int, FrameInput> log, int untilFrame)
    {
        BattleState state = BattleState.CreateDefault();
        for (int f = 0; f <= untilFrame; f++)
        {
            state = sim.Step(state,
                log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f)).State;
        }

        return state;
    }

    // ─────────────────────────────────────────────────────────────
    // 完整招式时间线（B8，FireballPrototype = 4/3/8）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Move_Timeline_AdvancesThroughPhasesAndBack()
    {
        var sim = NewSim();
        var log = Log236A(completionFrame: 10);

        // 状态帧约定：state(k) = 完成第 k-1 帧推进后的权威状态。
        // 帧 9 结束时仍无识别（A 未到）→ 待机（但按住下 → 蹲姿）。
        BattleState s = Run(sim, log, untilFrame: 9);
        Assert.Equal(FighterLogicState.Crouching, s.Fighter(0).LogicState);

        // 帧 10 识别并消费（零帧延迟）→ state(11) 显示 Startup(4)。
        s = sim.Step(s, log[10]).State;
        Assert.Equal(FighterLogicState.Startup, s.Fighter(0).LogicState);
        Assert.Equal(DemoRoster.FireballPrototype, s.Fighter(0).MoveId);
        Assert.Equal(4, s.Fighter(0).PhaseRemainingFrames);

        // Startup 恰好 4 帧：state(12/13/14) 剩 3/2/1，state(15) 进入 Active(3)。
        s = sim.Step(s, FrameInput.Empty(11)).State;
        Assert.Equal((FighterLogicState.Startup, 3), (s.Fighter(0).LogicState, s.Fighter(0).PhaseRemainingFrames));
        s = sim.Step(s, FrameInput.Empty(12)).State;
        Assert.Equal((FighterLogicState.Startup, 2), (s.Fighter(0).LogicState, s.Fighter(0).PhaseRemainingFrames));
        s = sim.Step(s, FrameInput.Empty(13)).State;
        Assert.Equal((FighterLogicState.Startup, 1), (s.Fighter(0).LogicState, s.Fighter(0).PhaseRemainingFrames));
        s = sim.Step(s, FrameInput.Empty(14)).State;
        Assert.Equal((FighterLogicState.Active, 3), (s.Fighter(0).LogicState, s.Fighter(0).PhaseRemainingFrames));

        // Active 恰好 3 帧：state(18) 进入 Recovery(8)。
        s = sim.Step(s, FrameInput.Empty(15)).State;
        Assert.Equal((FighterLogicState.Active, 2), (s.Fighter(0).LogicState, s.Fighter(0).PhaseRemainingFrames));
        s = sim.Step(s, FrameInput.Empty(16)).State;
        Assert.Equal((FighterLogicState.Active, 1), (s.Fighter(0).LogicState, s.Fighter(0).PhaseRemainingFrames));
        s = sim.Step(s, FrameInput.Empty(17)).State;
        Assert.Equal((FighterLogicState.Recovery, 8), (s.Fighter(0).LogicState, s.Fighter(0).PhaseRemainingFrames));

        // Recovery 恰好 8 帧：state(26) 回到站立待机。
        for (int f = 18; f <= 24; f++)
            s = sim.Step(s, FrameInput.Empty(f)).State;
        Assert.Equal((FighterLogicState.Recovery, 1), (s.Fighter(0).LogicState, s.Fighter(0).PhaseRemainingFrames));
        s = sim.Step(s, FrameInput.Empty(25)).State;
        Assert.Equal(FighterLogicState.Standing, s.Fighter(0).LogicState);
        Assert.Null(s.Fighter(0).MoveId);
        Assert.Equal(0, s.Fighter(0).PhaseRemainingFrames);

        // 全程生命值与坐标不变（无输入时地面运动不产生位移）。
        Assert.Equal(1000, s.Fighter(0).Health);
        Assert.Equal(-120, s.Fighter(0).PositionX.ToIntTruncated());
    }

    [Fact]
    public void Move_SecondIndependentCommand_CanFireAgainAfterCompletion()
    {
        // 第一条 236A 完整走完后，重新输入独立手势 → 可再次发动（B8）。
        var sim = NewSim();
        var first = Log236A(completionFrame: 10);

        BattleState s = Run(sim, first, untilFrame: 27); // 完成并回到待机
        Assert.Equal(FighterLogicState.Standing, s.Fighter(0).LogicState);

        // 第二次 236A（帧 28 起）。
        var second = Log236A(completionFrame: 32);
        s = RunFrom(sim, s, second, fromFrame: 28, untilFrame: 33);

        Assert.Equal(FighterLogicState.Startup, s.Fighter(0).LogicState);
        Assert.Equal(DemoRoster.FireballPrototype, s.Fighter(0).MoveId);
    }

    private static BattleState RunFrom(
        BattleSimulation sim, BattleState state, Dictionary<int, FrameInput> log,
        int fromFrame, int untilFrame)
    {
        for (int f = fromFrame; f <= untilFrame; f++)
        {
            state = sim.Step(state,
                log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f)).State;
        }

        return state;
    }

    // ─────────────────────────────────────────────────────────────
    // 动作期间重复请求 / 蹲下不覆盖动作（B9）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DuringMove_RepeatInput_DoesNotRestartOrDuplicate()
    {
        var sim = NewSim();
        // 帧 4 完成 236A；帧 5-9 期间继续按 A（按住不重复、无新边沿）。
        var log = Log236A(completionFrame: 4);
        BattleState s = Run(sim, log, untilFrame: 4);
        Assert.Equal(FighterLogicState.Startup, s.Fighter(0).LogicState); // state(5) = Startup(4)

        for (int f = 5; f <= 9; f++)
            s = sim.Step(s, new FrameInput(f, InputBits.LightPunch, InputBits.None)).State;

        // 只有一次发动：动作按时间线推进（帧 9 已进入 Active），缓冲中的结果已消费。
        Assert.Equal(FighterLogicState.Active, s.Fighter(0).LogicState);
        Assert.Equal(2, s.Fighter(0).PhaseRemainingFrames);
        Assert.All(s.Fighter(0).RecognitionState.Buffer, r => Assert.True(r.Consumed));
    }

    [Fact]
    public void DuringMove_CrouchInput_DoesNotOverride()
    {
        var sim = NewSim();
        var log = Log236A(completionFrame: 4);
        BattleState s = Run(sim, log, untilFrame: 5);
        Assert.Equal(FighterLogicState.Startup, s.Fighter(0).LogicState); // state(6) = Startup(3)

        // Startup/Active 期间按住下：状态不被覆盖（B9）。
        for (int f = 6; f <= 12; f++)
            s = sim.Step(s, new FrameInput(f, InputBits.Down, InputBits.None)).State;

        Assert.True(s.Fighter(0).IsInMove);
        Assert.NotEqual(FighterLogicState.Crouching, s.Fighter(0).LogicState);

        // 收招完成后，按住下 → 回到待机即蹲下。
        for (int f = 13; f <= 20; f++)
            s = sim.Step(s, new FrameInput(f, InputBits.Down, InputBits.None)).State;
        Assert.Equal(FighterLogicState.Crouching, s.Fighter(0).LogicState);
    }

    // ─────────────────────────────────────────────────────────────
    // P1/P2 独立 + 角色专属表
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void P1AndP2_Independent_CharactersHaveOwnTables()
    {
        var sim = NewSim();
        // P1（Kai）做 236A；P2（Rei，朝左）同帧做 623A 手势（Rei 没有该指令 → 不识别不发动）。
        var log = new Dictionary<int, FrameInput>();
        for (int f = 0; f <= 10; f++)
        {
            InputBits p1 = f switch
            {
                <= 8 => InputBits.Down,
                9 => InputBits.Down | InputBits.Right,
                _ => InputBits.Right | InputBits.LightPunch,
            };
            // P2 朝左：→（相对）= Left；623A 手势 Left, Down, Down|Left + 轻拳位。
            InputBits p2 = f switch
            {
                <= 8 => InputBits.Left,
                9 => InputBits.Left | InputBits.Down,
                _ => InputBits.Left | InputBits.Down | InputBits.LightPunch,
            };
            log[f] = new FrameInput(f, p1, p2);
        }

        BattleState s = Run(sim, log, untilFrame: 11);

        Assert.Equal(FighterLogicState.Startup, s.Fighter(0).LogicState);
        Assert.Equal(DemoRoster.FireballPrototype, s.Fighter(0).MoveId);
        // P2：623A 不在 Rei 指令表 → 无识别、无动作。
        Assert.Equal(FighterLogicState.Standing, s.Fighter(1).LogicState);
        Assert.Null(s.Fighter(1).MoveId);
        Assert.DoesNotContain(s.Fighter(1).RecognitionState.Buffer, r => r.Match.CommandId == "623A");
    }

    // ─────────────────────────────────────────────────────────────
    // 确定性 / 旧快照重放（B6.3/B6.8）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Determinism_SameInputs_ProduceSameActionStates()
    {
        var log = Log236A(completionFrame: 10);
        var inputs = new List<FrameInput>();
        for (int f = 0; f <= 20; f++)
            inputs.Add(log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f));

        BattleState RunAll()
        {
            var sim = NewSim();
            BattleState s = BattleState.CreateDefault();
            foreach (FrameInput input in inputs)
                s = sim.Step(s, input).State;
            return s;
        }

        BattleState a = RunAll();
        BattleState b = RunAll();

        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(a.Fighter(p).LogicState, b.Fighter(p).LogicState);
            Assert.Equal(a.Fighter(p).MoveId, b.Fighter(p).MoveId);
            Assert.Equal(a.Fighter(p).PhaseRemainingFrames, b.Fighter(p).PhaseRemainingFrames);
            Assert.Equal(a.Fighter(p).RecognitionState, b.Fighter(p).RecognitionState);
        }
    }

    [Fact]
    public void OldSnapshot_ReExecutesSameInput_AndReproducesActionState()
    {
        // 回滚重放需求：从旧快照重新执行同一输入，动作状态与首次完全一致。
        var sim = NewSim();
        var log = Log236A(completionFrame: 10);

        BattleState start = BattleState.CreateDefault();
        BattleState mid = sim.Step(start, log[0]).State; // 旧快照（帧 1）

        BattleState firstRun = RunFrom(sim, mid, log, fromFrame: 1, untilFrame: 12);
        BattleState secondRun = RunFrom(sim, mid, log, fromFrame: 1, untilFrame: 12);

        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(firstRun.Fighter(p).LogicState, secondRun.Fighter(p).LogicState);
            Assert.Equal(firstRun.Fighter(p).MoveId, secondRun.Fighter(p).MoveId);
            Assert.Equal(firstRun.Fighter(p).PhaseRemainingFrames, secondRun.Fighter(p).PhaseRemainingFrames);
            Assert.Equal(firstRun.Fighter(p).RecognitionState, secondRun.Fighter(p).RecognitionState);
        }
    }

    [Fact]
    public void DifferentFrameCadence_SameLogicLog_SameResult()
    {
        // 「不同渲染帧率」等价于「一次 Advance 推进 1..N 帧」：同一逻辑输入日志
        // 无论被分成几批执行，权威状态一致。
        var log = Log236A(completionFrame: 10);
        var inputs = new List<FrameInput>();
        for (int f = 0; f <= 20; f++)
            inputs.Add(log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f));

        BattleState allAtOnce = Run(NewSim(), log, untilFrame: 20);

        var sim = NewSim();
        BattleState batched = BattleState.CreateDefault();
        // 批次大小 1/2/5 混合推进。
        int index = 0;
        foreach (int batch in new[] { 1, 2, 5, 1, 2, 5, 1, 4 })
        {
            for (int i = 0; i < batch && index < inputs.Count; i++, index++)
                batched = sim.Step(batched, inputs[index]).State;
        }

        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(allAtOnce.Fighter(p).LogicState, batched.Fighter(p).LogicState);
            Assert.Equal(allAtOnce.Fighter(p).RecognitionState, batched.Fighter(p).RecognitionState);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 会话级：两种推进路径、暂停、帧同步、观察者故障（B6.7/B6.9）
    // ─────────────────────────────────────────────────────────────

    private static BattleSession NewSession(Dictionary<int, FrameInput> log) => new(
        scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1000),
        simulation: new BattleSimulation(Roster),
        frameCommitted: null);

    [Fact]
    public void Session_AdvanceAndStepExact_KeepFrameSyncDuringMove()
    {
        var log = Log236A(completionFrame: 10);
        var session = NewSession(log);
        session.StepExact(10, inputProvider: f => log[f]);

        // 按住下期间角色蹲下（B9）：state(10) 为蹲姿（识别尚未完成——A 未到）。
        Assert.Equal(FighterLogicState.Crouching, session.CurrentState.Fighter(0).LogicState);

        // 暂停：Advance 不推进、动作阶段不动。
        session.Scheduler.Pause();
        int advanced = session.Advance(1.0);
        Assert.Equal(0, advanced);
        Assert.Equal(FighterLogicState.Crouching, session.CurrentState.Fighter(0).LogicState);

        // 显式单步在暂停下依然生效（D20）：帧 10 完成 236A 并消费（蹲姿也允许发动）→ Startup。
        session.StepExact(1, inputProvider: f => log[f]);
        Assert.Equal(FighterLogicState.Startup, session.CurrentState.Fighter(0).LogicState);

        session.Scheduler.Resume();
        session.StepExact(5, inputProvider: f =>
            log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f));

        // 帧 11-15 推进后处于 Active（时间线：Startup 占 state 11-14，state 15 起 Active）。
        Assert.True(session.IsFrameSyncIntact);
        Assert.Equal(FighterLogicState.Active, session.CurrentState.Fighter(0).LogicState);
        Assert.Equal(2, session.CurrentState.Fighter(0).PhaseRemainingFrames);
    }

    [Fact]
    public void Session_AdvancePath_DrivesSameTimeline()
    {
        // 时间驱动路径（Advance + 幂等委托输入源）与直接 Step 一致。
        var log = Log236A(completionFrame: 10);
        var session = NewSession(log);

        // 60Hz × 0.5s = 30 帧；输入委托按帧号查日志。
        session.Advance(0.5, inputProvider: f =>
            log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f));

        Assert.True(session.IsFrameSyncIntact);
        Assert.Equal(30, session.CurrentState.Frame);
        // 时间线：帧 10 识别消费 → Startup 占帧 11-14 → Active 15-17 → Recovery 18-25
        // → 帧 27 回到待机。帧 30 应为站立待机、生命值不变。
        Assert.Equal(FighterLogicState.Standing, session.CurrentState.Fighter(0).LogicState);
        Assert.Null(session.CurrentState.Fighter(0).MoveId);
        Assert.Equal(1000, session.CurrentState.Fighter(0).Health);
    }

    [Fact]
    public void Session_ObserverFault_LeavesAuthoritativeStateConsistent()
    {
        // B6.9：Faulted 机制不退化——权威状态（含动作与识别快照）保持帧同步，
        // Recover 后可继续（识别已内嵌于权威状态，无派生状态漏帧问题）。
        var log = Log236A(completionFrame: 10);
        bool observerThrown = false;
        var session = new BattleSession(
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 1000),
            simulation: new BattleSimulation(Roster),
            frameCommitted: (_, _) =>
            {
                if (!observerThrown)
                {
                    observerThrown = true;
                    throw new InvalidOperationException("observer boom");
                }
            });

        Assert.Throws<InvalidOperationException>(
            () => session.StepExact(3, inputProvider: f => log[f]));

        Assert.True(session.IsFaulted);
        Assert.Equal(0, session.FaultedFrame); // 帧 0 提交后观察者抛出
        Assert.True(session.IsFrameSyncIntact);

        session.Recover();
        // 故障帧本身已完整提交（帧同步保持），故障使本批次中止：
        // 只提交了帧 0 → state(1)；恢复后再推进 2 帧到 state(3)。
        session.StepExact(2, inputProvider: f =>
            log.TryGetValue(f, out FrameInput input) ? input : FrameInput.Empty(f));
        Assert.True(session.IsFrameSyncIntact);
        Assert.Equal(3, session.CurrentState.Frame);
    }

    // ─────────────────────────────────────────────────────────────
    // 状态机含于快照（B2.10）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ActionState_IsPartOfSnapshot_MiddleSnapshotUnaffected()
    {
        var sim = NewSim();
        var log = Log236A(completionFrame: 10);

        BattleState mid = sim.Step(BattleState.CreateDefault(), log[0]).State;
        BattleState midCopy = mid;

        BattleState current = RunFrom(sim, mid, log, fromFrame: 1, untilFrame: 16);
        Assert.Equal(FighterLogicState.Active, current.Fighter(0).LogicState);
        Assert.Equal(1, current.Fighter(0).PhaseRemainingFrames); // state(17) = Active(1)

        // 中间快照完全未受后续推进影响（含动作状态与识别快照）。
        // 帧 0 输入为下方向 → state(1) 为蹲姿。
        Assert.Equal(1, midCopy.Frame);
        Assert.Equal(FighterLogicState.Crouching, midCopy.Fighter(0).LogicState);
        Assert.Equal(midCopy.Fighter(0), mid.Fighter(0));
    }
}
