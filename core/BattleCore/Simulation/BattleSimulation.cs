using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// 战斗内核的唯一调度入口，纯函数式推进一逻辑帧。
///
/// <para><b>阶段 05B 权威流水线（每帧固定顺序，文档与测试共同固定）：</b></para>
/// <list type="number">
/// <item><b>输入校验</b>：<see cref="FrameInput.Frame"/> 必须等于 <c>state.Frame</c>；</item>
/// <item><b>逐角色（双方独立计算候选，互不读取对方中间结果）</b>：
///       ① 按「当帧开始朝向」（D46）解析输入并推进 <see cref="RecognitionState"/>（D55）；
///       ② <see cref="FighterStateMachine.Tick"/>：动作阶段 → 起跳预备 → 待机更新
///          （起跳边沿 → PreJump）→ 消费（D58/D60/D67/D68）；
///       ③ 运动：空中按 <see cref="AirborneMovement.Integrate"/> 定点积分（D69，
///          落地同帧修正并回站立——唯一由运动阶段写入的状态边）；站立按
///          <see cref="GroundMovement.StepX"/> 步行（D65）；其余状态原地；</item>
/// <item><b>场地边界（D70）</b>：双方候选 X 一律钳制到 [MinX, MaxX]（含空中；含边界值，
///       不反弹、不振荡、不穿墙）；</item>
/// <item><b>双人最小间距（D71）</b>：仅双方都<b>不在空中</b>时强制——按中点对称推开；
///       完全重叠时平局规则固定（P1 向左、P2 向右）；推挤后再钳制边界
///       （<b>墙优先于间距</b>：靠墙时最终间距可小于最小值，但绝不越界）；</item>
/// <item><b>朝向更新（D72）</b>：帧末进行——地面待机（站立/蹲下）面向对手最终位置；
///       X 相等时保持原朝向（不抖动）；PreJump/Airborne/动作三阶段朝向冻结。
///       本帧输入始终按帧开始朝向解析（D46 不变），帧末朝向只影响后续帧；</item>
/// <item><b>构造全新不可变 <see cref="BattleState"/></b>（含每帧更新
///       <c>UpHeldLastFrame</c>——下一帧起跳边沿判定的事实来源）。</item>
/// </list>
///
/// <para><b>所有权与一致性</b>：传入状态永不修改；识别、消费、运动、推挤都是纯计算——
/// Step 抛异常时不产生任何新状态，会话提交失败时整个候选状态被丢弃，
/// 重试同一帧得到同一结果，不存在半提交。识别与消费在权威转换内同帧完成（零帧延迟）；
/// <b>落地帧不消费指令</b>（落地发生在状态机之后），落地后的第一个地面帧按正常待机消费
/// （D69 明确语义，测试固定）。双方候选先各自算完再做双人间距/朝向处理，
/// 计算顺序不产生 P1 先手优势（有镜像对称测试固定）。</para>
///
/// <para>明确不做（留给后续阶段）：碰撞判定、攻击/受击、动画、取消规则、回合裁决。
/// 不读取 Godot，不使用浮点，不采样设备输入。</para>
/// </summary>
public sealed class BattleSimulation
{
    private readonly FightRoster _roster;
    private readonly StageDefinition _stage;

    /// <param name="roster">双方角色配置；null 表示空配置（双方无指令无招式，
    /// 等价于阶段 01–03 的默认行为——识别状态仍随帧推进，只是没有可匹配指令）。</param>
    /// <param name="stage">场地配置（D70）；null 表示默认演示场地
    /// （X ∈ [-640, 640]、地面 0、最小间距 60）。</param>
    public BattleSimulation(FightRoster? roster = null, StageDefinition? stage = null)
    {
        _roster = roster ?? FightRoster.CreateEmpty();
        _stage = stage ?? StageDefinition.CreateDefault();
    }

    /// <summary>对局双方的角色配置。</summary>
    public FightRoster Roster => _roster;

    /// <summary>本模拟使用的场地配置。</summary>
    public StageDefinition Stage => _stage;

