using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Presentation;

/// <summary>
/// 一帧表现姿态（阶段 10C，D125）：脚在原点、Y 向上的矩形 + 旋转 + 颜色。
///
/// <para>之所以用「矩形姿态」而不是贴图，是因为当前没有可用的正式角色素材（C6）：
/// 先用几何占位把「状态 → 动画 → 画面」这条管线打通，未来换成贴图只需替换
/// <see cref="FighterVisual"/> 的绘制实现，不动内核与配置结构。</para>
/// </summary>
public readonly record struct PoseFrame(
    int Width,
    int Height,
    int OffsetX,
    int OffsetY,
    int RotationDegrees,
    string Color);

/// <summary>一段循环或单次的姿态序列（阶段 10C，D125）。</summary>
public sealed class AnimationClip
{
    public AnimationClip(string name, IEnumerable<PoseFrame> frames, bool loop, int framesPerStep = 6)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("动画名不能为空。", nameof(name));
        ArgumentNullException.ThrowIfNull(frames);

        PoseFrame[] list = frames.ToArray();
        if (list.Length == 0)
            throw new ArgumentException($"动画 {name} 至少需要一帧。", nameof(frames));
        if (framesPerStep < 1)
            throw new ArgumentOutOfRangeException(nameof(framesPerStep), framesPerStep,
                "每帧持续时间必须 ≥ 1 个逻辑帧。");

        Name = name;
        Frames = Array.AsReadOnly(list);
        Loop = loop;
        FramesPerStep = framesPerStep;
    }

    /// <summary>动画名（配置内的唯一键）。</summary>
    public string Name { get; }

    /// <summary>姿态序列（只读）。</summary>
    public IReadOnlyList<PoseFrame> Frames { get; }

    /// <summary>是否循环播放。</summary>
    public bool Loop { get; }

    /// <summary>每一帧停留的逻辑帧数。</summary>
    public int FramesPerStep { get; }

    /// <summary>按「已过的逻辑帧数」取姿态下标（不循环则夹在最后一帧）。</summary>
    public int FrameIndexAt(int elapsedFrames)
    {
        if (elapsedFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedFrames), elapsedFrames,
                "已经过帧数不能为负。");

        int step = elapsedFrames / FramesPerStep;
        return Loop ? step % Frames.Count : Math.Min(step, Frames.Count - 1);
    }
}

/// <summary>动画解析结果（阶段 10C，D125）：给表现层的只读姿态。</summary>
public readonly record struct ResolvedPose(string ClipName, int FrameIndex, PoseFrame Frame);

/// <summary>
/// <b>角色表现配置</b>（阶段 10C，D125）：状态/招式 → 动画名 → 姿态序列。
///
/// <para><b>与战斗数据严格分离</b>：本类型不参与任何权威判定，只被
/// <see cref="AnimationResolver"/> 读取；缺失动作允许降级到 fallback，
/// 而权威角色配置非法仍会拒绝开赛（两者不能混淆）。</para>
/// </summary>
public sealed class AnimationSet
{
    private readonly Dictionary<string, AnimationClip> _clips;
    private readonly Dictionary<string, string> _states;
    private readonly Dictionary<string, MoveAnimation> _moves;

    public AnimationSet(
        string fighterId,
        IEnumerable<AnimationClip> clips,
        IReadOnlyDictionary<string, string> states,
        IReadOnlyDictionary<string, MoveAnimation>? moves = null)
    {
        if (string.IsNullOrWhiteSpace(fighterId))
            throw new ArgumentException("角色 ID 不能为空。", nameof(fighterId));
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(states);

        FighterId = fighterId;
        _clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        foreach (AnimationClip clip in clips)
        {
            if (clip is null)
                throw new ArgumentException("动画列表包含 null。", nameof(clips));
            if (!_clips.TryAdd(clip.Name, clip))
                throw new ArgumentException($"动画名重复：{clip.Name}。", nameof(clips));
        }

        if (_clips.Count == 0)
            throw new ArgumentException("至少需要一个动画。", nameof(clips));

        _states = new Dictionary<string, string>(states, StringComparer.Ordinal);
        foreach ((string state, string clip) in _states)
        {
            if (!_clips.ContainsKey(clip))
                throw new ArgumentException($"状态 {state} 引用了不存在的动画 {clip}。", nameof(states));
        }

        _moves = moves is null
            ? new Dictionary<string, MoveAnimation>(StringComparer.Ordinal)
            : new Dictionary<string, MoveAnimation>(moves, StringComparer.Ordinal);
        foreach ((string moveId, MoveAnimation animation) in _moves)
        {
            if (animation is null)
                throw new ArgumentException($"招式 {moveId} 的动画配置为 null。", nameof(moves));
            foreach (string clip in animation.ReferencedClips())
            {
                if (!_clips.ContainsKey(clip))
                    throw new ArgumentException(
                        $"招式 {moveId} 引用了不存在的动画 {clip}。", nameof(moves));
            }
        }

        Clips = Array.AsReadOnly(_clips.Values.ToArray());
    }

