using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 07B（D90/D91）：连段统计与数据驱动取消。
///
/// <para><b>测试配置</b>（双方同配置，便于镜像与互中）：</para>
/// <list type="bullet">
/// <item>Jab（5A = 裸按键轻拳）：Startup 1 / Active 3 / Recovery 6；攻击框 Active 前两帧生效，
///       伤害 10、命中硬直 20、防御硬直 10。</item>
/// <item>Special（236B = ↓↘→ + 重拳）：Startup 1 / Active 3 / Recovery 5；攻击框仅首帧，
///       伤害 20、命中硬直 20。</item>
/// <item>默认取消规则：Jab → Special，窗口 [1,6]（自发动帧起算），Active|Recovery，
///       需要<b>命中确认</b>（且确认必须来自更早帧）。</item>
/// </list>
///
/// <para><b>时间线</b>：帧 0 按 A → 帧 1 进入 Active 首帧并命中；帧 4 是第 4 个经过帧
/// （帧开始状态仍是 Active），帧 5 起帧开始状态才是 Recovery。</para>
/// </summary>
public class ComboCancelTests
{
    private const string Jab = "Jab";
    private const string Special = "Special";

    /// <summary>默认取消规则：命中确认，窗口 [1,6]，Active|Recovery。</summary>
    private static CancelDefinition HitCancel(
        int first = 1, int last = 6,
        MoveCancelPhases phases = MoveCancelPhases.Active | MoveCancelPhases.Recovery) =>
        new(Special, first, last, phases, requiresHitConfirm: true);

