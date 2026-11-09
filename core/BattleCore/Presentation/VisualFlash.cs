namespace NewKOF.BattleCore.Presentation;

/// <summary>Presentation-only exclusive flash end, in committed logic frames.</summary>
public sealed class VisualFlash
{
    private int _until = -1;
    private double _terminalSeconds;
    public void Until(int frame) { _until = Math.Max(_until, frame); _terminalSeconds = 0; }
    public bool ActiveAt(int frame) => frame < _until;
    public void Reset() { _until = -1; _terminalSeconds = 0; }
    /// <summary>Only after a round has stopped: finish transient colour feedback without moving animation.</summary>
    public bool AdvanceTerminal(int frame, double deltaSeconds)
    {
        if (!ActiveAt(frame)) return false;
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        _terminalSeconds += deltaSeconds;
        if (_terminalSeconds * 60 < _until - (long)frame) return false;
        Reset();
        return true;
    }
    public static bool IsImpact(FeedbackKind kind) => kind is FeedbackKind.Hit or FeedbackKind.ProjectileImpact or FeedbackKind.ThrowImpact;
}
