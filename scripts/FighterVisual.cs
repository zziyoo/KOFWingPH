using Godot;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.State;

namespace NewKOF;

/// <summary>
/// <b>角色视觉控制器</b>（阶段 10C，D125）：把 <see cref="AnimationResolver"/> 解析出的姿态
/// 应用到 Godot 节点上。
///
/// <para><b>单向数据流</b>：<c>FighterState → AnimationResolver → ResolvedPose → Node2D/ColorRect</c>。
/// 本类<b>绝不</b>写回战斗状态、不改位置速度、不判定命中或硬直；动画只是权威状态的投影。</para>
///
/// <para>姿态坐标以「脚在原点、Y 向上」定义，转成 Godot 的 Y 向下：矩形顶边 =
/// <c>-(offsetY + height)</c>。朝左时水平偏移与旋转取镜像。</para>
/// </summary>
public sealed class FighterVisual
{
    private readonly Node2D _root;
    private readonly ColorRect _body;
    private readonly FighterVisualClock _clock = new();

    private AnimationSet _set;
    private MugenSpriteVisual? _sprite;
    private readonly VisualFlash _flash = new();

    private readonly string? _visualOverride;
    private readonly string? _overrideFighter;

    public FighterVisual(Node2D root, ColorRect body, AnimationSet set, string? visualOverride = null, string? overrideFighter = null)
    {
        _root = root ?? throw new System.ArgumentNullException(nameof(root));
        _body = body ?? throw new System.ArgumentNullException(nameof(body));
        _set = set ?? throw new System.ArgumentNullException(nameof(set));
        _visualOverride = visualOverride;
        _overrideFighter = overrideFighter;
        _sprite = MugenSpriteVisual.TryLoad(_root, VisualId(set), OverrideApplies(set));
    }

    /// <summary>当前使用的表现配置。</summary>
    public AnimationSet AnimationSet => _set;

    /// <summary>当前动画名（诊断/HUD 用）。</summary>
    public string CurrentClip { get; private set; } = "-";

    /// <summary>切换表现配置（换人/换角色时）；同一实例不重置时钟。</summary>
    public void SetAnimationSet(AnimationSet set)
    {
        if (set is null)
            throw new System.ArgumentNullException(nameof(set));
        if (ReferenceEquals(_set, set))
            return;

        _set = set;
        _sprite?.Dispose();
        _sprite = MugenSpriteVisual.TryLoad(_root, VisualId(set), OverrideApplies(set));
        _clock.Reset();
        _flash.Reset();
    }

    private string VisualId(AnimationSet set) => SpriteBinding.SelectVisualId(set.FighterId, _visualOverride, _overrideFighter);
    private bool OverrideApplies(AnimationSet set) => _visualOverride is not null &&
        (_overrideFighter is null || _overrideFighter == set.FighterId);

    /// <summary>重置动画时钟（新回合/新交锋）。</summary>
    public void Reset()
    {
        _clock.Reset();
        _flash.Reset();
        _sprite?.SetFlash(false);
    }

    /// <summary>受击闪白：在指定逻辑帧之前把姿态染成白色（纯表现，不改任何状态）。</summary>
    public void FlashUntil(int logicFrame) =>
        _flash.Until(logicFrame);

    public bool AdvanceTerminalFlash(int logicFrame, double delta) => _flash.AdvanceTerminal(logicFrame, delta);

    /// <summary>
    /// 观察一个已提交逻辑帧（阶段 10 最终补修，D127/D128）：推进动画时钟。
    ///
    /// <para>必须按时间顺序、<b>每个逻辑帧调用一次</b>（不管该帧有没有事件）。冻结判据取
    /// <b>帧开始状态</b>的 <c>HitstopRemainingFrames</c>——递减到 0 的那一帧仍是冻结帧。</para>
    /// </summary>
    public void ObserveFrame(in PresentationFrame frame, int fighterIndex)
    {
        FighterState before = frame.StateBefore.Fighter(fighterIndex);
        FighterState after = frame.StateAfter.Fighter(fighterIndex);
        _clock.Observe(
            frame.LogicFrame,
            after.LogicState,
            after.MoveId,
            before.HitstopRemainingFrames > 0,
            AnimationResolver.ResolveClipName(after, _set));
        if (_sprite is not null && OS.GetCmdlineUserArgs().Contains("--sprite-frame-audit"))
        {
            string clip = AnimationResolver.ResolveClipName(after, _set);
            GD.Print($"[SpriteFrameAudit] fighter={fighterIndex} logic={frame.LogicFrame} state={after.LogicState} move={after.MoveId ?? "-"} frozen={before.HitstopRemainingFrames > 0} facing={after.Facing} " +
                _sprite.Describe(clip, _clock.VisualFrame, after.MoveElapsedFrames, after.IsInMove, after.MoveId, after.LogicState.ToString()));
        }
    }

    /// <summary>按权威状态刷新一次画面（只读投影；不再自行推进时钟）。</summary>
    public void Apply(FighterState fighter, int logicFrame)
    {
        // 时钟只由 ObserveFrame 推进：渲染频率、重复渲染都不会改变动画时间线。
        int visualFrame = _clock.VisualFrame;

        ResolvedPose pose = AnimationResolver.Resolve(fighter, _set, visualFrame);
        PoseFrame frame = pose.Frame;
        CurrentClip = $"{pose.ClipName}#{pose.FrameIndex}";

        bool facingLeft = fighter.Facing == Facing.Left;
        bool usedSprite = _sprite?.Apply(pose.ClipName, visualFrame, fighter.MoveElapsedFrames, fighter.IsInMove, facingLeft,
            fighter.MoveId, fighter.LogicState.ToString()) == true;
        if (_sprite is not null && OS.GetCmdlineUserArgs().Contains("--sprite-frame-audit"))
            GD.Print($"[SpriteRenderAudit] fighter={fighter.Id} logic={logicFrame} state={fighter.LogicState} sprite_selected={usedSprite} " +
                _sprite.Describe(pose.ClipName, visualFrame, fighter.MoveElapsedFrames, fighter.IsInMove, fighter.MoveId, fighter.LogicState.ToString()));
        _sprite?.SetFlash(_flash.ActiveAt(logicFrame));
        _body.Visible = !usedSprite;
        if (usedSprite)
        {
            _root.RotationDegrees = 0;
            return;
        }
        _sprite?.Hide();
        int offsetX = facingLeft ? -frame.OffsetX : frame.OffsetX;

        _body.Size = new Vector2(frame.Width, frame.Height);
        _body.Position = new Vector2(
            offsetX - frame.Width / 2f,
            -(frame.OffsetY + frame.Height));
        _body.Color = _flash.ActiveAt(logicFrame)
            ? Colors.White
            : Color.FromHtml(frame.Color);
        _root.RotationDegrees = facingLeft ? -frame.RotationDegrees : frame.RotationDegrees;
    }
}
