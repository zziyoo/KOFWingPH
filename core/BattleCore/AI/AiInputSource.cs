using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;
namespace NewKOF.BattleCore.AI;

/// <summary>One transaction owns both AI candidates and the original human buffer.</summary>
public sealed class AiInputSource : IInputSource
{
    private readonly Func<BattleState> _state;
    private readonly FightRoster _roster;
    private readonly InputBuffer _human;
    private readonly IAiPolicy?[] _policies;
    private readonly AiPolicyState?[] _states;
    private FrameInput? _prepared;
    private FrameInput _humanPrepared;
    private AiPolicyState?[]? _candidates;
    private readonly string _phase;
    private readonly List<FrameInput> _history = new();
    public IReadOnlyList<FrameInput> History { get; }
    public FrameInput? LastInput { get; private set; }
    public AiPolicyState? PolicyState(int player) => _states[player];
    public AiInputSource(Func<BattleState> state, FightRoster roster, InputBuffer human,
        IAiPolicy? player1 = null, IAiPolicy? player2 = null, uint seed = 1,
        AiPolicyState? state1 = null, AiPolicyState? state2 = null, string phase = "Battle", int scope = 1)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(roster); ArgumentNullException.ThrowIfNull(human);
        _state=state; _roster=roster; _human=human; _phase=phase;
        _policies=new[]{player1,player2};
        var current=state();
        _states=new[]{state1 ?? (player1 is null ? null : new AiPolicyState(current.Frame,seed,roster.Player1.FighterId)),
            state2 ?? (player2 is null ? null : new AiPolicyState(current.Frame,seed ^ 0xA511E9B3u,roster.Player2.FighterId))};
        if(scope < 1) throw new ArgumentOutOfRangeException(nameof(scope));
        for(int i=0;i<2;i++) if(_policies[i] is not null) {
            bool restoring = i==0 ? state1 is not null : state2 is not null;
            if(_states[i]!.NextFrame != current.Frame || _states[i]!.FighterId != roster.Fighter(i).FighterId ||
                (restoring && (_states[i]!.Player != i || _states[i]!.Scope != scope)))
                throw new ArgumentException("AI snapshot does not match the current fighter/frame/seat/scope.");
            _states[i]=_states[i]! with { Player=i, Scope=scope };
        }
        History=_history.AsReadOnly();
    }
    public FrameInput Prepare(int frame)
    {
        if(_prepared is { } cached) {
            if(cached.Frame != frame) throw new InvalidOperationException("Uncommitted AI frame.");
            return cached;
        }
        var battle=_state();
        if(battle.Frame != frame || (LastInput is { } last && last.Frame+1 != frame)) throw new InvalidOperationException("AI frame mismatch.");
        var human=_human.Prepare(frame);
        var next=new AiPolicyState?[2]; var bits=new[]{human.Player1,human.Player2};
        for(int i=0;i<2;i++) if(_policies[i] is { } policy) {
            var old=_states[i]!;
            if(old.NextFrame != frame || old.FighterId != _roster.Fighter(i).FighterId) throw new InvalidOperationException("AI identity mismatch.");
            var decision=policy.Decide(new AiObservation(battle,_roster,i,_phase),old);
            if(((ushort)decision.Bits & ~255) != 0 || decision.State.NextFrame != frame+1 || decision.State.FighterId != old.FighterId || decision.State.Player != old.Player || decision.State.Scope != old.Scope)
                throw new InvalidOperationException("Invalid AI candidate.");
            bits[i]=decision.Bits; next[i]=decision.State;
        }
        _humanPrepared=human; _candidates=next;
        return (_prepared=new FrameInput(frame,bits[0],bits[1])).Value;
    }
    /// <summary>Explicit recovery after a rejected/cancelled external transaction. Committed policy and PRNG remain unchanged.</summary>
    public void DiscardPrepared() { _prepared=null; _candidates=null; }
    public void Commit(FrameInput input)
    {
        // All potentially rejecting checks precede the first mutation. Only the concrete InputBuffer is composed.
        if(_prepared is not { } prepared || input != prepared || _candidates is null || _human.Prepare(input.Frame) != _humanPrepared)
            throw new InvalidOperationException("AI commit must match the prepared transaction.");
        _human.Commit(_humanPrepared);
        for(int i=0;i<2;i++) if(_policies[i] is not null) _states[i]=_candidates[i];
        LastInput=input; _history.Add(input); if(_history.Count>600) _history.RemoveAt(0);
        _prepared=null; _candidates=null;
    }
}
