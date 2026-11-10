using System.Text.Json;
using Godot;
using NewKOF.BattleCore.Presentation;

namespace NewKOF;

/// <summary>Optional visual projection of imported AIR frames; never affects simulation.</summary>
internal sealed class MugenSpriteVisual
{
    private readonly Sprite2D _sprite;
    private readonly ShaderMaterial _flashMaterial;
    private readonly string _directory;
    private readonly float _scale;
    private readonly SpriteBinding _binding;
    private readonly Dictionary<string, (SpriteClipBinding Clip, ActionData Action, SpriteTimeline Timeline)> _clips;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly JsonElement[] _sprites;
    private readonly Dictionary<int, Texture2D> _textures = new();
    private readonly HashSet<int> _missingPages = new();
    private readonly Func<string, Texture2D?> _loadTexture;
    private sealed record ActionData(JsonElement[] Frames, int LoopStart);
    public bool HasClip(string clip) => _clips.ContainsKey(clip);

    // Read-only projection for correlating committed battle frames with AIR data.
    public string Describe(string clip, int visualFrame, int moveFrame, bool inMove, string? moveId, string phase)
    {
        if (inMove) clip = _binding.ResolveClip(clip, moveId, phase);
        if (!_clips.TryGetValue(clip, out var mapped) &&
            (_binding.Fallback is null || !_clips.TryGetValue(_binding.Fallback, out mapped)))
            return $"clip={clip} fallback=geometry";
        int elapsed = inMove && mapped.Clip.MoveClock ? moveFrame : visualFrame;
        int tick = (int)Math.Min(int.MaxValue, (long)Math.Max(0, elapsed) + mapped.Clip.StartTick);
        int index = mapped.Clip.FrameAt(mapped.Timeline, tick, mapped.Action.Frames.Length);
        JsonElement frame = mapped.Action.Frames[index];
        return $"clip={clip} action={mapped.Clip.Action} tick={tick} frame={index} sprite={frame.GetProperty("group").GetInt32()},{frame.GetProperty("image").GetInt32()} sprite_index={frame.GetProperty("sprite_index")} duration={frame.GetProperty("time").GetInt32()}";
    }

    private MugenSpriteVisual(Node2D root, string directory, float scale,
        SpriteBinding binding, Dictionary<int, ActionData> actions, JsonElement[] sprites, Func<string, Texture2D?> loadTexture)
    {
        _directory = directory; _scale = scale * (float)binding.Scale; _binding = binding; _sprites = sprites; _loadTexture = loadTexture;
        _clips = new(StringComparer.Ordinal);
        foreach (var (name, clip) in binding.Clips)
        {
            if (!actions.TryGetValue(clip.Action, out var action)) continue;
            if (clip.FrameSchedule is not null && clip.FrameSchedule.Any(index => index >= action.Frames.Length))
                throw new ArgumentException($"Scheduled frame outside action {clip.Action}.");
            var timeline = new SpriteTimeline(action.Frames.Select(f => f.GetProperty("time").GetInt32()),
                action.LoopStart, clip.Loop, clip.FirstFrame, clip.LastFrame);
            _clips.Add(name, (clip, action, timeline));
        }
        _sprite = new Sprite2D { Centered = false, RegionEnabled = true,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Visible = false };
        _flashMaterial = new ShaderMaterial { Shader = new Shader { Code =
            "shader_type canvas_item; uniform bool flash = false; void fragment() { if (flash) { COLOR.rgb = vec3(1.0); } }" } };
        _sprite.Material = _flashMaterial;
        root.AddChild(_sprite);
    }

