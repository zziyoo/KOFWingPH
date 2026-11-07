using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 06C（D81/D82/D83）：攻击实例去重、确定性事件流与提交后发布。
///
/// <para>演示阵容 Kai 5A：Startup 3 / Active 2（攻击范围全程）/ Recovery 6、伤害 30、
/// 命中硬直 12。识别链路时间线：帧 0 按 A → 消费 → 帧 0–2 Startup →
/// 帧 3 = Active 首帧（elapsed 0，命中）→ 帧 4 = elapsed 1（去重抑制）→ 帧 5 Recovery。</para>
/// </summary>
public class CombatEventTests
{
    private static BattleSimulation NewSim() => new(DemoRoster.CreateKaiVsRei());

    private static BattleState DefaultPair() => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion);

    private static FighterState P1(BattleState s) => s.Fighter(0);
    private static FighterState P2(BattleState s) => s.Fighter(1);

    // ─────────────────────────────────────────────────────────────
    // D81：同一攻击实例只结算一次
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MultiFrameActive_HitsOnlyOnce()
    {
        // 5A Active 2 帧：帧 3 命中（970）；帧 4 接触仍在但同实例已结算 → 不重复扣血。
        var sim = NewSim();
        BattleState state = DefaultPair();

        BattleState frame3 = sim.Step(state, new FrameInput(0, InputBits.LightPunch, InputBits.None)).State;
        for (int f = 1; f <= 3; f++)
            frame3 = sim.Step(frame3, new FrameInput(f, InputBits.None, InputBits.None)).State;

        Assert.Equal(970, P2(frame3).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(frame3).LogicState);

        // 帧 4：第二 Active 帧——接触仍会发生，但去重后不再结算。
        BattleState frame4 = sim.Step(frame3, new FrameInput(4, InputBits.None, InputBits.None)).State;
        Assert.Equal(970, P2(frame4).Health); // 不重复扣血
        Assert.Equal(FighterLogicState.Hitstun, P2(frame4).LogicState);
        Assert.Equal(11, P2(frame4).StunRemainingFrames); // 硬直正常倒计时（未重置）
    }

    [Fact]
    public void BlockedAttack_AlsoConsumed_NoDoubleGuardSettlement()
    {
        // 被防御亦视为已结算（D81）：第二 Active 帧不再重复防御结算（Blockstun 不重置）。
        var sim = NewSim();
        BattleState state = DefaultPair();

        BattleState frame3 = sim.Step(state, new FrameInput(0, InputBits.LightPunch, InputBits.None)).State;
        for (int f = 1; f <= 3; f++)
            frame3 = sim.Step(frame3, new FrameInput(f, InputBits.None, InputBits.Right)).State;

        Assert.Equal(1000, P2(frame3).Health);
        Assert.Equal(FighterLogicState.Blockstun, P2(frame3).LogicState);
        Assert.Equal(8, P2(frame3).StunRemainingFrames);

        BattleState frame4 = sim.Step(frame3, new FrameInput(4, InputBits.None, InputBits.Right)).State;
        Assert.Equal(1000, P2(frame4).Health);
        Assert.Equal(7, P2(frame4).StunRemainingFrames); // 正常倒计时（未重置）
    }

    [Fact]
    public void NewInstance_CanHitAgain_AfterPreviousResolved()
    {
        // 第一次攻击完整结束后，第二次独立发动（新发动帧）可再次命中。
        var sim = NewSim();
        BattleState state = DefaultPair();

        // 第一次 5A：帧 0 发动、帧 3 命中（970）、帧 5–10 Recovery、帧 11 回站立。
        for (int f = 0; f <= 11; f++)
            state = sim.Step(state, new FrameInput(f, f == 0 ? InputBits.LightPunch : InputBits.None, InputBits.None)).State;

        Assert.Equal(FighterLogicState.Standing, P1(state).LogicState);
        Assert.Equal(970, P2(state).Health);

        // 第二次 5A（新实例，发动帧 12）：帧 15 命中（940）。
        for (int f = 12; f <= 15; f++)
            state = sim.Step(state, new FrameInput(f, f == 12 ? InputBits.LightPunch : InputBits.None, InputBits.None)).State;

        Assert.Equal(940, P2(state).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(state).LogicState);
    }

    [Fact]
    public void InterruptedAttack_DoesNotRevive()
    {
        // 双方互中：P1 的招式被中断 → 攻击框消失 → 后续帧不再产生接触/命中。
        var sim = NewSim();
        BattleState state = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right) with
            {
                LogicState = FighterLogicState.Active,
                MoveId = DemoRoster.StandLightPunch,
                PhaseRemainingFrames = 2,
                MoveStartedFrame = 0,
            },
            FighterState.Create(1, 1000, 0, Facing.Left) with
            {
                LogicState = FighterLogicState.Active,
                MoveId = DemoRoster.BackDraft,
                PhaseRemainingFrames = 2,
                MoveStartedFrame = 0,
            },
        }, BattleState.CreateDefault().StateVersion);

        BattleState after = sim.Step(state, FrameInput.Empty(0)).State;

        // 双方互中（P2 吃 5A 30、P1 吃 BackDraft 45），双双进入硬直、招式被中断。
        Assert.Equal(955, P1(after).Health);
        Assert.Equal(970, P2(after).Health);
        Assert.Equal(FighterLogicState.Hitstun, P1(after).LogicState);
        Assert.Null(P1(after).MoveId);

        // 后续帧：双方都无攻击框（已中断）→ 无接触、无进一步伤害。
        BattleState later = sim.Step(after, new FrameInput(1, InputBits.None, InputBits.None)).State;
        Assert.Equal(955, P1(later).Health);
        Assert.Equal(970, P2(later).Health);
    }

    // ─────────────────────────────────────────────────────────────
    // D82：确定性事件流
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Events_FramesTypesAndIds_AreExact()
    {
        var sim = NewSim();
        BattleState state = DefaultPair();
        var all = new List<BattleEvent>();

        for (int f = 0; f <= 4; f++)
        {
            BattleStepResult result = sim.Step(state,
                new FrameInput(f, f == 0 ? InputBits.LightPunch : InputBits.None, InputBits.None));
            state = result.State;
            all.AddRange(result.Events);
        }

        // 帧 0：MoveStarted。
        var started = Assert.Single(all, e => e.Type == BattleEventType.MoveStarted);
        Assert.Equal((0, 0, 0, 0, DemoRoster.StandLightPunch),
            (started.Frame, started.EventId, started.SourceFighterId, started.TargetFighterId, started.MoveId));

        // 帧 3：AttackActive → Hit(30) → HitstunStarted(12)，EventId = Frame×16+序号。
        var frame3 = all.Where(e => e.Frame == 3).ToArray();
        Assert.Equal(3, frame3.Length);
        Assert.Equal(BattleEventType.AttackActive, frame3[0].Type);
        Assert.Equal(3 * 16, frame3[0].EventId);
        Assert.Equal(BattleEventType.Hit, frame3[1].Type);
        Assert.Equal(3 * 16 + 1, frame3[1].EventId);
        Assert.Equal(30, frame3[1].Amount);
        Assert.Equal((0, 1), (frame3[1].SourceFighterId, frame3[1].TargetFighterId));
        Assert.Equal(BattleEventType.HitstunStarted, frame3[2].Type);
        Assert.Equal(12, frame3[2].Amount);

        // 帧 4：第二 Active 帧无 AttackActive（仅首帧）、命中被去重 → 无事件。
        Assert.Empty(all.Where(e => e.Frame == 4));

        // EventId 全局唯一。
        Assert.Equal(all.Count, all.Select(e => e.EventId).Distinct().Count());
    }

    [Fact]
    public void Events_AreReadOnly()
    {
        var sim = NewSim();
        BattleState state = DefaultPair();
        BattleStepResult result = sim.Step(state, new FrameInput(0, InputBits.LightPunch, InputBits.None));

        // D61 同一先例：强转 List 得 null，IList 写操作抛异常。
        Assert.Null(result.Events as List<BattleEvent>);
        Assert.Throws<NotSupportedException>(() =>
        {
            IList<BattleEvent> mutable = (IList<BattleEvent>)result.Events;
            mutable.Add(new BattleEvent(0, 999, BattleEventType.Hit, 0, 1, "X", 1));
        });

        Assert.Null(result.Contacts as List<Combat.AttackContact>);
        Assert.Throws<NotSupportedException>(() =>
        {
            IList<Combat.AttackContact> mutable = (IList<Combat.AttackContact>)result.Contacts;
            mutable.Add(new Combat.AttackContact(0, 1, "X", 0));
        });
    }

    [Fact]
    public void SameInputs_Replay_ProducesIdenticalEvents()
    {
        Func<int, FrameInput> script = f => new FrameInput(
            f, f == 0 ? InputBits.LightPunch : InputBits.None, f >= 3 ? InputBits.Right : InputBits.None);

        var simA = NewSim();
        var simB = NewSim();
        BattleState a = DefaultPair();
        BattleState b = DefaultPair();
        var eventsA = new List<BattleEvent>();
        var eventsB = new List<BattleEvent>();

        for (int f = 0; f <= 20; f++)
        {
            var ra = simA.Step(a, script(f));
            a = ra.State;
            eventsA.AddRange(ra.Events);
            var rb = simB.Step(b, script(f));
            b = rb.State;
            eventsB.AddRange(rb.Events);
        }

        Assert.Equal(eventsA, eventsB);
        Assert.Equal(a.Fighter(0), b.Fighter(0));
        Assert.Equal(a.Fighter(1), b.Fighter(1));
    }

    // ─────────────────────────────────────────────────────────────
    // D83：提交后发布与会话语义
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Session_PublishesEvents_AfterCommit()
    {
        var collected = new List<BattleEvent>();
        var session = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()),
            eventsCommitted: (input, state, events) => collected.AddRange(events));

        // P1 走近 P2 再 5A：命中帧产生 Hit 事件。
        session.StepExact(90, inputProvider: f => new FrameInput(f, InputBits.Right, InputBits.None));
        session.StepExact(1, inputProvider: f => new FrameInput(f, InputBits.LightPunch, InputBits.None));
        session.StepExact(6, inputProvider: f => new FrameInput(f, InputBits.None, InputBits.None));

        // 命中发生在 5A 的首个 Active 帧；收集到 MoveStarted/AttackActive/Hit/HitstunStarted。
        Assert.Contains(collected, e => e.Type == BattleEventType.MoveStarted && e.SourceFighterId == 0);
        Assert.Contains(collected, e => e.Type == BattleEventType.AttackActive && e.SourceFighterId == 0);
        var hit = Assert.Single(collected, e => e.Type == BattleEventType.Hit);
        Assert.Equal(30, hit.Amount);
        Assert.Equal((0, 1), (hit.SourceFighterId, hit.TargetFighterId));
        Assert.Contains(collected, e => e.Type == BattleEventType.HitstunStarted);
        Assert.Equal(collected.Count, collected.Select(e => e.EventId).Distinct().Count());

        // 状态与事件一致：Hit 数量 × 30 = 实际扣血（6 帧窗口内恰好一次命中）。
        Assert.Equal(1000 - 30 * collected.Count(e => e.Type == BattleEventType.Hit),
            session.CurrentState.Fighter(1).Health);
    }

    [Fact]
    public void Session_FailedInputCommit_DoesNotPublishEvents()
    {
        // 输入提交失败：该帧没有事件发布（观察者不被调用），权威状态不动。
        var collected = new List<BattleEvent>();
        var failSource = new CommitFailSource(failOnFrame: 0);
        var session = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()),
            eventsCommitted: (input, state, events) => collected.AddRange(events));

        Assert.Throws<InvalidOperationException>(() =>
            session.Advance(1.0 / 60.0, inputSource: failSource));

        Assert.Empty(collected);
        Assert.Equal(0, session.CurrentState.Frame);
    }

    [Fact]
    public void Session_EventsObserverThrow_EntersFaulted()
    {
        int calls = 0;
        var session = new BattleSession(
            simulation: new BattleSimulation(DemoRoster.CreateKaiVsRei()),
            eventsCommitted: (input, state, events) =>
            {
                calls++;
                if (calls == 1)
                    throw new InvalidOperationException("观察者故障");
            });

        // 首帧：事件观察者抛异常 → 该帧已提交（状态/记账落定）→ 故障封闭。
        Assert.Throws<InvalidOperationException>(() =>
            session.StepExact(1, inputProvider: f => FrameInput.Empty(f)));
        Assert.True(session.IsFaulted);
        Assert.Equal(0, session.FaultedFrame);
        Assert.Equal(1, session.CurrentState.Frame); // 帧已提交

        // Recover 后继续：不重发首帧事件；后续帧正常投递（消费者按 EventId 去重）。
        session.Recover();
        session.StepExact(1, inputProvider: f => FrameInput.Empty(f));
        Assert.Equal(2, session.CurrentState.Frame);
        Assert.Equal(2, calls);
        Assert.False(session.IsFaulted);
    }

    [Fact]
    public void MidSnapshotReplay_ProducesIdenticalEvents()
    {
        Func<int, FrameInput> script = f => new FrameInput(
            f,
            f == 10 ? InputBits.LightPunch : (f < 8 ? InputBits.Right : InputBits.None),
            InputBits.None);

        // 完整运行（含事件收集）。
        var simFull = NewSim();
        BattleState full = BattleState.CreateDefault();
        var fullEvents = new List<BattleEvent>();
        for (int f = 0; f < 40; f++)
        {
            var r = simFull.Step(full, script(f));
            full = r.State;
            fullEvents.AddRange(r.Events);
        }

        // 前半 + 中间快照续跑：两段事件拼接后必须与完整运行逐位一致。
        Assert.True(fullEvents.Count > 0, "脚本应产生事件（帧 10 发动、帧 13 命中）");

        var simHalf = NewSim();
        BattleState half = BattleState.CreateDefault();
        var firstHalfEvents = new List<BattleEvent>();
        for (int f = 0; f < 20; f++)
        {
            var r = simHalf.Step(half, script(f));
            half = r.State;
            firstHalfEvents.AddRange(r.Events);
        }

        var continuedEvents = new List<BattleEvent>();
        BattleState cont = half;
        for (int f = 20; f < 40; f++)
        {
            var r = simHalf.Step(cont, script(f));
            cont = r.State;
            continuedEvents.AddRange(r.Events);
        }

        // 事件（含 EventId）与状态逐位一致；原始快照未被污染。
        Assert.Equal(fullEvents, firstHalfEvents.Concat(continuedEvents).ToList());
        Assert.Equal(full.Fighter(0), cont.Fighter(0));
        Assert.Equal(full.Fighter(1), cont.Fighter(1));
    }

    [Fact]
    public void LongRun_10000Frames_CombatDeterministic_EventsUnique()
    {
        // 混合战斗脚本（移动/攻击/防御/跳跃）10000 帧 ×2：状态与事件流逐位一致。
        static FrameInput Script(int f) => new(
            f,
            f switch
            {
                < 20 => InputBits.Right,
                20 => InputBits.LightPunch,
                < 60 => InputBits.Right,
                60 => InputBits.Up | InputBits.Right,
                < 100 => InputBits.Right,
                100 => InputBits.LightPunch,
                < 140 => InputBits.Left,
                < 180 => InputBits.Right,
                180 => InputBits.HeavyPunch,
                _ => (f % 120 < 60) ? InputBits.Right : InputBits.Left,
            },
            f switch
            {
                30 => InputBits.LightPunch,
                < 70 => InputBits.Right,
                70 => InputBits.Up,
                < 110 => InputBits.Left | InputBits.Down,
                110 => InputBits.HeavyKick,
                _ => (f % 90 < 45) ? InputBits.Right : InputBits.None,
            });

        static (BattleState, List<BattleEvent>) Run()
        {
            var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
            BattleState state = BattleState.CreateDefault();
            var events = new List<BattleEvent>();
            for (int f = 0; f < 10000; f++)
            {
                var r = sim.Step(state, Script(f));
                state = r.State;
                events.AddRange(r.Events);
            }
            return (state, events);
        }

        var (stateA, eventsA) = Run();
        var (stateB, eventsB) = Run();

        Assert.Equal(stateA.Fighter(0), stateB.Fighter(0));
        Assert.Equal(stateA.Fighter(1), stateB.Fighter(1));
        Assert.Equal(eventsA, eventsB);
        Assert.Equal(eventsA.Count, eventsA.Select(e => e.EventId).Distinct().Count());
    }

    /// <summary>测试专用输入源：指定帧的 Commit 抛异常（复用 03A 的失败注入模式）。</summary>
    private sealed class CommitFailSource : IInputSource
    {
        private readonly int _failOnFrame;
        public CommitFailSource(int failOnFrame) => _failOnFrame = failOnFrame;
        public FrameInput Prepare(int frame) => FrameInput.Empty(frame);
        public void Commit(FrameInput input)
        {
            if (input.Frame == _failOnFrame)
                throw new InvalidOperationException("注入的提交失败");
        }
    }
}