    /// <summary>
    /// 逐角色候选：一帧内该角色「识别 → 状态机 → 运动」后的中间结果（Step 私有，
    /// 可变结构体仅供流水线内部组装；最终仍以不可变 <see cref="FighterState"/> 提交）。
    /// </summary>
    private struct FighterCandidate
    {
        public FighterLogicState LogicState;
        public string? MoveId;
        public int PhaseRemainingFrames;
        public RecognitionState Recognition;
        public FixedPoint PositionX;
        public FixedPoint PositionY;
        public FixedPoint VelocityX;
        public FixedPoint VelocityY;
        public JumpKind JumpKind;
        public Facing Facing;
        public bool UpHeld;

        /// <summary>是否处于地面可转向状态（站立/蹲下；动作与空中朝向冻结，D72）。</summary>
        public readonly bool CanTurn =>
            LogicState is FighterLogicState.Standing or FighterLogicState.Crouching;
    }

    /// <summary>
    /// 由当前状态与帧输入推进一帧，返回新状态与结果描述。
    /// 传入状态不会被修改；返回值中的 State 一定是新实例。
    /// </summary>
    /// <exception cref="ArgumentNullException">state 为 null。</exception>
    /// <exception cref="InvalidOperationException">输入帧号与状态帧号不匹配；
    /// 或角色状态携带了未配置的招式（配置与状态不匹配，立即失败而非静默修正）。</exception>
    public BattleStepResult Step(BattleState state, FrameInput input)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (input.Frame != state.Frame)
        {
            throw new InvalidOperationException(
                $"输入帧号({input.Frame})与当前状态帧号({state.Frame})不一致；" +
                "输入必须在状态帧号上提交，以保证重放可复现。");
        }

        int frameAfter = state.Frame + 1;

        // ── 第 1-2 阶段：逐角色「识别 → 状态机 → 运动」（双方独立，互不读取对方）──
        var candidates = new FighterCandidate[state.Fighters.Count];
        for (int i = 0; i < candidates.Length; i++)
        {
            FighterState fighter = state.Fighters[i];
            FighterDefinition definition = _roster.Fighter(i);
            InputBits bits = input.ForPlayer(i);

            // 1) 指令识别（D55）：纯函数推进，朝向取「帧开始状态」（D46）。
            RecognitionState recognition = fighter.RecognitionState.Advance(
                bits, fighter.Facing, state.Frame, i, definition.Commands);

            // 2) 动作状态机 + 起跳 + 指令消费（D58/D60/D67/D68）。
            RelativeInput resolved = DirectionResolver.Resolve(bits, fighter.Facing);
            FighterActionTick tick = FighterStateMachine.Tick(
                fighter.LogicState,
                fighter.MoveId,
                fighter.PhaseRemainingFrames,
                resolved,
                recognition,
                definition,
                state.Frame,
                fighter.UpHeldLastFrame);

            // 3) 运动（D65 地面步行 / D69 空中积分）。
            FixedPoint x = fighter.PositionX;
            FixedPoint y = fighter.PositionY;
            FixedPoint vx = FixedPoint.Zero;
            FixedPoint vy = FixedPoint.Zero;
            FighterLogicState logic = tick.State;
            JumpKind jumpKind = JumpKind.None;

            if (tick.State == FighterLogicState.Airborne)
            {
                JumpLaunch? launch = tick.Launch;
                AirborneTick air = AirborneMovement.Integrate(
                    fighter.PositionX,
                    fighter.PositionY,
                    launch?.VelocityX ?? fighter.VelocityX,
                    launch?.VelocityY ?? fighter.VelocityY,
                    definition.Movement.JumpGravity);

                x = air.PositionX;
                y = air.PositionY;
                vx = air.VelocityX;
                vy = air.VelocityY;
                jumpKind = launch?.Kind ?? fighter.JumpKind;

                if (air.Landed)
                {
                    // D69 落地边：贴地、速度归零、回站立（同帧完成，无弹跳）。
                    // 落地帧不消费指令（状态机已按空中运行）；下一个地面帧恢复消费。
                    logic = FighterLogicState.Standing;
                    y = FixedPoint.Zero;
                    vx = FixedPoint.Zero;
                    vy = FixedPoint.Zero;
                    jumpKind = JumpKind.None;
                }
            }
            else if (tick.State == FighterLogicState.Standing)
            {
                // 地面步行（D65）：仅站立待机响应前后输入。
                x = GroundMovement.StepX(tick.State, fighter.PositionX, fighter.Facing, resolved, definition.Movement);
            }
            // PreJump / Crouching / 动作三阶段：原地（速度归零，坐标不变）。

            candidates[i] = new FighterCandidate
            {
                LogicState = logic,
                MoveId = tick.MoveId,
                PhaseRemainingFrames = tick.PhaseRemainingFrames,
                Recognition = tick.Recognition,
                PositionX = x,
                PositionY = y,
                VelocityX = vx,
                VelocityY = vy,
                JumpKind = jumpKind,
                Facing = fighter.Facing,
                UpHeld = resolved.Up,
            };
        }

