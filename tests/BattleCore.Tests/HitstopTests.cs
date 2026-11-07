using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 08A（D96）：<b>命中停顿 Hitstop</b>。
///
/// <para><b>时间线</b>：命中/成功防御发生的当帧<b>照常完成</b>伤害与事件结算；
/// 停顿计数从<b>下一逻辑帧</b>开始消费——N 帧停顿 = 冻结后续 N 个逻辑帧，
/// 第 N+1 帧恢复。冻结期间动作阶段、硬直计时、击退衰减、空中积分、取消窗口全部停摆，
/// 只有输入识别继续推进（缓冲不丢失、不消费）。</para>
///
/// <para><b>动作有效时间</b>：<c>MoveElapsedFrames</c> 只在未冻结的帧 +1，
/// 取消窗口用它而不是 <c>Frame − MoveStartedFrame</c>，因此停顿不会让窗口提前过期。</para>
/// </summary>
public class HitstopTests
{
    private const string Jab = "Jab";
    private const string Heavy = "Heavy";
    private const string Launch = "Launch";

    /// <summary>双方同配置（可分别指定停顿帧数）。</summary>
    private static BattleSimulation Sim(int p1Hitstop, int p2Hitstop = -1, bool withCancel = false)
    {
        if (p2Hitstop < 0)
            p2Hitstop = p1Hitstop;

        MovementDefinition movement = MovementDefinition.CreateDefault();

        FighterDefinition Build(string id, int hitstop) => new(
            id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
                new CommandDefinition("5B", Array.Empty<CommandDirection>(), InputBits.HeavyPunch,
                    priority: 5, bufferLifetimeFrames: 24),
                new CommandDefinition("5C", Array.Empty<CommandDirection>(), InputBits.LightKick,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[]
            {
                new MoveDefinition(Jab, "5A", 1, 3, 4,
                    attacks: new[]
                    {
                        new AttackDefinition(
                            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                            FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                            0, 0, AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10,
                            groundKnockback: FixedPoint.FromInt(4),
                            groundKnockbackFriction: FixedPoint.FromInt(1),
                            hitstopFrames: hitstop),
                    },
                    cancels: withCancel
                        ? new[] { new CancelDefinition(Heavy, 2, 6, allowWhiff: true) }
                        : null),
                new MoveDefinition(Heavy, "5B", 1, 3, 4,
                    attacks: new[]
                    {
                        new AttackDefinition(
                            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                            FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                            0, 0, AttackHeight.Mid, damage: 20, hitstunFrames: 20, blockstunFrames: 10),
                    }),
                new MoveDefinition(Launch, "5C", 1, 3, 4,
                    attacks: new[]
                    {
                        new AttackDefinition(
                            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
                            FixedPoint.FromInt(30), FixedPoint.FromInt(30),
                            0, 0, AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10,
                            launchVelocityX: FixedPoint.FromInt(2),
                            launchVelocityY: FixedPoint.FromInt(6),
                            knockdownFrames: 8,
                            wakeupFrames: 5,
                            hitstopFrames: hitstop),
                    }),
            },
            movement);

        return new BattleSimulation(new FightRoster(Build("Kai", p1Hitstop), Build("Rei", p2Hitstop)));
    }

    private static BattleState Duel(int p2Health = 1000) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, -60, Facing.Right),
            FighterState.Create(1, 1000, 0, Facing.Left) with { Health = p2Health },
        },
        BattleState.CreateDefault().StateVersion);

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    /// <summary>帧 0 发动 5A、帧 1 命中，返回命中帧的结果。</summary>
    private static (BattleState state, BattleStepResult hit) Attack(
        BattleSimulation sim, BattleState state, InputBits button = InputBits.LightPunch)
    {
        state = sim.Step(state, F(state.Frame, button)).State;
        BattleStepResult hit = sim.Step(state, F(state.Frame, InputBits.None));
        return (hit.State, hit);
    }

    // ─────────────────────────────────────────────────────────────
    // 1. 精确冻结帧数
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ZeroHitstop_BehavesLikeNoFreeze()
    {
        var sim = Sim(p1Hitstop: 0);
        (BattleState state, BattleStepResult hit) = Attack(sim, Duel());

        Assert.Equal(0, hit.State.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(0, hit.State.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(20, hit.State.Fighter(1).StunRemainingFrames);

        // 没有停顿：硬直逐帧消耗、击退逐帧衰减。
        state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(19, state.Fighter(1).StunRemainingFrames);
        Assert.Equal(FixedPoint.FromInt(3), state.Fighter(1).VelocityX);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void Hitstop_FreezesExactlyNFrames(int n)
    {
        var sim = Sim(p1Hitstop: n);
        (BattleState state, BattleStepResult hit) = Attack(sim, Duel());

        Assert.Equal(n, hit.State.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(n, hit.State.Fighter(1).HitstopRemainingFrames);

        int frozenPhase = hit.State.Fighter(0).PhaseRemainingFrames; // 3（Active）
        int frozenX = hit.State.Fighter(1).PositionX.ToIntTruncated();

        // 后续 N 帧：全部冻结（动作阶段 / 硬直 / 位置 / 击退速度都不动）。
        for (int k = 1; k <= n; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

            Assert.Equal(n - k, state.Fighter(1).HitstopRemainingFrames);
            Assert.Equal(n - k, state.Fighter(0).HitstopRemainingFrames);
            Assert.Equal(20, state.Fighter(1).StunRemainingFrames);
            Assert.Equal(frozenPhase, state.Fighter(0).PhaseRemainingFrames);
            Assert.Equal(frozenX, state.Fighter(1).PositionX.ToIntTruncated());
            Assert.Equal(FixedPoint.FromInt(4), state.Fighter(1).VelocityX);
        }

        // 第 N+1 帧恢复正常推进。
        state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(0, state.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(19, state.Fighter(1).StunRemainingFrames);
        Assert.Equal(frozenPhase - 1, state.Fighter(0).PhaseRemainingFrames);
        Assert.Equal(FixedPoint.FromInt(3), state.Fighter(1).VelocityX); // 击退开始衰减
    }

    [Fact]
    public void Hitstop_DoesNotAdvanceDuringFreeze_EvenWithInput()
    {
        // 冻结期间持续按攻击键：不发动招式、不消费指令、不产生 MoveStarted。
        var sim = Sim(p1Hitstop: 5);
        (BattleState state, _) = Attack(sim, Duel());

        for (int k = 0; k < 5; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.LightPunch));
            state = r.State;

            Assert.Equal(FighterLogicState.Hitstun, state.Fighter(1).LogicState);
            Assert.Equal(20, state.Fighter(1).StunRemainingFrames);
            Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.MoveStarted);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 互中 / 不同停顿 / 防御
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MutualHit_UsesMaxHitstopForBoth()
    {
        var sim = Sim(p1Hitstop: 2, p2Hitstop: 6);
        BattleState state = Duel();
        state = sim.Step(state, F(0, InputBits.LightPunch, InputBits.LightPunch)).State;
        state = sim.Step(state, F(1, InputBits.None, InputBits.None)).State;

        // 双方同帧各自命中：两人都按最大值冻结（不存在先手）。
        Assert.Equal(6, state.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(6, state.Fighter(1).HitstopRemainingFrames);
        Assert.Equal(20, state.Fighter(0).StunRemainingFrames);
        Assert.Equal(20, state.Fighter(1).StunRemainingFrames);

        for (int k = 0; k < 6; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;
            Assert.Equal(20, state.Fighter(0).StunRemainingFrames);
            Assert.Equal(20, state.Fighter(1).StunRemainingFrames);
        }
        Assert.Equal(0, state.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(0, state.Fighter(1).HitstopRemainingFrames);

        state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;
        Assert.Equal(19, state.Fighter(0).StunRemainingFrames);
        Assert.Equal(19, state.Fighter(1).StunRemainingFrames);
    }

    [Fact]
    public void BlockedHit_AlsoFreezesBoth()
    {
        var sim = Sim(p1Hitstop: 4);
        BattleState state = Duel();
        state = sim.Step(state, F(0, InputBits.LightPunch)).State;
        state = sim.Step(state, F(1, InputBits.None, InputBits.Right)).State; // P2 朝左，「后」= Right

        Assert.Equal(FighterLogicState.Blockstun, state.Fighter(1).LogicState);
        Assert.Equal(4, state.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(4, state.Fighter(1).HitstopRemainingFrames);

        int blockstun = state.Fighter(1).StunRemainingFrames;
        for (int k = 0; k < 4; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.Right)).State;
            Assert.Equal(blockstun, state.Fighter(1).StunRemainingFrames);
        }
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 击退 / 击飞 / KO
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AirHitstun_DoesNotFallDuringFreeze()
    {
        var sim = Sim(p1Hitstop: 4);
        (BattleState state, _) = Attack(sim, Duel(), InputBits.LightKick);

        Assert.Equal(FighterLogicState.AirHitstun, state.Fighter(1).LogicState);
        Assert.Equal(FixedPoint.FromInt(6), state.Fighter(1).VelocityY);
        Assert.Equal(FixedPoint.Zero, state.Fighter(1).PositionY);

        for (int k = 0; k < 4; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            Assert.Equal(FixedPoint.Zero, state.Fighter(1).PositionY);          // 重力不积分
            Assert.Equal(FixedPoint.FromInt(6), state.Fighter(1).VelocityY);
        }

        state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        Assert.Equal(FixedPoint.FromFraction(11, 2), state.Fighter(1).PositionY); // 5.5
        Assert.Equal(FixedPoint.FromFraction(11, 2), state.Fighter(1).VelocityY); // 5.5
    }

    [Fact]
    public void Hitstop_DoesNotApplyDamageTwice()
    {
        var sim = Sim(p1Hitstop: 6);
        BattleState state = Duel();
        (state, _) = Attack(sim, state);
        Assert.Equal(990, state.Fighter(1).Health);

        for (int k = 0; k < 10; k++)
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;

        Assert.Equal(990, state.Fighter(1).Health);
    }

    [Fact]
    public void LethalHit_ResolvesKoOnTheHitFrame()
    {
        var sim = Sim(p1Hitstop: 6);
        (BattleState state, BattleStepResult hit) = Attack(sim, Duel(p2Health: 5));

        Assert.Equal(0, state.Fighter(1).Health);
        Assert.True(state.Round.IsFinished);
        Assert.Equal(RoundOutcome.P1Win, state.Round.Outcome);
        Assert.Contains(hit.Events, e => e.Type == BattleEventType.Knockout);

        // 终局冻结（D89）优先于停顿：下一帧角色逐位不变，不再扣血/发事件。
        BattleStepResult frozen = sim.Step(state, F(state.Frame, InputBits.None));
        Assert.Equal(state.Fighter(1), frozen.State.Fighter(1));
        Assert.Empty(frozen.Events);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 取消窗口与动作有效时间
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CancelWindow_SurvivesHitstop()
    {
        // 5A：1/3/4，命中停顿 8；取消窗口 = 动作经过帧 [2,6]，允许空挥。
        // 命中帧 1 → 冻结 2..9 → 第 10 帧恢复（动作经过帧 = 2，窗口仍开）。
        // 若窗口按全局帧差计算，第 10 帧已经是 「10 > 6」——窗口会提前过期。
        var sim = Sim(p1Hitstop: 8, withCancel: true);
        BattleState state = Duel();
        state = sim.Step(state, F(0, InputBits.LightPunch)).State;
        state = sim.Step(state, F(1, InputBits.None)).State;     // 命中 + 停顿 8

        // 冻结期间按 5B：只进缓冲，不消费（8 帧停顿 = 冻结帧 2..9）。
        for (int k = 0; k < 8; k++)
            state = sim.Step(state, F(state.Frame, InputBits.HeavyPunch)).State;

        Assert.Equal(Jab, state.Fighter(0).MoveId);
        Assert.Equal(1, state.Fighter(0).MoveElapsedFrames); // 冻结期间动作时间不前进

        // 恢复帧：动作经过帧 2 落在窗口内 → 取消成立（新实例）。
        BattleStepResult resume = sim.Step(state, F(state.Frame, InputBits.HeavyPunch));
        state = resume.State;

        Assert.Equal(FighterLogicState.Startup, state.Fighter(0).LogicState);
        Assert.Equal(Heavy, state.Fighter(0).MoveId);
        Assert.Equal(0, state.Fighter(0).MoveElapsedFrames);
        Assert.Contains(resume.Events, e => e.Type == BattleEventType.MoveStarted && e.MoveId == Heavy);

        // 缓冲结果只被消费一次：之后不重复发动。
        BattleStepResult after = sim.Step(state, F(state.Frame, InputBits.None));
        Assert.DoesNotContain(after.Events, e => e.Type == BattleEventType.MoveStarted);
        Assert.Equal(Heavy, after.State.Fighter(0).MoveId);
    }

    [Fact]
    public void MoveElapsedFrames_TracksActionTimeWithoutHitstop()
    {
        // 无停顿：动作经过帧逐帧 +1，与 frame − MoveStartedFrame 完全一致（旧契约不变）。
        var sim = Sim(p1Hitstop: 0);
        BattleState state = Duel();
        state = sim.Step(state, F(0, InputBits.LightPunch)).State;

        Assert.Equal(Jab, state.Fighter(0).MoveId);
        Assert.Equal(0, state.Fighter(0).MoveElapsedFrames);

        // 状态携带的是「处理完帧 F 之后」的结果，此时 state.Frame 已经是 F+1。
        for (int k = 0; k < 6; k++)
        {
            Assert.Equal(state.Frame - 1 - state.Fighter(0).MoveStartedFrame,
                state.Fighter(0).MoveElapsedFrames);
            state = sim.Step(state, F(state.Frame, InputBits.None)).State;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 事件、快照、确定性
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FrozenFrames_DoNotRepeatAttackActive()
    {
        var sim = Sim(p1Hitstop: 5);
        (BattleState state, BattleStepResult hit) = Attack(sim, Duel());

        int activeCount = hit.Events.Count(e => e.Type == BattleEventType.AttackActive);
        Assert.Equal(1, activeCount);

        for (int k = 0; k < 8; k++)
        {
            BattleStepResult r = sim.Step(state, F(state.Frame, InputBits.None));
            state = r.State;
            activeCount += r.Events.Count(e => e.Type == BattleEventType.AttackActive);
        }
        Assert.Equal(1, activeCount);
    }

    [Fact]
    public void MidSnapshotReplay_IsIdentical()
    {
        static BattleState Advance(BattleSimulation sim, BattleState state, int toFrame)
        {
            while (state.Frame < toFrame)
                state = sim.Step(state, F(state.Frame, InputBits.None)).State;
            return state;
        }

        var simA = Sim(p1Hitstop: 5);
        BattleState full = Duel();
        full = simA.Step(full, F(0, InputBits.LightPunch)).State;
        full = Advance(simA, full, 24);

        // 跑到帧 5（冻结期内）取中间快照，再用<b>新的</b>模拟实例续跑到同一帧。
        var simB = Sim(p1Hitstop: 5);
        BattleState head = Duel();
        head = simB.Step(head, F(0, InputBits.LightPunch)).State;
        head = Advance(simB, head, 5);
        BattleState resumed = Advance(simB, head, 24);

        Assert.Equal(full.Fighter(0), resumed.Fighter(0));
        Assert.Equal(full.Fighter(1), resumed.Fighter(1));
        Assert.Equal(full.Round, resumed.Round);
    }

    [Fact]
    public void StepExactAndAdvance_AgreeUnderHitstop()
    {
        FrameInput Provider(int frame) => new(frame, InputBits.None, InputBits.None);

        var exact = new BattleSession(
            initialState: Duel(),
            simulation: Sim(p1Hitstop: 5),
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 8));

        var scheduled = new BattleSession(
            initialState: Duel(),
            simulation: Sim(p1Hitstop: 5),
            scheduler: new FixedStepScheduler(maxStepsPerAdvance: 8));

        // 帧 0 发动，其余空输入（两条路径同脚本）。
        exact.StepExact(1, inputProvider: f => new FrameInput(f, InputBits.LightPunch, InputBits.None));
        scheduled.Advance(FixedStepScheduler.FixedStepSeconds,
            inputProvider: f => new FrameInput(f, InputBits.LightPunch, InputBits.None));

        exact.StepExact(20, inputProvider: Provider);
        for (int i = 0; i < 20; i++)
            scheduled.Advance(FixedStepScheduler.FixedStepSeconds, inputProvider: Provider);

        Assert.Equal(exact.CurrentState.Frame, scheduled.CurrentState.Frame);
        Assert.Equal(exact.CurrentState.Fighter(0), scheduled.CurrentState.Fighter(0));
        Assert.Equal(exact.CurrentState.Fighter(1), scheduled.CurrentState.Fighter(1));
    }

    // ─────────────────────────────────────────────────────────────
    // 6. 构造期校验
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AttackDefinition_RejectsIllegalHitstop()
    {
        static AttackDefinition Make(int hitstop) => new(
            FixedPoint.FromInt(45), FixedPoint.FromInt(60),
            FixedPoint.FromInt(30), FixedPoint.FromInt(30),
            hitstopFrames: hitstop);

        Assert.Throws<ArgumentOutOfRangeException>(() => Make(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(AttackDefinition.MaxHitstopFrames + 1));
        Assert.Equal(AttackDefinition.MaxHitstopFrames, Make(AttackDefinition.MaxHitstopFrames).HitstopFrames);
    }

    [Fact]
    public void FighterState_RejectsIllegalTimeFields()
    {
        // 非动作状态携带动作经过帧数 → 非法。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 0,
            Facing: Facing.Right, LogicState: FighterLogicState.Standing,
            MoveElapsedFrames: 3).Validate());

        // 停顿剩余帧数越界 → 非法。
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 0,
            Facing: Facing.Right, HitstopRemainingFrames: -1).Validate());
        Assert.Throws<InvalidOperationException>(() => new FighterState(
            Id: 0, MaxHealth: 1000, Health: 1000, PositionX: 0, PositionY: 0,
            Facing: Facing.Right, HitstopRemainingFrames: AttackDefinition.MaxHitstopFrames + 1).Validate());
    }
}
