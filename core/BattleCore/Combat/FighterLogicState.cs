namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 角色逻辑状态（阶段 04）。全部转换只发生在整数逻辑帧上（<c>BattleSimulation.Step</c>），
/// 与渲染帧率无关；由权威快照（<c>FighterState</c>）携带，Godot 只读。
///
/// <para><b>合法转换图（唯一全集，别无其他边）：</b></para>
/// <list type="bullet">
/// <item><see cref="Standing"/> ↔ <see cref="Crouching"/>（按当帧解析后的「下」方向切换）；</item>
/// <item>任一待机状态（Standing/Crouching）→ <see cref="Startup"/>（消费一条有效指令）；</item>
/// <item><see cref="Startup"/> → <see cref="Active"/> → <see cref="Recovery"/> → 待机
///       （按招式数据配置的阶段帧数逐帧推进）。</item>
/// </list>
///
/// <para>
/// <b>本阶段刻意不含</b> Airborne（无跳跃）、Hitstun/Blockstun（无受击/防御结算）、
/// Knockdown 等——它们属于后续阶段的结算规则，不以空实现冒充已支持。
/// 状态机与快照字段按「可扩展的显式枚举」设计，新增状态时补全转换边即可。
/// </para>
/// </summary>
public enum FighterLogicState
{
    /// <summary>站立待机（默认初始状态）。</summary>
    Standing = 0,

    /// <summary>蹲下待机（解析后的「下」方向按住时）。</summary>
    Crouching = 1,

    /// <summary>招式启动阶段（指令已消费，尚未生效）。</summary>
    Startup = 2,

    /// <summary>招式生效阶段（本阶段不执行攻击判定——判定属于后续阶段）。</summary>
    Active = 3,

    /// <summary>招式收招阶段（结束后回到待机）。</summary>
    Recovery = 4,
}

/// <summary>
/// 招式允许的<b>发动起点</b>状态（待机状态子集）。
/// Startup/Active/Recovery 期间不接受新招式（动作阶段按数据走完为止）。
/// </summary>
[Flags]
public enum MoveAllowedStates
{
    None = 0,

    /// <summary>允许从站立待机发动。</summary>
    Standing = 1 << 0,

    /// <summary>允许从蹲下待机发动。</summary>
    Crouching = 1 << 1,

    /// <summary>站立或蹲下皆可（大多数地面技的默认）。</summary>
    GroundIdle = Standing | Crouching,
}