    /// <summary>角色 ID（与权威 FighterId 对应）。</summary>
    public string FighterId { get; }

    /// <summary>全部动画（只读；顺序按插入）。</summary>
    public IReadOnlyList<AnimationClip> Clips { get; }

    /// <summary>状态 → 动画名（键名与 <see cref="FighterLogicState"/> 一致，另支持 <c>Walking</c>）。</summary>
    public IReadOnlyDictionary<string, string> States => _states;

    /// <summary>招式 → 三段动画。</summary>
    public IReadOnlyDictionary<string, MoveAnimation> Moves => _moves;

    /// <summary>兜底动画：优先 <c>idle</c>，否则第一个动画。</summary>
    public AnimationClip FallbackClip =>
        _clips.TryGetValue("idle", out AnimationClip? idle) ? idle : _clips.Values.First();

    /// <summary>按名字取动画；不存在返回 null（解析器据此降级）。</summary>
    public AnimationClip? FindClip(string name) =>
        _clips.TryGetValue(name, out AnimationClip? clip) ? clip : null;

    /// <summary>
    /// <b>缺失资源也允许降级</b>：一个只有 <c>idle</c> 的姿态集合，覆盖全部状态。
    /// 用于「某角色没有表现配置」或配置加载失败时的显式回退。
    /// </summary>
    public static AnimationSet CreateFallback(string fighterId, string color = "#B0B6C4")
    {
        var idle = new AnimationClip("idle", new[]
        {
            new PoseFrame(60, 120, 0, 0, 0, color),
        }, loop: true);

        var crouch = new AnimationClip("crouch", new[]
        {
            new PoseFrame(64, 70, 0, 0, 0, color),
        }, loop: true);

        var hurt = new AnimationClip("hurt", new[]
        {
            new PoseFrame(64, 112, -8, 0, -12, color),
        }, loop: true);

        var attack = new AnimationClip("attack", new[]
        {
            new PoseFrame(88, 118, 12, 0, 0, color),
        }, loop: true);

        var states = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Standing"] = "idle",
            ["Walking"] = "idle",
            ["Crouching"] = "crouch",
            ["PreJump"] = "idle",
            ["Airborne"] = "idle",
            ["Startup"] = "attack",
            ["Active"] = "attack",
            ["Recovery"] = "attack",
            ["Hitstun"] = "hurt",
            ["Blockstun"] = "hurt",
            ["AirHitstun"] = "hurt",
            ["Knockdown"] = "hurt",
            ["Wakeup"] = "hurt",
        };
        return new AnimationSet(fighterId, new[] { idle, crouch, hurt, attack }, states);
    }
}

/// <summary>一个招式的三段动画（阶段 10C，D125）。</summary>
public sealed class MoveAnimation
{
    public MoveAnimation(string startup, string active, string recovery)
    {
        Startup = Require(startup, nameof(startup));
        Active = Require(active, nameof(active));
        Recovery = Require(recovery, nameof(recovery));
    }

    public string Startup { get; }
    public string Active { get; }
    public string Recovery { get; }

    /// <summary>三段引用的动画名。</summary>
    public IEnumerable<string> ReferencedClips()
    {
        yield return Startup;
        yield return Active;
        yield return Recovery;
    }

    private static string Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("招式动画名不能为空。", name);
        return value;
    }
}
