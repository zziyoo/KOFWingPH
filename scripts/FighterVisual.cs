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
    private int _flashUntilFrame = -1;

    public FighterVisual(Node2D root, ColorRect body, AnimationSet set)
    {
        _root = root ?? throw new System.ArgumentNullException(nameof(root));
        _body = body ?? throw new System.ArgumentNullException(nameof(body));
        _set = set ?? throw new System.ArgumentNullException(nameof(set));
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
        _clock.Reset();
    }

    /// <summary>重置动画时钟（新回合/新交锋）。</summary>
    public void Reset()
    {
        _clock.Reset();
        _flashUntilFrame = -1;
    }

    /// <summary>受击闪白：在指定逻辑帧之前把姿态染成白色（纯表现，不改任何状态）。</summary>
    public void FlashUntil(int logicFrame) =>
        _flashUntilFrame = System.Math.Max(_flashUntilFrame, logicFrame);

    /// <summary>按权威状态刷新一次画面（只读投影）。</summary>
    public void Apply(FighterState fighter, int logicFrame)
    {
        int visualFrame = _clock.Sync(
            logicFrame, fighter.LogicState, fighter.MoveId, fighter.HitstopRemainingFrames);

        ResolvedPose pose = AnimationResolver.Resolve(fighter, _set, visualFrame);
        PoseFrame frame = pose.Frame;
        CurrentClip = $"{pose.ClipName}#{pose.FrameIndex}";

        bool facingLeft = fighter.Facing == Facing.Left;
        int offsetX = facingLeft ? -frame.OffsetX : frame.OffsetX;

        _body.Size = new Vector2(frame.Width, frame.Height);
        _body.Position = new Vector2(
            offsetX - frame.Width / 2f,
            -(frame.OffsetY + frame.Height));
        _body.Color = logicFrame < _flashUntilFrame
            ? Colors.White
            : Color.FromHtml(frame.Color);
        _root.RotationDegrees = facingLeft ? -frame.RotationDegrees : frame.RotationDegrees;
    }
}
