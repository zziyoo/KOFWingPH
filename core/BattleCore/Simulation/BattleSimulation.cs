using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// 战斗内核的唯一调度入口，纯函数式推进一逻辑帧。
///
/// <para>阶段 04（D55/D58）起，<see cref="Step"/> 在权威转换内部完成整条
/// 「输入 → 指令识别 → 缓冲 → 消费 → 状态机推进」链路：</para>
/// <list type="number">
/// <item>按「当帧开始朝向」（D46）解析输入并推进该角色的
///       <see cref="RecognitionState"/>（纯函数，识别状态随快照携带）；</item>
/// <item>调用 <see cref="FighterStateMachine.Tick"/>：推进动作阶段 → 待机更新 →
///       按确定规则消费一条有效识别结果并进入 Startup；</item>
/// <item>把新的动作状态与识别快照写入<b>全新的</b> <see cref="FighterState"/>。</item>
/// </list>
///
/// <para><b>所有权与一致性</b>：传入状态永不修改；识别与消费都是纯计算——
/// Step 抛异常时不产生任何新状态，会话提交失败时整个候选状态（含消费标记）被丢弃，
/// 重试同一帧得到同一结果，不存在「消费已发生但动作未提交」的半提交（B6.10）。
/// 识别与消费都在权威转换内同帧完成（<b>零帧延迟</b>，明确语义）。</para>
///
/// <para>阶段 05A（D63/D64/D65）起，<see cref="Step"/> 在每帧内按固定顺序推进：
/// 「输入解析 → 指令识别 → 状态机 → 地面运动 → 全新快照」。位置为 Q16.16 定点数
/// （<c>FighterState.PositionX/PositionY</c>，世界单位、Y 向上），地面步行只发生在
/// 站立待机、由角色运动配置驱动；运动结果完全由初始状态、配置与逻辑输入决定。</para>
///
/// <para>明确不做（留给后续阶段）：跳跃/重力、场地边界与推挤、碰撞、攻击判定、伤害受击、
/// 动画、取消规则、回合裁决。不读取 Godot，不使用浮点，不采样设备输入。</para>
/// </summary>
public sealed class BattleSimulation
{
    private readonly FightRoster _roster;

    /// <param name="roster">双方角色配置；null 表示空配置（双方无指令无招式，
    /// 等价于阶段 01–03 的默认行为——识别状态仍随帧推进，只是没有可匹配指令）。</param>
    public BattleSimulation(FightRoster? roster = null)
    {
        _roster = roster ?? FightRoster.CreateEmpty();
    }

    /// <summary>对局双方的角色配置。</summary>
    public FightRoster Roster => _roster;

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

        var nextFighters = new FighterState[state.Fighters.Count];
        for (int i = 0; i < nextFighters.Length; i++)
        {
            FighterState fighter = state.Fighters[i];
            FighterDefinition definition = _roster.Fighter(i);
            InputBits bits = input.ForPlayer(i);

            // 1) 指令识别（D55）：纯函数推进，朝向取「帧开始状态」（D46）。
            RecognitionState recognition = fighter.RecognitionState.Advance(
                bits, fighter.Facing, state.Frame, i, definition.Commands);

            // 2) 动作状态机 + 指令消费（D58/D60）。
            RelativeInput resolved = DirectionResolver.Resolve(bits, fighter.Facing);
            FighterActionTick tick = FighterStateMachine.Tick(
                fighter.LogicState,
                fighter.MoveId,
                fighter.PhaseRemainingFrames,
                resolved,
                recognition,
                definition,
                state.Frame);

            // 3) 地面运动（05A/D65）：按「本帧结束逻辑状态」与帧开始朝向推进位置。
            //    仅站立待机响应前后输入；蹲下与动作阶段原地。定点数，无浮点。
            FixedPoint nextX = GroundMovement.StepX(
                tick.State, fighter.PositionX, fighter.Facing, resolved, definition.Movement);

            // 4) 全新角色快照（record with：原状态不受影响）。
            nextFighters[i] = fighter with
            {
                LogicState = tick.State,
                MoveId = tick.MoveId,
                PhaseRemainingFrames = tick.PhaseRemainingFrames,
                Recognition = tick.Recognition,
                PositionX = nextX,
            };
        }

        var nextState = new BattleState(frameAfter, nextFighters, state.StateVersion);

        return new BattleStepResult(state.Frame, frameAfter, input, nextState);
    }
}
