using System.Text.Json;
using Godot;
using NewKOF.BattleCore.Presentation;

namespace NewKOF;

/// <summary>Optional visual projection of imported AIR frames; never affects simulation.</summary>
internal sealed class MugenSpriteVisual
{
    private readonly Sprite2D _sprite;
    private readonly string _directory;
    private readonly float _scale;
    private readonly Dictionary<string, int> _bindings;
    private readonly Dictionary<int, ActionData> _actions;
    private readonly JsonElement[] _sprites;
    private readonly Dictionary<int, Texture2D> _textures = new();
    private sealed record ActionData(JsonElement[] Frames, SpriteTimeline Timeline);

    private MugenSpriteVisual(Node2D root, string directory, float scale,
        Dictionary<string, int> bindings, Dictionary<int, ActionData> actions, JsonElement[] sprites)
    {
        _directory = directory; _scale = scale; _bindings = bindings; _actions = actions; _sprites = sprites;
        _sprite = new Sprite2D { Centered = false, RegionEnabled = true,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Visible = false };
        root.AddChild(_sprite);
    }

    public static MugenSpriteVisual? TryLoad(Node2D root, string fighterId)
    {
        if (fighterId.Length == 0 || fighterId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            return null;
        string directory = $"res://data/imported-characters/{fighterId}/";
        string bindingPath = directory + "binding.json";
        string manifestPath = directory + "manifest.json";
        if (!Godot.FileAccess.FileExists(bindingPath) || !Godot.FileAccess.FileExists(manifestPath)) return null;
        try
        {
            using var binding = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(bindingPath));
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(manifestPath));
            var bindings = binding.RootElement.GetProperty("clips").EnumerateObject()
                .ToDictionary(x => x.Name, x => x.Value.GetInt32(), StringComparer.Ordinal);
            var rootData = document.RootElement;
            int localcoord = rootData.GetProperty("localcoord").GetInt32();
            if (localcoord <= 0 || localcoord > 8192) return null;
            var sprites = rootData.GetProperty("sprites").EnumerateArray().Select(x => x.Clone()).ToArray();
            var actions = new Dictionary<int, ActionData>();
            foreach (var a in rootData.GetProperty("actions").EnumerateArray())
            {
                var frames = a.GetProperty("frames").EnumerateArray().Select(x => x.Clone()).ToArray();
                if (frames.Length == 0) continue;
                int loop = a.GetProperty("loop_start").ValueKind == JsonValueKind.Null ? 0 : a.GetProperty("loop_start").GetInt32();
                if (loop >= frames.Length) loop = 0;
                var timeline = new SpriteTimeline(frames.Select(f => f.GetProperty("time").GetInt32()), loop);
                actions.TryAdd(a.GetProperty("action").GetInt32(), new ActionData(frames, timeline));
            }
            return new MugenSpriteVisual(root, directory, 320f / localcoord, bindings, actions, sprites);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        {
            GD.PushWarning($"Invalid optional MUGEN presentation for {fighterId}: {ex.Message}");
            return null;
        }
    }

    public void Dispose() => _sprite.QueueFree();
    public void Hide() => _sprite.Visible = false;

    public bool Apply(string clip, int elapsed, bool facingLeft)
    {
        if (!_bindings.TryGetValue(clip, out int id) || !_actions.TryGetValue(id, out var action)) return false;
        try
        {
            JsonElement frame = action.Frames[action.Timeline.FrameAt(elapsed)];
            var index = frame.GetProperty("sprite_index");
            if (index.ValueKind == JsonValueKind.Null)
            {
                Hide();
                // Negative identifiers are intentional AIR blank frames; missing positive keys use fallback.
                return frame.GetProperty("group").GetInt32() < 0 || frame.GetProperty("image").GetInt32() < 0;
            }
            int spriteIndex = index.GetInt32();
            if (spriteIndex < 0 || spriteIndex >= _sprites.Length) return false;
            JsonElement sprite = _sprites[spriteIndex], region = sprite.GetProperty("region");
            int page = region.GetProperty("page").GetInt32();
            if (page < 0 || page > 10000) return false;
            if (!_textures.TryGetValue(page, out var texture))
            {
                string path = _directory + $"atlas-{page:000}.png";
                if (!Godot.FileAccess.FileExists(path)) return false;
                Image image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
                if (image is null || image.IsEmpty()) return false;
                texture = ImageTexture.CreateFromImage(image);
                _textures.Add(page, texture);
            }
            string flip = frame.GetProperty("flip").GetString() ?? "";
            bool h = flip.Contains('H'), v = flip.Contains('V');
            float x = frame.GetProperty("x").GetInt32(), y = frame.GetProperty("y").GetInt32();
            int ox = sprite.GetProperty("origin_x").GetInt32(), oy = sprite.GetProperty("origin_y").GetInt32();
            float face = facingLeft ? -1 : 1;
            _sprite.Texture = texture;
            _sprite.RegionRect = new Rect2(region.GetProperty("x").GetInt32(), region.GetProperty("y").GetInt32(),
                region.GetProperty("w").GetInt32(), region.GetProperty("h").GetInt32());
            _sprite.Position = new Vector2(face * (x + (h ? ox : -ox)) * _scale, (y + (v ? oy : -oy)) * _scale);
            _sprite.Scale = new Vector2(face * (h ? -_scale : _scale), v ? -_scale : _scale);
            _sprite.Visible = true;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        {
            Hide();
            return false;
        }
    }
}
