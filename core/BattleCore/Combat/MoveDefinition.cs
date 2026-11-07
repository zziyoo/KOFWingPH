using NewKOF.BattleCore.Commands;

namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 一条<b>招式定义</b>（阶段 04，纯数据）：说明「哪个指令触发哪个动作、
/// 动作各阶段持续多少帧、允许从哪些待机状态发动」。
///
/// <para><b>职责分离（B3）</b>：<see cref="CommandRecognizer"/> 只负责把输入识别成
/// <see cref="CommandDefinition.CommandId"/>；本类负责把 CommandId 映射为动作并定义
/// 时间轴。识别器不认识任何招式，招式表不参与识别流程。</para>
///
/// <para>MoveId 只是逻辑上的动作 ID（如 <c>FireballPrototype</c>），
/// <b>不</b>生成飞行道具、不执行攻击判定、不扣生命值——判定/伤害属于后续阶段。</para>
/// </summary>
public sealed class MoveDefinition
{
    /// <param name="moveId">招式唯一标识（同一角色内不得重复）。</param>
    /// <param name="commandId">触发的指令 ID；必须存在于同角色的指令表中。</param>
    /// <param name="startupFrames">启动阶段帧数（≥ 1）。</param>
    /// <param name="activeFrames">生效阶段帧数（≥ 1；本阶段不执行判定）。</param>
    /// <param name="recoveryFrames">收招阶段帧数（≥ 1）。</param>
    /// <param name="allowedFrom">允许发动的待机状态集合，不能为 <see cref="MoveAllowedStates.None"/>。</param>
    /// <exception cref="ArgumentException">ID 为空 / 阶段帧数 &lt; 1 / 允许状态为空。</exception>
    public MoveDefinition(
        string moveId,
        string commandId,
        int startupFrames,
        int activeFrames,
        int recoveryFrames,
        MoveAllowedStates allowedFrom = MoveAllowedStates.GroundIdle)
    {
        if (string.IsNullOrWhiteSpace(moveId))
            throw new ArgumentException("招式 ID 不能为空。", nameof(moveId));
        if (string.IsNullOrWhiteSpace(commandId))
            throw new ArgumentException("招式必须关联一个指令 ID。", nameof(commandId));
        if (startupFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(startupFrames), startupFrames,
                "启动阶段帧数必须 ≥ 1（本阶段不支持 0 帧阶段）。");
        if (activeFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(activeFrames), activeFrames,
                "生效阶段帧数必须 ≥ 1。");
        if (recoveryFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(recoveryFrames), recoveryFrames,
                "收招阶段帧数必须 ≥ 1。");
        if (allowedFrom == MoveAllowedStates.None || (allowedFrom & ~MoveAllowedStates.GroundIdle) != 0)
            throw new ArgumentException(
                $"允许发动状态只能是 Standing/Crouching 的组合，收到 {allowedFrom}。", nameof(allowedFrom));

        MoveId = moveId;
        CommandId = commandId;
        StartupFrames = startupFrames;
        ActiveFrames = activeFrames;
        RecoveryFrames = recoveryFrames;
        AllowedFrom = allowedFrom;
    }

    /// <summary>招式唯一标识（同一角色内唯一）。</summary>
    public string MoveId { get; }

    /// <summary>触发该招式的指令 ID（与角色指令表中的 <see cref="CommandDefinition.CommandId"/> 对应）。</summary>
    public string CommandId { get; }

    /// <summary>启动阶段帧数（≥ 1）。</summary>
    public int StartupFrames { get; }

    /// <summary>生效阶段帧数（≥ 1；本阶段不执行判定）。</summary>
    public int ActiveFrames { get; }

    /// <summary>收招阶段帧数（≥ 1）。</summary>
    public int RecoveryFrames { get; }

    /// <summary>允许发动的待机状态集合。</summary>
    public MoveAllowedStates AllowedFrom { get; }

    /// <summary>招式总帧数（诊断用）。</summary>
    public int TotalFrames => StartupFrames + ActiveFrames + RecoveryFrames;
}
