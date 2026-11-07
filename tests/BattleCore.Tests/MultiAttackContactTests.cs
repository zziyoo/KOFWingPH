using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 06 专项补修 P1：<b>多攻击定义 → Contact.AttackIndex 映射</b>。
///
/// <para><b>缺陷</b>：<c>FighterBoxes.GetActiveHitboxes</c> 先按生效帧过滤攻击定义，
/// 只返回 Rect 列表；<c>HitDetection.Collect</c> 却用这个<b>过滤后列表的下标</b>作为
/// <see cref="AttackContact.AttackIndex"/>。一旦招式有多个攻击定义且生效窗口不同
/// （例如 Attack[0] 只在第 2 个 Active 帧生效、Attack[1] 只在第 1 个），
/// 第 1 帧的接触会被记成 AttackIndex 0，结算时取到 Attack[0] 的伤害/高度/硬直。</para>
///
/// <para><b>测试招式</b>（Startup 1 / Active 3 / Recovery 4，两个攻击定义几何相同，
/// 均覆盖目标，因此只靠时序区分）：</para>
/// <list type="bullet">
/// <item>Attack[0]：Active 第 2 帧生效（elapsed 1），High / 伤害 10 / 命中硬直 10 / 防御硬直 4。</item>
/// <item>Attack[1]：Active 第 1 帧生效（elapsed 0），Low / 伤害 30 / 命中硬直 14 / 防御硬直 6。</item>
/// </list>
/// </summary>
public class MultiAttackContactTests
{
    private const string Dual = "Dual";
    private const string Twin = "Twin";

    private const int ActiveFrames = 3;

    /// <summary>统一的攻击几何：前向 45、上 60、半宽/半高 30——足够覆盖间距 60 的目标。</summary>
    private static AttackDefinition Attack(
        int first, int last, AttackHeight height, int damage, int hitstun, int blockstun) =>
        new(FixedPoint.FromInt(45), FixedPoint.FromInt(60),
            FixedPoint.FromInt(30), FixedPoint.FromInt(30),
            first, last, height, damage, hitstun, blockstun);

    private static FightRoster CreateRoster()
    {
        CommandDefinition[] commands =
        {
            new("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5),
            new("5B", Array.Empty<CommandDirection>(), InputBits.HeavyPunch, priority: 5),
        };

        MoveDefinition[] moves =
        {
            // 不同生效窗口：只有靠原始序号才能区分这两个攻击定义。
            new(Dual, "5A", startupFrames: 1, activeFrames: ActiveFrames, recoveryFrames: 4,
                attacks: new[]
                {
                    Attack(first: 1, last: 1, AttackHeight.High, damage: 10, hitstun: 10, blockstun: 4),
                    Attack(first: 0, last: 0, AttackHeight.Low, damage: 30, hitstun: 14, blockstun: 6),
                }),

            // 同一帧两个攻击框都生效：用于固定「同帧多框」的选择优先级。
            new(Twin, "5B", startupFrames: 1, activeFrames: ActiveFrames, recoveryFrames: 4,
                attacks: new[]
                {
                    Attack(first: 0, last: 0, AttackHeight.High, damage: 10, hitstun: 10, blockstun: 4),
                    Attack(first: 0, last: 0, AttackHeight.Low, damage: 30, hitstun: 14, blockstun: 6),
                }),
        };

        var fighter = new FighterDefinition("Kai", commands, moves);
        var mirrored = new FighterDefinition("Rei", commands, moves);
        return new FightRoster(fighter, mirrored);
    }

    private static BattleSimulation NewSim() => new(CreateRoster());

    /// <summary>
    /// 构造「P1 已在 Active 的指定 elapsed 帧」的权威状态。
    /// Step 内先推进状态机，故 elapsed 0 需构造 remaining = ActiveFrames + 1。
    /// </summary>
    private static BattleState ActiveAt(int elapsed, string moveId = Dual, int p2Health = 1000,
        int p1X = -60, Facing p1Facing = Facing.Right, int p2X = 0)
    {
        var attacker = FighterState.Create(0, 1000, p1X, p1Facing) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = moveId,
            PhaseRemainingFrames = ActiveFrames - elapsed + 1,
            MoveStartedFrame = 0,
        };

        FighterState defender = FighterState.Create(1, 1000, p2X, Facing.Left);
        if (p2Health < 1000)
            defender = defender with { Health = p2Health };

        return new BattleState(0, new[] { attacker, defender }, BattleState.CreateDefault().StateVersion);
    }

    private static FighterState P2(BattleState s) => s.Fighter(1);

    // ─────────────────────────────────────────────────────────────
    // 原始攻击序号（几何层）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Contact_OnFirstActiveFrame_ReportsOriginalAttackIndex()
    {
        // 第 1 个 Active 帧只有 Attack[1] 生效：过滤后列表长度 1，但序号必须是 1。
        BattleStepResult result = NewSim().Step(ActiveAt(elapsed: 0), FrameInput.Empty(0));

        AttackContact contact = Assert.Single(result.Contacts);
        Assert.Equal((0, 1, Dual), (contact.AttackerId, contact.TargetId, contact.MoveId));
        Assert.Equal(1, contact.AttackIndex);
    }

    [Fact]
    public void Contact_OnSecondActiveFrame_ReportsOriginalAttackIndex()
    {
        // 第 2 个 Active 帧只有 Attack[0] 生效：序号 0。
        BattleStepResult result = NewSim().Step(ActiveAt(elapsed: 1), FrameInput.Empty(0));

        AttackContact contact = Assert.Single(result.Contacts);
        Assert.Equal(0, contact.AttackIndex);
    }

