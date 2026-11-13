namespace NewKOF.BattleCore.State;

/// <summary>Seat-local controller counters and the one-frame world pause queued for the next step.</summary>
public readonly record struct ScriptedPauseState(int Player1Counter, int Player2Counter, bool IsActive)
{
    public const int ResetCounter = 25;

    public int ForSeat(int seat) => seat switch
    {
        0 => Player1Counter,
        1 => Player2Counter,
        _ => throw new ArgumentOutOfRangeException(nameof(seat)),
    };

    internal void Validate(bool roundFinished)
    {
        if (Player1Counter is < 0 or > ResetCounter || Player2Counter is < 0 or > ResetCounter ||
            IsActive && Math.Max(Player1Counter, Player2Counter) == 0 || roundFinished && this != default)
            throw new ArgumentException("Scripted pause does not match the round or controller counters.");
    }
}
