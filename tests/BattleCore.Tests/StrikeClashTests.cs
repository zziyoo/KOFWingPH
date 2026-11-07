using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 08C（D98）：<b>攻击相杀</b>（Hitbox × Hitbox）。
///
/// <para><b>最小规则</b>：两个攻击实例在同一帧都有生效攻击框、<b>正面积重叠</b>
/// （仅边缘接触不算），<b>且双方都没有打中对方受击框</b>（攻击在空中对撞）→ 相杀：
/// 双方攻击中断进入各自招式的 Recovery、无伤害、发一条 <c>StrikeClash</c>。
/// 只要有任意一方真正打中受击框，就按 D80 正常结算（互中或单方命中），不判相杀。</para>
///
/// <para><b>几何</b>（本文件固定配置）：攻击框 = 前向偏移 45 ± 半宽 20 → 距中心 25..65；
/// 受击框半宽 30。因此：d &lt; 95 双方都能打中身体（互中）；95 ≤ d &lt; 130 攻击框重叠
/// 但都打不到身体（<b>相杀区</b>）；d ≥ 130 完全不相交。</para>
/// </summary>
public class StrikeClashTests
{
    private const int ReachMin = 25;
    private const int ReachMax = 65;

    private static BattleSimulation Sim(int hitstop = 0, bool p1DoubleBox = false)
    {
        MovementDefinition movement = MovementDefinition.CreateDefault();

        AttackDefinition Box(int offsetForward, int halfWidth, int hitstopFrames) => new(
            FixedPoint.FromInt(offsetForward), FixedPoint.FromInt(60),
            FixedPoint.FromInt(halfWidth), FixedPoint.FromInt(20),
            0, 0, AttackHeight.Mid, damage: 10, hitstunFrames: 20, blockstunFrames: 10,
            hitstopFrames: hitstopFrames);

        FighterDefinition Build(string id, bool doubleBox) => new(
            id,
            new[]
            {
                new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch,
                    priority: 5, bufferLifetimeFrames: 24),
            },
            new[]
            {
                new MoveDefinition("Jab", "5A", 1, 3, 4,
                    attacks: doubleBox
                        ? new[] { Box(45, 10, hitstop), Box(110, 10, hitstop) }
                        : new[] { Box(45, 20, hitstop) }),
            },
            movement);