    private static BattleSimulation Sim(
        bool jabCanCancel = true,
        bool p2CanCancel = true,
        CancelDefinition? p1Cancel = null,
        CancelDefinition? p2Cancel = null)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition Build(string id, bool canCancel, CancelDefinition? cancel) => new(
            id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5),
                new CommandDefinition("236B",
                    new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
                    InputBits.HeavyPunch, maxSequenceFrames: 20, buttonToleranceFrames: 3, priority: 10),
            },
            new[]
            {
                new MoveDefinition(Jab, "5A", startupFrames: 1, activeFrames: 3, recoveryFrames: 6,
                    attacks: new[]
                    {
                        new AttackDefinition(
                            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                            FixedPoint.FromInt(20), FixedPoint.FromInt(20),
                            firstActiveFrame: 0, lastActiveFrame: 1,
                            AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10),
                    },
                    cancels: canCancel
                        ? new[] { cancel ?? HitCancel() }
                        : Array.Empty<CancelDefinition>()),
                new MoveDefinition(Special, "236B", startupFrames: 1, activeFrames: 3, recoveryFrames: 5,
                    attacks: new[]
                    {
                        new AttackDefinition(
                            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                            FixedPoint.FromInt(24), FixedPoint.FromInt(24),
                            firstActiveFrame: 0, lastActiveFrame: 0,
                            AttackHeight.Mid, damage: 20, hitstunFrames: 20, blockstunFrames: 10),
                    }),
            },
            movement);

        return new BattleSimulation(new FightRoster(
            Build("Kai", jabCanCancel, p1Cancel),
            Build("Rei", p2CanCancel, p2Cancel)));
    }

    private static BattleState Duel(int p1X = -60, int p2X = 0, int p2Health = 1000)
    {
        FighterState p2 = FighterState.Create(1, 1000, p2X, Facing.Left);
        if (p2Health < 1000)
            p2 = p2 with { Health = p2Health };

        return new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, p1X, Facing.Right),
            p2,
        }, BattleState.CreateDefault().StateVersion);
    }

    /// <summary>取消输入脚本：0 = 轻拳（发动 Jab），1..3 = ↓↘→，<paramref name="buttonFrame"/> = 重拳。</summary>
    private static InputBits CancelScript(int frame, int buttonFrame = 4) => frame switch
    {
        0 => InputBits.LightPunch,
        1 => InputBits.Down,
        2 => InputBits.Down | InputBits.Right,
        3 => InputBits.Right,
        _ => frame == buttonFrame ? InputBits.HeavyPunch : InputBits.None,
    };

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) =>
        new(frame, p1, p2);

    private static BattleState Step(BattleSimulation sim, BattleState state, InputBits p1, InputBits p2 = InputBits.None) =>
        sim.Step(state, F(state.Frame, p1, p2)).State;

    // ─────────────────────────────────────────────────────────────
    // 连段（D90）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FirstHit_StartsComboAtOne()
    {
        var sim = Sim();
        BattleState state = Duel();

        state = Step(sim, state, InputBits.LightPunch, InputBits.None);     // 帧 0：发动
        state = Step(sim, state, InputBits.None, InputBits.None);           // 帧 1：命中

        Assert.Equal(990, state.Fighter(1).Health);
        Assert.Equal(1, state.Fighter(1).Combo.Count);
        Assert.Equal(0, state.Fighter(1).Combo.AttackerId);
        Assert.False(state.Fighter(0).Combo.IsActive);                       // 攻击者自己没有连段记录
    }

    [Fact]
    public void SecondIndependentHit_ExtendsCombo()
    {
        var sim = Sim();
        BattleState state = Duel();

        // 第一击（帧 0 发动，帧 1 命中）→ P2 Hitstun(20)。
        for (int f = 0; f <= 1; f++)
            state = Step(sim, state, f == 0 ? InputBits.LightPunch : InputBits.None);

        Assert.Equal(1, state.Fighter(1).Combo.Count);

        // P1 帧 10 回到待机；第二记独立 Jab（新实例）帧 10 发动、帧 11 命中。
        for (int f = 2; f <= 11; f++)
            state = Step(sim, state, f == 10 ? InputBits.LightPunch : InputBits.None);

        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Equal(2, state.Fighter(1).Combo.Count);
        Assert.Equal(0, state.Fighter(1).Combo.AttackerId);
    }

    [Fact]
    public void SameInstance_DoesNotIncrementTwice()
    {
        // Jab 的攻击框在 Active 前两帧都生效：第二帧仍有接触，但 D81 去重 → 只结算一次、连段不涨。
        var sim = Sim();
        BattleState state = Duel();

        state = Step(sim, state, InputBits.LightPunch);
        BattleStepResult hitFrame = sim.Step(state, F(1, InputBits.None, InputBits.None));
        state = hitFrame.State;
        Assert.Single(hitFrame.Contacts);

        BattleStepResult secondActive = sim.Step(state, F(2, InputBits.None, InputBits.None));
        state = secondActive.State;

        Assert.Single(secondActive.Contacts);            // 几何上仍然命中
        Assert.Empty(secondActive.Events);               // 但去重后无结算事件
        Assert.Equal(990, state.Fighter(1).Health);
        Assert.Equal(1, state.Fighter(1).Combo.Count);
    }

    [Fact]
    public void ComboResets_WhenTargetRecovers()
    {
        var sim = Sim();
        BattleState state = Duel();

        for (int f = 0; f <= 1; f++)
            state = Step(sim, state, f == 0 ? InputBits.LightPunch : InputBits.None);

        // 等到 P2 真正恢复待机（硬直 20，帧 1 命中 → 帧 21 归零）。
        for (int f = 2; f <= 21; f++)
            state = Step(sim, state, InputBits.None);
        Assert.Equal(FighterLogicState.Standing, state.Fighter(1).LogicState);
        Assert.False(state.Fighter(1).Combo.IsActive);   // 恢复自由行动的帧末清空旧连段

        // 再打一次：新连段 1 Hit。
        for (int f = 22; f <= 23; f++)
            state = Step(sim, state, f == 22 ? InputBits.LightPunch : InputBits.None);

        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Equal(1, state.Fighter(1).Combo.Count);
    }

    [Fact]
    public void HitOnLastHitstunFrame_ExtendsCombo()
    {
        // 帧 1 命中 → 帧 20 是硬直最后 1 帧（仍未恢复自由行动）→ 计入同一连段。
        var sim = Sim();
        BattleState state = Duel();

        for (int f = 0; f <= 1; f++)
            state = Step(sim, state, f == 0 ? InputBits.LightPunch : InputBits.None);

        for (int f = 2; f <= 20; f++)
            state = Step(sim, state, f == 19 ? InputBits.LightPunch : InputBits.None);

        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(1).LogicState);
        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Equal(2, state.Fighter(1).Combo.Count);
    }

    [Fact]
    public void HitOnRecoveryFrame_StartsNewCombo()
    {
        // 帧 21 硬直归零 → 待机（恢复自由行动）。这一帧被命中按新连段 1 Hit 计。
        var sim = Sim();
        BattleState state = Duel();

        for (int f = 0; f <= 1; f++)
            state = Step(sim, state, f == 0 ? InputBits.LightPunch : InputBits.None);

        for (int f = 2; f <= 21; f++)
            state = Step(sim, state, f == 20 ? InputBits.LightPunch : InputBits.None);

        Assert.Equal(980, state.Fighter(1).Health);
        Assert.Equal(1, state.Fighter(1).Combo.Count);
    }

    [Fact]
    public void BlockedHit_DoesNotCountAndBreaksCombo()
    {
        var sim = Sim();
        BattleState state = Duel();

        state = Step(sim, state, InputBits.LightPunch);
        BattleStepResult blocked = sim.Step(state, F(1, InputBits.None, InputBits.Right)); // P2 朝左，「后」= Right
        state = blocked.State;

        Assert.Contains(blocked.Events, e => e.Type == BattleEventType.Blocked);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.False(state.Fighter(1).Combo.IsActive);

        // 之后正常命中仍是 1 Hit（防御打断了连段）。
        for (int f = 2; f <= 13; f++)
            state = Step(sim, state, f == 12 ? InputBits.LightPunch : InputBits.None);

        Assert.Equal(990, state.Fighter(1).Health);
        Assert.Equal(1, state.Fighter(1).Combo.Count);
    }

    [Fact]
    public void MutualHits_TrackIndependentCombos()
    {
        var sim = Sim();
        BattleState state = Duel();

        state = Step(sim, state, InputBits.LightPunch, InputBits.LightPunch); // 双方同帧发动
        state = Step(sim, state, InputBits.None, InputBits.None);             // 同帧互中

        Assert.Equal(990, state.Fighter(0).Health);
        Assert.Equal(990, state.Fighter(1).Health);
        Assert.Equal((1, 1), (state.Fighter(0).Combo.Count, state.Fighter(1).Combo.Count));
        Assert.Equal(1, state.Fighter(0).Combo.AttackerId);  // P1 的连段来自 P2
        Assert.Equal(0, state.Fighter(1).Combo.AttackerId);
    }

    [Fact]
    public void ComboIsPartOfSnapshot_AndReplayIsIdentical()
    {
        static (BattleState state, List<BattleEvent> events) Run()
        {
            var sim = Sim();
            BattleState state = Duel();
            var events = new List<BattleEvent>();
            for (int f = 0; f <= 12; f++)
            {
                BattleStepResult r = sim.Step(state, F(f, f == 0 || f == 10 ? InputBits.LightPunch : InputBits.None));
                state = r.State;
                events.AddRange(r.Events);
            }
            return (state, events);
        }

        var a = Run();
        var b = Run();

        Assert.Equal(a.state.Fighter(0), b.state.Fighter(0));
        Assert.Equal(a.state.Fighter(1), b.state.Fighter(1));
        Assert.Equal(a.state.Fighter(1).Combo.Count, b.state.Fighter(1).Combo.Count);
        Assert.Equal(a.events, b.events);
        Assert.Equal(2, a.state.Fighter(1).Combo.Count);
    }

    // ─────────────────────────────────────────────────────────────
    // 取消（D91）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_OnHitConfirm_StartsTargetMove()
    {
        var sim = Sim();
        BattleState state = Duel();
        var events = new List<BattleEvent>();

        for (int f = 0; f <= 4; f++)
        {
            BattleStepResult r = sim.Step(state, F(f, CancelScript(f)));
            state = r.State;
            events.AddRange(r.Events);
        }

        // 帧 4：Jab 被取消，Special 作为新的独立实例发动。
        Assert.Equal(Special, state.Fighter(0).MoveId);
        Assert.Equal(FighterLogicState.Startup, state.Fighter(0).LogicState);
        Assert.Equal(4, state.Fighter(0).MoveStartedFrame);
        Assert.Contains(events, e => e.Type == BattleEventType.MoveStarted
            && e.MoveId == Special && e.Frame == 4);
        Assert.Equal(990, state.Fighter(1).Health); // 旧 Jab 的命中已结算
    }

    [Fact]
    public void Cancel_ProducesSecondHitAndTwoHitCombo()
    {
        var sim = Sim();
        BattleState state = Duel();

        for (int f = 0; f <= 5; f++)
            state = Step(sim, state, CancelScript(f));   // 帧 5：Special 进入 Active 并命中

        Assert.Equal(970, state.Fighter(1).Health);      // 10 + 20
        Assert.Equal(2, state.Fighter(1).Combo.Count);
        Assert.Equal(Special, state.Fighter(0).MoveId);
    }

    [Fact]
    public void Whiff_CannotCancel_WhenRuleRequiresHitConfirm()
    {
        var sim = Sim();
        BattleState state = Duel(p2X: 400);              // 目标太远：Jab 空挥

        for (int f = 0; f <= 4; f++)
            state = Step(sim, state, CancelScript(f));

        Assert.Equal(Jab, state.Fighter(0).MoveId);      // 没有取消
        Assert.NotEqual(4, state.Fighter(0).MoveStartedFrame);
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    [Fact]
    public void BlockConfirm_AllowsCancelConfiguredForBlock()
    {
        var sim = Sim(p1Cancel: new CancelDefinition(
            Special, 1, 6, MoveCancelPhases.Active | MoveCancelPhases.Recovery,
            requiresBlockConfirm: true));

        BattleState state = Duel();
        for (int f = 0; f <= 4; f++)
        {
            // P2 帧 0..3 按住「后」防御，帧 4 松开（不影响已发生的防御确认）。
            InputBits p2 = f <= 3 ? InputBits.Right : InputBits.None;
            state = Step(sim, state, CancelScript(f), p2);
        }

        Assert.Equal(Special, state.Fighter(0).MoveId);
        Assert.Equal(1000, state.Fighter(1).Health);     // 被防御，不掉血
        Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);
    }

    [Fact]
    public void CancelWindowTooEarly_IsRejected()
    {
        var sim = Sim(p1Cancel: HitCancel(first: 5, last: 6));
        BattleState state = Duel();

        for (int f = 0; f <= 4; f++)
            state = Step(sim, state, CancelScript(f));

        Assert.Equal(Jab, state.Fighter(0).MoveId);      // 帧 4 早于窗口 [5,6]
    }

    [Fact]
    public void CancelWindowTooLate_IsRejected()
    {
        var sim = Sim(p1Cancel: HitCancel(first: 1, last: 2));
        BattleState state = Duel();

        for (int f = 0; f <= 4; f++)
            state = Step(sim, state, CancelScript(f));

        Assert.Equal(Jab, state.Fighter(0).MoveId);      // 帧 4 晚于窗口 [1,2]
    }

    [Fact]
    public void CancelPhaseFilter_RejectsRecovery()
    {
        // 规则只允许 Active 阶段；把按键推迟到帧 5（帧开始状态是 Recovery）→ 不能取消。
        var sim = Sim(p1Cancel: HitCancel(phases: MoveCancelPhases.Active));
        BattleState state = Duel();

        for (int f = 0; f <= 5; f++)
            state = Step(sim, state, CancelScript(f, buttonFrame: 5));

        Assert.Equal(Jab, state.Fighter(0).MoveId);
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(0).LogicState);
    }

    [Fact]
    public void CancelWithoutMatchingRule_IsRejected()
    {
        // P2 没有取消规则：同样的输入脚本 + 同样的命中确认，P2 不得取消。
        var sim = Sim(p2CanCancel: false);
        BattleState state = Duel();

        for (int f = 0; f <= 4; f++)
            state = Step(sim, state, InputBits.None, CancelScript(f));

        Assert.Equal(Jab, state.Fighter(1).MoveId);
        Assert.Equal(990, state.Fighter(0).Health);      // P2 的 Jab 确实命中过（有确认）
    }

    [Fact]
    public void CancelledMove_DoesNotProduceSecondInstanceEvent()
    {
        // 取消只消费一次缓冲：Special 不会被同一帧/后续帧重复发动。
        var sim = Sim();
        BattleState state = Duel();
        var events = new List<BattleEvent>();

        for (int f = 0; f <= 12; f++)
        {
            BattleStepResult r = sim.Step(state, F(f, CancelScript(f)));
            state = r.State;
            events.AddRange(r.Events);
        }

        Assert.Equal(1, events.Count(e => e.Type == BattleEventType.MoveStarted && e.MoveId == Special));
        Assert.Equal(1, events.Count(e => e.Type == BattleEventType.MoveStarted && e.MoveId == Jab));
    }

    [Fact]
    public void CancelAfterRoundEnd_IsImpossible()
    {
        // 回合终局冻结：即使缓冲里还有可用指令，也不会再发动任何招式。
        var sim = Sim();
        BattleState state = Duel(p2Health: 10);

        BattleState ended = Step(sim, state, InputBits.LightPunch);   // 帧 0 发动
        BattleStepResult ko = sim.Step(ended, F(1, InputBits.None, InputBits.None));
        Assert.True(ko.State.Round.IsFinished);

        BattleState frozen = ko.State;
        for (int f = 2; f <= 6; f++)
            frozen = Step(sim, frozen, CancelScript(f));

        Assert.Equal(ko.State.Fighter(0), frozen.Fighter(0));
        Assert.Equal(ko.State.Fighter(1), frozen.Fighter(1));
    }

    [Fact]
    public void MidSnapshotReplay_CancelChainIsIdentical()
    {
        // 完整跑。
        var simFull = Sim();
        BattleState full = Duel();
        var fullEvents = new List<BattleEvent>();
        for (int f = 0; f <= 12; f++)
        {
            BattleStepResult r = simFull.Step(full, F(f, CancelScript(f)));
            full = r.State;
            fullEvents.AddRange(r.Events);
        }

        // 帧 2 的中间快照续跑（状态与事件都必须逐位一致）。
        var simHalf = Sim();
        BattleState half = Duel();
        var halfEvents = new List<BattleEvent>();
        for (int f = 0; f <= 2; f++)
        {
            BattleStepResult r = simHalf.Step(half, F(f, CancelScript(f)));
            half = r.State;
            halfEvents.AddRange(r.Events);
        }

        BattleState snapshot = half;                      // 中间快照（下一帧 = 3）
        for (int f = 3; f <= 12; f++)
        {
            BattleStepResult r = simHalf.Step(snapshot, F(f, CancelScript(f)));
            snapshot = r.State;
            halfEvents.AddRange(r.Events);
        }
        half = snapshot;

        Assert.Equal(fullEvents, halfEvents);
        Assert.Equal(full.Fighter(0), half.Fighter(0));
        Assert.Equal(full.Fighter(1), half.Fighter(1));
        Assert.Equal(Special, half.Fighter(0).MoveId);
    }

    // ─────────────────────────────────────────────────────────────
    // 取消数据合法性（构造期）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CancelDefinition_RejectsInvalidConfig()
    {
        Assert.Throws<ArgumentException>(() => new CancelDefinition("", 0, 1, allowWhiff: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CancelDefinition(Special, -1, 1, allowWhiff: true));
        Assert.Throws<ArgumentException>(() => new CancelDefinition(Special, 3, 1, allowWhiff: true));
        Assert.Throws<ArgumentException>(() =>
            new CancelDefinition(Special, 0, 1, MoveCancelPhases.None, allowWhiff: true));
        // 没有任何确认条件 → 永远无法触发，构造期拒绝。
        Assert.Throws<ArgumentException>(() => new CancelDefinition(Special, 0, 1));
    }

    [Fact]
    public void FighterDefinition_RejectsCancelToUnknownOrSelfMove()
    {
        CommandDefinition[] commands =
        {
            new("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5),
        };
        AttackDefinition attack = new(
            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
            FixedPoint.FromInt(20), FixedPoint.FromInt(20),
            0, 0, AttackHeight.Mid, 10, 20, 10);

        Assert.Throws<ArgumentException>(() => new FighterDefinition("X", commands, new[]
        {
            new MoveDefinition(Jab, "5A", 1, 3, 6, attacks: new[] { attack },
                cancels: new[] { new CancelDefinition("Missing", 1, 3, allowWhiff: true) }),
        }));

        Assert.Throws<ArgumentException>(() => new FighterDefinition("X", commands, new[]
        {
            new MoveDefinition(Jab, "5A", 1, 3, 6, attacks: new[] { attack },
                cancels: new[] { new CancelDefinition(Jab, 1, 3, allowWhiff: true) }),
        }));
    }

    [Theory]
    [InlineData(false, 46, 7, 0, false, "Near")]
    [InlineData(false, 47, 7, 1, false, "Far")]
    [InlineData(true, 46, 11, 2, false, "Near")]
    [InlineData(true, 47, 11, 2, false, "Far")]
    [InlineData(true, 46, 6, 0, false, null)]
    [InlineData(true, 46, 7, 3, false, null)]
    [InlineData(true, 46, 7, 0, true, null)]
    public void Reentry_UsesCurrentDistanceDirectionAndFreshInput(bool startedNear, int distance, int tick,
        int inputAge, bool down, string? expected)
    {
        var command = new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
            bufferLifetimeFrames: 4);
        CancelDefinition[] Rules(string source) => new[]
        {
            new CancelDefinition("Near", 7, 11, allowWhiff: true, allowSameMove: source == "Near",
                standingInputOnly: true, maximumInputAge: 2),
            new CancelDefinition("Far", 7, 11, allowWhiff: true, allowSameMove: source == "Far",
                standingInputOnly: true, maximumInputAge: 2),
        };
        var fighter = new FighterDefinition("Probe", new[] { command }, new[]
        {
            new MoveDefinition("Near", "5A", 3, 4, 5, cancels: Rules("Near"), maximumStartBodyDistance: 16),
            new MoveDefinition("Far", "5A", 3, 4, 5, cancels: Rules("Far")),
        }, groundBodyWidths: new GroundBodyWidths(15, 16));

        RecognitionState input = RecognitionState.Empty;
        for (int frame = 20 - inputAge; frame <= 20; frame++)
            input = input.Advance((frame == 20 - inputAge ? InputBits.LightPunch : InputBits.None) |
                (down ? InputBits.Down : InputBits.None), Facing.Right, frame, 0, fighter.Commands);
        FighterLogicState before = tick <= 7 ? FighterLogicState.Active : FighterLogicState.Recovery;
        int remaining = tick <= 7 ? 8 - tick : 13 - tick;

        FighterActionTick result = FighterStateMachine.Tick(before, startedNear ? "Near" : "Far", remaining,
            DirectionResolver.Resolve(down ? InputBits.Down : InputBits.None, Facing.Right), input, fighter, 20,
            moveStartedFrame: 20 - tick, moveElapsedFrames: tick, opponentDistance: distance,
            opponentBodyDistance: distance - 30);
        Assert.Equal(expected, result.ConsumedMatch is null ? null : result.MoveId);
        if (expected is not null)
        {
            Assert.Equal(20, result.MoveStartedFrame);
            Assert.Equal(0, result.MoveElapsedFrames);
            Assert.Contains(result.Recognition.Buffer, result => result.Consumed);
        }
    }
}
