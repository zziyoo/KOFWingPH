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
///       （按招式数据配置的阶段帧数逐帧推进）；</item>
/// <item>可受击状态 → <see cref="Hitstun"/> → 待机（被命中结算，阶段 06B/D78；
///       动作被中断，硬直归零同帧恢复待机更新）；</item>
/// <item>可防御状态 → <see cref="Blockstun"/> → 待机（成功防御结算，D78；
///       规则与 Hitstun 对称，不掉血）。</item>
/// </list>
///
/// <para>
/// <b>不允许的状态跳转</b>：Hitstun/Blockstun 期间不能步行、起跳或发动招式
/// （硬直归零同帧才回到待机更新路径）；<b>不存在</b> Hitstun → Startup、
/// Blockstun → Airborne 等未定义边。阶段 11 允许正常 Airborne 防御 High/Mid，并进入 AirBlockstun；
/// 空中受击与空中防御分别按其专属状态积分。</para>
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

    /// <summary>受击硬直（阶段 06B，D78）：命中结算进入，剩余帧数存
    /// <c>FighterState.StunRemainingFrames</c>；期间不能步行/起跳/发动招式；
    /// 归零同帧回待机更新。动作中受击 → 招式中断（MoveId 清空）。</summary>
    Hitstun = 7,

    /// <summary>防御硬直（阶段 06B，D78）：成功防御结算进入，不掉血；
    /// 其余语义与 Hitstun 对称。</summary>
    Blockstun = 8,

    /// <summary>
    /// 空中受击（阶段 07C，D93）：被击飞或空中被打中后进入，持续到<b>落地</b>
    /// （本阶段没有任何空中行动，因此不需要独立的空中硬直计时）。
    /// 携带权威速度，按 <c>AirborneMovement</c> 同一套定点积分推进；落地转
    /// <see cref="Knockdown"/>（或 <see cref="Wakeup"/> / 待机）。不可防御、不可行动。
    /// </summary>
    AirHitstun = 9,

    /// <summary>
    /// 倒地（阶段 07C，D93）：落地后贴地倒下的状态，倒计时存
    /// <c>StunRemainingFrames</c>；期间不可行动、可被命中（本阶段无起身无敌）。
    /// 归零后若配置了起身帧数则转 <see cref="Wakeup"/>，否则同帧回到待机更新。
    /// </summary>
    Knockdown = 10,

    /// <summary>
    /// 起身（阶段 07C，D93）：从倒地恢复的过程，倒计时存 <c>StunRemainingFrames</c>；
    /// 期间不可行动、可被命中；归零同帧回到待机更新与指令消费（与硬直恢复同语义）。
    /// </summary>
    Wakeup = 11,

    /// <summary>空中防御硬直，保留跳跃运动，落地转地面剩余硬直。</summary>
    AirBlockstun = 12,
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

/// <summary>角色逻辑状态的语义查询（阶段 07B，D90）。</summary>
public static class FighterLogicStateExtensions
{
    /// <summary>
    /// 是否属于<b>受击延续状态</b>——角色在此状态下不能自由行动，
    /// 因此再次被有效命中时仍计入同一连段（D90）；恢复待机的那一帧不算延续。
    /// </summary>
    public static bool IsHitReaction(this FighterLogicState state) =>
        state is FighterLogicState.Hitstun
            or FighterLogicState.AirHitstun
            or FighterLogicState.Knockdown
            or FighterLogicState.Wakeup;

    /// <summary>
    /// 是否属于<b>空中状态</b>（阶段 07 最终补修，D94）：
    /// <see cref="FighterLogicState.Airborne"/> 与 <see cref="FighterLogicState.AirHitstun"/>。
    ///
    /// <para>这是「是否参与地面双人最小间距推挤（D73）」与「帧开始是否在地面」的
    /// <b>唯一</b>判定依据——各调用点复制一份状态列表会让新增空中状态被静默漏掉
    /// （07C 的 <see cref="FighterLogicState.AirHitstun"/> 就是这样被当成地面角色的）。
    /// <b>不能</b>改用 <c>PositionY &gt; 0</c>：击飞当帧允许 Y=0 且垂直速度为正（D93）。</para>
    ///
    /// <para>D73 顺序约束、D70 场地边界仍然作用于空中角色；本判定只决定
    /// 「是否执行双人最小间距求解」与「是否可用候选顺序建立新地面顺序」。</para>
    /// </summary>
    public static bool IsAirborneLike(this FighterLogicState state) =>
        state is FighterLogicState.Airborne or FighterLogicState.AirHitstun or FighterLogicState.AirBlockstun;

    /// <summary>是否处于招式三阶段（Startup/Active/Recovery）。</summary>
    public static bool IsInMove(this FighterLogicState state) =>
        state is FighterLogicState.Startup or FighterLogicState.Active or FighterLogicState.Recovery;
}