        return new BattleSimulation(new FightRoster(Build("Kai", p1DoubleBox), Build("Rei", false)));
    }

    private static BattleState Duel(int p1X, int p2X) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, Facing.Right),
            FighterState.Create(1, 1000, p2X, Facing.Left),
        },
        BattleState.CreateDefault().StateVersion);

    private static FrameInput F(int frame, InputBits p1, InputBits p2 = InputBits.None) => new(frame, p1, p2);

    /// <summary>双方同帧发动 5A（帧 0 按键、帧 1 生效），返回生效帧结果。</summary>
    private static (BattleState state, BattleStepResult result) BothJab(BattleSimulation sim, BattleState state)
    {
        state = sim.Step(state, F(state.Frame, InputBits.LightPunch, InputBits.LightPunch)).State;
        BattleStepResult active = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));
        return (active.State, active);
    }

    // ─────────────────────────────────────────────────────────────
    // 1. 相杀 vs 互中 vs 不相交
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MidRange_Clashes_NoDamage_BothRecovery()
    {
        // d = 120：攻击框重叠（跨 -5..5），但都够不到对方身体。
        var sim = Sim();
        (BattleState state, BattleStepResult result) = BothJab(sim, Duel(-60, 60));

        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(1).LogicState);
        Assert.Equal(4, state.Fighter(0).PhaseRemainingFrames); // 本招式的 RecoveryFrames
        Assert.Equal(4, state.Fighter(1).PhaseRemainingFrames);

        Assert.Single(result.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.Hit);
    }

    [Fact]
    public void CloseRange_BothHit_IsTradeNotClash()
    {
        // d = 60：双方都真正打中身体 → D80 互中，绝不判相杀。
        var sim = Sim();
        (BattleState state, BattleStepResult result) = BothJab(sim, Duel(-30, 30));

        Assert.Equal(990, state.Fighter(0).Health);
        Assert.Equal(990, state.Fighter(1).Health);
        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(1).LogicState);
        Assert.Equal(2, result.Events.Count(e => e.Type == BattleEventType.Hit));
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.StrikeClash);
    }

    [Fact]
    public void EdgeContactOnly_DoesNotClash()
    {
        // d = 130：两个攻击框只在一个点上相接 → 正面积规则（D76）不算相杀。
        var sim = Sim();
        (BattleState state, BattleStepResult result) = BothJab(sim, Duel(-65, 65));

        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(FighterLogicState.Active, state.Fighter(0).LogicState); // 招式照常继续
    }

    [Fact]
    public void OneUnitInsideOverlap_Clashes()
    {
        // d = 129：重叠 1 世界单位（正面积）→ 相杀。
        var sim = Sim();
        (BattleState state, BattleStepResult result) = BothJab(sim, Duel(-64, 65));

        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Contains(result.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(0).LogicState);
    }

    [Fact]
    public void OneSidedAttack_NoClash()
    {
        // 只有 P1 出招：没有第二个攻击框，正常命中（d=60 够得到）。
        var sim = Sim();
        BattleState state = Duel(-30, 30);
        state = sim.Step(state, F(0, InputBits.LightPunch)).State;
        BattleStepResult hit = sim.Step(state, F(1, InputBits.None));
        state = hit.State;

        Assert.Equal(990, state.Fighter(1).Health);
        Assert.Contains(hit.Events, e => e.Type == BattleEventType.Hit);
        Assert.DoesNotContain(hit.Events, e => e.Type == BattleEventType.StrikeClash);
    }

    [Fact]
    public void DifferentActiveFrames_NoClash()
    {
        // P1 的 Active（帧 1–3）早已结束，P2 到帧 4 才生效 → 不同帧，无相杀。
        var sim = Sim();
        BattleState state = Duel(-60, 60);
        int clashes = 0;

        for (int f = 0; f < 12; f++)
        {
            InputBits p1 = f == 0 ? InputBits.LightPunch : InputBits.None;
            InputBits p2 = f == 3 ? InputBits.LightPunch : InputBits.None;
            BattleStepResult r = sim.Step(state, F(f, p1, p2));
            state = r.State;
            clashes += r.Events.Count(e => e.Type == BattleEventType.StrikeClash);
        }

        Assert.Equal(0, clashes);
        Assert.Equal(1000, state.Fighter(1).Health); // 双方都挥空（d=120 够不到）
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 相杀后状态
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AfterClash_SameInstanceDoesNotClashAgain()
    {
        var sim = Sim();
        (BattleState state, _) = BothJab(sim, Duel(-60, 60));

        int clashes = 0;
        for (int f = 2; f < 10; f++)
        {
            BattleStepResult r = sim.Step(state, F(f, InputBits.None, InputBits.None));
            state = r.State;
            clashes += r.Events.Count(e => e.Type == BattleEventType.StrikeClash);
        }

        Assert.Equal(0, clashes);
        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    [Fact]
    public void AfterClash_BothCanActAgain()
    {
        var sim = Sim();
        (BattleState state, _) = BothJab(sim, Duel(-60, 60));

        // Recovery(4) 走完回待机。
        while (state.Fighter(0).LogicState != FighterLogicState.Standing)
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;

        BattleStepResult again = sim.Step(state, F(state.Frame, InputBits.LightPunch, InputBits.LightPunch));
        state = again.State;

        Assert.Equal(FighterLogicState.Startup, state.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Startup, state.Fighter(1).LogicState);
        Assert.Equal(2, again.Events.Count(e => e.Type == BattleEventType.MoveStarted));
    }

    [Fact]
    public void NewInstances_CanClashAgain()
    {
        var sim = Sim();
        (BattleState state, _) = BothJab(sim, Duel(-60, 60));

        while (state.Fighter(0).LogicState != FighterLogicState.Standing)
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;

        BattleStepResult second = sim.Step(state, F(state.Frame, InputBits.LightPunch, InputBits.LightPunch));
        state = second.State;
        BattleStepResult clash = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None));

        Assert.Contains(clash.Events, e => e.Type == BattleEventType.StrikeClash);
    }

    [Fact]
    public void ClashTriggersConfiguredHitstop()
    {
        var sim = Sim(hitstop: 5);
        (BattleState state, BattleStepResult result) = BothJab(sim, Duel(-60, 60));

        Assert.Contains(result.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(5, state.Fighter(0).HitstopRemainingFrames);
        Assert.Equal(5, state.Fighter(1).HitstopRemainingFrames);

        int phase = state.Fighter(0).PhaseRemainingFrames;
        for (int k = 0; k < 5; k++)
        {
            state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;
            Assert.Equal(phase, state.Fighter(0).PhaseRemainingFrames); // 冻结期间不推进
        }
    }

    [Fact]
    public void Clash_DoesNotTriggerKo()
    {
        // 目标只剩 5 血也打不死——相杀不产生伤害。
        var sim = Sim();
        BattleState state = Duel(-60, 60);
        state = new BattleState(0, new[]
        {
            state.Fighter(0),
            state.Fighter(1) with { Health = 5 },
        }, state.StateVersion, state.Round);

        (state, BattleStepResult result) = BothJab(sim, state);

        Assert.Equal(5, state.Fighter(1).Health);
        Assert.False(state.Round.IsFinished);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.Knockout);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 多攻击框（实例级裁决）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MultiHitbox_OneBoxClashesButAnotherHits_IsHitNotClash()
    {
        // P1 有两个框：A（35..55）只与对手攻击框重叠、B（100..120）打中对手身体。
        // 实例级裁决：只要有任意一框真正命中，就按命中结算，不判相杀。
        var sim = Sim(p1DoubleBox: true);
        BattleState state = Duel(-110, 0);
        state = sim.Step(state, F(0, InputBits.LightPunch, InputBits.LightPunch)).State;
        BattleStepResult r = sim.Step(state, F(1, InputBits.None, InputBits.None));
        state = r.State;

        Assert.Equal(990, state.Fighter(1).Health);              // B 框命中
        Assert.Equal(FighterLogicState.Hitstun, state.Fighter(1).LogicState);
        Assert.DoesNotContain(r.Events, e => e.Type == BattleEventType.StrikeClash);
    }

    [Fact]
    public void MultiHitbox_NeitherReaches_Clashes()
    {
        // 同一套双框招式，把距离拉到 160：A、B 都够不到身体，但 A 与对手攻击框重叠。
        var sim = Sim(p1DoubleBox: true);
        BattleState state = Duel(-100, 60);
        state = sim.Step(state, F(0, InputBits.LightPunch, InputBits.LightPunch)).State;
        BattleStepResult r = sim.Step(state, F(1, InputBits.None, InputBits.None));
        state = r.State;

        Assert.Equal(1000, state.Fighter(1).Health);
        Assert.Contains(r.Events, e => e.Type == BattleEventType.StrikeClash);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 对称性与确定性
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MirrorScenario_IsIdentical()
    {
        // 身份镜像：把两人位置与朝向对调，相杀结论一致、双方状态对称。
        var sim = Sim();
        BattleState state = Duel(60, -60);
        state = new BattleState(0, new[]
        {
            state.Fighter(0) with { Facing = Facing.Left },
            state.Fighter(1) with { Facing = Facing.Right },
        }, state.StateVersion, state.Round);

        (state, BattleStepResult result) = BothJab(sim, state);

        Assert.Contains(result.Events, e => e.Type == BattleEventType.StrikeClash);
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Recovery, state.Fighter(1).LogicState);
        Assert.Equal(1000, state.Fighter(0).Health);
        Assert.Equal(1000, state.Fighter(1).Health);
    }

    [Fact]
    public void MidSnapshotReplay_IsIdentical()
    {
        static BattleState Advance(BattleSimulation sim, BattleState state, int toFrame)
        {
            while (state.Frame < toFrame)
                state = sim.Step(state, F(state.Frame, InputBits.None, InputBits.None)).State;
            return state;
        }

        var simA = Sim();
        BattleState full = Duel(-60, 60);
        full = simA.Step(full, F(0, InputBits.LightPunch, InputBits.LightPunch)).State;
        full = Advance(simA, full, 20);

        var simB = Sim();
        BattleState head = Duel(-60, 60);
        head = simB.Step(head, F(0, InputBits.LightPunch, InputBits.LightPunch)).State;
        head = Advance(simB, head, 3);
        BattleState resumed = Advance(simB, head, 20);

        Assert.Equal(full.Fighter(0), resumed.Fighter(0));
        Assert.Equal(full.Fighter(1), resumed.Fighter(1));
        Assert.Equal(full.Round, resumed.Round);
    }
}
