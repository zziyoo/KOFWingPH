using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 04 最终补修 P2：非法 <see cref="FighterLogicState"/> 不得静默通过。
///
/// <para>
/// 缺陷（修复前实测可复现）：<see cref="FighterState.Validate"/> 的待机分支把一切
/// 非动作状态当作待机——<c>(FighterLogicState)999</c> 携带空 MoveId 与 0 帧即可通过
/// 构造；<see cref="FighterStateMachine.Tick"/> 的 <c>IsInMove(999)</c> 为 false，
/// 未知状态被静默「修正」成 Standing/Crouching，违背「非法状态立即失败」契约。</para>
///
/// <para>
/// 修复（D62）：两处入口都用 <see cref="Enum.IsDefined{TEnum}(TEnum)"/> 集中校验
/// （唯一事实来源 = 枚举本身，不会出现两份不一致的状态清单）——
/// Validate 无论其他字段是什么一律拒绝未定义状态；Tick 推进前拒绝未定义状态。</para>
/// </summary>
public class FighterStateValidationTests
{
    private static readonly FighterDefinition Kai = DemoRoster.CreateKai();

    private static FighterState FighterWith(FighterLogicState state, string? moveId, int remaining) =>
        new(Id: 0, MaxHealth: 1000, Health: 1000, PositionX: -120, PositionY: 0,
            Facing: Facing.Right, LogicState: state, MoveId: moveId,
            PhaseRemainingFrames: remaining, Recognition: null);

    private static RelativeInput Neutral() =>
        new(Forward: false, Back: false, Up: false, Down: false, Attacks: InputBits.None);

