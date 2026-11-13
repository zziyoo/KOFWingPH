namespace NewKOF.BattleCore.Presentation;

/// <summary>Read-only projection of the most recent committed hit during grounded hitstun.</summary>
public sealed class HitReactionVisualPose
{
    public string? MoveId { get; private set; }
    public int Elapsed { get; private set; }

    public void Observe(bool hitstun, bool frozen, string? incomingMoveId)
    {
        if (!hitstun)
            Reset();
        else if (incomingMoveId is not null)
        {
            MoveId = incomingMoveId;
            Elapsed = 0;
        }
        else if (MoveId is not null && !frozen)
            Elapsed++;
    }

    public void Reset()
    {
        MoveId = null;
        Elapsed = 0;
    }
}
