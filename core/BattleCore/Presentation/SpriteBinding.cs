using System.Text.Json;
using NewKOF.BattleCore.Combat;

namespace NewKOF.BattleCore.Presentation;

public sealed record SpriteClipBinding(int Action, bool MoveClock, int StartTick, int FirstFrame, int? LastFrame, bool Loop,
    IReadOnlyList<int>? FrameSchedule = null, int OffsetX = 0, int OffsetY = 0, string? SilhouetteColor = null)
{
    public int FrameAt(SpriteTimeline timeline, int elapsed, int frameCount)
    {
        if (FrameSchedule is null) return timeline.FrameAt(elapsed);
        int index = FrameSchedule[Math.Min(Math.Max(0, elapsed), FrameSchedule.Count - 1)];
        if (index >= frameCount) throw new ArgumentException("Scheduled frame is outside the action.");
        return index;
    }
}

/// <summary>Validated visual-only mapping. Version 1 integer clips remain supported.</summary>
public sealed class SpriteBinding
{
    private sealed record HitReactionBinding(string Initial, string Followup, int FollowupTick);

    public string VisualId { get; private init; } = "";
    public double Scale { get; private init; }
    public int OffsetX { get; private init; }
    public int OffsetY { get; private init; }
    public string? Fallback { get; private init; }
    public IReadOnlyDictionary<string, SpriteClipBinding> Clips { get; private init; } = null!;
    private Dictionary<string, Dictionary<string, string>> _moves = new(StringComparer.Ordinal);
    private Dictionary<string, HitReactionBinding> _hitReactions = new(StringComparer.Ordinal);
    private Dictionary<string, string> _states = new(StringComparer.Ordinal);
    public string ResolveClip(string defaultClip, string? moveId, string phase) =>
        moveId is not null && _moves.TryGetValue(moveId, out var stages) && stages.TryGetValue(phase, out var clip)
            ? clip : defaultClip;

    public string ResolveStateClip(string defaultClip, string state) =>
        _states.TryGetValue(state, out string? clip) ? clip : defaultClip;

    public (string Clip, int Tick)? ResolveHitReaction(string? moveId, int elapsed)
    {
        if (moveId is null || !_hitReactions.TryGetValue(moveId, out HitReactionBinding? reaction))
            return null;
        return elapsed < reaction.FollowupTick
            ? (reaction.Initial, Math.Max(0, elapsed - 1))
            : (reaction.Followup, elapsed - reaction.FollowupTick);
    }
    public static bool ValidId(string id) => id.Length is > 0 and <= 80 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    public static string SelectVisualId(string fighterId, string? visualOverride, string? onlyFighter) =>
        visualOverride is not null && (onlyFighter is null || onlyFighter == fighterId) ? visualOverride : fighterId;

