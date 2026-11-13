using Godot;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.State;

namespace NewKOF;

/// <summary>One scene object per projectile ID in the current bout.</summary>
internal sealed partial class ProjectileVisualLayer : Node2D
{
    private sealed record Entry(Node2D Root, Polygon2D Fallback, MugenSpriteVisual? Sprite, string Clip);
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly ProjectileVisuals _clock = new();

    public void Observe(BattleState state) => _clock.Observe(state);

    public void Apply(BattleState state, FighterVisual? p1, FighterVisual? p2, Vector2 origin)
    {
        bool audit = OS.GetCmdlineUserArgs().Contains("--sprite-frame-audit");
        if (audit)
            GD.Print($"[ProjectileSetAudit] state={state.Frame} count={state.Projectiles.Count}");
        foreach (int id in _entries.Keys.ToArray())
            if (!state.Projectiles.Any(p => p.ProjectileId == id))
                Remove(id);

        foreach (var projectile in state.Projectiles)
        {
            if (!_entries.TryGetValue(projectile.ProjectileId, out var entry))
            {
                var source = (projectile.OwnerFighterId == 0 ? p1 : p2)?.SpriteSource;
                entry = Create(projectile, source);
                _entries.Add(projectile.ProjectileId, entry);
            }

            int age = _clock.Age(projectile);
            bool left = ProjectileVisuals.FacesLeft(projectile);
            entry.Root.Position = origin + new Vector2((float)projectile.PositionX.ToDouble(), -(float)projectile.PositionY.ToDouble());
            bool sprite = entry.Sprite?.Apply(entry.Clip, age, 0, false, left) == true;
            entry.Fallback.Visible = !sprite;
            entry.Fallback.Scale = new Vector2(left ? -1 : 1, 0.85f + 0.05f * (age % 4));
            if (audit)
                GD.Print($"[ProjectileRenderAudit] state={state.Frame} id={projectile.ProjectileId} owner={projectile.OwnerFighterId} " +
                    $"age={age} left={left} x={projectile.PositionX.RawValue} y={projectile.PositionY.RawValue} sprite={sprite} " +
                    (entry.Sprite?.Describe(entry.Clip, age, 0, false, null, "") ?? "fallback=geometry"));
        }
    }

    private Entry Create(ProjectileState projectile, MugenSpriteVisual? source)
    {
        var root = new Node2D();
        AddChild(root);
        var fallback = new Polygon2D
        {
            Color = new Color("#FFBC5B"),
            Polygon = new[] { new Vector2(-21, -4), new Vector2(5, -9), new Vector2(13, 0),
                new Vector2(5, 9), new Vector2(-21, 4), new Vector2(-12, 0) },
        };
        root.AddChild(fallback);
        string clip = $"projectile_{projectile.TypeId}";
        return new Entry(root, fallback, source?.HasClip(clip) == true ? source.CreateCompanion(root) : null, clip);
    }

    private void Remove(int id)
    {
        var entry = _entries[id];
        entry.Root.Hide();
        entry.Root.QueueFree();
        _entries.Remove(id);
    }

    public void Clear()
    {
        foreach (int id in _entries.Keys.ToArray()) Remove(id);
        _clock.Reset();
    }
}
