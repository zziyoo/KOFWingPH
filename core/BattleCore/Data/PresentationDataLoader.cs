using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NewKOF.BattleCore.Presentation;

namespace NewKOF.BattleCore.Data;

/// <summary>
/// 表现配置加载失败（阶段 10C，D125）。消息固定包含
/// <c>来源 / 字段路径 / 原因</c>，例如
/// <c>presentation/Kai.json: animations[2].frames[0].width 必须在 1..400 内（实际 999）</c>。
/// </summary>
public sealed class PresentationDataException : Exception
{
    public PresentationDataException(string sourceName, string fieldPath, string reason, Exception? inner = null)
        : base($"{sourceName}: {fieldPath} {reason}", inner)
    {
        SourceName = sourceName;
        FieldPath = fieldPath;
        Reason = reason;
    }

    /// <summary>来源文件名（如 <c>presentation/Kai.json</c>）。</summary>
    public string SourceName { get; }

    /// <summary>出错字段路径。</summary>
    public string FieldPath { get; }

    /// <summary>失败原因。</summary>
    public string Reason { get; }
}

/// <summary>一份表现配置文档（来源路径 + 文本），与 JSON 加载器解耦（阶段 10C，D125）。</summary>
public sealed class PresentationDataDocument
{
    public PresentationDataDocument(string sourceName, string json)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("来源名不能为空。", nameof(sourceName));
        SourceName = sourceName;
        Json = json ?? throw new ArgumentNullException(nameof(json));
    }

    /// <summary>来源路径（错误信息用）。</summary>
    public string SourceName { get; }

    /// <summary>JSON 文本。</summary>
    public string Json { get; }
}

