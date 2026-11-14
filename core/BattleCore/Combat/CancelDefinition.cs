namespace NewKOF.BattleCore.Combat;

/// <summary>允许发动取消的阶段（阶段 07B，D91）。</summary>
[Flags]
public enum MoveCancelPhases
{
    /// <summary>不允许（非法配置）。</summary>
    None = 0,

    /// <summary>生效阶段可取消。</summary>
    Active = 1 << 0,

    /// <summary>收招阶段可取消。</summary>
    Recovery = 1 << 1,
}

/// <summary>
/// 一条<b>招式取消规则</b>（阶段 07B，D91）：挂在<b>来源招式</b>的
/// <see cref="MoveDefinition.Cancels"/> 上——来源招式即「拥有该规则的 MoveDefinition」，
/// 不再重复存 SourceMoveId（避免两份可能不一致的事实）。
///
/// <para><b>窗口单位</b>：<see cref="WindowFirstFrame"/> / <see cref="WindowLastFrame"/> 是
/// <b>自发动帧起算的经过帧数</b>（发动当帧 = 0，含两端），与招式阶段无关；
/// <see cref="Phases"/> 是额外的阶段过滤器（Active / Recovery）。
/// 两者必须同时满足——这样「第几个 Active 帧」这类语义可以用固定逻辑帧表达，
/// 不依赖渲染时间。</para>
///
/// <para><b>确认条件</b>：<see cref="RequiresHitConfirm"/> / <see cref="RequiresBlockConfirm"/>
/// 读取攻击者<b>本攻击实例</b>的命中/防御确认帧（结算写入），并且要求
/// <c>当前帧 &gt; 确认帧</c>——本帧刚获得的确认从<b>下一逻辑帧</b>起才可用于取消，
/// 避免「命中 → 同帧取消 → 新攻击同帧判定」的循环。
/// <see cref="AllowWhiff"/> 表示不需要任何确认（空挥取消）。
/// 三者至少配置一个。</para>
///
/// <para>合法性（目标招式存在、自身目标显式允许、窗口/阶段/确认满足、缓冲区有对应未消费指令）
/// 由 <c>FighterStateMachine</c> 在权威状态上判定，规则本身是纯数据。</para>
/// </summary>
public sealed class CancelDefinition
{
    /// <param name="targetMoveId">允许取消到的目标招式 ID（必须在同一角色配置中）。</param>
    /// <param name="windowFirstFrame">窗口起始帧（自发动帧起算的经过帧数，≥ 0，含）。</param>
    /// <param name="windowLastFrame">窗口结束帧（≥ 起始帧，含）。</param>
    /// <param name="phases">允许的阶段（默认 Active | Recovery）。</param>
    /// <param name="requiresHitConfirm">是否需要命中确认（本实例、且确认发生在更早的帧）。</param>
    /// <param name="requiresBlockConfirm">是否需要防御确认（同上）。</param>
    /// <param name="allowWhiff">是否允许空挥取消（不需要确认）。</param>
    /// <param name="allowSameMove">是否允许以新实例重新进入来源招式。</param>
    /// <param name="standingInputOnly">可取消的输入在持下时不启动站立招式。若没有其他取消胜出，该输入失效。</param>
    /// <param name="maximumInputAge">从实际按键边沿计算的最长输入年龄，含当帧。缺省使用既有指令缓冲。</param>
    /// <exception cref="ArgumentException">目标为空 / 窗口倒置 / 阶段为空 / 未配置任何确认条件。</exception>
    public CancelDefinition(
        string targetMoveId,
        int windowFirstFrame,
        int windowLastFrame,
        MoveCancelPhases phases = MoveCancelPhases.Active | MoveCancelPhases.Recovery,
        bool requiresHitConfirm = false,
        bool requiresBlockConfirm = false,
        bool allowWhiff = false,
        bool allowSameMove = false,
        bool standingInputOnly = false,
        int? maximumInputAge = null,
        bool retainBufferedMotionHelper = false)
    {
        if (string.IsNullOrWhiteSpace(targetMoveId))
            throw new ArgumentException("取消目标招式 ID 不能为空。", nameof(targetMoveId));
        if (windowFirstFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(windowFirstFrame), windowFirstFrame,
                "取消窗口起始帧不能为负（自发动帧起算的经过帧数）。");
        if (windowLastFrame < windowFirstFrame)
            throw new ArgumentException(
                $"取消窗口非法（first={windowFirstFrame} > last={windowLastFrame}）。", nameof(windowLastFrame));
        if (phases == MoveCancelPhases.None || (phases & ~(MoveCancelPhases.Active | MoveCancelPhases.Recovery)) != 0)
            throw new ArgumentException($"取消阶段只能是 Active/Recovery 的组合，收到 {phases}。", nameof(phases));
        if (!requiresHitConfirm && !requiresBlockConfirm && !allowWhiff)
            throw new ArgumentException(
                "取消规则必须至少配置一种确认条件（命中确认 / 防御确认 / 空挥取消），否则永远无法触发。",
                nameof(allowWhiff));
        if (maximumInputAge < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumInputAge), "按键年龄不能为负。");

