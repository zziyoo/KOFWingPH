namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 角色逻辑状态（阶段 04/05B）。全部转换只发生在整数逻辑帧上（<c>BattleSimulation.Step</c>），
/// 与渲染帧率无关；由权威快照（<c>FighterState</c>）携带，Godot 只读。
///
/// <para><b>合法转换图（唯一全集，别无其他边）：</b></para>
/// <list type="bullet">
/// <item><see cref="Standing"/> ↔ <see cref="Crouching"/>（按当帧解析后的「下」方向切换）；</item>
/// <item>任一待机状态（Standing/Crouching）→ <see cref="PreJump"/>（「上」按下边沿，05B/D67）；</item>
/// <item><see cref="PreJump"/> → <see cref="Airborne"/>（预备窗口归零同帧起跳，跳跃类型与
///       初始速度在起跳帧按当帧输入锁定，D67/D68）；</item>
/// <item><see cref="Airborne"/> → <see cref="Standing"/>（落地：运动积分触及地面时修正位置与
///       速度——这是唯一由运动阶段写入的状态边，D69）；</item>
/// <item>任一待机状态（Standing/Crouching）→ <see cref="Startup"/>（消费一条有效指令）；</item>
/// <item><see cref="Startup"/> → <see cref="Active"/> → <see cref="Recovery"/> → 待机
///       （按招式数据配置的阶段帧数逐帧推进）。</item>
/// </list>
///
/// <para>
/// <b>本阶段刻意不含</b> Hitstun/Blockstun（无受击/防御结算）、Knockdown 等——它们属于
/// 后续阶段的结算规则，不以空实现冒充已支持。本阶段也<b>不做空中攻击</b>：
/// Startup/Active/Recovery 只在地面发生。</para>
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

    /// <summary>起跳预备（阶段 05B）：从「上」边沿进入，预备窗口内不消费指令、
    /// 不移动、不被蹲下覆盖；窗口归零同帧起跳（D67）。</summary>
    PreJump = 5,

    /// <summary>空中（阶段 05B）：垂直速度受重力积分，水平速度起跳时锁定；
    /// 不消费指令、不空中控制；触地同帧落地回站立（D68/D69）。</summary>
    Airborne = 6,
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
