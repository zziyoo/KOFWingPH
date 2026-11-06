using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// 战斗内核的唯一调度入口，纯函数式推进一逻辑帧。
///
/// 阶段 01 的 <see cref="Step"/> 只做三件事：
/// 1. 校验输入帧号与当前状态帧号一致；
/// 2. 帧号 +1；
/// 3. 角色数据原样带入下一状态（本阶段没有移动/攻击/受击规则）。
///
/// 明确不做（留给后续阶段）：状态机、碰撞、伤害、动画、取消规则、回合裁决。
/// 不读取 Godot 物理系统，不使用浮点，不采样设备输入。
/// </summary>
public sealed class BattleSimulation
{
    /// <summary>
    /// 由当前状态与帧输入推进一帧，返回新状态与结果描述。
    /// 传入状态不会被修改；返回值中的 State 一定是新实例。
    /// </summary>
    /// <exception cref="ArgumentNullException">state 为 null。</exception>
    /// <exception cref="InvalidOperationException">输入帧号与状态帧号不匹配。</exception>
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

        // 阶段 01：角色数据直接带入下一帧（record 为不可变值类型，天然是新副本）。
        var nextFighters = new FighterState[state.Fighters.Count];
        for (int i = 0; i < nextFighters.Length; i++)
            nextFighters[i] = state.Fighters[i];

        var nextState = new BattleState(frameAfter, nextFighters, state.StateVersion);

        return new BattleStepResult(state.Frame, frameAfter, input, nextState);
    }
}
