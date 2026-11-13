using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Presentation;

/// <summary>Shared projection of the authoritative grab for both fighters in one bout.</summary>
public sealed class ThrowVisual
{
    private PendingThrow? _pending;
    private int _releasedBy = -1;
    private int _instance = -1;
    private string? _move;

    public void Observe(BattleState state, IReadOnlyList<BattleEvent> events)
    {
        _pending = state.PendingThrow;
        if (_releasedBy >= 0 && !IsReleasedMove(state.Fighter(_releasedBy)))
            _releasedBy = -1;
        foreach (var e in events)
        {
            if (e.Type != BattleEventType.ThrowHit)
                continue;
            FighterState source = state.Fighter(e.SourceFighterId);
            if (source.IsInMove && source.MoveId == e.MoveId)
            {
                _releasedBy = source.Id;
                _instance = source.MoveStartedFrame;
                _move = source.MoveId;
            }
        }
    }

    public string? Clip(FighterState fighter)
    {
        if (_pending?.Thrower == fighter.Id)
            return "throw_hold";
        if (_pending?.Target == fighter.Id)
            return "throw_caught";
        return IsReleasedMove(fighter) ? "throw_release" : null;
    }

    private bool IsReleasedMove(FighterState fighter) => fighter.Id == _releasedBy &&
        fighter.IsInMove && fighter.MoveStartedFrame == _instance && fighter.MoveId == _move;

    public void Reset()
    {
        _pending = null;
        _releasedBy = -1;
        _instance = -1;
        _move = null;
    }
}