/// <summary>
/// <b>表现配置加载器</b>（阶段 10C，D125）：<c>JSON → DTO → 严格校验 → AnimationSet</c>。
///
/// <para><b>与战斗配置严格分离</b>：本加载器不读、不写、也不校验 <c>data/fighters/*.json</c>；
/// 表现资源缺失允许降级（调用方用 <see cref="AnimationSet.CreateFallback"/>），
/// 但表现配置<b>格式非法必须报错</b>，绝不静默用默认姿态掩盖。</para>
///
/// <para>规则：<c>schemaVersion</c> 必须存在且为 1；未知字段拒绝（
/// <see cref="JsonUnmappedMemberHandling.Disallow"/>）；动画名唯一；<c>states</c>/<c>moves</c>
/// 引用的动画必须存在；数值范围显式校验。</para>
/// </summary>
public static class PresentationDataLoader
{
    /// <summary>当前支持的表现配置版本。</summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
    };

    private static readonly Regex ColorPattern =
        new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const int MaxPoseSize = 400;
    private const int MaxPoseOffset = 400;

    /// <summary>从字符串加载。</summary>
    public static AnimationSet Load(string json, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(json);

        PresentationDocumentDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<PresentationDocumentDto>(json, Options)
                ?? throw Fail(sourceName, "<json>", "JSON 根节点为 null。");
        }
        catch (PresentationDataException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new PresentationDataException(sourceName, "<json>", $"JSON 格式错误：{ex.Message}", ex);
        }

        if (dto.SchemaVersion is not int version)
            throw Fail(sourceName, "schemaVersion", "必填字段缺失。");
        if (version != SupportedSchemaVersion)
            throw Fail(sourceName, "schemaVersion",
                $"不支持的版本 {version}（当前只支持 {SupportedSchemaVersion}）。");
        if (string.IsNullOrWhiteSpace(dto.FighterId))
            throw Fail(sourceName, "fighterId", "必填字段缺失或为空。");
        if (dto.Animations is null || dto.Animations.Count == 0)
            throw Fail(sourceName, "animations", "必填字段缺失或为空。");
        if (dto.States is null || dto.States.Count == 0)
            throw Fail(sourceName, "states", "必填字段缺失或为空。");

        var clips = new List<AnimationClip>(dto.Animations.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < dto.Animations.Count; i++)
        {
            AnimationDto? clip = dto.Animations[i];
            string path = $"animations[{i}]";
            if (clip is null)
                throw Fail(sourceName, path, "动画不能为 null。");
            if (string.IsNullOrWhiteSpace(clip.Name))
                throw Fail(sourceName, $"{path}.name", "必填字段缺失或为空。");
            if (!seen.Add(clip.Name))
                throw Fail(sourceName, $"{path}.name", $"动画名重复：{clip.Name}。");
            if (clip.Frames is null || clip.Frames.Count == 0)
                throw Fail(sourceName, $"{path}.frames", "至少需要一帧。");

            int framesPerStep = clip.FramesPerStep ?? 6;
            if (framesPerStep < 1 || framesPerStep > 120)
                throw Fail(sourceName, $"{path}.framesPerStep",
                    $"必须在 1..120 内（实际 {framesPerStep}）。");

            var frames = new List<PoseFrame>(clip.Frames.Count);
            for (int f = 0; f < clip.Frames.Count; f++)
            {
                FrameDto? frame = clip.Frames[f];
                string framePath = $"{path}.frames[{f}]";
                if (frame is null)
                    throw Fail(sourceName, framePath, "帧不能为 null。");

                int width = RequireRange(sourceName, $"{framePath}.width", frame.Width, 1, MaxPoseSize);
                int height = RequireRange(sourceName, $"{framePath}.height", frame.Height, 1, MaxPoseSize);
                int offsetX = RequireRange(sourceName, $"{framePath}.offsetX", frame.OffsetX ?? 0,
                    -MaxPoseOffset, MaxPoseOffset);
                int offsetY = RequireRange(sourceName, $"{framePath}.offsetY", frame.OffsetY ?? 0,
                    -MaxPoseOffset, MaxPoseOffset);
                int rotation = RequireRange(sourceName, $"{framePath}.rotation", frame.Rotation ?? 0,
                    -180, 180);
                string color = frame.Color ?? "#B0B6C4";
                if (!ColorPattern.IsMatch(color))
                    throw Fail(sourceName, $"{framePath}.color",
                        $"必须是 #RRGGBB 形式（实际 {color}）。");

                frames.Add(new PoseFrame(width, height, offsetX, offsetY, rotation, color));
            }

            clips.Add(new AnimationClip(clip.Name, frames, clip.Loop ?? true, framesPerStep));
        }

        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string state, string clipName) in dto.States)
        {
            if (string.IsNullOrWhiteSpace(state))
                throw Fail(sourceName, "states", "状态名不能为空。");
            if (string.IsNullOrWhiteSpace(clipName))
                throw Fail(sourceName, $"states.{state}", "动画名不能为空。");
            if (!seen.Contains(clipName))
                throw Fail(sourceName, $"states.{state}", $"引用了不存在的动画 {clipName}。");
            states[state] = clipName;
        }

        var moves = new Dictionary<string, MoveAnimation>(StringComparer.Ordinal);
        if (dto.Moves is not null)
        {
            foreach ((string moveId, MoveDto? move) in dto.Moves)
            {
                if (string.IsNullOrWhiteSpace(moveId))
                    throw Fail(sourceName, "moves", "招式 ID 不能为空。");
                if (move is null)
                    throw Fail(sourceName, $"moves.{moveId}", "配置不能为 null。");

                string startup = RequireClip(sourceName, $"moves.{moveId}.startup", move.Startup, seen);
                string active = RequireClip(sourceName, $"moves.{moveId}.active", move.Active, seen);
                string recovery = RequireClip(sourceName, $"moves.{moveId}.recovery", move.Recovery, seen);
                moves[moveId] = new MoveAnimation(startup, active, recovery);
            }
        }

        return new AnimationSet(dto.FighterId, clips, states, moves);
    }

    /// <summary>从流加载。</summary>
    public static AnimationSet Load(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return Load(reader.ReadToEnd(), sourceName);
    }

    /// <summary>批量加载目录（FighterId 必须唯一）。</summary>
    public static IReadOnlyDictionary<string, AnimationSet> LoadCatalog(
        IEnumerable<PresentationDataDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var result = new Dictionary<string, AnimationSet>(StringComparer.Ordinal);
        foreach (PresentationDataDocument document in documents)
        {
            if (document is null)
                throw new ArgumentException("文档列表包含 null。", nameof(documents));

            AnimationSet set = Load(document.Json, document.SourceName);
            if (!result.TryAdd(set.FighterId, set))
                throw new ArgumentException(
                    $"表现配置的角色 ID 重复：{set.FighterId}（{document.SourceName}）。",
                    nameof(documents));
        }

        return result;
    }

    private static string RequireClip(
        string sourceName, string path, string? value, HashSet<string> known)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Fail(sourceName, path, "必填字段缺失或为空。");
        if (!known.Contains(value))
            throw Fail(sourceName, path, $"引用了不存在的动画 {value}。");
        return value;
    }

    private static int RequireRange(
        string sourceName, string path, int? value, int min, int max)
    {
        if (value is not int actual)
            throw Fail(sourceName, path, "必填字段缺失。");
        if (actual < min || actual > max)
            throw Fail(sourceName, path, $"必须在 {min}..{max} 内（实际 {actual}）。");
        return actual;
    }

    private static PresentationDataException Fail(string sourceName, string path, string reason) =>
        new(sourceName, path, reason);

    // ── DTO（严格：未知字段拒绝、缺省用可空类型显式判定） ──────────────

    private sealed class PresentationDocumentDto
    {
        [JsonPropertyName("schemaVersion")] public int? SchemaVersion { get; set; }
        [JsonPropertyName("fighterId")] public string? FighterId { get; set; }
        [JsonPropertyName("animations")] public List<AnimationDto?>? Animations { get; set; }
        [JsonPropertyName("states")] public Dictionary<string, string>? States { get; set; }
        [JsonPropertyName("moves")] public Dictionary<string, MoveDto?>? Moves { get; set; }
    }

    private sealed class AnimationDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("loop")] public bool? Loop { get; set; }
        [JsonPropertyName("framesPerStep")] public int? FramesPerStep { get; set; }
        [JsonPropertyName("frames")] public List<FrameDto?>? Frames { get; set; }
    }

    private sealed class FrameDto
    {
        [JsonPropertyName("width")] public int? Width { get; set; }
        [JsonPropertyName("height")] public int? Height { get; set; }
        [JsonPropertyName("offsetX")] public int? OffsetX { get; set; }
        [JsonPropertyName("offsetY")] public int? OffsetY { get; set; }
        [JsonPropertyName("rotation")] public int? Rotation { get; set; }
        [JsonPropertyName("color")] public string? Color { get; set; }
    }

    private sealed class MoveDto
    {
        [JsonPropertyName("startup")] public string? Startup { get; set; }
        [JsonPropertyName("active")] public string? Active { get; set; }
        [JsonPropertyName("recovery")] public string? Recovery { get; set; }
    }
}
