using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 07 最终补修（D94）：<b>空中状态与地面推挤的资格划分</b>。
///
/// <para><b>修复前实测缺陷</b>：<c>ApplySeparation</c> 的空中排除只判断
/// <see cref="FighterLogicState.Airborne"/>，<c>bothGroundAtFrameStart</c> 同样只排除它——
/// 阶段 07C 新增的 <see cref="FighterLogicState.AirHitstun"/>（Y 可能为 0，因此不能靠高度判断）
/// 被当成地面角色：
/// ① 空中受击者被地面最小间距求解器额外推动；
/// ② 空中越身被「帧开始顺序约束」强行拉回原侧（越身失效）；
/// ③ 落地帧无法用候选顺序建立新的地面左右顺序。</para>
///
/// <para><b>修复后规则（D94）</b>：唯一空中判定 <c>IsAirborneLike()</c>
/// = Airborne | AirHitstun，Step 的两处判断共用它；地面边界（D70）仍然作用于空中角色。</para>
/// </summary>
public class AirSeparationTests
{
    /// <summary>默认场地：X ∈ [-640, 640]，最小中心间距 60，地面 Y=0。</summary>
    private const int Separation = 60;

    private static BattleSimulation Sim() => new(DemoRoster.CreateKaiVsRei());

    /// <summary>构造一个合法的空中受击角色（y &gt; 0，携带倾倒落地计划）。</summary>
    private static FighterState AirHit(int id, int x, int y, int vx, int vy, Facing facing = Facing.Right) =>
        FighterState.Create(id, 1000, x, facing) with
        {
            LogicState = FighterLogicState.AirHitstun,
            PositionY = FixedPoint.FromInt(y),
            VelocityX = FixedPoint.FromInt(vx),
            VelocityY = FixedPoint.FromInt(vy),
            LandingKnockdownFrames = 8,
            LandingWakeupFrames = 5,
        };

    private static BattleState Duel(FighterState p1, FighterState p2) =>
        new(0, new[] { p1, p2 }, BattleState.CreateDefault().StateVersion);

    private static FighterState Ground(int id, int x, Facing facing) =>
        FighterState.Create(id, 1000, x, facing);

    // ─────────────────────────────────────────────────────────────
    // 1. 空中受击不参与地面最小间距推挤
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AirHitstunOverlappingStanding_IsNotPushed()
    {
        // P1 空中受击（y=20，上升 2/帧）与 P2 站立间距仅 30（< 60）。
        // 修复前：被地面求解器推成 -15 / +45（空中轨迹被凭空改写）。
        var sim = Sim();
        BattleState state = Duel(AirHit(0, 0, 20, vx: 0, vy: 2), Ground(1, 30, Facing.Left));

        BattleState after = sim.Step(state, FrameInput.Empty(0)).State;

        Assert.Equal(FighterLogicState.AirHitstun, after.Fighter(0).LogicState);
        Assert.Equal(FixedPoint.Zero, after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), after.Fighter(1).PositionX);