    public static SpriteBinding Parse(string json, string expectedId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        int version = root.TryGetProperty("schemaVersion", out var v) ? v.GetInt32() : 1;
        if (version is not (1 or 2)) throw new ArgumentException("Unsupported binding schema.");
        string id = root.TryGetProperty("visualId", out var identifier) ? identifier.GetString() ?? "" : expectedId;
        if (!ValidId(id) || !string.Equals(id, expectedId, StringComparison.Ordinal)) throw new ArgumentException("Invalid visual ID.");
        double scale = root.TryGetProperty("scale", out var s) ? s.GetDouble() : 1;
        if (!double.IsFinite(scale) || scale <= 0 || scale > 16) throw new ArgumentException("Invalid visual scale.");
        int x = root.TryGetProperty("offsetX", out var ox) ? ox.GetInt32() : 0;
        int y = root.TryGetProperty("offsetY", out var oy) ? oy.GetInt32() : 0;
        if (Math.Abs((long)x) > 8192 || Math.Abs((long)y) > 8192) throw new ArgumentException("Invalid visual offset.");
        string? fallback = root.TryGetProperty("fallback", out var f) ? f.GetString() : null;
        var clips = new Dictionary<string, SpriteClipBinding>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("clips").EnumerateObject())
        {
            var c = item.Value;
            int action = c.ValueKind == JsonValueKind.Number ? c.GetInt32() : c.GetProperty("action").GetInt32();
            string clock = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("clock", out var cl) ? cl.GetString() ?? "" : "move";
            if (clock is not ("move" or "phase")) throw new ArgumentException("Invalid clip clock.");
            int start = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("startTick", out var st) ? st.GetInt32() : 0;
            int first = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("firstFrame", out var fi) ? fi.GetInt32() : 0;
            int? last = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("lastFrame", out var la) ? la.GetInt32() : null;
            bool loop = !(c.ValueKind == JsonValueKind.Object && c.TryGetProperty("loop", out var lp)) || lp.GetBoolean();
            if (start < 0 || first < 0 || (last.HasValue && last < first)) throw new ArgumentException("Invalid clip range.");
            IReadOnlyList<int>? schedule = null;
            if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty("frameSchedule", out var fs))
            {
                var values = fs.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                if (values.Length is < 1 or > 4096 || values.Any(v => v < 0) || clock != "move" || loop || start != 0 || first != 0 || last.HasValue)
                    throw new ArgumentException("Invalid frame schedule.");
                schedule = Array.AsReadOnly(values);
            }
            int offsetX = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("offsetX", out var cx) ? cx.GetInt32() : 0;
            int offsetY = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("offsetY", out var cy) ? cy.GetInt32() : 0;
            string? color = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("silhouetteColor", out var sc) ? sc.GetString() : null;
            if (Math.Abs((long)offsetX) > 8192 || Math.Abs((long)offsetY) > 8192 ||
                (color is not null && (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(char.IsAsciiHexDigit))))
                throw new ArgumentException("Invalid clip appearance.");
            clips.Add(item.Name, new SpriteClipBinding(action, clock == "move", start, first, last, loop, schedule, offsetX, offsetY, color));
        }
        if (fallback is not null && !clips.ContainsKey(fallback)) throw new ArgumentException("Unknown fallback clip.");
        var moves = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (root.TryGetProperty("moves", out var moveData))
        {
            foreach (var move in moveData.EnumerateObject())
            {
                var stages = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var stage in move.Value.EnumerateObject())
                {
                    string target = stage.Value.GetString() ?? "";
                    if (stage.Name is not ("Startup" or "Active" or "Recovery") || !clips.ContainsKey(target))
                        throw new ArgumentException("Invalid move phase binding.");
                    stages.Add(stage.Name, target);
                }
                moves.Add(move.Name, stages);
            }
        }
        var reactions = new Dictionary<string, HitReactionBinding>(StringComparer.Ordinal);
        if (root.TryGetProperty("hitReactions", out var reactionData))
        {
            foreach (var item in reactionData.EnumerateObject())
            {
                var reaction = item.Value;
                if (string.IsNullOrWhiteSpace(item.Name)
                    || !reaction.TryGetProperty("initial", out var initial) || initial.GetString() is not string initialName
                    || !reaction.TryGetProperty("followup", out var followup) || followup.GetString() is not string followupName
                    || !reaction.TryGetProperty("followupTick", out var tick) || !tick.TryGetInt32(out int switchTick)
                    || switchTick < 1 || initialName == followupName
                    || !clips.ContainsKey(initialName) || !clips.ContainsKey(followupName))
                    throw new ArgumentException($"Invalid hit reaction binding for {item.Name}.");
                reactions.Add(item.Name, new HitReactionBinding(initialName, followupName, switchTick));
            }
        }
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("states", out var stateData))
        {
            foreach (var item in stateData.EnumerateObject())
            {
                string target = item.Value.GetString() ?? "";
                if ((item.Name != "Walking" && !Enum.GetNames<FighterLogicState>().Contains(item.Name))
                    || !clips.ContainsKey(target))
                    throw new ArgumentException($"Invalid state clip binding for {item.Name}.");
                states.Add(item.Name, target);
            }
        }
        return new SpriteBinding { VisualId = id, Scale = scale, OffsetX = x, OffsetY = y, Clips = clips,
            Fallback = fallback, _moves = moves, _hitReactions = reactions, _states = states };
    }
}
