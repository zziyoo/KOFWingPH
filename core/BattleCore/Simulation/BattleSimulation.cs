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
/// <para><b>阶段 07A 起的固定流水线（顺序即语义，改动必须同步 ARCHITECTURE）：</b></para>
/// <list type="number">
/// <item>帧号与回合状态校验；<b>终局则冻结</b>（帧号 +1，角色/回合/事件逐位不变，D89）；</item>
/// <item>逐角色：指令识别（D46/D55）→ 状态机（含取消判定，D91）→ 运动（地面步行/击退/空中积分）；</item>
/// <item>场地边界钳制（D70）→ 双人最小间距统一求解（D73）；</item>
/// <item>攻击生效快照（结算前，D87）→ 接触检测（D76）→ 双方并行解析结算（D80/D81）；</item>
/// <item>统一应用伤害/硬直/击退/击飞与动作中断（D78/D90）→ 连段更新（D91）→ 朝向更新（D72）；</item>
/// <item><b>KO 裁决</b>：只依据全部结算后的最终体力（D89，双 KO = DoubleKO）；</item>
/// <item>收集战斗事件（D82/D89）→ 构造不可变 <see cref="BattleState"/>；</item>
/// <item><c>BattleSession</c> 完整提交后才向观察者投递事件（D83）。</item>
/// </list>
///
/// <para>不读取 Godot，不使用浮点，不采样设备输入；表现层只读。</para>
/// </summary>
public sealed class BattleSimulation
{
    private readonly FightRoster _roster;
    private readonly StageDefinition _stage;
    private readonly bool _useFighterSeparation;

    /// <param name="roster">双方角色配置；null 表示空配置（双方无指令无招式，
    /// 等价于阶段 01–03 的默认行为——识别状态仍随帧推进，只是没有可匹配指令）。</param>
    /// <param name="stage">场地配置（D70）；null 表示默认演示场地
    /// （X ∈ [-640, 640]、地面 0、最小间距 60）。</param>
    public BattleSimulation(FightRoster? roster = null, StageDefinition? stage = null)
    {
        _roster = roster ?? FightRoster.CreateEmpty();
        _useFighterSeparation = stage is null && _roster.FacingGroundSeparation.HasValue;
        _stage = stage ?? StageDefinition.CreateDefault(_roster.FacingGroundSeparation);
    }

    /// <summary>对局双方的角色配置。</summary>
    public FightRoster Roster => _roster;

    private static FixedPoint OpponentBodyDistance(FighterState fighter, FighterState opponent,
        FighterDefinition definition, FighterDefinition opponentDefinition)
    {
        if (definition.GroundBodyWidths is not { } own)
            return FixedPoint.Zero;

        int facing = fighter.Facing == Facing.Right ? 1 : -1;
        int opponentFacing = opponent.Facing == Facing.Right ? 1 : -1;
        Int128 distance = ((Int128)opponent.PositionX.RawValue - fighter.PositionX.RawValue) * facing;
        GroundBodyWidths? other = opponent.LogicState.IsAirborneLike()
            ? opponentDefinition.AirBodyWidths ?? opponentDefinition.GroundBodyWidths
            : opponentDefinition.GroundBodyWidths;
        bool opponentFront = (distance >= 0) == (facing != opponentFacing);
        FixedPoint width = (opponentFront ? other?.Front : other?.Back) ?? FighterBoxes.PushboxHalfWidth;
        Int128 opponentEdge = (Int128)width.RawValue * facing * opponentFacing * (opponentFront ? 1 : -1);
        Int128 bodyDistance = distance - own.Front.RawValue + opponentEdge;
        return FixedPoint.FromRaw((long)Int128.Clamp(bodyDistance, long.MinValue, long.MaxValue));
    }

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
        public bool BackHeld;
        public int Health;
        public int StunRemainingFrames;
        public int MoveStartedFrame;
        public bool MoveStartedThisFrame;
        public FixedPoint KnockbackFriction;
        public int LandingKnockdownFrames;
        public int LandingWakeupFrames;
        public int MoveElapsedFrames;
        public int HitstopRemainingFrames;
        public int ProjectileSpawnMask;

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

        // ── 终局冻结（D89）：帧号照常前进，角色、回合状态与事件逐位不变。
        //    选择「允许 Step 但冻结」而非「禁止 Step」：BattleSession 的
        //    「Frame − 初始帧 == TotalSteps」不变量与 Godot 的正常 Advance 调用都不需要特判，
        //    终局不会让游戏进入异常/故障状态，也不会重复产生 KO 事件。
        if (state.Round.IsFinished)
        {
            var frozen = new BattleState(frameAfter, state.Fighters, state.StateVersion, state.Round,
                state.Projectiles, state.NextProjectileId, state.RoundTimerRemainingFrames);
            return new BattleStepResult(state.Frame, frameAfter, input, frozen);
        }

