using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Presentation;

/// <summary>
/// <b>表现时钟</b>（阶段 10C，D125）：把权威逻辑帧换算成「动画已经过帧数」，
/// 并在 <b>Hitstop 期间停止推进</b>。
///
/// <para><b>只读投影</b>：本类不写入任何战斗状态，也不参与判定；它只是把
/// <c>Frame</c> / <c>HitstopRemainingFrames</c> 解释成动画时间。状态或招式发生变化时
/// 时钟归零，保证新动画从第一帧开始播放，而不是接着上一个动作的相位。</para>
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
    /// 与权威状态同步一次，返回可用于取动画帧的「已经过帧数」。
    /// 同一逻辑帧重复调用不会推进（渲染帧率高于逻辑帧率时安全）。
    /// </summary>
    public int Sync(int logicFrame, FighterLogicState state, string? moveId, int hitstopRemaining)
    {
        string stateKey = state.ToString();
        string moveKey = moveId ?? string.Empty;
        if (stateKey != _lastState || moveKey != _lastMove)
        {
            // 状态/招式切换：新动画从第 0 帧开始。
            VisualFrame = 0;
            _lastState = stateKey;
            _lastMove = moveKey;
        }
        else if (hitstopRemaining <= 0 && LastLogicFrame >= 0 && logicFrame > LastLogicFrame)
        {
            // 只在非冻结且逻辑帧真的前进时推进；渲染帧率无关。
            VisualFrame += logicFrame - LastLogicFrame;
        }

        LastLogicFrame = logicFrame;
        return VisualFrame;
    }

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