    public static MugenSpriteVisual? TryLoad(Node2D root, string fighterId, bool required = false,
        string baseDirectory = "res://data/imported-characters/", Func<string, Texture2D?>? loadTexture = null)
    {
        if (!SpriteBinding.ValidId(fighterId))
        {
            if (required) GD.PushWarning("MUGEN: invalid visual ID; geometric fallback.");
            return null;
        }
        string directory = $"{baseDirectory}{fighterId}/";
        string bindingPath = directory + "binding.json";
        string manifestPath = directory + "manifest.json";
        if (!Godot.FileAccess.FileExists(bindingPath) || !Godot.FileAccess.FileExists(manifestPath))
        {
            if (required) GD.PushWarning($"MUGEN {fighterId}: missing manifest or binding; geometric fallback.");
            return null;
        }
        try
        {
            var binding = SpriteBinding.Parse(Godot.FileAccess.GetFileAsString(bindingPath), fighterId);
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(manifestPath));
            var rootData = document.RootElement;
            int localcoord = rootData.GetProperty("localcoord").GetInt32();
            if (localcoord <= 0 || localcoord > 8192) throw new ArgumentException("Invalid localcoord.");
            var sprites = rootData.GetProperty("sprites").EnumerateArray().Select(x => x.Clone()).ToArray();
            var actions = new Dictionary<int, ActionData>();
            foreach (var a in rootData.GetProperty("actions").EnumerateArray())
            {
                var frames = a.GetProperty("frames").EnumerateArray().Select(x => x.Clone()).ToArray();
                if (frames.Length == 0) continue;
                int loop = a.GetProperty("loop_start").ValueKind == JsonValueKind.Null ? 0 : a.GetProperty("loop_start").GetInt32();
                if (loop >= frames.Length) loop = 0;
                actions.TryAdd(a.GetProperty("action").GetInt32(), new ActionData(frames, loop));
            }
            return new MugenSpriteVisual(root, directory, 320f / localcoord, binding, actions, sprites, loadTexture ?? LoadProjectTexture);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException or FormatException or System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Invalid optional MUGEN presentation for {fighterId}: {ex.Message}");
            return null;
        }
    }

    private static Texture2D? LoadProjectTexture(string path) =>
        ResourceLoader.Exists(path, "Texture2D") ? ResourceLoader.Load<Texture2D>(path) : null;

    public void Dispose() { _sprite.Visible = false; _sprite.Texture = null; _textures.Clear(); _sprite.QueueFree(); }
    public void Hide() => _sprite.Visible = false;
    public void SetFlash(bool enabled) => _flashMaterial.SetShaderParameter("flash", enabled);

    private bool Missing(string message)
    {
        if (_reported.Add(message)) GD.PushWarning($"MUGEN {_binding.VisualId}: {message}; geometric fallback.");
        Hide();
        return false;
    }

    public bool Apply(string clip, int visualFrame, int moveFrame, bool inMove, bool facingLeft, string? moveId = null, string phase = "")
    {
        if (inMove) clip = _binding.ResolveClip(clip, moveId, phase);
        if (!_clips.TryGetValue(clip, out var mapped) &&
            (_binding.Fallback is null || !_clips.TryGetValue(_binding.Fallback, out mapped)))
            return Missing($"unmapped clip or missing action: {clip}");
        try
        {
            int elapsed = inMove && mapped.Clip.MoveClock ? moveFrame : visualFrame;
            long shifted = (long)Math.Max(0, elapsed) + mapped.Clip.StartTick;
            JsonElement frame = mapped.Action.Frames[mapped.Clip.FrameAt(mapped.Timeline, (int)Math.Min(int.MaxValue, shifted), mapped.Action.Frames.Length)];
            var index = frame.GetProperty("sprite_index");
            if (index.ValueKind == JsonValueKind.Null)
            {
                Hide();
                // Negative identifiers are intentional AIR blank frames; missing positive keys use fallback.
                return frame.GetProperty("group").GetInt32() < 0 || frame.GetProperty("image").GetInt32() < 0 ||
                    Missing($"missing sprite in clip {clip}");
            }
            int spriteIndex = index.GetInt32();
            if (spriteIndex < 0 || spriteIndex >= _sprites.Length) return Missing($"invalid sprite index {spriteIndex}");
            JsonElement sprite = _sprites[spriteIndex], region = sprite.GetProperty("region");
            int page = region.GetProperty("page").GetInt32();
            if (page < 0 || page > 10000) return Missing($"invalid atlas page {page}");
            if (_missingPages.Contains(page)) return Missing($"missing atlas {page}");
            if (!_textures.TryGetValue(page, out var texture))
            {
                string path = _directory + $"atlas-{page:000}.png";
                try { texture = _loadTexture(path); }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _missingPages.Add(page);
                    return Missing($"failed to load atlas {page}: {ex.Message}");
                }
                if (texture is null) { _missingPages.Add(page); return Missing($"failed to load atlas {page}"); }
                _textures.Add(page, texture);
            }
            string flip = frame.GetProperty("flip").GetString() ?? "";
            bool h = flip.Contains('H'), v = flip.Contains('V');
            float x = frame.GetProperty("x").GetInt32(), y = frame.GetProperty("y").GetInt32();
            int ox = sprite.GetProperty("origin_x").GetInt32(), oy = sprite.GetProperty("origin_y").GetInt32();
            float face = facingLeft ? -1 : 1;
            int rx = region.GetProperty("x").GetInt32(), ry = region.GetProperty("y").GetInt32();
            int rw = region.GetProperty("w").GetInt32(), rh = region.GetProperty("h").GetInt32();
            if (rx < 0 || ry < 0 || rw <= 0 || rh <= 0 || (long)rx + rw > texture.GetWidth() || (long)ry + rh > texture.GetHeight())
                return Missing($"invalid atlas region in {clip}");
            _sprite.Texture = texture;
            _sprite.RegionRect = new Rect2(rx, ry, rw, rh);
            _sprite.Position = new Vector2(face * (x + _binding.OffsetX + (h ? ox : -ox)) * _scale, (y + _binding.OffsetY + (v ? oy : -oy)) * _scale);
            _sprite.Scale = new Vector2(face * (h ? -_scale : _scale), v ? -_scale : _scale);
            _sprite.Visible = true;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException or FormatException or System.IO.IOException or UnauthorizedAccessException)
        {
            return Missing($"invalid frame in {clip}: {ex.Message}");
        }
    }
}