        // ── 第 1-2 阶段：逐角色「识别 → 状态机 → 运动」（只读取帧开始快照）──
        Int128 separation = (Int128)state.Fighters[0].PositionX.RawValue - state.Fighters[1].PositionX.RawValue;
        FixedPoint opponentDistance = FixedPoint.FromRaw((long)Int128.Min(Int128.Abs(separation), long.MaxValue));
        var candidates = new FighterCandidate[state.Fighters.Count];
        var frozenThisFrame = new bool[candidates.Length];
        for (int i = 0; i < candidates.Length; i++)
        {
            FighterState fighter = state.Fighters[i];
            FighterDefinition definition = _roster.Fighter(i);
            InputBits bits = input.ForPlayer(i);

            // ── 命中停顿（08A/D96）：本帧<b>整体冻结</b>——动作阶段、硬直计时、击退衰减、
            //    空中积分、取消窗口全部停摆；只有输入识别照常推进（缓冲不丢失、不消费）。
            //    N 帧停顿 = 冻结后续 N 个逻辑帧，第 N+1 帧恢复（计数器逐帧 −1）。
            if (fighter.HitstopRemainingFrames > 0 || state.PendingThrow is not null)
            {
                RecognitionState frozenRecognition = fighter.RecognitionState.Advance(
                    bits, fighter.Facing, state.Frame, i, definition.Commands);

                candidates[i] = new FighterCandidate
                {
                    LogicState = fighter.LogicState,
                    MoveId = fighter.MoveId,
                    PhaseRemainingFrames = fighter.PhaseRemainingFrames,
                    Recognition = frozenRecognition,
                    PositionX = fighter.PositionX,
                    PositionY = fighter.PositionY,
                    VelocityX = fighter.VelocityX,
                    VelocityY = fighter.VelocityY,
                    JumpKind = fighter.JumpKind,
                    Facing = fighter.Facing,
                    UpHeld = fighter.UpHeldLastFrame,
                    // 防御资格仍按本帧输入解析（停顿不额外授予/剥夺防御）。
                    BackHeld = DirectionResolver.Resolve(bits, fighter.Facing).Back,
                    Health = fighter.Health,
                    StunRemainingFrames = fighter.StunRemainingFrames,
                    MoveStartedFrame = fighter.MoveStartedFrame,
                    MoveElapsedFrames = fighter.MoveElapsedFrames,
                    MoveStartedThisFrame = false,
                    KnockbackFriction = fighter.KnockbackFriction,
                    LandingKnockdownFrames = fighter.LandingKnockdownFrames,
                    LandingWakeupFrames = fighter.LandingWakeupFrames,
                    HitstopRemainingFrames = Math.Max(0, fighter.HitstopRemainingFrames - 1),
                    ProjectileSpawnMask = fighter.ProjectileSpawnMask,
                };
                frozenThisFrame[i] = true;
                continue;
            }

            // 1) 指令识别（D55）：纯函数推进，朝向取「帧开始状态」（D46）。
            RecognitionState recognition = fighter.RecognitionState.Advance(
                bits, fighter.Facing, state.Frame, i, definition.Commands);

            // 2) 动作状态机 + 起跳 + 指令消费（D58/D60/D67/D68）；硬直倒计时（D78）。
            RelativeInput resolved = DirectionResolver.Resolve(bits, fighter.Facing);

            // 动作有效经过帧数（D96）：帧开始时已在招式中 → 上一帧值 + 1；
            // 本帧发动/取消时由状态机返回 0（新实例）。
            int moveElapsed = fighter.IsInMove ? fighter.MoveElapsedFrames + 1 : 0;

            FighterActionTick tick = FighterStateMachine.Tick(
                fighter.LogicState,
                fighter.MoveId,
                fighter.PhaseRemainingFrames,
                resolved,
                recognition,
                definition,
                state.Frame,
                fighter.UpHeldLastFrame,
                fighter.StunRemainingFrames,
                fighter.MoveStartedFrame,
                fighter.HitConfirmFrame,
                fighter.BlockConfirmFrame,
                fighter.LandingWakeupFrames,
                moveElapsed,
                opponentDistance,
                OpponentBodyDistance(fighter, state.Fighters[1 - i], definition, _roster.Fighter(1 - i)));

            // 3) 运动（D65 地面步行 / D69 空中积分）。
            FixedPoint x = fighter.PositionX;
            FixedPoint y = fighter.PositionY;
            FixedPoint vx = FixedPoint.Zero;
            FixedPoint vy = FixedPoint.Zero;
            FighterLogicState logic = tick.State;
            JumpKind jumpKind = JumpKind.None;

            // 落地计划（D93）：只有空中受击会消耗它们，落地后立即清空。
            int landingKnockdown = fighter.LandingKnockdownFrames;
            int landingWakeup = fighter.LandingWakeupFrames;
            int stunRemaining = tick.StunRemainingFrames;
            FixedPoint knockbackFriction = FixedPoint.Zero;

            if (tick.State is FighterLogicState.Airborne or FighterLogicState.AirBlockstun)
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
                    logic = stunRemaining > 0 ? FighterLogicState.Blockstun : FighterLogicState.Standing;
                    y = FixedPoint.Zero;
                    vx = FixedPoint.Zero;
                    vy = FixedPoint.Zero;
                    jumpKind = JumpKind.None;
                }
            }
            else if (tick.State == FighterLogicState.AirHitstun)
            {
                // 空中受击（D93）：与跳跃共用同一套定点积分；落地转倒地 / 起身 / 站立。
                AirborneTick air = AirborneMovement.Integrate(
                    fighter.PositionX,
                    fighter.PositionY,
                    fighter.VelocityX,
                    fighter.VelocityY,
                    definition.Movement.JumpGravity);

                x = air.PositionX;
                y = air.PositionY;
                vx = air.VelocityX;
                vy = air.VelocityY;

                if (air.Landed)
                {
                    // 触地：贴地、速度归零；倒地计划转为状态计时（D93 时间线）。
                    y = FixedPoint.Zero;
                    vx = FixedPoint.Zero;
                    vy = FixedPoint.Zero;
                    jumpKind = JumpKind.None;

                    if (landingKnockdown > 0)
                    {
                        logic = FighterLogicState.Knockdown;
                        stunRemaining = landingKnockdown;
                        landingKnockdown = 0;
                    }
                    else if (landingWakeup > 0)
                    {
                        logic = FighterLogicState.Wakeup;
                        stunRemaining = landingWakeup;
                        landingWakeup = 0;
                    }
                    else
                    {
                        logic = FighterLogicState.Standing;
                        stunRemaining = 0;
                    }
                }
            }
            else if (tick.State is FighterLogicState.Hitstun or FighterLogicState.Blockstun)
            {
                // 地面击退（D92）：每帧位移 = 当前速度，速度按摩擦向 0 衰减且不过零。
                // 位移发生在运动阶段，因此仍然经过场地钳制（D70）与双人最小间距（D73）。
                FixedPoint speed = fighter.VelocityX;
                if (speed != FixedPoint.Zero)
                {
                    GroundReactionMovement? reaction = definition.Movement.GroundReaction;
                    if (reaction is not null && logic == FighterLogicState.Hitstun && stunRemaining == 1)
                        speed *= reaction.LastHitStepScale;
                    x = fighter.PositionX + speed;
                    FixedPoint friction = fighter.KnockbackFriction;
                    FixedPoint? crouchRetention = logic == FighterLogicState.Blockstun && resolved.Down
                        ? reaction?.CrouchGuardVelocityRetention : null;
                    FixedPoint next = reaction is not null
                        ? speed * (crouchRetention ?? reaction.VelocityRetention)
                        : (speed > FixedPoint.Zero ? speed - friction : speed + friction);
                    if (reaction is not null && crouchRetention is null && (next > -FixedPoint.One && next < FixedPoint.One))
                        next = FixedPoint.Zero;
                    if ((speed > FixedPoint.Zero && next < FixedPoint.Zero)
                        || (speed < FixedPoint.Zero && next > FixedPoint.Zero))
                        next = FixedPoint.Zero;

                    vx = next;
                    // 速度归零即摩擦失效（不变式：两者同时为零或同时非零）。
                    knockbackFriction = next == FixedPoint.Zero ? FixedPoint.Zero : friction;
                }
            }
            else if (tick.State == FighterLogicState.Standing)
            {
                // 地面步行（D65）：仅站立待机响应前后输入。
                x = GroundMovement.StepX(tick.State, fighter.PositionX, fighter.Facing, resolved, definition.Movement);
            }
            // PreJump / Crouching / 动作三阶段 / 倒地 / 起身：原地（速度归零，坐标不变）。

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
                BackHeld = resolved.Back,
                Health = fighter.Health,
                StunRemainingFrames = stunRemaining,
                MoveStartedFrame = tick.MoveStartedFrame,
                MoveStartedThisFrame = tick.ConsumedMatch is not null,
                KnockbackFriction = knockbackFriction,
                LandingKnockdownFrames = landingKnockdown,
                LandingWakeupFrames = landingWakeup,
                MoveElapsedFrames = tick.MoveElapsedFrames,
                HitstopRemainingFrames = 0, // 命中停顿只由结算写入（本帧未冻结）。
                // 投掷物生成掩码只属于当前攻击实例：换招/回到待机由状态机给出 0（见下方归一化）。
                ProjectileSpawnMask = tick.MoveStartedFrame == fighter.MoveStartedFrame
                    ? fighter.ProjectileSpawnMask
                    : 0,
            };
        }

        // ── 第 3 阶段：场地边界（D70）——双方候选统一钳制（含空中）。
        for (int i = 0; i < candidates.Length; i++)
            candidates[i].PositionX = _stage.ClampX(candidates[i].PositionX);

        // ── 第 4 阶段：双人最小间距（D71 修订：帧开始顺序约束 + 墙角残差再分配）。
        // 「双方都在地面」= 双方都不是空中状态（D94 统一判定：Airborne | AirHitstun）。
        bool bothGroundAtFrameStart =
            !state.Fighters[0].LogicState.IsAirborneLike() &&
            !state.Fighters[1].LogicState.IsAirborneLike();
        ApplySeparation(candidates, bothGroundAtFrameStart,
            state.Fighters[0].PositionX, state.Fighters[1].PositionX, frozenThisFrame);

        // ── 第 5 阶段：候选 → FighterState（结算前视图；原状态不受影响）。
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
                Health = candidates[i].Health,
                StunRemainingFrames = candidates[i].StunRemainingFrames,
                MoveStartedFrame = candidates[i].MoveStartedFrame,
                KnockbackFriction = candidates[i].KnockbackFriction,
                LandingKnockdownFrames = candidates[i].LandingKnockdownFrames,
                LandingWakeupFrames = candidates[i].LandingWakeupFrames,
                MoveElapsedFrames = candidates[i].MoveElapsedFrames,
                HitstopRemainingFrames = candidates[i].HitstopRemainingFrames,
                ProjectileSpawnMask = candidates[i].ProjectileSpawnMask,
            };

            // 命中/防御确认属于「当前攻击实例」：换招、被中断或回到待机后必须清空（D91）。
            nextFighters[i] = NormalizeConfirms(nextFighters[i]);
            // 落地计划只属于空中受击/倒地状态（D93）。
            nextFighters[i] = NormalizeLandingPlan(nextFighters[i]);
        }

        // ── 第 6 阶段：命中结算（D78/D79/D80/D81）——接触检测 + 双方同时判定（互不先手）
        //    + 攻击实例去重（同一发动对同一目标只结算一次，被防御亦算已结算）。
        IReadOnlyList<AttackContact> contacts = state.PendingThrow is not null ? Array.Empty<AttackContact>() : HitDetection.Detect(
            nextFighters[0], nextFighters[1], _roster.Fighter(0), _roster.Fighter(1),
            state.Fighters[0].LogicState, state.Fighters[1].LogicState);

        // ── 第 6a 阶段：攻击相杀（D98）——两个攻击实例的攻击框正面积重叠，
        //    且双方都<b>没有</b>打中对方受击框（攻击在空中对撞）。任一方真正命中的帧
        //    按 D80 正常结算（互中/单方命中），不判相杀：同一帧不能既相杀又命中。
        bool strikeClash = state.PendingThrow is null && contacts.Count == 0
            && FighterBoxes.HitboxesOverlap(
                nextFighters[0], _roster.Fighter(0), nextFighters[1], _roster.Fighter(1));

        // 结算前事件快照必须早于<b>任何</b>状态改写（D87 / D102）：ApplyStrikeClash 会把双方
        // 从 Active 直接改成 Recovery，若先改写再采集，首个 Active 帧相杀时两条 AttackActive
        // （以及投技尝试）会全部丢失。事件组装只读这两份快照，绝不回读改写后的状态。
        AttackActiveSnapshot[] attackActive = CollectAttackActive(nextFighters, _roster, frozenThisFrame);
        ThrowAttempt[] throwAttempts = CollectThrowAttempts(nextFighters, _roster, frozenThisFrame);

        if (strikeClash)
            ApplyStrikeClash(nextFighters, _roster);

        var outcomes = new CombatSettlement?[2];
        ApplyCombatSettlement(state.Frame, nextFighters, contacts,
            candidates[0].BackHeld, candidates[1].BackHeld, outcomes);

        // ── 第 6b 阶段：投技仲裁与结算（D97）——必须晚于打击结算：有效打击会中断投技。
        PendingThrow? pending = state.PendingThrow;
        ThrowResolution throwResolution;
        bool tech = false;
        if (pending is not null)
        {
            if (TechPressed(state, input, pending.Target))
            {
                tech = true;
                throwResolution = new ThrowResolution(false, false, pending.Thrower, pending.Target, pending.MoveId, 0, pending.Definition.ThrowId);
                for (int i = 0; i < nextFighters.Length; i++)
                    nextFighters[i] = ReleaseGrab(nextFighters[i]);
                SeparateTech(nextFighters);
                pending = null;
            }
            else if (state.Frame == pending.StartedFrame + 2)
            {
                var attempts = new ThrowAttempt[2];
                attempts[pending.Thrower] = new ThrowAttempt(true, false, pending.Target,
                    pending.Definition, pending.MoveId, true);
                throwResolution = ApplyThrows(state.Frame, nextFighters, attempts, outcomes, false,
                    allowPending: false, out _);
                pending = null;
            }
            else throwResolution = default;
        }
        else
        {
            throwResolution = ApplyThrows(state.Frame, nextFighters, throwAttempts, outcomes, strikeClash,
                allowPending: true, out pending);
            if (pending is not null && TechPressed(state, input, pending.Target))
            {
                tech = true;
                throwResolution = new ThrowResolution(false, false, pending.Thrower, pending.Target, pending.MoveId, 0, pending.Definition.ThrowId);
                for (int i = 0; i < nextFighters.Length; i++) nextFighters[i] = ReleaseGrab(nextFighters[i]);
                SeparateTech(nextFighters);
                pending = null;
            }
        }

        // ── 第 6c 阶段：飞行道具（D99）——已有实体移动/对撞/命中，随后本帧新生成的实体入列。
        //    放在近战与投技之后：近战优先占用目标本帧的受击资格（相杀/投技同理），
        //    投掷物作为独立实体仍然可以结算（不同来源互不取消），但每名目标每帧至多被
        //    一枚投掷物命中（按 ID 升序取第一枚）。
        ProjectileStep projectileStep = StepProjectiles(
            state.Frame, nextFighters, state.Projectiles, candidates, state.NextProjectileId);

        // Independent projectiles remain active. A hit to either participant cancels the unresolved grab.
        if (pending is not null && projectileStep.Events.Any(e => e.Type == BattleEventType.ProjectileHit))
            pending = null;

        // 攻击实例附属状态归一化（D100）：掩码属于「角色 + 发动帧」这一次攻击实例，
        // 不是角色本身。命中结算/投技结算/相杀会改写状态，必须在全部结算完成后统一核账：
        // 离开招式或换到新实例就清零，同一实例 Active → Recovery 则原样保留。
        for (int i = 0; i < nextFighters.Length; i++)
            nextFighters[i] = NormalizeLandingPlan(NormalizeSpawnMask(
                nextFighters[i], state.Fighters[i].MoveStartedFrame));

        // 连段收尾（D90）：恢复自由行动的角色清空旧记录（命中帧本身不会到这里——命中后必为受击状态）。
        UpdateCombos(nextFighters);

        // ── 第 7 阶段：KO 裁决（D89）——只依据<b>全部结算完成之后</b>的双方体力，
        //    双方同帧归零即 DoubleKO（不存在先手优势）；已结束的回合原样保留。
        RoundState nextRound = RoundState.FromFinalHealth(state.Round, nextFighters[0].Health, nextFighters[1].Health);

        // ── 第 7b 阶段：权威回合计时（09B，D107/D108）——计时器是 BattleState 的一部分，
        //    只有「成功推进一个进行中战斗逻辑帧」才递减 1（Step 成功即代表该帧已提交）。
        //    Hitstop 冻结帧仍是一个成功提交的逻辑帧，照常扣时；暂停/未提交/被丢弃的时间预算
        //    不会走到这里；终局冻结在 Step 开头已返回，因此已结束回合不再扣时。
        //
        //    顺序即语义（D108）：本帧的攻击/伤害/防御/投技/飞行道具/KO 已全部结算完成，
        //    只有回合仍进行中（本帧没有 KO/DoubleKO）且计时从 1 归零，才按剩余体力比例
        //    裁决 Time Over —— 绝不因为计时归零就取消同帧已经合法成立的攻击。
        int? timerAfter = state.RoundTimerRemainingFrames;
        bool timeOverThisFrame = false;
        if (timerAfter.HasValue)
        {
            int decremented = Math.Max(0, timerAfter.Value - 1);
            timerAfter = decremented;

            if (decremented == 0 && !nextRound.IsFinished)
            {
                FighterState p1 = nextFighters[0];
                FighterState p2 = nextFighters[1];
                RoundOutcome timeOutcome = TimeOverResolver.Resolve(
                    p1.Health, p1.MaxHealth, p2.Health, p2.MaxHealth);
                nextRound = RoundState.ForOutcome(timeOutcome,
                    timeOutcome == RoundOutcome.Draw
                        ? RoundEndReason.TimeOverDraw
                        : RoundEndReason.TimeOver);
                timeOverThisFrame = true;
            }
        }

        // ── 第 8 阶段：战斗事件（D82）——由权威结算产生，帧内顺序固定。
        IReadOnlyList<BattleEvent> events = CollectEvents(
            state.Frame, nextFighters, candidates, outcomes, attackActive,
            throwAttempts, throwResolution, strikeClash,
            projectileStep.Events, state.Round, nextRound, timeOverThisFrame, tech);

        // ── 第 9 阶段：朝向更新（D72）——基于结算后的最终状态与位置；
        //    被命中进入硬直的角色朝向冻结（非待机不转向）。
        for (int i = 0; i < nextFighters.Length; i++)
        {
            FighterState fighter = nextFighters[i];
            if (fighter.LogicState is not (FighterLogicState.Standing or FighterLogicState.Crouching))
                continue; // PreJump/Airborne/动作/硬直朝向冻结。

            FixedPoint opponentX = nextFighters[1 - i].PositionX;
            if (opponentX > fighter.PositionX)
                nextFighters[i] = fighter with { Facing = Facing.Right };
            else if (opponentX < fighter.PositionX)
                nextFighters[i] = fighter with { Facing = Facing.Left };
            // X 相等：保持原朝向（平局不抖动，D72）。
        }

        var nextState = new BattleState(frameAfter, nextFighters, state.StateVersion, nextRound,
            projectileStep.Projectiles, projectileStep.NextProjectileId, timerAfter, nextRound.IsFinished ? null : pending);

        var clashEvents = events.Where(e => e.Type == BattleEventType.ProjectileClash).ToArray();
        var clashPositions = projectileStep.Events.Where(e => e.Type == BattleEventType.ProjectileClash).ToArray();
        var anchors = new EventAnchor[clashEvents.Length];
        for (int i = 0; i < anchors.Length; i++)
            anchors[i] = new EventAnchor(clashEvents[i].EventId,
                clashPositions[i].AnchorX!.Value, clashPositions[i].AnchorY!.Value);
        return new BattleStepResult(state.Frame, frameAfter, input, nextState, contacts, events, anchors);
    }

    /// <summary>
    /// 命中结算（阶段 06B，D78/D79/D80）。
    ///
    /// <para><b>同时性（D80）</b>：先基于<b>同一份</b>结算前状态为双方各解析一个
    /// 命中结果，再统一应用——先处理的角色不会打断另一角色同帧成立的攻击
    /// （双方同帧互中时各自掉血并进入硬直，无相杀、无先手优势）。</para>
    ///
    /// <para><b>防御（D79）</b>：资格 = 帧末状态为站立/蹲下待机 + 按住「后」
    /// （输入按<b>帧开始朝向</b>解析，D46——帧末转身不回头解释本帧输入）。
    /// 高度表（D77）：High 只能站防、Low 只能蹲防、Mid 皆可。成功防御不掉血、
    /// 进入 Blockstun；否则命中：Health = max(0, Health − Damage) 并进入 Hitstun，
    /// 进行中的招式被中断（MoveId/阶段清空）。动作中（Startup/Active/Recovery）、
    /// PreJump 与已有硬直中<b>不能防御</b>。空中被击显式跳过（本阶段不做空中受击）。</para>
    ///
    /// <para>每次结算每名被击者至多应用一条接触（接触顺序由
    /// <see cref="HitDetection.Detect"/> 固定）；体力边界由 max(0, …) 保证，
    /// 不允许负数或溢出；同一命中只扣一次（重复命中抑制属 06C）。</para>
    /// </summary>
    private void ApplyCombatSettlement(
        int frame,
        FighterState[] fighters,
        IReadOnlyList<AttackContact> contacts,
        bool backHeldP1,
        bool backHeldP2,
        CombatSettlement?[] outcomes)
    {
        if (contacts.Count == 0)
            return;

        // 同时性：先解析双方结果（此时数组仍是结算前状态），再统一应用。
        // （CombatSettlement 含 string 引用，不可 stackalloc；接触帧才到达此处，2 元素分配可忽略。）
        for (int defender = 0; defender < fighters.Length; defender++)
            outcomes[defender] = ResolveSettlement(
                defender, fighters, contacts,
                defender == 0 ? backHeldP1 : backHeldP2);

        for (int defender = 0; defender < fighters.Length; defender++)
        {
            CombatSettlement? outcome = outcomes[defender];
            if (outcome is not CombatSettlement settled)
                continue;

            FighterState defenderState = fighters[defender];
            int attacker = 1 - defender;

            // 攻击者的确认记录（D91）：命中/防御确认只属于「当前攻击实例」。
            // 先于被击者状态写入，避免后面的 with 覆盖；实例身份由 MoveStartedFrame 校验。
            FighterState attackerState = fighters[attacker];
            if (attackerState.IsInMove && attackerState.MoveStartedFrame == settled.AttackStartFrame)
            {
                fighters[attacker] = settled.Blocked
                    ? attackerState with { BlockConfirmFrame = frame }
                    : attackerState with { HitConfirmFrame = frame };
            }

            // 连段（D90）：用<b>结算前</b>的状态判断目标是否仍在受击延续中；
            // 成功防御不增加命中数，并立即清空连段（防御方重新获得行动框架）。
            ComboState comboAfter = settled.Blocked
                ? ComboState.None
                : ComboState.AfterHit(defenderState.Combo, attacker, defenderState.LogicState.IsHitReaction());

            if (settled.Blocked)
            {
                fighters[defender] = defenderState with
                {
                    LogicState = defenderState.LogicState.IsAirborneLike() ? FighterLogicState.AirBlockstun : FighterLogicState.Blockstun,
                    MoveId = null,
                    PhaseRemainingFrames = 0,
                    StunRemainingFrames = settled.StunFrames,
                    // 防御击退（D92）：独立配置，默认 0；仍然遵守「速度与摩擦同时存在」不变式。
                    VelocityX = defenderState.LogicState.IsAirborneLike() ? defenderState.VelocityX : settled.KnockbackX,
                    VelocityY = defenderState.LogicState.IsAirborneLike() ? defenderState.VelocityY : FixedPoint.Zero,
                    KnockbackFriction = defenderState.LogicState.IsAirborneLike() ? FixedPoint.Zero : settled.KnockbackFriction,
                    // 被防御亦视为该攻击实例已结算（D81）：记录发动帧，抑制后续帧重复结算。
                    LastHitAttackStartFrame = settled.AttackStartFrame,
                    LastHitContactGroups = settled.ContactGroups,
                    // 被击者自身的动作被打断：攻击实例身份一并清除（防御者通常无动作，
                    // 但同帧互中时防御不成立，此处理覆盖所有路径）。
                    MoveStartedFrame = -1,
                    MoveElapsedFrames = 0, // 招式中断：动作时间随实例一并清除（D96）
                    // 被击者的攻击实例随之中断：确认帧属于旧实例，必须一并清空（D91 不变式）。
                    HitConfirmFrame = -1,
                    BlockConfirmFrame = -1,
                    ComboRecord = comboAfter,
                };
                continue; // 防御不会击飞：到此为止。
            }

            int healthAfter = defenderState.Health - settled.Damage;
            if (healthAfter < 0)
                healthAfter = 0; // 体力下限：不允许负数（结算显式钳制，不依赖下游校验）。

            // 事件/结算的唯一事实来源：本次<b>实际</b>扣除的体力
            // （攻击配置伤害可能被 max(0, …) 截断——Target 只剩 10 时 30 伤害只扣 10）。
            int appliedDamage = defenderState.Health - healthAfter;
            outcomes[defender] = settled with { Damage = appliedDamage };

            if (settled.Launch)
            {
                // 击飞 / 空中追击（D93）：进入空中受击，速度由<b>攻击配置</b>决定（方向远离攻击者）。
                // 地面目标当帧位置仍为 0——位置从下一帧起由空中积分推进（Validate 允许 Y=0 且 vy>0）。
                fighters[defender] = defenderState with
                {
                    LogicState = FighterLogicState.AirHitstun,
                    MoveId = null,
                    PhaseRemainingFrames = 0,
                    StunRemainingFrames = 0,
                    Health = healthAfter,
                    VelocityX = settled.LaunchVelocityX,
                    VelocityY = settled.LaunchVelocityY,
                    JumpKind = JumpKind.None, // 击飞不是跳跃：不携带跳跃类型
                    KnockbackFriction = FixedPoint.Zero,
                    LandingKnockdownFrames = settled.KnockdownFrames,
                    LandingWakeupFrames = settled.WakeupFrames,
                    LastHitAttackStartFrame = settled.AttackStartFrame,
                    LastHitContactGroups = settled.ContactGroups,
                    MoveStartedFrame = -1,
                    MoveElapsedFrames = 0, // 招式中断：动作时间随实例一并清除（D96）
                    HitConfirmFrame = -1,
                    BlockConfirmFrame = -1,
                    ComboRecord = comboAfter,
                };
            }
            else if (settled.AirHit)
            {
                // 空中被非击飞攻击命中（D93）：保持原轨迹继续下落，落地直接站起（无倒地计划）。
                fighters[defender] = defenderState with
                {
                    LogicState = FighterLogicState.AirHitstun,
                    MoveId = null,
                    PhaseRemainingFrames = 0,
                    StunRemainingFrames = 0,
                    Health = healthAfter,
                    VelocityX = defenderState.VelocityX,
                    VelocityY = defenderState.VelocityY,
                    JumpKind = JumpKind.None,
                    KnockbackFriction = FixedPoint.Zero,
                    LandingKnockdownFrames = 0,
                    LandingWakeupFrames = 0,
                    LastHitAttackStartFrame = settled.AttackStartFrame,
                    LastHitContactGroups = settled.ContactGroups,
                    MoveStartedFrame = -1,
                    MoveElapsedFrames = 0, // 招式中断：动作时间随实例一并清除（D96）
                    HitConfirmFrame = -1,
                    BlockConfirmFrame = -1,
                    ComboRecord = comboAfter,
                };
            }
            else
            {
                // 地面命中（D92）：进入受击硬直，可携带逐帧衰减的水平击退。
                fighters[defender] = defenderState with
                {
                    LogicState = FighterLogicState.Hitstun,
                    MoveId = null,
                    PhaseRemainingFrames = 0,
                    StunRemainingFrames = settled.StunFrames,
                    Health = healthAfter,
                    VelocityX = settled.KnockbackX,
                    VelocityY = FixedPoint.Zero,
                    KnockbackFriction = settled.KnockbackFriction,
                    LastHitAttackStartFrame = settled.AttackStartFrame,
                    LastHitContactGroups = settled.ContactGroups,
                    MoveStartedFrame = -1, // 招式中断（D78）：实例身份随 MoveId 一并清除。
                    MoveElapsedFrames = 0, // 动作时间随实例一并清除（D96）。
                    // 确认帧属于被中断的旧实例，必须一并清空（D91 不变式）。
                    HitConfirmFrame = -1,
                    BlockConfirmFrame = -1,
                    ComboRecord = comboAfter,
                };
            }
        }

        // 每人取自己收到的最大停顿；D81 去重保证每个攻击实例只结算一次。
        // 写入发生在全部状态应用之后，避免 with 覆盖。
        for (int defender = 0; defender < fighters.Length; defender++)
        {
            if (outcomes[defender] is not CombatSettlement settled || settled.Hitstop == default)
                continue;

            int attacker = 1 - defender;
            fighters[attacker] = fighters[attacker] with
            {
                HitstopRemainingFrames = Math.Max(
                    fighters[attacker].HitstopRemainingFrames, settled.Hitstop.AttackerFrames),
            };
            fighters[defender] = fighters[defender] with
            {
                HitstopRemainingFrames = Math.Max(
                    fighters[defender].HitstopRemainingFrames, settled.Hitstop.DefenderFrames),
            };
        }
    }

    /// <summary>
    /// 采集本帧的<b>投技尝试</b>（阶段 08B，D97）。必须在命中结算<b>之前</b>调用：
    /// 结算会把被打中的人送进 Hitstun，事后判断「能不能抓」会得到完全不同的答案。
    ///
    /// <para>资格 = 处于 Active + 招式配置了投技 + 当前经过帧在该投技的生效窗口 +
    /// 目标状态可投（<see cref="ThrowDefinition.AllowsTarget"/>，空中/倒地/起身一律不可）+
    /// 目标在投技者<b>面前</b>且水平距离 ≤ 范围 + 本实例尚未抓过该目标（D81 同构去重）。</para>
    /// </summary>
    private static ThrowAttempt[] CollectThrowAttempts(
        FighterState[] fighters, FightRoster roster, bool[] frozenThisFrame)
    {
        var attempts = new ThrowAttempt[fighters.Length];

        for (int i = 0; i < fighters.Length; i++)
        {
            if (frozenThisFrame[i])
                continue; // 冻结帧不推进动作时间，生效帧会重复成立。

            FighterState attacker = fighters[i];
            if (attacker.LogicState != FighterLogicState.Active || attacker.MoveId is null)
                continue;

            MoveDefinition? move = roster.Fighter(i).FindMoveById(attacker.MoveId);
            if (move is null || move.Throws.Count == 0)
                continue;

            int elapsed = move.ActiveFrames - attacker.PhaseRemainingFrames;

            ThrowDefinition? definition = null;
            int firstActive = int.MaxValue;
            for (int t = 0; t < move.Throws.Count; t++)
            {
                ThrowDefinition candidate = move.Throws[t];
                if (candidate.FirstActiveFrame < firstActive)
                    firstActive = candidate.FirstActiveFrame;
                if (definition is null
                    && elapsed >= candidate.FirstActiveFrame
                    && elapsed <= candidate.LastActiveFrame)
                {
                    definition = candidate;
                }
            }

            if (definition is null)
                continue;

            int target = 1 - i;
            FighterState defender = fighters[target];
            bool inRange = definition.AllowsTarget(defender.LogicState)
                && defender.LastThrowStartFrame != attacker.MoveStartedFrame
                && IsWithinThrowReach(attacker, defender, definition.Range);

            attempts[i] = new ThrowAttempt(
                Active: true,
                FirstActive: elapsed == firstActive,
                TargetIndex: target,
                Definition: definition,
                MoveId: attacker.MoveId,
                InRange: inRange);
        }

        return attempts;
    }

    /// <summary>
    /// 投技的几何资格（D97）：目标必须在投技者<b>面前</b>且水平中心距离 ≤ 范围。
    /// 位置完全重合时视为可抓（确定性的平局规则，与 D92 的「方向退化」同源）。
    /// </summary>
    private static bool IsWithinThrowReach(
        FighterState attacker, FighterState defender, FixedPoint range)
    {
        // 只有地面目标可投（状态由 ThrowDefinition.AllowsTarget 先筛过），高度必须贴地。
        if (defender.PositionY != FixedPoint.Zero)
            return false;

        FixedPoint dx = defender.PositionX - attacker.PositionX;
        FixedPoint distance = dx < FixedPoint.Zero ? -dx : dx;
        if (distance > range)
            return false;

        if (dx == FixedPoint.Zero)
            return true; // 完全重合：允许（平局规则固定）。

        bool inFront = attacker.Facing == Facing.Right
            ? dx > FixedPoint.Zero
            : dx < FixedPoint.Zero;
        return inFront;
    }

    /// <summary>
    /// 攻击相杀的<b>状态应用</b>（阶段 08C，D98）：双方的攻击实例同时被打断，直接进入
    /// 本招式的 <see cref="FighterLogicState.Recovery"/>（剩余帧 = 该招式的 RecoveryFrames）。
    ///
    /// <para><b>为什么是 Recovery</b>：它是既有状态里唯一「合法携带 MoveId/MoveStartedFrame、
    /// 不产生攻击框、走完就能回到待机重新行动」的状态——不需要新状态，也不会绕过 D62 的校验。
    /// 进入 Recovery 后 <c>GetActiveHitboxes</c> 立即为空，因此同一实例不会在下一次
    /// Active 帧重复相杀；确认帧一并清空（旧实例的确认不能带到新阶段）。</para>
    ///
    /// <para>相杀停顿取双方招式 <c>HitstopFrames</c> 的较大值（D96 同一套字段），
    /// 冻结期间不会再产生任何相杀/命中事件。</para>
    /// </summary>
    private static void ApplyStrikeClash(FighterState[] fighters, FightRoster roster)
    {
        int hitstop = 0;

        for (int i = 0; i < fighters.Length; i++)
        {
            FighterState fighter = fighters[i];
            if (fighter.LogicState != FighterLogicState.Active || fighter.MoveId is null)
                continue;

            MoveDefinition? move = roster.Fighter(i).FindMoveById(fighter.MoveId);
            if (move is null)
                continue;

            if (move.Attacks.Count == 0)
                continue;

            for (int a = 0; a < move.Attacks.Count; a++)
                hitstop = Math.Max(hitstop, move.Attacks[a].HitstopFrames);

            fighters[i] = fighter with
            {
                LogicState = FighterLogicState.Recovery,
                PhaseRemainingFrames = move.RecoveryFrames,
                HitConfirmFrame = -1,
                BlockConfirmFrame = -1,
            };
        }

        if (hitstop <= 0)
            return;

        for (int i = 0; i < fighters.Length; i++)
        {
            fighters[i] = fighters[i] with
            {
                HitstopRemainingFrames = Math.Max(fighters[i].HitstopRemainingFrames, hitstop),
            };
        }
    }

    /// <summary>
    /// 投技仲裁与结算（阶段 08B，D97）。<b>唯一冲突规则表</b>：
    /// <list type="number">
    /// <item>投技者本帧被<b>有效打击命中</b>（非防御）→ 投技被打断，不成立；</item>
    /// <item>目标本帧被有效打击命中 → 抓不住（避免同帧双重结算）；</item>
    /// <item>双方本帧都有有效投技 → <b>互相冲突，两边都不成立</b>（不固定 P1 先手），发一条 ThrowClash；</item>
    /// <item>否则该投技成立：扣血 → 目标倒地 → 沿「远离投技者」方向平移（D70 边界 + D73 间距）。</item>
    /// </list>
    /// </summary>
    private void SeparateTech(FighterState[] fighters)
    {
        int left = fighters[0].PositionX <= fighters[1].PositionX ? 0 : 1;
        int right = 1 - left;
        fighters[left] = fighters[left] with { PositionX = _stage.ClampX(fighters[left].PositionX - FixedPoint.FromInt(8)) };
        fighters[right] = fighters[right] with { PositionX = _stage.ClampX(fighters[right].PositionX + FixedPoint.FromInt(8)) };
    }

    private static bool TechPressed(BattleState state, FrameInput input, int target)
    {
        const InputBits chord = InputBits.LightPunch | InputBits.LightKick;
        InputBits now = input.ForPlayer(target);
        return (now & chord) == chord && (state.Fighter(target).RecognitionState.HeldAttacks & chord) != chord;
    }

    private static FighterState ReleaseGrab(FighterState fighter) => fighter with {
        LogicState = FighterLogicState.Standing, MoveId = null, PhaseRemainingFrames = 0,
        StunRemainingFrames = 0, MoveStartedFrame = -1, MoveElapsedFrames = 0,
        VelocityX = FixedPoint.Zero, VelocityY = FixedPoint.Zero, KnockbackFriction = FixedPoint.Zero,
        HitConfirmFrame = -1, BlockConfirmFrame = -1, ProjectileSpawnMask = 0,
        LandingKnockdownFrames = 0, LandingWakeupFrames = 0, ComboRecord = ComboState.None
    };

    private ThrowResolution ApplyThrows(
        int frame,
        FighterState[] fighters,
        ThrowAttempt[] attempts,
        CombatSettlement?[] outcomes,
        bool strikeClash,
        bool allowPending,
        out PendingThrow? pending)
    {
        pending = null;
        bool[] interrupted = new bool[fighters.Length];
        for (int i = 0; i < fighters.Length; i++)
        {
            // 被有效打击命中、或本帧攻击相杀（招式被迫中断）→ 投技不成立。
            interrupted[i] = strikeClash || outcomes[i] is CombatSettlement { Blocked: false };
        }

        bool[] valid = new bool[fighters.Length];
        for (int i = 0; i < fighters.Length; i++)
        {
            if (!attempts[i].Active || !attempts[i].InRange || interrupted[i])
                continue;
            if (interrupted[attempts[i].TargetIndex])
                continue;
            valid[i] = true;
        }

        if (valid[0] && valid[1])
            return new ThrowResolution(Clash: true, Hit: false, Thrower: 0, Target: 1, MoveId: null, Damage: 0);

        int thrower = valid[0] ? 0 : valid[1] ? 1 : -1;
        if (thrower < 0)
            return default;

        ThrowAttempt attempt = attempts[thrower];
        ThrowDefinition definition = attempt.Definition
            ?? throw new InvalidOperationException(
                $"帧 {frame}：投技仲裁内部错误（角色 {thrower} 的尝试缺少定义）。");

        int target = attempt.TargetIndex;
        FighterState attacker = fighters[thrower];
        FighterState defender = fighters[target];

        if (allowPending && definition.Techable)
        {
            pending = new PendingThrow(thrower, target, attacker.MoveStartedFrame, attempt.MoveId!, frame, definition);
            fighters[target] = defender with { LastThrowStartFrame = attacker.MoveStartedFrame };
            return default;
        }

        int healthAfter = defender.Health - definition.Damage;
        if (healthAfter < 0)
            healthAfter = 0;
        int appliedDamage = defender.Health - healthAfter;

        // 方向 = 远离投技者（与 D92 同一规则：权威相对位置，重合时退化为朝向）。
        int direction = defender.PositionX > attacker.PositionX ? 1
            : defender.PositionX < attacker.PositionX ? -1
            : (attacker.Facing == Facing.Right ? 1 : -1);

        FixedPoint push = direction > 0 ? definition.PushDistance : -definition.PushDistance;
        FixedPoint targetX = _stage.ClampX(defender.PositionX + push);

        // D73：不得因投技位移与投技者重叠（保持最小间距；仍受 D70 边界约束）。
        FixedPoint separation = _stage.MinimumFighterSeparation;
        if (targetX >= attacker.PositionX && targetX < attacker.PositionX + separation)
            targetX = _stage.ClampX(attacker.PositionX + separation);
        else if (targetX <= attacker.PositionX && targetX > attacker.PositionX - separation)
            targetX = _stage.ClampX(attacker.PositionX - separation);

        fighters[target] = defender with
        {
            LogicState = FighterLogicState.Knockdown,
            MoveId = null,
            PhaseRemainingFrames = 0,
            StunRemainingFrames = definition.KnockdownFrames,
            Health = healthAfter,
            PositionX = targetX,
            PositionY = FixedPoint.Zero,
            VelocityX = FixedPoint.Zero,
            VelocityY = FixedPoint.Zero,
            JumpKind = JumpKind.None,
            KnockbackFriction = FixedPoint.Zero,
            LandingKnockdownFrames = 0,
            LandingWakeupFrames = definition.WakeupFrames,
            MoveStartedFrame = -1,
            MoveElapsedFrames = 0,
            HitConfirmFrame = -1,
            BlockConfirmFrame = -1,
            // 被投记录：同一投技实例不会再抓第二次（D97 去重）。
            LastThrowStartFrame = attacker.MoveStartedFrame,
            // 连段（D90）：被投属于受击延续，沿用同一套连段记录。
            ComboRecord = ComboState.AfterHit(
                defender.Combo, thrower, defender.LogicState.IsHitReaction()),
        };

        // 投技者：成功抓取也算「命中确认」（可用于取消规则，D91 同源）。
        FighterState attackerAfter = fighters[thrower];
        if (attackerAfter.IsInMove && attackerAfter.MoveStartedFrame == attacker.MoveStartedFrame)
            fighters[thrower] = attackerAfter with { HitConfirmFrame = frame };

        // 命中停顿（D96）：投技同样可以配置停顿，双方同值。
        if (definition.HitstopFrames > 0)
        {
            fighters[thrower] = fighters[thrower] with
            {
                HitstopRemainingFrames = Math.Max(
                    fighters[thrower].HitstopRemainingFrames, definition.HitstopFrames),
            };
            fighters[target] = fighters[target] with
            {
                HitstopRemainingFrames = Math.Max(
                    fighters[target].HitstopRemainingFrames, definition.HitstopFrames),
            };
        }

        return new ThrowResolution(
            Clash: false, Hit: true, Thrower: thrower, Target: target,
            MoveId: attempt.MoveId, Damage: appliedDamage);
    }

    /// <summary>
    /// 飞行道具阶段（阶段 08D，D99）。顺序固定：
    /// <list type="number">
    /// <item>已有实体按自己的速度前进一帧、寿命 −1；寿命归零或越界（D70 的 X 边界 / 地面以下）直接移除；</item>
    /// <item><b>投掷物 × 投掷物</b>：不同发射者的实体正面积重叠 → 双方同时消失（同级抵消，无 P1 先手）；</item>
    /// <item><b>投掷物 × 角色</b>：重叠受击框即结算（命中或防御），实体消失；每名目标每帧至多被一枚命中（ID 升序）；</item>
    /// <item>本帧新生成的实体入列：<b>生成当帧只建立实体</b>，不运动、不判定（下一帧才开始）。</item>
    /// </list>
    ///
    /// <para>实体只依赖自身快照（位置/速度/寿命/来源身份），发射者进入收招、被击中或被投中都
    /// 不会删除已有实体——它们不读发射者当前状态。每枚实体最多结算一次（命中即消失）。</para>
    /// </summary>
    private ProjectileStep StepProjectiles(
        int frame,
        FighterState[] fighters,
        IReadOnlyList<ProjectileState> incoming,
        FighterCandidate[] candidates,
        int nextProjectileId)
    {
        var events = new List<ProjectileEvent>(4);
        var alive = new List<ProjectileState>(incoming.Count);

        // ① 运动 + 寿命 + 出界。
        for (int i = 0; i < incoming.Count; i++)
        {
            ProjectileState projectile = incoming[i];
            int life = projectile.RemainingLifetime - 1;
            if (life <= 0)
                continue;
            FixedPoint x = projectile.PositionX + projectile.VelocityX;
            FixedPoint y = projectile.PositionY + projectile.VelocityY;
            if (x < _stage.MinX || x > _stage.MaxX)
                continue;
            if (y < FixedPoint.Zero)
                continue;

            alive.Add(projectile with { PositionX = x, PositionY = y, RemainingLifetime = life });
        }

        // ② 投掷物 × 投掷物：不同发射者、正面积重叠 → 同归于尽。
        var definitions = new ProjectileDefinition[alive.Count];
        for (int i = 0; i < alive.Count; i++)
            definitions[i] = ResolveProjectile(alive[i]);

        var destroyed = new bool[alive.Count];
        for (int i = 0; i < alive.Count; i++)
        {
            for (int j = i + 1; j < alive.Count; j++)
            {
                if (destroyed[i] || destroyed[j])
                    continue;
                if (alive[i].OwnerFighterId == alive[j].OwnerFighterId)
                    continue;

                if (!ProjectileBox(alive[i], definitions[i]).Overlaps(ProjectileBox(alive[j], definitions[j])))
                    continue;

                destroyed[i] = true;
                destroyed[j] = true;
                events.Add(new ProjectileEvent(
                    BattleEventType.ProjectileClash,
                    Math.Min(alive[i].OwnerFighterId, alive[j].OwnerFighterId),
                    Math.Max(alive[i].OwnerFighterId, alive[j].OwnerFighterId),
                    MoveId: null,
                    Amount: 0,
                    AnchorX: (alive[i].PositionX + alive[j].PositionX) / FixedPoint.FromInt(2),
                    AnchorY: (alive[i].PositionY + alive[j].PositionY) / FixedPoint.FromInt(2)));
            }
        }

        // ③ 投掷物 × 角色：每名目标每帧至多一枚（按 ID 升序先到先得）。
        var survivors = new List<ProjectileState>(alive.Count);
        var alreadyHit = new bool[fighters.Length];
        for (int i = 0; i < alive.Count; i++)
        {
            if (destroyed[i])
                continue;

            ProjectileState projectile = alive[i];
            int target = 1 - projectile.OwnerFighterId;
            if (alreadyHit[target])
            {
                survivors.Add(projectile); // 本帧该目标已被另一枚命中：这枚继续飞（不消失）。
                continue;
            }

            ProjectileDefinition definition = definitions[i];
            FixedRect projectileBox = ProjectileBox(projectile, definition);
            if (!FighterBoxes.GetHurtboxes(fighters[target], _roster.Fighter(target)).Any(projectileBox.Overlaps))
            {
                survivors.Add(projectile);
                continue;
            }

            alreadyHit[target] = true;
            ApplyProjectileHit(
                frame, fighters, projectile, definition, candidates[target].BackHeld, events);
            // 命中或被防御后实体消失（不加入 survivors）——一次结算，不会重复扣血。
        }

        // ④ 本帧新生成的实体：只建立，不运动、不判定（D99）。
        for (int i = 0; i < fighters.Length; i++)
        {
            FighterState fighter = fighters[i];
            if (fighter.LogicState != FighterLogicState.Active || fighter.MoveId is null)
                continue;

            MoveDefinition? move = _roster.Fighter(i).FindMoveById(fighter.MoveId);
            if (move is null || move.Projectiles.Count == 0)
                continue;

            int elapsed = move.ActiveFrames - fighter.PhaseRemainingFrames;
            int mask = fighter.ProjectileSpawnMask;

            for (int k = 0; k < move.Projectiles.Count; k++)
            {
                ProjectileDefinition definition = move.Projectiles[k];
                if (elapsed != definition.SpawnAtActiveFrame)
                    continue;
                if ((mask & (1 << k)) != 0)
                    continue;

                // 无论是否因数量上限被拒绝，都要标记「本次发动已经处理过这一条」——
                // 命中停顿会冻结动作时间，否则同一帧会反复尝试生成。
                mask |= 1 << k;

                if (CountAlive(survivors, i, definition.ProjectileTypeId) >= definition.MaxAlive)
                    continue;

                if (nextProjectileId == int.MaxValue)
                    throw new InvalidOperationException(
                        $"帧 {frame}：投掷物 ID 空间耗尽（NextProjectileId 已达 int 上限）。");

                FixedPoint forwardSign = fighter.Facing == Facing.Right
                    ? FixedPoint.One
                    : -FixedPoint.One;

                survivors.Add(new ProjectileState(
                    ProjectileId: nextProjectileId++,
                    OwnerFighterId: i,
                    TypeId: definition.ProjectileTypeId,
                    SourceMoveId: fighter.MoveId,
                    PositionX: _stage.ClampX(fighter.PositionX + forwardSign * definition.Hit.OffsetForward),
                    PositionY: fighter.PositionY + definition.Hit.OffsetUp,
                    VelocityX: forwardSign * definition.SpeedX,
                    VelocityY: definition.SpeedY,
                    RemainingLifetime: definition.LifetimeFrames));
            }

            fighters[i] = fighter with { ProjectileSpawnMask = mask };
        }

        return new ProjectileStep(survivors, events, nextProjectileId);
    }

    /// <summary>场上（含本帧新生成）属于该发射者、该类型的实体数量（数量上限判定用）。</summary>
    private static int CountAlive(List<ProjectileState> projectiles, int owner, string typeId)
    {
        int count = 0;
        for (int i = 0; i < projectiles.Count; i++)
        {
            if (projectiles[i].OwnerFighterId == owner
                && string.Equals(projectiles[i].TypeId, typeId, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    /// <summary>投掷物的权威碰撞盒（中心 ± 半宽/半高，全部定点数）。</summary>
    private static FixedRect ProjectileBox(ProjectileState projectile, ProjectileDefinition definition) =>
        new(
            projectile.PositionX - definition.Hit.HalfWidth,
            projectile.PositionX + definition.Hit.HalfWidth,
            projectile.PositionY - definition.Hit.HalfHeight,
            projectile.PositionY + definition.Hit.HalfHeight);

    /// <summary>按来源身份解析投掷物定义（配置缺失立即失败，不静默跳过）。</summary>
    private ProjectileDefinition ResolveProjectile(ProjectileState projectile)
    {
        MoveDefinition? move = _roster.Fighter(projectile.OwnerFighterId).FindMoveById(projectile.SourceMoveId);
        if (move is not null)
        {
            for (int i = 0; i < move.Projectiles.Count; i++)
            {
                if (string.Equals(move.Projectiles[i].ProjectileTypeId, projectile.TypeId, StringComparison.Ordinal))
                    return move.Projectiles[i];
            }
        }

        throw new InvalidOperationException(
            $"投掷物 {projectile.ProjectileId} 的类型 {projectile.TypeId}" +
            $"（来源招式 {projectile.SourceMoveId}）在角色 {projectile.OwnerFighterId} 的配置中不存在。");
    }

    // ── D103：远程命中停顿的唯一入口（命中 / 成功防御共用）────────────────
    /// <summary>
    /// 远程命中停顿（阶段 08 最终补修，D103）：飞行道具触发的 Hitstop <b>只作用于被命中或
    /// 成功防御的目标</b>，绝不自动冻结远处的发射者（D99 的远程停顿语义）。
    ///
    /// <para>命中与成功防御共用本方法，避免两个分支再次分叉：<c>frames = 0</c> 不产生停顿；
    /// 已有更长的停顿用 <see cref="Math.Max(int, int)"/> 保留，不覆盖成更小值；冻结期间
    /// 不重复消费攻击、Blockstun 不提前计时（由 D96 冻结分支统一保证）。</para>
    /// </summary>
    private static void ApplyRemoteHitstop(FighterState[] fighters, int target, int frames)
    {
        if (frames <= 0)
            return;
        fighters[target] = fighters[target] with
        {
            HitstopRemainingFrames = Math.Max(fighters[target].HitstopRemainingFrames, frames),
        };
    }

    /// <summary>
    /// 投掷物命中/被防御的结算（D99）：与前程打击共用同一套防御表（D77）、击退/击飞（D92/D93）
    /// 与实扣伤害语义；<b>命中与成功防御都按配置停顿，且只作用于被击者</b>。
    /// </summary>
    private void ApplyProjectileHit(
        int frame,
        FighterState[] fighters,
        ProjectileState projectile,
        ProjectileDefinition definition,
        bool backHeld,
        List<ProjectileEvent> events)
    {
        int target = 1 - projectile.OwnerFighterId;
        FighterState defender = fighters[target];
        bool defenderAirborne = defender.LogicState.IsAirborneLike();

        bool guarding = CanGuard(defender.LogicState, backHeld, definition.Hit.Height);

        // 方向 = 投掷物自己的飞行方向（不是「远离发射者」——弹可能已经飞过发射者）。
        int direction = projectile.VelocityX >= FixedPoint.Zero ? 1 : -1;

        if (guarding)
        {
            FixedPoint knockback = definition.Hit.BlockKnockback;
            FixedPoint knockbackX = direction > 0 ? knockback : -knockback;
            FixedPoint friction = knockback == FixedPoint.Zero
                ? FixedPoint.Zero
                : definition.Hit.GroundKnockbackFriction;

            fighters[target] = defender with
            {
                LogicState = defenderAirborne ? FighterLogicState.AirBlockstun : FighterLogicState.Blockstun,
                MoveId = null,
                PhaseRemainingFrames = 0,
                StunRemainingFrames = definition.Hit.BlockstunFrames,
                VelocityX = defenderAirborne ? defender.VelocityX : knockbackX,
                VelocityY = defenderAirborne ? defender.VelocityY : FixedPoint.Zero,
                KnockbackFriction = defenderAirborne ? FixedPoint.Zero : friction,
                MoveStartedFrame = -1,
                MoveElapsedFrames = 0,
                ProjectileSpawnMask = 0,
                HitConfirmFrame = -1,
                BlockConfirmFrame = -1,
            };

            events.Add(new ProjectileEvent(BattleEventType.ProjectileBlocked,
                projectile.OwnerFighterId, target, projectile.TypeId, definition.Hit.BlockstunFrames));
            // 成功防御与命中走同一个停顿入口（D103）：只冻结目标，不冻结远处发射者。
            ApplyRemoteHitstop(fighters, target, definition.Hit.HitstopFrames);
            return;
        }

        int healthAfter = defender.Health - definition.Hit.Damage;
        if (healthAfter < 0)
            healthAfter = 0;
        int appliedDamage = defender.Health - healthAfter;

        ComboState comboAfter = ComboState.AfterHit(
            defender.Combo, projectile.OwnerFighterId, defender.LogicState.IsHitReaction());

        if (definition.Hit.Launches)
        {
            fighters[target] = defender with
            {
                LogicState = FighterLogicState.AirHitstun,
                MoveId = null,
                PhaseRemainingFrames = 0,
                StunRemainingFrames = 0,
                Health = healthAfter,
                VelocityX = direction > 0
                    ? definition.Hit.LaunchVelocityX
                    : -definition.Hit.LaunchVelocityX,
                VelocityY = definition.Hit.LaunchVelocityY,
                JumpKind = JumpKind.None,
                KnockbackFriction = FixedPoint.Zero,
                LandingKnockdownFrames = definition.Hit.KnockdownFrames,
                LandingWakeupFrames = definition.Hit.WakeupFrames,
                MoveStartedFrame = -1,
                MoveElapsedFrames = 0,
                ProjectileSpawnMask = 0,
                HitConfirmFrame = -1,
                BlockConfirmFrame = -1,
                ComboRecord = comboAfter,
            };
        }
        else if (defenderAirborne)
        {
            fighters[target] = defender with
            {
                LogicState = FighterLogicState.AirHitstun,
                MoveId = null,
                PhaseRemainingFrames = 0,
                StunRemainingFrames = 0,
                Health = healthAfter,
                VelocityX = defender.VelocityX,
                VelocityY = defender.VelocityY,
                JumpKind = JumpKind.None,
                KnockbackFriction = FixedPoint.Zero,
                LandingKnockdownFrames = 0,
                LandingWakeupFrames = 0,
                MoveStartedFrame = -1,
                MoveElapsedFrames = 0,
                ProjectileSpawnMask = 0,
                HitConfirmFrame = -1,
                BlockConfirmFrame = -1,
                ComboRecord = comboAfter,
            };
        }
        else
        {
            FixedPoint knockback = definition.Hit.GroundKnockback;
            FixedPoint knockbackX = direction > 0 ? knockback : -knockback;
            FixedPoint friction = knockback == FixedPoint.Zero
                ? FixedPoint.Zero
                : definition.Hit.GroundKnockbackFriction;

            fighters[target] = defender with
            {
                LogicState = FighterLogicState.Hitstun,
                MoveId = null,
                PhaseRemainingFrames = 0,
                StunRemainingFrames = definition.Hit.HitstunFrames,
                Health = healthAfter,
                VelocityX = knockbackX,
                VelocityY = FixedPoint.Zero,
                KnockbackFriction = friction,
                MoveStartedFrame = -1,
                MoveElapsedFrames = 0,
                ProjectileSpawnMask = 0,
                HitConfirmFrame = -1,
                BlockConfirmFrame = -1,
                ComboRecord = comboAfter,
            };
        }

        // 远程停顿只作用于被击者（D96 同一套字段；不冻结发射者）。
        ApplyRemoteHitstop(fighters, target, definition.Hit.HitstopFrames);

        events.Add(new ProjectileEvent(BattleEventType.ProjectileHit,
            projectile.OwnerFighterId, target, projectile.TypeId, appliedDamage));
    }

    /// <summary>
    /// 命中的连段记录归一化（阶段 07B，D90）：<b>帧末</b>状态已不在受击延续中的角色，
    /// 清空旧连段——「恢复自由行动」的判定落在帧末（含硬直归零转待机的那一帧）。
    /// </summary>
    private static void UpdateCombos(FighterState[] fighters)
    {
        for (int i = 0; i < fighters.Length; i++)
        {
            FighterState fighter = fighters[i];
            if (fighter.Combo.IsActive && !fighter.LogicState.IsHitReaction())
                fighters[i] = fighter with { ComboRecord = ComboState.None };
        }
    }

    // ── D100：攻击实例附属状态的唯一清理入口 ────────────────────────────────
    /// <summary>
    /// 攻击实例附属状态归一化（阶段 08 最终补修，D100）。
    ///
    /// <para><c>ProjectileSpawnMask</c> 记录的是<b>这一次攻击实例</b>（角色 + 发动帧）已经生成过
    /// 哪些投掷物。命中结算（Hitstun / AirHitstun / Blockstun）与投技结算（Knockdown）会把角色
    /// 直接改出招式状态，但只清 <c>MoveId</c> / <c>MoveStartedFrame</c> 并不足以让掩码归零——
    /// <see cref="FighterState.Validate"/> 明确拒绝「非动作状态携带生成掩码」，整帧会因此失败。</para>
    ///
    /// <para>规则（唯一入口，不做每帧无条件清零）：掩码非零时，只要帧末<b>不在招式中</b>，
    /// 或<b>发动帧已不同于帧开始</b>（取消到新实例），就清零；否则属于同一实例的正常阶段
    /// 转换（Active → Recovery），必须保留，使尚未触发的生成配置仍可生效且不会重复生成。</para>
    ///
    /// <para>本方法只改 <c>FighterState</c> 的掩码：已生成的 <see cref="ProjectileState"/>、
    /// <c>NextProjectileId</c> 与实体身份（Owner / SourceMoveId / ID）一概不动。</para>
    /// </summary>
    private static FighterState NormalizeSpawnMask(FighterState fighter, int instanceStartFrame)
    {
        if (fighter.ProjectileSpawnMask == 0)
            return fighter;
        if (!fighter.IsInMove || fighter.MoveStartedFrame != instanceStartFrame)
            return fighter with { ProjectileSpawnMask = 0 };
        return fighter;
    }

    /// <summary>
    /// 落地计划归一化（阶段 07C，D93）：只有 <see cref="FighterLogicState.AirHitstun"/> 可以携带
    /// 完整的落地计划；<see cref="FighterLogicState.Knockdown"/> 只保留待用的起身帧数；
    /// 其余状态必须为 0（落地/起身完成后立即清空，防止旧计划污染下一次击飞）。
    /// </summary>
    private static FighterState NormalizeLandingPlan(FighterState fighter)
    {
        if (fighter.LogicState == FighterLogicState.AirHitstun)
            return fighter;

        bool cleared = fighter.LandingKnockdownFrames == 0 && fighter.LandingWakeupFrames == 0;
        if (fighter.LogicState == FighterLogicState.Knockdown)
            return fighter.LandingKnockdownFrames == 0 ? fighter : fighter with { LandingKnockdownFrames = 0 };

        return cleared ? fighter : fighter with { LandingKnockdownFrames = 0, LandingWakeupFrames = 0 };
    }

    /// <summary>
    /// 命中/防御确认帧归一化（阶段 07B，D91）：确认只属于当前攻击实例。
    /// 非动作状态一律清空；动作中若确认帧早于当前发动帧（换招）也清空。
    /// </summary>
    private static FighterState NormalizeConfirms(FighterState fighter)
    {
        if (!fighter.IsInMove)
        {
            return fighter.HitConfirmFrame == -1 && fighter.BlockConfirmFrame == -1
                ? fighter
                : fighter with { HitConfirmFrame = -1, BlockConfirmFrame = -1 };
        }

        int hit = fighter.HitConfirmFrame >= fighter.MoveStartedFrame ? fighter.HitConfirmFrame : -1;
        int block = fighter.BlockConfirmFrame >= fighter.MoveStartedFrame ? fighter.BlockConfirmFrame : -1;

        return hit == fighter.HitConfirmFrame && block == fighter.BlockConfirmFrame
            ? fighter
            : fighter with { HitConfirmFrame = hit, BlockConfirmFrame = block };
    }

    /// <summary>单名被击者的结算解析（不修改状态；null = 本帧不被结算）。</summary>
    private CombatSettlement? ResolveSettlement(
        int defender,
        FighterState[] fighters,
        IReadOnlyList<AttackContact> contacts,
        bool backHeld)
    {
        FighterState attackerState = fighters[1 - defender];
        if (attackerState.MoveStartedFrame < 0 || attackerState.MoveId is not { } moveId ||
            _roster.Fighter(1 - defender).FindMoveById(moveId) is not { } move)
            return null;

        FighterState defenderState = fighters[defender];
        bool sameInstance = attackerState.MoveStartedFrame == defenderState.LastHitAttackStartFrame;
        uint priorGroups = sameInstance ? defenderState.LastHitContactGroups : 0;
        // Detect 的顺序决定本帧优先级。同组已结算时仍检查后续独立组。
        foreach (AttackContact contact in contacts)
        {
            if (contact.TargetId != defender || contact.MoveId != moveId || contact.AttackIndex >= move.Attacks.Count)
                continue;
            AttackDefinition attack = move.Attacks[contact.AttackIndex];
            uint group = attack.ContactGroup == 0 ? 0 : 1u << (attack.ContactGroup - 1);
            if (sameInstance && (group == 0 || (priorGroups & group) != 0))
                continue;
            return ResolveAttack(attack, moveId, attackerState, defenderState, backHeld, priorGroups, group);
        }
        return null;
    }

    private static CombatSettlement ResolveAttack(AttackDefinition attack, string moveId,
        FighterState attackerState, FighterState defenderState, bool backHeld, uint priorGroups, uint group)
    {
        // 防御资格（D79）：站立/蹲下待机 + 按住「后」（帧开始朝向解析的相对输入）。
        // D131：正常 Airborne 可防 High/Mid，AirHitstun 与动作状态没有防御资格。
        bool guarding = CanGuard(defenderState.LogicState, backHeld, attack.Height) ||
            (priorGroups != 0 && defenderState.LogicState == FighterLogicState.Blockstun && attack.Height == AttackHeight.Mid);

        // 击退/击飞方向（D92/D93）：按<b>命中时双方的权威相对位置</b>决定「远离攻击者」；
        // 位置完全相同时退化为攻击者的朝向（确定性平局规则）。
        int direction = defenderState.PositionX > attackerState.PositionX ? 1
            : defenderState.PositionX < attackerState.PositionX ? -1
            : (attackerState.Facing == Facing.Right ? 1 : -1);

        FixedPoint knockback = guarding ? attack.BlockKnockback : attack.GroundKnockback;
        FixedPoint knockbackX = direction > 0 ? knockback : -knockback;
        FixedPoint friction = knockback == FixedPoint.Zero
            ? FixedPoint.Zero
            : attack.GroundKnockbackFriction;

        if (guarding)
        {
            return new CombatSettlement(
                Blocked: true, Damage: 0, StunFrames: attack.BlockstunFrames,
                MoveId: moveId, AttackStartFrame: attackerState.MoveStartedFrame,
                ContactGroups: priorGroups | group,
                AirHit: false, Launch: false,
                KnockbackX: knockbackX, KnockbackFriction: friction,
                LaunchVelocityX: FixedPoint.Zero, LaunchVelocityY: FixedPoint.Zero,
                KnockdownFrames: 0, WakeupFrames: 0,
                Hitstop: attack.ResolveMeleeHitstop(blocked: true));
        }

        bool launches = attack.Launches;
        int damage = priorGroups != 0 && attack.DamageAfterPriorContact is { } afterContact ? afterContact
            : defenderState.LogicState is FighterLogicState.Startup or FighterLogicState.Active
                ? attack.CounterDamage ?? attack.Damage : attack.Damage;
        return new CombatSettlement(
            Blocked: false, Damage: damage, StunFrames: attack.HitstunFrames,
            MoveId: moveId, AttackStartFrame: attackerState.MoveStartedFrame,
            ContactGroups: priorGroups | group,
            AirHit: defenderState.LogicState.IsAirborneLike(), Launch: launches,
            KnockbackX: launches ? FixedPoint.Zero : knockbackX,
            KnockbackFriction: launches ? FixedPoint.Zero : friction,
            LaunchVelocityX: launches ? (direction > 0 ? attack.LaunchVelocityX : -attack.LaunchVelocityX)
                : FixedPoint.Zero,
            LaunchVelocityY: launches ? attack.LaunchVelocityY : FixedPoint.Zero,
            KnockdownFrames: launches ? attack.KnockdownFrames : 0,
            WakeupFrames: launches ? attack.WakeupFrames : 0,
            Hitstop: attack.ResolveMeleeHitstop(blocked: false));
    }

    /// <summary>防御高度表（D77）：High 只能站防、Low 只能蹲防、Mid 皆可。</summary>
    private static bool CanGuard(FighterLogicState state, bool backHeld, AttackHeight height) =>
        backHeld && (state == FighterLogicState.Airborne
            ? height is AttackHeight.High or AttackHeight.Mid
            : state is FighterLogicState.Standing or FighterLogicState.Crouching && GuardCovers(height, state));

    private static bool GuardCovers(AttackHeight height, FighterLogicState guardState) => height switch
    {
        AttackHeight.High => guardState == FighterLogicState.Standing,
        AttackHeight.Low => guardState == FighterLogicState.Crouching,
        _ => true, // Mid：站防与蹲防皆可。
    };

    /// <summary>
    /// 结算前的<b>攻击生效快照</b>（D87）：本帧是否进入生效窗 + 该次发动的招式身份。
    ///
    /// <para>招式身份必须在结算前取——命中结算会把被中断者的 <c>MoveId</c> 清空，
    /// 事后读 <see cref="FighterState.MoveId"/> 会得到 null（同帧互中时两个事件全丢负载）。
    /// 快照是纯值数组，不共享任何可变状态，也不写回权威快照。</para>
    /// </summary>
    /// <param name="IsActive">本帧是否是该攻击实例的首个生效帧。</param>
    /// <param name="MoveId">该次发动的招式 ID（IsActive 时保证非空）。</param>
    private readonly record struct AttackActiveSnapshot(bool IsActive, string? MoveId);

    /// <summary>本帧某个角色的投技尝试（阶段 08B，D97；结算前采集）。</summary>
    private readonly record struct ThrowAttempt(
        bool Active,
        bool FirstActive,
        int TargetIndex,
        ThrowDefinition? Definition,
        string? MoveId,
        bool InRange);

    /// <summary>本帧飞行道具的一条事件（D99），在事件收集阶段按发生顺序写入事件流。</summary>
    private readonly record struct ProjectileEvent(
        BattleEventType Type,
        int Source,
        int Target,
        string? MoveId,
        int Amount,
        FixedPoint? AnchorX = null,
        FixedPoint? AnchorY = null);

    /// <summary>本帧飞行道具阶段的输出（D99）：存活实体、事件、下一个可分配 ID。</summary>
    private readonly record struct ProjectileStep(
        IReadOnlyList<ProjectileState> Projectiles,
        List<ProjectileEvent> Events,
        int NextProjectileId);

    /// <summary>本帧投技的最终仲裁结果（D97）。</summary>
    private readonly record struct ThrowResolution(
        bool Clash,
        bool Hit,
        int Thrower,
        int Target,
        string? MoveId,
        int Damage,
        string? ThrowId = null);

    /// <summary>
    /// 计算本帧「攻击实例首个生效帧」快照（D82 的 AttackActive 语义来源）。
    ///
    /// <para>必须在<b>结算之前</b>用结算前状态调用：本帧生效的攻击即使在同帧命中结算中
    /// 被打断（进入 Hitstun），其生效事实与招式身份都已经发生，事件不得丢失。</para>
    ///
    /// <para>语义 = <b>该次发动的首个生效帧</b>：本帧有至少一个攻击定义进入生效窗口
    /// （<c>elapsed == min(FirstActiveFrame)</c>），且下一次发动（新 MoveStarted）会重新计数。
    /// 一个攻击实例至多产生一条 AttackActive——多攻击定义、错开窗口也只发一次。</para>
    /// </summary>
    private static AttackActiveSnapshot[] CollectAttackActive(
        FighterState[] fighters, FightRoster roster, bool[] frozenThisFrame)
    {
        var snapshots = new AttackActiveSnapshot[fighters.Length];

        for (int i = 0; i < fighters.Length; i++)
        {
            // 冻结帧不推进动作时间，因此「首个生效帧」会重复成立——必须跳过（D96）。
            if (frozenThisFrame[i])
                continue;

            FighterState fighter = fighters[i];
            if (fighter.LogicState != FighterLogicState.Active || fighter.MoveId is null)
                continue;

            MoveDefinition? move = roster.Fighter(i).FindMoveById(fighter.MoveId);
            if (move is null || move.Attacks.Count == 0)
                continue;

            int elapsed = move.ActiveFrames - fighter.PhaseRemainingFrames;

            int firstActive = int.MaxValue;
            for (int a = 0; a < move.Attacks.Count; a++)
            {
                int first = move.Attacks[a].FirstActiveFrame;
                if (first < firstActive)
                    firstActive = first;
            }

            if (elapsed == firstActive)
                snapshots[i] = new AttackActiveSnapshot(IsActive: true, fighter.MoveId);
        }

        return snapshots;
    }

    /// <summary>
    /// 收集本帧战斗事件（D82）：帧内顺序按权威阶段固定——
    /// ① MoveStarted（攻击者 ID 升序）；② AttackActive（攻击者 ID 升序，每次发动仅首个生效帧）；
    /// ③ 逐被击者（ID 升序）：Hit → HitstunStarted 或 Blocked → BlockstunStarted；
    /// ④ 回合终局（D89）：Knockout 或 DoubleKnockout → RoundEnded（只在本帧刚结束时产生）。
    /// EventId = Frame × 16 + 帧内序号（帧号或帧内事件数超上限时显式失败）。
    /// </summary>
    /// <param name="attackActive">结算前快照（见 <see cref="CollectAttackActive"/>）；
    /// AttackActive 的招式身份只从这里取，绝不回读结算后的状态。</param>
    /// <param name="previousRound">帧开始的回合状态（判断「本帧刚结束」）。</param>
    /// <param name="nextRound">结算后的回合状态（事件负载的唯一来源）。</param>
    /// <param name="timeOverThisFrame">
    /// 本帧是否由计时耗尽完成回合裁决（09B，D109）。true 时在 RoundEnded 之前发一条
    /// <c>TimeOver</c>；与 KO 互斥（同帧 KO 以 KO 为准，本标志恒 false）。
    /// </param>
    private static IReadOnlyList<BattleEvent> CollectEvents(
        int frame,
        FighterState[] fighters,
        FighterCandidate[] candidates,
        CombatSettlement?[] outcomes,
        AttackActiveSnapshot[] attackActive,
        ThrowAttempt[] throwAttempts,
        ThrowResolution throwResolution,
        bool strikeClash,
        List<ProjectileEvent> projectileEvents,
        RoundState previousRound,
        RoundState nextRound,
        bool timeOverThisFrame,
        bool throwTech = false)
    {
        List<BattleEvent>? events = null;
        int seq = 0;

        void Add(BattleEventType type, int source, int target, string? moveId, int amount)
        {
            if (frame > BattleEvent.MaxFrameForEventIds)
                throw new InvalidOperationException(
                    $"帧 {frame} 超过 EventId 可用上限 {BattleEvent.MaxFrameForEventIds}" +
                    $"（Frame × {BattleEvent.MaxEventsPerFrame} 会溢出 int）。");
            if (seq >= BattleEvent.MaxEventsPerFrame)
                throw new InvalidOperationException(
                    $"帧 {frame} 的事件数超过每帧上限 {BattleEvent.MaxEventsPerFrame}（EventId 空间不足）。");
            // 生效事件必须可追溯招式：标记为生效却没有身份，说明快照取错了（D87 不变式）。
            if (type == BattleEventType.AttackActive && string.IsNullOrEmpty(moveId))
                throw new InvalidOperationException(
                    $"帧 {frame}：AttackActive 缺少招式身份（结算前快照为空）。" +
                    "生效事件不允许用 null MoveId 表示。");
            if (type is BattleEventType.ThrowAttempt or BattleEventType.ThrowHit
                && string.IsNullOrEmpty(moveId))
                throw new InvalidOperationException(
                    $"帧 {frame}：{type} 缺少招式身份（投技事件必须可追溯到招式）。");
            events ??= new List<BattleEvent>(8);
            events.Add(new BattleEvent(frame, frame * BattleEvent.MaxEventsPerFrame + seq, type, source, target, moveId, amount));
            seq++;
        }

        // ① MoveStarted。
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].MoveStartedThisFrame)
                Add(BattleEventType.MoveStarted, i, i, candidates[i].MoveId, 0);
        }

        // ② AttackActive（每次发动的首个生效帧；标记与招式身份都来自结算前快照）。
        for (int i = 0; i < fighters.Length; i++)
        {
            if (attackActive[i].IsActive)
                Add(BattleEventType.AttackActive, i, i, attackActive[i].MoveId, 0);
        }

        // ③ 投技尝试（每次发动的首个生效帧；与 AttackActive 同构，冻结帧已跳过）。
        for (int i = 0; i < fighters.Length; i++)
        {
            if (throwAttempts[i].Active && throwAttempts[i].FirstActive)
                Add(BattleEventType.ThrowAttempt, i, throwAttempts[i].TargetIndex, throwAttempts[i].MoveId, 0);
        }

        // ④ 逐被击者：命中/防御 + 硬直开始。
        for (int defender = 0; defender < fighters.Length; defender++)
        {
            if (outcomes[defender] is not CombatSettlement settled)
                continue;

            int attacker = 1 - defender;
            if (settled.Blocked)
            {
                Add(BattleEventType.Blocked, attacker, defender, settled.MoveId, 0);
                Add(BattleEventType.BlockstunStarted, attacker, defender, settled.MoveId, settled.StunFrames);
            }
            else
            {
                Add(BattleEventType.Hit, attacker, defender, settled.MoveId, settled.Damage);
                Add(BattleEventType.HitstunStarted, attacker, defender, settled.MoveId, settled.StunFrames);
            }
        }

        // ⑤ 攻击相杀（D98）：双方攻击在空中对撞 → 一条事件，无伤害。
        if (strikeClash)
            Add(BattleEventType.StrikeClash, 0, 1, null, 0);

        // ⑥ 投技结果（D97）：成功一条 ThrowHit（Amount = 实扣伤害）；双方互投则一条 ThrowClash。
        //    每帧至多一条（1v1 下投技冲突只有「互相抓」一种，两边都不成立）。
        if (throwTech)
        {
            Add(BattleEventType.ThrowTech, throwResolution.Target, throwResolution.Thrower, throwResolution.MoveId, 0);
            events![^1] = events[^1] with { ThrowId = throwResolution.ThrowId };
        }
        else if (throwResolution.Clash)
            Add(BattleEventType.ThrowClash, 0, 1, null, 0);
        else if (throwResolution.Hit)
            Add(BattleEventType.ThrowHit, throwResolution.Thrower, throwResolution.Target,
                throwResolution.MoveId, throwResolution.Damage);

        // ⑦ 飞行道具事件（D99）：对撞 / 命中 / 被防御，按发生顺序（ID 升序）追加。
        for (int i = 0; i < projectileEvents.Count; i++)
        {
            ProjectileEvent e = projectileEvents[i];
            Add(e.Type, e.Source, e.Target, e.MoveId, e.Amount);
        }

        // ⑧ 回合终局（D89 / 09B D109）：只在「本帧刚结束」时产生一次。
        //    事件顺序固定：KO/DoubleKO 或 TimeOver（互斥）→ RoundEnded。
        //    - KO：Knockout(胜者→败者) 或 DoubleKnockout(0→1)，绝不发 TimeOver；
        //    - Time Over：只发一条 TimeOver，绝不把超时胜利伪装成 Knockout；
        //    - 两者同帧时 D108 已保证以 KO 为准（timeOverThisFrame = false）。
        //    RoundEnded.Amount = (int)RoundOutcome，是消费方判断胜负的唯一依据；
        //    TimeOverDraw 的 Amount = (int)Draw，且 WinnerFighterId = null（不取 .Value）。
        if (!previousRound.IsFinished && nextRound.IsFinished)
        {
            if (timeOverThisFrame)
            {
                Add(BattleEventType.TimeOver, 0, 1, null, (int)nextRound.Outcome);
            }
            else if (nextRound.IsDoubleKO)
            {
                Add(BattleEventType.DoubleKnockout, 0, 1, null, 0);
            }
            else
            {
                int winner = nextRound.WinnerFighterId!.Value;
                Add(BattleEventType.Knockout, winner, 1 - winner, null, 0);
            }

            Add(BattleEventType.RoundEnded, nextRound.WinnerFighterId ?? -1, -1, null, (int)nextRound.Outcome);
        }

        return events is null
            ? (IReadOnlyList<BattleEvent>)Array.Empty<BattleEvent>()
            : events.AsReadOnly();
    }

    /// <summary>单次命中的结算结果（不可变）。</summary>
    /// <param name="Blocked">是否被防御（true = Blockstun 不掉血；false = 命中扣血 + Hitstun）。</param>
    /// <param name="Damage">本次<b>实扣</b>伤害（已应用 max(0, …) 钳制；被防御时为 0）。
    /// 事件流直接使用该值，绝不再用配置伤害重新计算。</param>
    /// <param name="StunFrames">硬直帧数（命中硬直或防御硬直）。</param>
    /// <param name="MoveId">攻击招式 ID。</param>
    /// <param name="AttackStartFrame">攻击实例的发动帧（D81；写入被击者的已结算记录）。</param>
    /// <param name="AirHit">被击者在结算前是否处于空中（Airborne / AirHitstun，D93）。</param>
    /// <param name="Launch">本次是否击飞（攻击配置了垂直初速且未被防御）。</param>
    /// <param name="KnockbackX">地面击退的有符号水平速度（世界单位/帧；0 = 不击退）。</param>
    /// <param name="KnockbackFriction">击退摩擦（向 0 衰减）；无击退时为 0。</param>
    /// <param name="LaunchVelocityX">击飞有符号水平初速（远离攻击者）。</param>
    /// <param name="LaunchVelocityY">击飞垂直初速（向上）。</param>
    /// <param name="KnockdownFrames">落地后的倒地帧数（击飞时 ≥ 1）。</param>
    /// <param name="WakeupFrames">倒地后的起身帧数。</param>
    private readonly record struct CombatSettlement(
        bool Blocked,
        int Damage,
        int StunFrames,
        string MoveId,
        int AttackStartFrame,
        uint ContactGroups,
        bool AirHit,
        bool Launch,
        FixedPoint KnockbackX,
        FixedPoint KnockbackFriction,
        FixedPoint LaunchVelocityX,
        FixedPoint LaunchVelocityY,
        int KnockdownFrames,
        int WakeupFrames,
        HitstopPair Hitstop);

    /// <summary>
    /// 双人最小间距统一求解（D71 修订，05 专项补修 P1）。
    ///
    /// <para><b>顺序约束（防地面穿越）</b>：帧开始双方都在地面（非空中，见
    /// <see cref="FighterLogicStateExtensions.IsAirborneLike"/>，D94）时，
    /// 以<b>帧开始相对顺序</b>为约束——地面角色禁止在一个逻辑帧内交换左右；
    /// 完全重叠的初态平局规则保持确定（P1 视为在左）。涉及空中（一方帧开始为
    /// Airborne——合法越身或落地帧）时以<b>候选顺序</b>为准，新地面顺序在落地帧建立；
    /// 空中越身不被本约束禁止；空中状态（Airborne / AirHitstun）<b>完全不参与</b>
    /// 本求解（D94：击飞当帧 Y 可能为 0，因此判定只看逻辑状态，不看高度）。</para>
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
    /// <param name="frozenThisFrame">本帧各角色是否处于 Hitstop 冻结（D101 固定锚点资格）。</param>
    ///
    /// <para><b>D101（阶段 08 最终补修）</b>：冻结资格优先于对称分摊。两人都不冻结时本求解器
    /// 逐位沿用旧 D73 行为；只有一方冻结时，冻结方坐标固定，缺口全部由未冻结方承担；
    /// 双方都冻结时保持二者已有的合法位置。冻结方位置在冻结分支里就是帧开始位置，
    /// 因此不会因对方移动而滑动，也不会留下非法的另一方坐标。</para>
    private void ApplySeparation(
        Span<FighterCandidate> candidates,
        bool bothGroundAtFrameStart,
        FixedPoint frameStartP1X,
        FixedPoint frameStartP2X,
        bool[] frozenThisFrame)
    {
        // 仅双方候选都非空中时强制（有一方在空中 → 不推挤，允许越身）。
        // D94：空中状态 = Airborne | AirHitstun（击飞当帧 Y 可能为 0，不能靠高度判断）。
        if (candidates[0].LogicState.IsAirborneLike() || candidates[1].LogicState.IsAirborneLike())
            return;

        FixedPoint p1Cand = candidates[0].PositionX;
        FixedPoint p2Cand = candidates[1].PositionX;

        // 顺序约束（P1-B）：帧开始双方都在地面 → 禁止本帧交换左右；
        // 涉及空中（越身/落地帧）→ 以候选顺序为准（新地面顺序在落地帧建立）。
        bool p1IsLeft = bothGroundAtFrameStart
            ? frameStartP1X <= frameStartP2X
            : p1Cand <= p2Cand;

        int lo = p1IsLeft ? 0 : 1; // 本帧结束时必须在左侧的角色
        int hi = 1 - lo;
        FixedPoint separation = _stage.MinimumFighterSeparation;
        if (_useFighterSeparation)
        {
            GroundBodyWidths left = _roster.Fighter(lo).GroundBodyWidths!;
            GroundBodyWidths right = _roster.Fighter(hi).GroundBodyWidths!;
            separation = (candidates[lo].Facing == Facing.Right ? left.Front : left.Back)
                + (candidates[hi].Facing == Facing.Left ? right.Front : right.Back);
        }
        FixedPoint loCand = candidates[lo].PositionX;
        FixedPoint hiCand = candidates[hi].PositionX;
        FixedPoint gap = hiCand - loCand;

        // ── D101：Hitstop 固定锚点求解（先于对称分摊）──────────────────────
        if (gap >= separation)
            return; // 候选已满足顺序与间距（冻结方本来也没动）。

        bool loFrozen = frozenThisFrame[lo];
        bool hiFrozen = frozenThisFrame[hi];
        if (loFrozen || hiFrozen)
        {
            if (loFrozen && hiFrozen)
                return; // 双方都冻结：帧开始位置本已合法，不做任何推挤。

            // 冻结方坐标固定，缺口全部由未冻结方承担；未冻结方仍受 D70 边界钳制。
            // 若人为注入「冻结方贴墙且已无空间」的非法初态，required 会被钳到边界、
            // 间距仍可能不足——那是约束本身不可同时满足，不编造额外位移；正常可达
            // 状态（上一帧已满足 D70/D73）不会出现这种情况。
            if (loFrozen)
            {
                FixedPoint required = loCand + separation;
                candidates[hi].PositionX = _stage.ClampX(hiCand < required ? required : hiCand);
            }
            else
            {
                FixedPoint required = hiCand - separation;
                candidates[lo].PositionX = _stage.ClampX(loCand > required ? required : loCand);
            }
            return;
        }

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
