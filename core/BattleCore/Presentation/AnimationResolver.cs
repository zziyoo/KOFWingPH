using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Presentation;

/// <summary>
/// <b>表现时钟</b>（阶段 10C，D125；阶段 10 最终补修，D128）：把权威逻辑帧换算成
/// 「动画已经过帧数」，并在 <b>Hitstop 期间停止推进</b>。
///
/// <para><b>只读投影</b>：本类不写入任何战斗状态，也不参与判定；它只是把
/// <c>Frame</c> / <c>HitstopRemainingFrames</c> 解释成动画时间。状态或招式发生变化时
/// 时钟归零，保证新动画从第一帧开始播放，而不是接着上一个动作的相位。</para>
///
/// <para><b>Hitstop 的真实执行顺序（D96 / 本轮复核）</b>：</para>
/// <list type="number">
/// <item>命中帧 N 本身<b>不冻结</b>：角色照常推进动作与运动，结算末尾把计数器写成配置值（如 4）。</item>
/// <item>N+1 … N+4 是<b>冻结帧</b>：每帧开始时计数器 &gt; 0 → 整帧不推进动作/运动，
///       只在提交时把计数器 −1（4→3→2→1→0）。</item>
/// <item>N+5 是恢复后的第一帧：帧开始时计数器已为 0 → 正常推进。</item>
/// </list>
///
/// <para><b>因此「本帧是否冻结」必须取帧开始状态的 <c>HitstopRemainingFrames &gt; 0</c></b>，
/// 不能用帧结束状态：计数器从 1 递减到 0 的那一帧<b>仍然是冻结帧</b>，
/// 但它的结束值已经是 0。这正是旧实现（只按 Advance 后的末状态同步）会多算一帧的原因。</para>
/// </summary>
public sealed class FighterVisualClock
{
    private string _lastState = string.Empty;
    private string _lastMove = string.Empty;

    /// <summary>动画已经过的逻辑帧数（Hitstop 期间不增长）。</summary>
    public int VisualFrame { get; private set; }

    /// <summary>上一次同步时的权威逻辑帧（-1 = 尚未同步）。</summary>
    public int LastLogicFrame { get; private set; } = -1;

    /// <summary>
    /// 观察一个<b>已提交逻辑帧</b>并返回可用于取动画帧的「已经过帧数」。
    ///
    /// <para>必须按时间顺序、<b>每个逻辑帧调用一次</b>（渲染帧率无关）；同一逻辑帧重复调用
    /// 不会推进。一次 <c>Advance</c> 提交了多个逻辑帧时，应逐帧调用本方法，
    /// 而不是只在批次结束时用末状态调用一次——否则中间的冻结/恢复信息不可见。</para>
    /// </summary>
    /// <param name="logicFrame">被观察的逻辑帧号（= 帧开始状态帧号）。</param>
    /// <param name="state">该帧<b>结束</b>时的逻辑状态（决定当前动画段）。</param>
    /// <param name="moveId">该帧结束时的招式 ID（null = 无招式）。</param>
    /// <param name="frozenThisFrame">
    /// 该帧是否整体冻结——取<b>帧开始状态</b>的 <c>HitstopRemainingFrames &gt; 0</c>
    /// （见类型注释：递减到 0 的那一帧仍是冻结帧）。
    /// </param>
    public int Observe(int logicFrame, FighterLogicState state, string? moveId, bool frozenThisFrame)
    {
        if (logicFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(logicFrame), logicFrame, "逻辑帧号不能为负。");

        string stateKey = state.ToString();
        string moveKey = moveId ?? string.Empty;
        if (stateKey != _lastState || moveKey != _lastMove)
        {
            // 状态/招式切换：新动画从第 0 帧开始（即使在冻结帧里切换也一样）。
            VisualFrame = 0;
            _lastState = stateKey;
            _lastMove = moveKey;
        }
        else if (!frozenThisFrame && LastLogicFrame >= 0 && logicFrame > LastLogicFrame)
        {
            // 只在「本帧未冻结」且逻辑帧真的前进时推进；渲染帧率无关。
            VisualFrame += logicFrame - LastLogicFrame;
        }

        // 乱序/重复观察不回退帧号：回退会让同一段时间被重复计入动画时间。
        if (logicFrame > LastLogicFrame)
            LastLogicFrame = logicFrame;
        return VisualFrame;
    }