        // 空中积分照常：vy 2 → 1.5，y 20 → 21.5。
        Assert.Equal(FixedPoint.FromFraction(3, 2), after.Fighter(0).VelocityY);
        Assert.Equal(FixedPoint.FromFraction(43, 2), after.Fighter(0).PositionY);
    }

    [Fact]
    public void AirHitstunOverlappingHitstun_IsNotPushed()
    {
        // 空中受击 vs 地面硬直：两者都不能被推挤（地面方处于受击硬直，无速度）。
        var sim = Sim();
        FighterState p2 = Ground(1, 30, Facing.Left) with
        {
            LogicState = FighterLogicState.Hitstun,
            StunRemainingFrames = 10,
        };

        BattleState after = sim.Step(Duel(AirHit(0, 0, 10, 0, 2), p2), FrameInput.Empty(0)).State;

        Assert.Equal(FixedPoint.Zero, after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), after.Fighter(1).PositionX);
    }

    [Fact]
    public void BothAirHitstun_AreNotPushedTogether()
    {
        // 双方都在空中受击且相互重叠：不进行任何地面推挤。
        var sim = Sim();
        BattleState after = sim.Step(
            Duel(AirHit(0, -10, 10, 0, 2), AirHit(1, 10, 10, 0, 2, Facing.Left)),
            FrameInput.Empty(0)).State;

        Assert.Equal(FixedPoint.FromInt(-10), after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(10), after.Fighter(1).PositionX);
        Assert.Equal(FighterLogicState.AirHitstun, after.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.AirHitstun, after.Fighter(1).LogicState);
    }

    [Fact]
    public void AirborneOverlappingStanding_IsNotPushed_Either()
    {
        // 回归守门：普通跳跃（Airborne）的既有规则不被 D94 改动。
        var sim = Sim();
        FighterState p1 = Ground(0, 0, Facing.Right) with
        {
            LogicState = FighterLogicState.Airborne,
            PositionY = FixedPoint.FromInt(20),
            VelocityY = FixedPoint.FromInt(2),
            JumpKind = JumpKind.ShortHop,
        };

        BattleState after = sim.Step(Duel(p1, Ground(1, 30, Facing.Left)), FrameInput.Empty(0)).State;

        Assert.Equal(FixedPoint.Zero, after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(30), after.Fighter(1).PositionX);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 空中越身（不被「帧开始顺序」拉回原侧）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AirHitstun_CrossesFromLeftToRight()
    {
        // P1 空中受击从 -30 以 45/帧飞向右侧：本帧候选即已越过 P2(0)。
        // 修复前：帧开始顺序约束把 P1 回投到 -22.5（越身被取消）。
        var sim = Sim();
        BattleState after = sim.Step(
            Duel(AirHit(0, -30, 20, 45, 2), Ground(1, 0, Facing.Left)),
            FrameInput.Empty(0)).State;

        Assert.Equal(FixedPoint.FromInt(15), after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.Zero, after.Fighter(1).PositionX);
        Assert.True(after.Fighter(0).PositionX > after.Fighter(1).PositionX, "空中越身必须被允许");
    }

    [Fact]
    public void AirHitstun_CrossesFromRightToLeft_Mirrored()
    {
        // 镜像：P1 在 +30 以 -45/帧向左飞，越过 P2(0)。
        var sim = Sim();
        BattleState after = sim.Step(
            Duel(AirHit(0, 30, 20, -45, 2, Facing.Left), Ground(1, 0, Facing.Right)),
            FrameInput.Empty(0)).State;

        Assert.Equal(FixedPoint.FromInt(-15), after.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.Zero, after.Fighter(1).PositionX);
        Assert.True(after.Fighter(0).PositionX < after.Fighter(1).PositionX);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 落地帧：建立新的地面顺序
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 落地帧夹具：P1 空中受击从 -10 以 20/帧飞过 P2(0)，本帧 y=0.5 → 触地转 Knockdown。
    /// 修复前：帧开始顺序把 P1 回投到 -25（越身结果被抹掉）。
    /// 修复后：按候选顺序（P1 在右）建立新地面顺序 → P1=35 / P2=-25（间距恰 60）。
    /// </summary>
    private static BattleSimulation LandingSim(out BattleState landed)
    {
        var sim = Sim();
        FighterState p1 = AirHit(0, -10, 0, 20, 0) with
        {
            // 只差 0.5 单位触地：vy 0 → -0.5，y 0.5 → 0 → Landed。
            PositionY = FixedPoint.FromFraction(1, 2),
        };

        landed = sim.Step(Duel(p1, Ground(1, 0, Facing.Left)), FrameInput.Empty(0)).State;
        return sim;
    }

    [Fact]
    public void LandingFrame_EntersKnockdown()
    {
        _ = LandingSim(out BattleState landed);

        Assert.Equal(FighterLogicState.Knockdown, landed.Fighter(0).LogicState);
        Assert.Equal(FixedPoint.Zero, landed.Fighter(0).PositionY);
        Assert.Equal(FixedPoint.Zero, landed.Fighter(0).VelocityX);
        Assert.Equal(FixedPoint.Zero, landed.Fighter(0).VelocityY);
        Assert.Equal(8, landed.Fighter(0).StunRemainingFrames);      // 倒地计划转为计时
        Assert.Equal(0, landed.Fighter(0).LandingKnockdownFrames);
        Assert.Equal(5, landed.Fighter(0).LandingWakeupFrames);      // 起身计划保留
    }

    [Fact]
    public void LandingFrame_EstablishesNewGroundOrder()
    {
        _ = LandingSim(out BattleState landed);

        // P1 已越过 P2：落地同帧按候选顺序建立新顺序并推出精确间距。
        Assert.Equal(FixedPoint.FromInt(35), landed.Fighter(0).PositionX);
        Assert.Equal(FixedPoint.FromInt(-25), landed.Fighter(1).PositionX);
        Assert.True(landed.Fighter(0).PositionX > landed.Fighter(1).PositionX, "落地帧必须建立新的左右顺序");
        Assert.Equal(FixedPoint.FromInt(Separation),
            landed.Fighter(0).PositionX - landed.Fighter(1).PositionX);
    }

    [Fact]
    public void AfterLanding_GroundFightersCannotCrossBack()
    {
        // 越身落地后 P2 持续朝右（朝 P1 方向）推进：新顺序被保持，不能再穿回左侧。
        BattleSimulation sim = LandingSim(out BattleState state);

        for (int f = 1; f <= 12; f++)
        {
            state = sim.Step(state, new FrameInput(f, InputBits.None, InputBits.Right)).State;
            Assert.True(state.Fighter(0).PositionX > state.Fighter(1).PositionX,
                $"帧 {f} 落地后的新地面顺序被回退");
            Assert.Equal(FixedPoint.FromInt(Separation),
                state.Fighter(0).PositionX - state.Fighter(1).PositionX);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 地面规则不被放宽（D73 回归）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GroundKnockback_StillCannotCrossOpponent()
    {
        // 地面受击（Hitstun）携带 45/帧击退从 -30 冲过 P2(0)：双方帧开始都在地面 →
        // 顺序约束仍然生效（D73 监督的地面击退），不得穿越。
        var sim = Sim();
        FighterState p1 = Ground(0, -30, Facing.Right) with
        {
            LogicState = FighterLogicState.Hitstun,
            StunRemainingFrames = 10,
            VelocityX = FixedPoint.FromInt(45),
            KnockbackFriction = FixedPoint.FromInt(45),
        };

        BattleState after = sim.Step(Duel(p1, Ground(1, 0, Facing.Left)), FrameInput.Empty(0)).State;

        Assert.True(after.Fighter(0).PositionX < after.Fighter(1).PositionX, "地面角色不得穿越对手");
        Assert.Equal(FixedPoint.FromFraction(-45, 2), after.Fighter(0).PositionX); // -22.5
        Assert.Equal(FixedPoint.FromFraction(75, 2), after.Fighter(1).PositionX);  // +37.5
        Assert.Equal(FixedPoint.FromInt(Separation),
            after.Fighter(1).PositionX - after.Fighter(0).PositionX);
    }

    [Fact]
    public void AirHitstun_IsStillClampedByStageBoundary()
    {
        // D70 对空中角色照旧生效：630 + 45 越过右边界 → 钳制到 640。
        var sim = Sim();
        BattleState after = sim.Step(
            Duel(AirHit(0, 630, 20, 45, 2), Ground(1, -300, Facing.Left)),
            FrameInput.Empty(0)).State;

        Assert.Equal(FixedPoint.FromInt(640), after.Fighter(0).PositionX);
        Assert.Equal(FighterLogicState.AirHitstun, after.Fighter(0).LogicState);
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 确定性
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_Twice_BitIdentical()
    {
        static BattleState Run()
        {
            var sim = Sim();
            BattleState state = new BattleState(0, new[]
            {
                AirHit(0, -30, 20, 45, 2),
                Ground(1, 0, Facing.Left),
            }, BattleState.CreateDefault().StateVersion);

            for (int f = 0; f < 80; f++)
            {
                InputBits p2 = f % 3 == 0 ? InputBits.Right : InputBits.None;
                state = sim.Step(state, new FrameInput(f, InputBits.None, p2)).State;
            }

            return state;
        }

        BattleState a = Run();
        BattleState b = Run();

        Assert.Equal(a.Fighter(0), b.Fighter(0));
        Assert.Equal(a.Fighter(1), b.Fighter(1));
        Assert.Equal(a.Round, b.Round);
    }
}
