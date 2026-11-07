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
/// <item><b>双人最小间距（D71 修订）</b>：仅双方都<b>不在空中</b>时强制——
///       帧开始双方都在地面时以帧开始顺序为约束（<b>禁止地面角色单帧交换左右</b>，
///       涉及空中时以候选顺序为准，越身合法）；按缺口对称分配（奇数原始单位余数归 P1），
///       触墙方无法承担的份额由仍有墙余量的一方补足（<b>墙角不残留重叠</b>，
///       如贴墙 640 时对方收敛到 580 而非 582）；完全重叠平局规则固定
///       （P1 向左、P2 向右）；同帧双方候选统一求解，无迭代、无逐帧震荡；</item>
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

        // ── 第 4 阶段：双人最小间距（D71 修订：帧开始顺序约束 + 墙角残差再分配）。
        bool bothGroundAtFrameStart =
            state.Fighters[0].LogicState != FighterLogicState.Airborne &&
            state.Fighters[1].LogicState != FighterLogicState.Airborne;
        ApplySeparation(candidates, bothGroundAtFrameStart,
            state.Fighters[0].PositionX, state.Fighters[1].PositionX);

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
    /// 双人最小间距统一求解（D71 修订，05 专项补修 P1）。
    ///
    /// <para><b>顺序约束（防地面穿越）</b>：帧开始双方都在地面（非 Airborne）时，
    /// 以<b>帧开始相对顺序</b>为约束——地面角色禁止在一个逻辑帧内交换左右；
    /// 完全重叠的初态平局规则保持确定（P1 视为在左）。涉及空中（一方帧开始为
    /// Airborne——合法越身或落地帧）时以<b>候选顺序</b>为准，新地面顺序在落地帧建立；
    /// 空中越身不被本约束禁止。</para>
    ///
    /// <para><b>求解（闭式，同帧双方统一计算，无迭代、无逐帧震荡）</b>：</para>
    /// <list type="number">
    /// <item>候选间距 ≥ 最小间距 → 不推挤；</item>
    /// <item>否则按缺口对称分配（deficit/2；<b>奇数原始单位的余数归 P1</b>——与 D71
    ///       平局规则同源，身份映射下镜像一致）；</item>
    /// <item>钳制到场地边界（D70）；</item>
    /// <item>若钳制后间距仍不足（一方触墙无法承担份额）：<b>剩余量由仍有墙余量的
    ///       一方承担</b>——二次对称分配、触墙方取其可用量、剩余全给未触墙方
    ///       （两侧同时触墙意味着间距已 = 场地宽度 ≥ 最小间距，由 StageDefinition
    ///       构造期校验保证不会发生）。墙角不再残留重叠（旧算法会停在 582/640）。</item>
    /// </list>
    ///
    /// <para>全部运算为 <see cref="FixedPoint"/> 整数算术（deficit/2 对偶数原始值精确、
    /// 奇数原始值向零截断后余数显式归属 P1），无浮点、无 NaN、无静默溢出；
    /// 纯函数——只修改候选副本，不入原快照，Step 失败整体丢弃。</para>
    /// </summary>
    /// <param name="candidates">双方候选（本帧「识别 → 状态机 → 运动」之后）。</param>
    /// <param name="bothGroundAtFrameStart">帧开始双方是否都在地面（决定顺序约束来源）。</param>
    /// <param name="frameStartP1X">帧开始 P1 的 X（顺序约束基准）。</param>
    /// <param name="frameStartP2X">帧开始 P2 的 X（顺序约束基准）。</param>
    private void ApplySeparation(
        Span<FighterCandidate> candidates,
        bool bothGroundAtFrameStart,
        FixedPoint frameStartP1X,
        FixedPoint frameStartP2X)
    {
        // 仅双方候选都非空中时强制（有一方在空中 → 不推挤，允许越身）。
        if (candidates[0].LogicState == FighterLogicState.Airborne ||
            candidates[1].LogicState == FighterLogicState.Airborne)
        {
            return;
        }

        FixedPoint separation = _stage.MinimumFighterSeparation;
        FixedPoint p1Cand = candidates[0].PositionX;
        FixedPoint p2Cand = candidates[1].PositionX;

        // 顺序约束（P1-B）：帧开始双方都在地面 → 禁止本帧交换左右；
        // 涉及空中（越身/落地帧）→ 以候选顺序为准（新地面顺序在落地帧建立）。
        bool p1IsLeft = bothGroundAtFrameStart
            ? frameStartP1X <= frameStartP2X
            : p1Cand <= p2Cand;

        int lo = p1IsLeft ? 0 : 1; // 本帧结束时必须在左侧的角色
        int hi = 1 - lo;
        FixedPoint loCand = candidates[lo].PositionX;
        FixedPoint hiCand = candidates[hi].PositionX;
        FixedPoint gap = hiCand - loCand;

        if (gap >= separation)
            return; // 候选已满足顺序与间距。

        FixedPoint deficit = separation - gap;

        // 第一次分配：对称分摊缺口；奇数原始单位的余数归 P1（身份规则，镜像一致）。
        FixedPoint half = deficit / FixedPoint.FromInt(2); // 向零截断（仅奇数 raw 丢失 1 单位）
        FixedPoint p1Share = deficit - half;
        FixedPoint p2Share = half;
        FixedPoint loShare = lo == 0 ? p1Share : p2Share;
        FixedPoint hiShare = hi == 0 ? p1Share : p2Share;

        FixedPoint loFinal = _stage.ClampX(loCand - loShare);
        FixedPoint hiFinal = _stage.ClampX(hiCand + hiShare);

        // 墙角残差再分配（P1-A）：触墙方无法承担的份额由仍有余量的一方补足。
        FixedPoint newGap = hiFinal - loFinal;
        if (newGap < separation)
        {
            FixedPoint residual = separation - newGap;
            FixedPoint roomLo = loFinal - _stage.MinX;
            FixedPoint roomHi = _stage.MaxX - hiFinal;

            FixedPoint halfResidual = residual / FixedPoint.FromInt(2);
            FixedPoint p1ResidualShare = residual - halfResidual; // 余数归 P1
            FixedPoint p2ResidualShare = halfResidual;
            FixedPoint loResidualShare = lo == 0 ? p1ResidualShare : p2ResidualShare;
            FixedPoint hiResidualShare = hi == 0 ? p1ResidualShare : p2ResidualShare;

            FixedPoint takeLo = loResidualShare < roomLo ? loResidualShare : roomLo;
            FixedPoint takeHi = hiResidualShare < roomHi ? hiResidualShare : roomHi;
            FixedPoint leftover = residual - takeLo - takeHi;
            if (leftover > FixedPoint.Zero)
            {
                // 恰有一侧触墙（两侧同时触墙 ⟹ 间距 = 场地宽度 ≥ 最小间距，不会发生）：
                // 剩余量全给仍有余量的一方。
                if (roomLo > takeLo)
                    loFinal -= leftover;
                else
                    hiFinal += leftover;
            }

            loFinal -= takeLo;
            hiFinal += takeHi;
            loFinal = _stage.ClampX(loFinal);
            hiFinal = _stage.ClampX(hiFinal);
        }

        candidates[lo].PositionX = loFinal;
        candidates[hi].PositionX = hiFinal;
    }
}