    /// <summary>
    /// 单帧便捷入口（阶段 10C 的旧调用方）。<paramref name="hitstopRemaining"/> 必须是
    /// <b>该逻辑帧生效中的停顿剩余帧数</b>（帧开始值），不是帧结束值。
    ///
    /// <para>只拿得到「批次末状态」的调用方无法用它还原中间帧：那种场景必须改用
    /// <see cref="Observe"/> 逐帧观察（见 D127/D128）。</para>
    /// </summary>
    public int Sync(int logicFrame, FighterLogicState state, string? moveId, int hitstopRemaining) =>
        Observe(logicFrame, state, moveId, hitstopRemaining > 0);

    /// <summary>重置（新回合/换人时用）。</summary>
    public void Reset()
    {
        VisualFrame = 0;
        LastLogicFrame = -1;
        _lastState = string.Empty;
        _lastMove = string.Empty;
    }
}

/// <summary>
/// <b>状态 → 动画解析器</b>（阶段 10C，D125）：<b>唯一</b>决定「现在播放哪段动画、第几帧」的地方。
///
/// <para>优先级：<br/>
/// ① 招式状态（Startup / Active / Recovery）且有该 MoveId 的配置 → 对应段；<br/>
/// ② 站立且带水平速度 → <c>Walking</c>（若配置了）；<br/>
/// ③ 逻辑状态名（<see cref="FighterLogicState"/> 的 ToString）——招式缺配置时就靠它回落到
/// 该阶段的通用动画；<br/>
/// ④ 仍未命中 → 兜底动画（<c>idle</c>）。</para>
///
/// <para><b>绝不</b>用动画帧反推命中、硬直或取消窗口——那些只由内核决定。</para>
/// </summary>
public static class AnimationResolver
{
    /// <summary>解析当前应播放的姿态。</summary>
    /// <param name="fighter">权威角色状态（只读）。</param>
    /// <param name="set">表现配置（缺失时用 <see cref="AnimationSet.CreateFallback"/>）。</param>
    /// <param name="visualFrame">表现时钟的当前值（用于非招式循环动画）。</param>
    public static ResolvedPose Resolve(FighterState fighter, AnimationSet set, int visualFrame)
    {
        ArgumentNullException.ThrowIfNull(set);
        string clipName = ResolveClipName(fighter, set);
        AnimationClip clip = set.FindClip(clipName) ?? set.FallbackClip;

        int elapsed = fighter.IsInMove
            ? Math.Max(0, fighter.MoveElapsedFrames)
            : Math.Max(0, visualFrame);

        int index = clip.FrameIndexAt(elapsed);
        return new ResolvedPose(clip.Name, index, clip.Frames[index]);
    }

    /// <summary>解析应播放的动画名（缺失时降级，不抛异常）。</summary>
    public static string ResolveClipName(FighterState fighter, AnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        // ① 招式三段：只看权威逻辑阶段，不看动画本身。
        if (fighter.IsInMove && fighter.MoveId is string moveId
            && set.Moves.TryGetValue(moveId, out MoveAnimation? move))
        {
            return fighter.LogicState switch
            {
                FighterLogicState.Startup => move.Startup,
                FighterLogicState.Active => move.Active,
                FighterLogicState.Recovery => move.Recovery,
                _ => move.Active,
            };
        }

        // ② 站立 + 有水平速度 = 行走（贴地把 PositionY 作为判据，空中不算）。
        if (fighter.LogicState == FighterLogicState.Standing
            && fighter.VelocityX != FixedPoint.Zero
            && fighter.PositionY == FixedPoint.Zero)
        {
            if (set.States.TryGetValue("Walking", out string? walking))
                return walking;
        }

        // ③ 逻辑状态名。
        string stateName = fighter.LogicState.ToString();
        if (set.States.TryGetValue(stateName, out string? mapped))
            return mapped;

        // ④ 降级：招式缺配置 → Startup 段；否则兜底。
        if (fighter.IsInMove && set.States.TryGetValue("Startup", out string? startup))
            return startup;
        return set.FallbackClip.Name;
    }
}