    [Fact]
    public void Contact_KeepsOriginalIndex_WhenFacingMirrored()
    {
        // 镜像同样按原始序号映射（朝向只影响几何，不影响序号）。
        BattleState state = ActiveAt(elapsed: 0, p1X: 60, p1Facing: Facing.Left, p2X: 0);
        BattleStepResult result = NewSim().Step(state, FrameInput.Empty(0));

        AttackContact contact = Assert.Single(result.Contacts);
        Assert.Equal(1, contact.AttackIndex);
    }

    // ─────────────────────────────────────────────────────────────
    // 结算必须来自同一份攻击定义（伤害 / 高度 / 硬直）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Settlement_OnFirstActiveFrame_UsesSecondAttackDefinition()
    {
        // Attack[1]：伤害 30、命中硬直 14。缺陷代码会取 Attack[0]（10 / 硬直 10）。
        BattleStepResult result = NewSim().Step(ActiveAt(elapsed: 0), FrameInput.Empty(0));
        BattleState after = result.State;

        Assert.Equal(970, P2(after).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(after).LogicState);
        Assert.Equal(14, P2(after).StunRemainingFrames);

        BattleEvent hit = Assert.Single(result.Events, e => e.Type == BattleEventType.Hit);
        Assert.Equal(30, hit.Amount);

        BattleEvent hitstun = Assert.Single(result.Events, e => e.Type == BattleEventType.HitstunStarted);
        Assert.Equal(14, hitstun.Amount);
    }

    [Fact]
    public void Settlement_OnSecondActiveFrame_UsesFirstAttackDefinition()
    {
        BattleStepResult result = NewSim().Step(ActiveAt(elapsed: 1), FrameInput.Empty(0));
        BattleState after = result.State;

        Assert.Equal(990, P2(after).Health);
        Assert.Equal(FighterLogicState.Hitstun, P2(after).LogicState);
        Assert.Equal(10, P2(after).StunRemainingFrames);

        BattleEvent hit = Assert.Single(result.Events, e => e.Type == BattleEventType.Hit);
        Assert.Equal(10, hit.Amount);
    }

    [Fact]
    public void FirstActiveFrame_HeightIsLow_SoCrouchGuardBlocks()
    {
        // Attack[1] 是 Low：蹲防（后 + 下）成功 → 不掉血，Blockstun 6。
        // 缺陷代码会取到 Attack[0]（High）→ 蹲防失败 → 掉 10 血进 Hitstun。
        BattleState state = ActiveAt(elapsed: 0);
        BattleStepResult result = NewSim().Step(state,
            new FrameInput(0, InputBits.None, InputBits.Right | InputBits.Down)); // P2 朝左，「后」= Right
        BattleState after = result.State;

        Assert.Equal(1000, P2(after).Health);
        Assert.Equal(FighterLogicState.Blockstun, P2(after).LogicState);
        Assert.Equal(6, P2(after).StunRemainingFrames);
        Assert.DoesNotContain(result.Events, e => e.Type == BattleEventType.Hit);
        Assert.Equal(6, Assert.Single(result.Events, e => e.Type == BattleEventType.BlockstunStarted).Amount);
    }

    // ─────────────────────────────────────────────────────────────
    // 同帧多攻击框：优先级必须明确并被测试固定
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SameFrame_TwoActiveHitboxes_BothReported_AndLowestIndexSettles()
    {
        // Twin：两个攻击定义都在 elapsed 0 生效。
        // 规则：两个接触都产生（序号 0、1 升序），结算取该被击者的第一条接触
        // → 必定是最小原始序号（Attack[0]：10 伤害 / High / 硬直 10）。
        BattleStepResult result = NewSim().Step(ActiveAt(elapsed: 0, moveId: Twin), FrameInput.Empty(0));

        Assert.Equal(new[] { 0, 1 }, result.Contacts.Select(c => c.AttackIndex).ToArray());

        BattleState after = result.State;
        Assert.Equal(990, P2(after).Health);
        Assert.Equal(10, P2(after).StunRemainingFrames);
        Assert.Equal(10, Assert.Single(result.Events, e => e.Type == BattleEventType.Hit).Amount);
    }

    [Fact]
    public void StaggeredWindows_EmitAttackActiveOncePerInstance()
    {
        // 目标拉远，避免命中打断，只看事件：Dual 的两个攻击窗口错开（Attack[0] 在 elapsed 1、
        // Attack[1] 在 elapsed 0）。AttackActive = 每次发动的首个生效帧 → 只在 elapsed 0 发一次；
        // elapsed 1 虽然又有攻击框进入生效窗口，但同一实例已发过。
        BattleStepResult first = NewSim().Step(ActiveAt(elapsed: 0, p2X: 400), FrameInput.Empty(0));
        Assert.Empty(first.Contacts);
        BattleEvent active = Assert.Single(first.Events, e => e.Type == BattleEventType.AttackActive);
        Assert.Equal(Dual, active.MoveId); // 结算前快照的招式身份（D87）

        BattleStepResult second = NewSim().Step(ActiveAt(elapsed: 1, p2X: 400), FrameInput.Empty(0));
        Assert.Empty(second.Contacts);
        Assert.DoesNotContain(second.Events, e => e.Type == BattleEventType.AttackActive);
    }
}
