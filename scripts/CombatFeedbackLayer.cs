using Godot;
using NewKOF.BattleCore.Presentation;

namespace NewKOF;

/// <summary>Event anchored effects evaluated from the displayed battle snapshot.</summary>
public partial class CombatFeedbackLayer : Node2D
{
    private sealed record Spark(long Id, FeedbackKind Kind, Vector2 Anchor, Color Color, float Size, int Born);
    private readonly List<Spark> _sparks = new();
    private int _frame;
    private double _terminalSeconds;
    private Vector2 _shake;

    public bool Enabled { get; set; } = true;
    public int ActiveSparks => _sparks.Count;

    public void Spawn(FeedbackCue cue, Vector2 position, Color color, int size, int stateFrame)
    {
        if (Enabled)
            _sparks.Add(new Spark(cue.Identity, cue.Kind, position, color, size, stateFrame));
    }

    public void Present(int frame, Vector2 shake)
    {
        _frame = frame;
        _shake = shake;
        UpdateSparks();
    }

    public void AdvanceTerminal(double delta)
    {
        if (_sparks.Count == 0) return;
        _terminalSeconds += delta;
        UpdateSparks();
    }

    private void UpdateSparks()
    {
        _sparks.RemoveAll(s => FeedbackTiming.Progress(s.Born, _frame, _terminalSeconds) >= 1);
        QueueRedraw();
    }

    public void Clear()
    {
        _sparks.Clear();
        _terminalSeconds = 0;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var s in _sparks)
        {
            float t = FeedbackTiming.Progress(s.Born, _frame, _terminalSeconds);
            Vector2 center = s.Anchor + _shake;
            float radius = s.Size * (0.24f + 0.55f * t);
            var color = new Color(s.Color, 1f - t);
            bool ring = s.Kind is FeedbackKind.Blocked or FeedbackKind.ProjectileBlocked or FeedbackKind.ThrowTech;
            if (ring)
                DrawArc(center, radius, 0, Mathf.Tau, 24, color, 2f, true);
            else
                DrawCircle(center, radius * 0.42f, color, true, -1, true);

            int rays = ring ? 4 : 8;
            float angle = (s.Id % 6) * Mathf.Pi / 3f;
            for (int i = 0; i < rays; i++)
            {
                Vector2 dir = Vector2.FromAngle(angle + i * Mathf.Tau / rays);
                DrawLine(center + dir * (radius * 0.55f), center + dir * radius, color, 2f, true);
            }
        }
    }
}
