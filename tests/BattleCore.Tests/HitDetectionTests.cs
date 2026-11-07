using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 06A（D76）：Pushbox / Hurtbox / Hitbox 职责与攻击判定几何。
///
/// <para>Kai 5A（Startup 3 / Active 2）时间线（D58）：帧 0–2 Startup、帧 3–4 Active
/// （帧 3 = elapsed 0 = 第 1 个生效帧）、帧 5–10 Recovery。攻击框：前向偏移 45、
/// 半宽 20 → 朝右时覆盖 X+25..X+65；站立 Hurtbox 宽 60（±30）、高 120。</para>
/// </summary>
public class HitDetectionTests
{
    private static readonly FighterDefinition Kai = DemoRoster.CreateKai();

    private static FighterState Standing(int x, Facing facing) =>
        FighterState.Create(0, 1000, x, facing);

    private static FighterState WithState(FighterState baseState, FighterLogicState state, string? moveId, int remaining) =>
        baseState with { LogicState = state, MoveId = moveId, PhaseRemainingFrames = remaining };

    // ─────────────────────────────────────────────────────────────
    // Hurtbox 姿态
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Hurtbox_Standing_Crouching_Airborne()
    {
        var standing = Standing(-120, Facing.Right);
        var stand = FighterBoxes.GetHurtbox(standing);
        Assert.Equal(FixedPoint.FromInt(-150), stand.MinX);
        Assert.Equal(FixedPoint.FromInt(-90), stand.MaxX);
        Assert.Equal(FixedPoint.Zero, stand.MinY);
        Assert.Equal(FixedPoint.FromInt(120), stand.MaxY);

        var crouch = FighterBoxes.GetHurtbox(standing with { LogicState = FighterLogicState.Crouching });
        Assert.Equal(FixedPoint.FromInt(70), crouch.MaxY);
        Assert.Equal(stand.MinX, crouch.MinX);

        // 空中框跟随权威 Y（脚底 = PositionY）。
        var air = FighterBoxes.GetHurtbox(standing with
        {
            LogicState = FighterLogicState.Airborne,
            PositionY = FixedPoint.FromInt(50),
            JumpKind = JumpKind.ShortHop,
            VelocityY = FixedPoint.FromInt(1),
        });
        Assert.Equal(FixedPoint.FromInt(50), air.MinY);
        Assert.Equal(FixedPoint.FromInt(170), air.MaxY);
    }

    [Fact]
    public void Pushbox_MatchesD73Separation()
    {
        // Pushbox 宽 60（±30）：D73 最小中心间距 60 = 双方实体相接；查询仅显示用。
        var push = FighterBoxes.GetPushbox(Standing(0, Facing.Right));
        Assert.Equal(FixedPoint.FromInt(-30), push.MinX);
        Assert.Equal(FixedPoint.FromInt(30), push.MaxX);
    }

    // ─────────────────────────────────────────────────────────────
    // Hitbox 时间线（Startup 无 / Active 首末帧有 / Recovery 无）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FighterLogicState.Standing, 0, false)]
    [InlineData(FighterLogicState.Startup, 3, false)]
    [InlineData(FighterLogicState.Startup, 1, false)]
    [InlineData(FighterLogicState.Active, 2, true)]   // elapsed 0：Active 第 1 帧
    [InlineData(FighterLogicState.Active, 1, true)]   // elapsed 1：Active 第 2（末）帧
    [InlineData(FighterLogicState.Recovery, 6, false)]
    [InlineData(FighterLogicState.Recovery, 1, false)]
    public void Hitbox_OnlyDuringConfiguredActiveFrames(FighterLogicState state, int remaining, bool expected)
    {
        var fighter = WithState(Standing(0, Facing.Right), state, DemoRoster.StandLightPunch, remaining);
        var boxes = FighterBoxes.GetActiveHitboxes(fighter, Kai);

        Assert.Equal(expected, boxes.Count > 0);
        if (expected)
        {
            // 朝右：偏移 45 半宽 20 → X+25..X+65；中心高 70 → Y 50..90。
            Assert.Equal(FixedPoint.FromInt(25), boxes[0].MinX);
            Assert.Equal(FixedPoint.FromInt(65), boxes[0].MaxX);
            Assert.Equal(FixedPoint.FromInt(50), boxes[0].MinY);
            Assert.Equal(FixedPoint.FromInt(90), boxes[0].MaxY);
        }
    }

