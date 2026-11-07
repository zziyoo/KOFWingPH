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
            var frozen = new BattleState(frameAfter, state.Fighters, state.StateVersion, state.Round);
            return new BattleStepResult(state.Frame, frameAfter, input, frozen);
        }

        // ── 第 1-2 阶段：逐角色「识别 → 状态机 → 运动」（双方独立，互不读取对方）──
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
            if (fighter.HitstopRemainingFrames > 0)
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
                    HitstopRemainingFrames = fighter.HitstopRemainingFrames - 1,
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
                moveElapsed);

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
                    x = fighter.PositionX + speed;
                    FixedPoint friction = fighter.KnockbackFriction;
                    FixedPoint next = speed > FixedPoint.Zero ? speed - friction : speed + friction;
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
            state.Fighters[0].PositionX, state.Fighters[1].PositionX);

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
            };

            // 命中/防御确认属于「当前攻击实例」：换招、被中断或回到待机后必须清空（D91）。
            nextFighters[i] = NormalizeConfirms(nextFighters[i]);
            // 落地计划只属于空中受击/倒地状态（D93）。
            nextFighters[i] = NormalizeLandingPlan(nextFighters[i]);
        }

        // ── 第 6 阶段：命中结算（D78/D79/D80/D81）——接触检测 + 双方同时判定（互不先手）
        //    + 攻击实例去重（同一发动对同一目标只结算一次，被防御亦算已结算）。
        IReadOnlyList<AttackContact> contacts = HitDetection.Detect(
            nextFighters[0], nextFighters[1], _roster.Fighter(0), _roster.Fighter(1));

        // AttackActive 必须取<b>结算前</b>视图（下方结算会原地改写 nextFighters）：
        // 本帧确实生效过的攻击不能因为同帧被打断而丢事件——连<b>招式身份</b>一起取，
        // 因为结算会把被中断者的 MoveId 清空。
        AttackActiveSnapshot[] attackActive = CollectAttackActive(nextFighters, _roster, frozenThisFrame);

        var outcomes = new CombatSettlement?[2];
        ApplyCombatSettlement(state.Frame, nextFighters, contacts,
            candidates[0].BackHeld, candidates[1].BackHeld, outcomes);

        // 连段收尾（D90）：恢复自由行动的角色清空旧记录（命中帧本身不会到这里——命中后必为受击状态）。
        UpdateCombos(nextFighters);

        // ── 第 7 阶段：KO 裁决（D89）——只依据<b>全部结算完成之后</b>的双方体力，
        //    双方同帧归零即 DoubleKO（不存在先手优势）；已结束的回合原样保留。
        RoundState nextRound = RoundState.FromFinalHealth(state.Round, nextFighters[0].Health, nextFighters[1].Health);

        // ── 第 8 阶段：战斗事件（D82）——由权威结算产生，帧内顺序固定。
        IReadOnlyList<BattleEvent> events = CollectEvents(
            state.Frame, nextFighters, candidates, outcomes, attackActive, state.Round, nextRound);

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

        var nextState = new BattleState(frameAfter, nextFighters, state.StateVersion, nextRound);

        return new BattleStepResult(state.Frame, frameAfter, input, nextState, contacts, events);
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
                    LogicState = FighterLogicState.Blockstun,
                    MoveId = null,
                    PhaseRemainingFrames = 0,
                    StunRemainingFrames = settled.StunFrames,
                    // 防御击退（D92）：独立配置，默认 0；仍然遵守「速度与摩擦同时存在」不变式。
                    VelocityX = settled.KnockbackX,
                    VelocityY = FixedPoint.Zero,
                    KnockbackFriction = settled.KnockbackFriction,
                    // 被防御亦视为该攻击实例已结算（D81）：记录发动帧，抑制后续帧重复结算。
                    LastHitAttackStartFrame = settled.AttackStartFrame,
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
                    MoveStartedFrame = -1, // 招式中断（D78）：实例身份随 MoveId 一并清除。
                    MoveElapsedFrames = 0, // 动作时间随实例一并清除（D96）。
                    // 确认帧属于被中断的旧实例，必须一并清空（D91 不变式）。
                    HitConfirmFrame = -1,
                    BlockConfirmFrame = -1,
                    ComboRecord = comboAfter,
                };
            }
        }

        // 命中停顿（08A/D96）：攻击方与被击方<b>同值</b>（配置在攻击数据上），
        // 同帧多个来源取最大值——不因旧攻击框残留反复刷新（D81 去重已保证每实例一次）。
        // 写入发生在全部状态应用之后，避免 with 覆盖。
        for (int defender = 0; defender < fighters.Length; defender++)
        {
            if (outcomes[defender] is not CombatSettlement settled || settled.HitstopFrames <= 0)
                continue;

            int attacker = 1 - defender;
            fighters[attacker] = fighters[attacker] with
            {
                HitstopRemainingFrames = Math.Max(
                    fighters[attacker].HitstopRemainingFrames, settled.HitstopFrames),
            };
            fighters[defender] = fighters[defender] with
            {
                HitstopRemainingFrames = Math.Max(
                    fighters[defender].HitstopRemainingFrames, settled.HitstopFrames),
            };
        }
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
        FighterState defenderState = fighters[defender];
        bool defenderAirborne = defenderState.LogicState.IsAirborneLike();

        // 取针对该被击者的第一条接触（Detect 顺序确定：攻击者 0→1、攻击序号升序）。
        AttackContact contact = default;
        bool found = false;
        for (int i = 0; i < contacts.Count; i++)
        {
            if (contacts[i].TargetId == defender)
            {
                contact = contacts[i];
                found = true;
                break;
            }
        }

        if (!found)
            return null;

        FighterDefinition attackerDefinition = _roster.Fighter(1 - defender);
        MoveDefinition? move = attackerDefinition.FindMoveById(contact.MoveId);
        if (move is null || contact.AttackIndex >= move.Attacks.Count)
            return null; // 配置与状态不匹配：防御性跳过（正常路径不可达）。

        AttackDefinition attack = move.Attacks[contact.AttackIndex];

        // 攻击实例去重（D81）：同一「攻击者 + 发动帧」实例对同一目标只结算一次
        // （被防御也算已结算）。新一次独立发动（新的发动帧）可再次命中；
        // 被中断的攻击不再产生攻击框，也不会复活。
        FighterState attackerState = fighters[1 - defender];
        if (attackerState.MoveStartedFrame < 0 ||
            attackerState.MoveStartedFrame == defenderState.LastHitAttackStartFrame)
            return null;

        // 防御资格（D79）：站立/蹲下待机 + 按住「后」（帧开始朝向解析的相对输入）。
        // 空中不可防御（D93）：Airborne / AirHitstun 一率不成立。
        bool guarding = !defenderAirborne && defenderState.LogicState switch
        {
            FighterLogicState.Standing => backHeld,
            FighterLogicState.Crouching => backHeld,
            _ => false, // 动作/PreJump/硬直/倒地中不能防御。
        } && GuardCovers(attack.Height, defenderState.LogicState);

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
                MoveId: contact.MoveId, AttackStartFrame: attackerState.MoveStartedFrame,
                AirHit: false, Launch: false,
                KnockbackX: knockbackX, KnockbackFriction: friction,
                LaunchVelocityX: FixedPoint.Zero, LaunchVelocityY: FixedPoint.Zero,
                KnockdownFrames: 0, WakeupFrames: 0,
                HitstopFrames: attack.HitstopFrames);
        }

        bool launches = attack.Launches;
        return new CombatSettlement(
            Blocked: false, Damage: attack.Damage, StunFrames: attack.HitstunFrames,
            MoveId: contact.MoveId, AttackStartFrame: attackerState.MoveStartedFrame,
            AirHit: defenderAirborne, Launch: launches,
            KnockbackX: launches ? FixedPoint.Zero : knockbackX,
            KnockbackFriction: launches ? FixedPoint.Zero : friction,
            LaunchVelocityX: launches ? (direction > 0 ? attack.LaunchVelocityX : -attack.LaunchVelocityX)
                : FixedPoint.Zero,
            LaunchVelocityY: launches ? attack.LaunchVelocityY : FixedPoint.Zero,
            KnockdownFrames: launches ? attack.KnockdownFrames : 0,
            WakeupFrames: launches ? attack.WakeupFrames : 0,
            HitstopFrames: attack.HitstopFrames);
    }

    /// <summary>防御高度表（D77）：High 只能站防、Low 只能蹲防、Mid 皆可。</summary>
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
    private static IReadOnlyList<BattleEvent> CollectEvents(
        int frame,
        FighterState[] fighters,
        FighterCandidate[] candidates,
        CombatSettlement?[] outcomes,
        AttackActiveSnapshot[] attackActive,
        RoundState previousRound,
        RoundState nextRound)
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

        // ③ 逐被击者：命中/防御 + 硬直开始。
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

        // ④ 回合终局（D89）：只在「本帧刚结束」时产生一次——
        //    单方 KO：Knockout(胜者→败者) + RoundEnded；双 KO：DoubleKnockout(0→1) + RoundEnded。
        //    RoundEnded.Amount = (int)RoundOutcome，是消费方判断胜负的唯一依据。
        if (!previousRound.IsFinished && nextRound.IsFinished)
        {
            if (nextRound.IsDoubleKO)
                Add(BattleEventType.DoubleKnockout, 0, 1, null, 0);
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
        bool AirHit,
        bool Launch,
        FixedPoint KnockbackX,
        FixedPoint KnockbackFriction,
        FixedPoint LaunchVelocityX,
        FixedPoint LaunchVelocityY,
        int KnockdownFrames,
        int WakeupFrames,
        int HitstopFrames);

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
    private void ApplySeparation(
        Span<FighterCandidate> candidates,
        bool bothGroundAtFrameStart,
        FixedPoint frameStartP1X,
        FixedPoint frameStartP2X)
    {
        // 仅双方候选都非空中时强制（有一方在空中 → 不推挤，允许越身）。
        // D94：空中状态 = Airborne | AirHitstun（击飞当帧 Y 可能为 0，不能靠高度判断）。
        if (candidates[0].LogicState.IsAirborneLike() || candidates[1].LogicState.IsAirborneLike())
            return;

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