    // ─────────────────────────────────────────────────────────────
    // Validate：未定义枚举一律拒绝
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(999)]
    [InlineData(-1)]
    [InlineData(9)] // 06B 起合法枚举为 0..8（Standing..Blockstun），9 是第一个未定义值
    [InlineData(100)]
    public void Validate_RejectsUndefinedStates_WithExplicitReason(int raw)
    {
        var fighter = FighterWith((FighterLogicState)raw, null, 0);

        var ex = Assert.Throws<InvalidOperationException>(fighter.Validate);
        Assert.Contains("未定义", ex.Message);
        Assert.Contains(raw.ToString(), ex.Message); // 错误原因包含具体非法值。
    }

    [Fact]
    public void Validate_RejectsUndefinedState_EvenWithMoveFields()
    {
        // 非法枚举 + 非空 MoveId + 正数帧：仍然拒绝（枚举检查优先于其他字段）。
        var fighter = FighterWith((FighterLogicState)999, DemoRoster.FireballPrototype, 3);

        var ex = Assert.Throws<InvalidOperationException>(fighter.Validate);
        Assert.Contains("未定义", ex.Message);
    }

    [Fact]
    public void BattleState_Construction_RejectsFighterWithUndefinedState()
    {
        var fighters = new[]
        {
            FighterWith((FighterLogicState)999, null, 0),
            FighterState.Create(1, 1000, 120, Facing.Left),
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => new BattleState(0, fighters, BattleState.CreateDefault().StateVersion));
        Assert.Contains("未定义", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    // Tick：推进前拒绝未定义状态（不依赖 BattleState 构造代为校验）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(999)]
    [InlineData(-1)]
    public void Tick_RejectsUndefinedStates_Directly(int raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FighterStateMachine.Tick(
            (FighterLogicState)raw, null, 0, Neutral(), RecognitionState.Empty, Kai, 0));
        Assert.Contains("未定义", ex.Message);
    }

    [Fact]
    public void Tick_DoesNotSilentlyCorrectUndefinedState()
    {
        // 未知状态不得被当作待机转换为 Standing/Crouching——必须失败而不是被修正。
        Assert.Throws<InvalidOperationException>(() => FighterStateMachine.Tick(
            (FighterLogicState)999, null, 0,
            new RelativeInput(false, false, false, true, InputBits.None),
            RecognitionState.Empty, Kai, 0));
    }

    // ─────────────────────────────────────────────────────────────
    // 合法状态回归（既有 FighterStateMachineTests 覆盖完整时间线；
    // 这里固定关键入口在新校验下仍正常工作）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LegalStates_StillValidateAndTick()
    {
        // 待机两态 + 动作三态全部通过 Validate。
        FighterWith(FighterLogicState.Standing, null, 0).Validate();
        FighterWith(FighterLogicState.Crouching, null, 0).Validate();
        FighterWith(FighterLogicState.Startup, DemoRoster.FireballPrototype, 4).Validate();
        FighterWith(FighterLogicState.Active, DemoRoster.FireballPrototype, 3).Validate();
        FighterWith(FighterLogicState.Recovery, DemoRoster.FireballPrototype, 8).Validate();

        // 负剩余帧数依旧拒绝（既有不变式保持）。
        Assert.Throws<InvalidOperationException>(() =>
            FighterWith(FighterLogicState.Startup, DemoRoster.FireballPrototype, -1).Validate());

        // 合法状态推进：站立 → 蹲 → 站。
        var crouch = FighterStateMachine.Tick(FighterLogicState.Standing, null, 0,
            new RelativeInput(false, false, false, true, InputBits.None),
            RecognitionState.Empty, Kai, 0);
        Assert.Equal(FighterLogicState.Crouching, crouch.State);

        var stand = FighterStateMachine.Tick(FighterLogicState.Crouching, null, 0,
            Neutral(), RecognitionState.Empty, Kai, 1);
        Assert.Equal(FighterLogicState.Standing, stand.State);

        // 合法动作推进：Startup → Active → Recovery → 待机。
        var active = FighterStateMachine.Tick(FighterLogicState.Startup,
            DemoRoster.FireballPrototype, 1, Neutral(), RecognitionState.Empty, Kai, 2);
        Assert.Equal(FighterLogicState.Active, active.State);
        Assert.Equal(3, active.PhaseRemainingFrames);

        var recovery = FighterStateMachine.Tick(FighterLogicState.Active,
            DemoRoster.FireballPrototype, 1, Neutral(), RecognitionState.Empty, Kai, 3);
        Assert.Equal(FighterLogicState.Recovery, recovery.State);
        Assert.Equal(8, recovery.PhaseRemainingFrames);

        var idle = FighterStateMachine.Tick(FighterLogicState.Recovery,
            DemoRoster.FireballPrototype, 1, Neutral(), RecognitionState.Empty, Kai, 4);
        Assert.Equal(FighterLogicState.Standing, idle.State);
        Assert.Null(idle.MoveId);
        Assert.Equal(0, idle.PhaseRemainingFrames);
    }

    [Fact]
    public void CorruptedState_CannotEnterSnapshot_ConstructionIsFirstGate()
    {
        // 两道门：
        // 门 1 —— 损坏状态无法进入权威快照（BattleState 构造期 Validate 直接拒绝，
        //         不存在「带非法状态的 BattleState」可供 Step 消费）。
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        var ex = Assert.Throws<InvalidOperationException>(() => new BattleState(
            0,
            new[] { FighterWith((FighterLogicState)999, null, 0), FighterState.Create(1, 1000, 120, Facing.Left) },
            BattleState.CreateDefault().StateVersion));
        Assert.Contains("未定义", ex.Message);

        // 门 2 —— 即使绕过构造（如未来新增构造路径），Tick 入口也独立拒绝：
        //         见 Tick_RejectsUndefinedStates_Directly（不依赖门 1 代为校验）。

        // 合法状态经 Step 正常推进，不受新校验影响。
        BattleState next = sim.Step(BattleState.CreateDefault(), FrameInput.Empty(0)).State;
        Assert.Equal(1, next.Frame);
        Assert.Equal(FighterLogicState.Standing, next.Fighter(0).LogicState);
    }
}