    [Fact]
    public void Hitbox_NoMove_NoBox()
    {
        Assert.Empty(FighterBoxes.GetActiveHitboxes(Standing(0, Facing.Right), Kai));
    }

    [Fact]
    public void Hitbox_MirrorsWithAuthoritativeFacing()
    {
        // 朝左：前向偏移镜像 → X−65..X−25。
        var left = WithState(Standing(0, Facing.Left), FighterLogicState.Active,
            DemoRoster.StandLightPunch, 2);
        var boxes = FighterBoxes.GetActiveHitboxes(left, Kai);

        Assert.Equal(FixedPoint.FromInt(-65), boxes[0].MinX);
        Assert.Equal(FixedPoint.FromInt(-25), boxes[0].MaxX);
    }

    [Fact]
    public void Hitbox_FollowsAuthoritativePosition()
    {
        var moved = WithState(Standing(300, Facing.Right), FighterLogicState.Active,
            DemoRoster.StandLightPunch, 2);
        var boxes = FighterBoxes.GetActiveHitboxes(moved, Kai);

        Assert.Equal(FixedPoint.FromInt(325), boxes[0].MinX);
        Assert.Equal(FixedPoint.FromInt(365), boxes[0].MaxX);
    }

    [Fact]
    public void AttackDefinition_RejectsInvalidGeometryAndRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttackDefinition(
            FixedPoint.Zero, FixedPoint.Zero, FixedPoint.Zero, FixedPoint.FromInt(10)));   // 半宽 0
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttackDefinition(
            FixedPoint.Zero, FixedPoint.Zero, FixedPoint.FromInt(10), FixedPoint.Zero));   // 半高 0
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttackDefinition(
            FixedPoint.Zero, FixedPoint.Zero, FixedPoint.FromInt(10), FixedPoint.FromInt(10),
            firstActiveFrame: -1));                                                        // 负帧号
        Assert.Throws<ArgumentException>(() => new AttackDefinition(
            FixedPoint.Zero, FixedPoint.Zero, FixedPoint.FromInt(10), FixedPoint.FromInt(10),
            firstActiveFrame: 3, lastActiveFrame: 1));                                     // 倒置范围
    }

    [Fact]
    public void MoveDefinition_RejectsAttackRangeBeyondActiveFrames()
    {
        Assert.Throws<ArgumentException>(() => new MoveDefinition("M", "C",
            startupFrames: 1, activeFrames: 2, recoveryFrames: 1,
            attacks: new[]
            {
                new AttackDefinition(FixedPoint.Zero, FixedPoint.Zero,
                    FixedPoint.FromInt(10), FixedPoint.FromInt(10),
                    firstActiveFrame: 0, lastActiveFrame: 2), // ActiveFrames=2 → 合法范围 0..1
            }));

        // 恰好在范围内：合法。
        var ok = new MoveDefinition("M", "C",
            startupFrames: 1, activeFrames: 2, recoveryFrames: 1,
            attacks: new[]
            {
                new AttackDefinition(FixedPoint.Zero, FixedPoint.Zero,
                    FixedPoint.FromInt(10), FixedPoint.FromInt(10),
                    firstActiveFrame: 0, lastActiveFrame: 1),
            });
        Assert.Single(ok.Attacks);
    }

    // ─────────────────────────────────────────────────────────────
    // Step 集成：接触候选与不退化
    // ─────────────────────────────────────────────────────────────

    /// <summary>P1 在 -120 按 A（5A），P2 在 -60 静立（间距 60）：攻击框覆盖 -95..-55，
    /// P2 Hurtbox -90..-30 → Active 帧相交。</summary>
    private static BattleState CustomState(int p1X, Facing p1Facing, int p2X, Facing p2Facing) => new(
        0,
        new[]
        {
            FighterState.Create(0, 1000, p1X, p1Facing),
            FighterState.Create(1, 1000, p2X, p2Facing),
        },
        BattleState.CreateDefault().StateVersion);

    private static (bool contact, BattleState state) Step5A(BattleSimulation sim, BattleState state, int frame) =>
        StepWith(sim, state, frame, InputBits.LightPunch);

    private static (bool contact, BattleState state) StepWith(
        BattleSimulation sim, BattleState state, int frame, InputBits p1)
    {
        BattleStepResult result = sim.Step(state, new FrameInput(frame, p1, InputBits.None));
        return (result.Contacts.Count > 0, result.State);
    }

    [Fact]
    public void Step_5A_Timeline_ContactsExactlyOnActiveFrames()
    {
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        BattleState state = CustomState(-120, Facing.Right, -60, Facing.Left);

        // 帧 0–2：Startup 无接触。
        for (int f = 0; f <= 2; f++)
        {
            var (contact, next) = Step5A(sim, state, f);
            Assert.False(contact, $"帧 {f}（Startup）不应有接触");
            state = next;
        }

        // 帧 3–4：Active 首帧与末帧都有接触；位置不因攻击改变（Pushbox 不受影响）。
        int xBefore = state.Fighter(0).PositionX.ToIntTruncated();
        for (int f = 3; f <= 4; f++)
        {
            var (contact, next) = Step5A(sim, state, f);
            Assert.True(contact, $"帧 {f}（Active）应有接触");
            state = next;
            Assert.Equal(-120, state.Fighter(0).PositionX.ToIntTruncated());
        }

        // 帧 5–7：Recovery 无接触。
        for (int f = 5; f <= 7; f++)
        {
            var (contact, next) = Step5A(sim, state, f);
            Assert.False(contact, $"帧 {f}（Recovery）不应有接触");
            state = next;
        }
    }

    [Fact]
    public void Step_MirrorConfigs_ProduceMirroredContacts()
    {
        // 直接构造「已进入 Active」的权威状态（绕过识别差异：Kai 的 5A 与 Rei 的 236A
        // 时间线不同，不影响几何镜像性）：
        // 场景 A：P1(Kai, -60, 朝右, 5A Active) 打 P2(Rei, 0)。
        // 场景 B（镜像）：P2(Rei, 60, 朝左, 236A Active) 打 P1(Kai, 0)。
        FighterState kaiAttacking = FighterState.Create(0, 1000, -60, Facing.Right) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = DemoRoster.StandLightPunch,
            PhaseRemainingFrames = 2,
            MoveStartedFrame = 0,
        };
        FighterState reiAttacking = FighterState.Create(1, 1000, 60, Facing.Left) with
        {
            LogicState = FighterLogicState.Active,
            MoveId = DemoRoster.BackDraft,
            PhaseRemainingFrames = 2,
            MoveStartedFrame = 0,
        };

        var simA = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        var stateA = new BattleState(0, new[]
        {
            kaiAttacking,
            FighterState.Create(1, 1000, 0, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        var simB = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        var stateB = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, 0, Facing.Right),
            reiAttacking,
        }, BattleState.CreateDefault().StateVersion);

        BattleStepResult resultA = simA.Step(stateA, FrameInput.Empty(0));
        BattleStepResult resultB = simB.Step(stateB, FrameInput.Empty(0));

        // 接触互为镜像（攻击者/被击者身份对调、位置取反、招式各自配置）。
        var contactA = Assert.Single(resultA.Contacts);
        var contactB = Assert.Single(resultB.Contacts);
        Assert.Equal((0, 1, DemoRoster.StandLightPunch), (contactA.AttackerId, contactA.TargetId, contactA.MoveId));
        Assert.Equal((1, 0, DemoRoster.BackDraft), (contactB.AttackerId, contactB.TargetId, contactB.MoveId));
    }

    [Fact]
    public void Detect_NeverProducesSelfContacts()
    {
        // 攻击框不命中使用者自己：任何接触的 AttackerId ≠ TargetId。
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        BattleState state = BattleState.CreateDefault();
        for (int f = 0; f <= 4; f++)
        {
            BattleStepResult result = sim.Step(state, new FrameInput(f, InputBits.LightPunch, InputBits.None));
            state = result.State;
            foreach (var contact in result.Contacts)
                Assert.NotEqual(contact.AttackerId, contact.TargetId);
        }
    }

    [Fact]
    public void Step_WallCornerPush_DoesNotRegress()
    {
        // 攻击判定引入后 D73 不退化：贴墙 640 + 持续推进仍收敛 580/640。
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        var state = new BattleState(0, new[]
        {
            FighterState.Create(0, 1000, 500, Facing.Right),
            FighterState.Create(1, 1000, 640, Facing.Left),
        }, BattleState.CreateDefault().StateVersion);

        for (int f = 0; f < 300; f++)
            state = sim.Step(state, new FrameInput(f, InputBits.Right, InputBits.None)).State;

        Assert.Equal(580, state.Fighter(0).PositionX.ToIntTruncated());
        Assert.Equal(640, state.Fighter(1).PositionX.ToIntTruncated());
    }

    // ─────────────────────────────────────────────────────────────
    // 阶段 06 最终收尾 P2：GetActiveHitboxesWithIndex 的只读封装（D61 约定）
    // ─────────────────────────────────────────────────────────────

    private static FighterState ActiveKai() =>
        WithState(Standing(0, Facing.Right), FighterLogicState.Active, DemoRoster.StandLightPunch, 2);

    [Fact]
    public void ActiveHitboxesWithIndex_NonEmpty_IsReadOnly()
    {
        IReadOnlyList<ActiveHitbox> boxes = FighterBoxes.GetActiveHitboxesWithIndex(ActiveKai(), Kai);

        // 快照：后续所有尝试都不得改变长度或内容。
        ActiveHitbox[] before = boxes.ToArray();
        Assert.Single(before);

        // 不能强转成可写 List，也不能强转成数组。
        Assert.Null(boxes as List<ActiveHitbox>);
        Assert.Null(boxes as ActiveHitbox[]);

        // 泛型与非泛型写操作都必须被拒绝。
        Assert.True(((IList<ActiveHitbox>)boxes).IsReadOnly);
        Assert.Throws<NotSupportedException>(() => { ((IList<ActiveHitbox>)boxes).Clear(); });
        Assert.Throws<NotSupportedException>(() => { ((IList<ActiveHitbox>)boxes)[0] = default; });
        Assert.Throws<NotSupportedException>(() => { ((IList<ActiveHitbox>)boxes).Add(default); });
        Assert.Throws<NotSupportedException>(() => { ((System.Collections.IList)boxes).Clear(); });
        Assert.Throws<NotSupportedException>(() => { ((System.Collections.IList)boxes)[0] = default; });
        Assert.Throws<NotSupportedException>(() => { ((System.Collections.IList)boxes).Add(null); });

        // 逐位一致（长度与每个元素）。
        Assert.Equal(before.Length, boxes.Count);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], boxes[i]);
    }

    [Fact]
    public void ActiveHitboxesWithIndex_Empty_SharesSingleInstance()
    {
        IReadOnlyList<ActiveHitbox> first = FighterBoxes.GetActiveHitboxesWithIndex(
            Standing(0, Facing.Right), Kai);
        IReadOnlyList<ActiveHitbox> second = FighterBoxes.GetActiveHitboxesWithIndex(
            Standing(0, Facing.Right), Kai);

        Assert.Empty(first);
        Assert.Same(first, second);           // 空结果共享同一实例（无每次分配）
        Assert.Null(first as List<ActiveHitbox>);
    }

    [Fact]
    public void ActiveHitboxesWithIndex_MultipleBoxes_KeepOrderAndOriginalIndex()
    {
        // 同一招两个攻击定义都在 elapsed 0 生效：顺序固定为配置顺序，序号是原始下标。
        var move = new MoveDefinition("Twin", "5A", startupFrames: 1, activeFrames: 3, recoveryFrames: 4,
            attacks: new[]
            {
                new AttackDefinition(FixedPoint.FromInt(30), FixedPoint.FromInt(40),
                    FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
                new AttackDefinition(FixedPoint.FromInt(70), FixedPoint.FromInt(60),
                    FixedPoint.FromInt(10), FixedPoint.FromInt(10)),
            });
        var definition = new FighterDefinition("Twin Fighter",
            new[] { new CommandDefinition("5A", Array.Empty<CommandDirection>(), InputBits.LightPunch, priority: 5) },
            new[] { move });

        FighterState fighter = WithState(Standing(0, Facing.Right), FighterLogicState.Active,
            "Twin", remaining: 3); // 直接查询无 tick：elapsed = ActiveFrames(3) − 3 = 0

        IReadOnlyList<ActiveHitbox> boxes = FighterBoxes.GetActiveHitboxesWithIndex(fighter, definition);

        Assert.Equal(2, boxes.Count);
        Assert.Equal(new[] { 0, 1 }, boxes.Select(b => b.AttackIndex).ToArray());
        Assert.Equal(FixedPoint.FromInt(20), boxes[0].Box.MinX);
        Assert.Equal(FixedPoint.FromInt(40), boxes[0].Box.MaxX);
        Assert.Equal(FixedPoint.FromInt(60), boxes[1].Box.MinX);
        Assert.Equal(FixedPoint.FromInt(80), boxes[1].Box.MaxX);

        // 几何视图与索引视图逐位一致（Godot 调试框走这里）。
        IReadOnlyList<FixedRect> geometry = FighterBoxes.GetActiveHitboxes(fighter, definition);
        Assert.Equal(boxes.Select(b => b.Box).ToArray(), geometry.ToArray());
    }
}
