namespace NewKOF.BattleCore.Presentation;

/// <summary>Impact effects age in committed frames, including frames spent in HitStop.</summary>
public static class FeedbackTiming
{
    public const int Lifetime = 9;

    public static float Progress(int born, int stateFrame, double terminalSeconds = 0) =>
        (float)Math.Clamp((stateFrame - (double)born + terminalSeconds * 60) / Lifetime, 0, 1);
}
