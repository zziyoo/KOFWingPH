using NewKOF.BattleCore.Combat;
namespace NewKOF.BattleCore.State;

/// <summary>Immutable grab identity and settlement data. Window includes StartedFrame through StartedFrame+2.</summary>
public sealed record PendingThrow(int Thrower, int Target, int AttackStartFrame, string MoveId,
    int StartedFrame, ThrowDefinition Definition)
{
    public int RemainingFrames(int frame) => Math.Max(0, StartedFrame + 3 - frame);
    public void Validate(int frame)
    {
        if (Thrower is < 0 or > 1 || Target != 1 - Thrower || AttackStartFrame < 0 ||
            StartedFrame < AttackStartFrame || StartedFrame >= frame || frame > StartedFrame + 2 ||
            string.IsNullOrWhiteSpace(MoveId) || Definition is null || !Definition.Techable)
            throw new InvalidOperationException("Invalid pending throw snapshot.");
    }
}