        TargetMoveId = targetMoveId;
        WindowFirstFrame = windowFirstFrame;
        WindowLastFrame = windowLastFrame;
        Phases = phases;
        RequiresHitConfirm = requiresHitConfirm;
        RequiresBlockConfirm = requiresBlockConfirm;
        AllowWhiff = allowWhiff;
        AllowSameMove = allowSameMove;
        StandingInputOnly = standingInputOnly;
        MaximumInputAge = maximumInputAge;
        RetainBufferedMotionHelper = retainBufferedMotionHelper;
    }

    /// <summary>目标招式 ID（同角色配置内）。</summary>
    public string TargetMoveId { get; }

    /// <summary>窗口起始帧（自发动帧起算的经过帧数，含）。</summary>
    public int WindowFirstFrame { get; }

    /// <summary>窗口结束帧（含）。</summary>
    public int WindowLastFrame { get; }

    /// <summary>允许取消的阶段（Active/Recovery 组合）。</summary>
    public MoveCancelPhases Phases { get; }

    /// <summary>是否需要命中确认（更早帧的确认）。</summary>
    public bool RequiresHitConfirm { get; }

    /// <summary>是否需要防御确认（更早帧的确认）。</summary>
    public bool RequiresBlockConfirm { get; }

    /// <summary>是否允许空挥取消（无需确认）。</summary>
    public bool AllowWhiff { get; }
    public bool AllowSameMove { get; }
    public bool StandingInputOnly { get; }
    public int? MaximumInputAge { get; }
    public bool RetainBufferedMotionHelper { get; }

    /// <summary>阶段是否允许（纯函数，供状态机与测试共用）。</summary>
    public bool AllowsPhase(FighterLogicState state) => AllowsPhase(Phases, state);

    internal static bool AllowsPhase(MoveCancelPhases phases, FighterLogicState state) => state switch
    {
        FighterLogicState.Active => (phases & MoveCancelPhases.Active) != 0,
        FighterLogicState.Recovery => (phases & MoveCancelPhases.Recovery) != 0,
        _ => false,
    };

    /// <summary>
    /// 确认条件是否满足（纯函数）。确认必须来自<b>更早</b>的逻辑帧（D91：本帧确认下一帧起可用）。
    /// </summary>
    /// <param name="frame">当前逻辑帧号。</param>
    /// <param name="attackStartFrame">当前攻击实例的发动帧（确认必须属于同一实例）。</param>
    public bool ConfirmSatisfied(int frame, int hitConfirmFrame, int blockConfirmFrame, int attackStartFrame)
    {
        if (AllowWhiff)
            return true;

        bool hitOk = RequiresHitConfirm
            && hitConfirmFrame >= attackStartFrame
            && frame > hitConfirmFrame;
        bool blockOk = RequiresBlockConfirm
            && blockConfirmFrame >= attackStartFrame
            && frame > blockConfirmFrame;

        return hitOk || blockOk;
    }
}

/// <summary>A source window for a real command whose target move is outside the loaded profile.</summary>
public sealed class ReservedCancelDefinition
{
    public ReservedCancelDefinition(string commandId, int windowFirstFrame, int windowLastFrame, MoveCancelPhases phases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        if (windowFirstFrame < 0 || windowLastFrame < windowFirstFrame)
            throw new ArgumentOutOfRangeException(nameof(windowFirstFrame), "Reserved cancel requires a valid action window.");
        if (phases == MoveCancelPhases.None || (phases & ~(MoveCancelPhases.Active | MoveCancelPhases.Recovery)) != 0)
            throw new ArgumentOutOfRangeException(nameof(phases), "Reserved cancel requires an active or recovery phase.");
        CommandId = commandId;
        WindowFirstFrame = windowFirstFrame;
        WindowLastFrame = windowLastFrame;
        Phases = phases;
    }

    public string CommandId { get; }
    public int WindowFirstFrame { get; }
    public int WindowLastFrame { get; }
    public MoveCancelPhases Phases { get; }

    internal bool IsOpen(FighterLogicState state, int elapsed) =>
        elapsed >= WindowFirstFrame && elapsed <= WindowLastFrame && CancelDefinition.AllowsPhase(Phases, state);
}