        // ── 第 3 阶段：场地边界（D70）——双方候选统一钳制（含空中）。
        for (int i = 0; i < candidates.Length; i++)
            candidates[i].PositionX = _stage.ClampX(candidates[i].PositionX);

        // ── 第 4 阶段：双人最小间距（D71）——仅双方都不在空中时强制。
        if (candidates[0].LogicState != FighterLogicState.Airborne &&
            candidates[1].LogicState != FighterLogicState.Airborne)
        {
            ApplySeparation(candidates);
        }

        // ── 第 5 阶段：朝向更新（D72）——帧末执行，基于最终候选位置。
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!candidates[i].CanTurn)
                continue; // PreJump/Airborne/动作三阶段朝向冻结。

            FixedPoint opponentX = candidates[1 - i].PositionX;
            FixedPoint myX = candidates[i].PositionX;
            if (opponentX > myX)
                candidates[i].Facing = Facing.Right;
            else if (opponentX < myX)
                candidates[i].Facing = Facing.Left;
            // X 相等：保持原朝向（平局不抖动，D72）。
        }

        // ── 第 6 阶段：全新角色快照（record with：原状态不受影响）。
        var nextFighters = new FighterState[candidates.Length];
        for (int i = 0; i < candidates.Length; i++)
        {
            FighterState fighter = state.Fighters[i];

            nextFighters[i] = fighter with
            {
                LogicState = candidates[i].LogicState,
                MoveId = candidates[i].MoveId,
                PhaseRemainingFrames = candidates[i].PhaseRemainingFrames,
                Recognition = candidates[i].Recognition,
                PositionX = candidates[i].PositionX,
                PositionY = candidates[i].PositionY,
                VelocityX = candidates[i].VelocityX,
                VelocityY = candidates[i].VelocityY,
                JumpKind = candidates[i].JumpKind,
                Facing = candidates[i].Facing,
                UpHeldLastFrame = candidates[i].UpHeld,
            };
        }

        var nextState = new BattleState(frameAfter, nextFighters, state.StateVersion);

        return new BattleStepResult(state.Frame, frameAfter, input, nextState);
    }

    /// <summary>
    /// 双人最小间距推挤（D71）：基于同一帧的两个候选位置对称分配——
    /// 不因先更新 P1 后更新 P2 而产生先手优势。完全重叠的平局规则固定：P1 向左、P2 向右。
    /// 推挤后重新钳制边界：<b>墙优先于间距</b>，靠墙时允许最终间距小于最小值，但绝不越界。
    /// </summary>
    private void ApplySeparation(Span<FighterCandidate> candidates)
    {
        FixedPoint separation = _stage.MinimumFighterSeparation;
        FixedPoint dx = candidates[0].PositionX - candidates[1].PositionX;
        FixedPoint magnitude = dx < FixedPoint.Zero ? -dx : dx;

        if (magnitude < separation)
        {
            FixedPoint overlap = separation - magnitude;
            FixedPoint half = overlap / FixedPoint.FromInt(2); // 向零截断（D63），确定性

            if (dx > FixedPoint.Zero)
            {
                // P1 在右：P1 继续向右、P2 向左。
                candidates[0].PositionX += half;
                candidates[1].PositionX -= half;
            }
            else if (dx < FixedPoint.Zero)
            {
                // P1 在左：P1 向左、P2 向右。
                candidates[0].PositionX -= half;
                candidates[1].PositionX += half;
            }
            else
            {
                // 完全重叠（异常初态/空中落地同点）：平局规则固定——P1 向左、P2 向右。
                candidates[0].PositionX -= half;
                candidates[1].PositionX += half;
            }

            // 墙优先于间距：推挤不得把任何人推出场地。
            candidates[0].PositionX = _stage.ClampX(candidates[0].PositionX);
            candidates[1].PositionX = _stage.ClampX(candidates[1].PositionX);
        }
    }
}
