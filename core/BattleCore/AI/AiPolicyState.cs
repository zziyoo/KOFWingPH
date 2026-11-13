using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.AI;

public readonly record struct AiCommandStep(CommandDirection Direction, InputBits Buttons);

/// <summary>Only committed public information. No keyboard state or in-flight human input is exposed.</summary>
public sealed record AiObservation(BattleState State, FightRoster Roster, int Player, string Phase = "Battle")
{
    public int Frame => State.Frame;
    public FighterState Self => State.Fighter(Player);
    public FighterState Opponent => State.Fighter(1-Player);
    public FighterDefinition Definition => Roster.Fighter(Player);
    public FighterDefinition OpponentDefinition => Roster.Fighter(1-Player);
    public FixedPoint Distance => Self.PositionX >= Opponent.PositionX ? Self.PositionX-Opponent.PositionX : Opponent.PositionX-Self.PositionX;
    public bool CanAct => Self.LogicState is FighterLogicState.Standing or FighterLogicState.Crouching;
    public bool IsAirborne => Self.LogicState.IsAirborneLike();
    public bool OpponentAttacking => Opponent.LogicState is FighterLogicState.Startup or FighterLogicState.Active;
    public bool OpponentVulnerable => Opponent.LogicState is FighterLogicState.Recovery or FighterLogicState.Hitstun or FighterLogicState.Blockstun;
    public IReadOnlyList<ProjectileState> Projectiles => State.Projectiles;
    public PendingThrow? Grab => State.PendingThrow;
    public int? Timer => State.RoundTimerRemainingFrames;
}

/// <summary>In-memory save point. All collections are defensively copied and read-only.</summary>
public sealed record AiPolicyState
{
    public const int FormatVersion = 1;
    public int Player { get; init; } = -1;
    public int Scope { get; init; } = 1;
    public int NextFrame { get; init; }
    public uint RandomState { get; init; }
    public string FighterId { get; init; }
    public int NextDecisionFrame { get; init; }
    public int ThreatSinceFrame { get; init; } = -1;
    public int PlanIndex { get; init; }
    public string Action { get; init; } = "Observe";
    public string? TargetMove { get; init; }
    public IReadOnlyList<AiCommandStep> Plan { get; private init; }
    public IReadOnlyList<AiObservation> Observations { get; private init; }
    public AiPolicyState(int nextFrame, uint seed, string fighterId,
        IEnumerable<AiCommandStep>? plan = null, IEnumerable<AiObservation>? observations = null)
    {
        if(nextFrame < 0 || string.IsNullOrWhiteSpace(fighterId)) throw new ArgumentException("Invalid AI snapshot.");
        NextFrame=nextFrame; RandomState=seed==0 ? 0x9E3779B9u : seed; FighterId=fighterId;
        Plan=Array.AsReadOnly((plan ?? Array.Empty<AiCommandStep>()).ToArray());
        Observations=Array.AsReadOnly((observations ?? Array.Empty<AiObservation>()).ToArray());
    }
    public bool ContentEquals(AiPolicyState other) =>
        Player==other.Player && Scope==other.Scope && NextFrame==other.NextFrame && RandomState==other.RandomState && FighterId==other.FighterId &&
        NextDecisionFrame==other.NextDecisionFrame && ThreatSinceFrame==other.ThreatSinceFrame &&
        PlanIndex==other.PlanIndex && Action==other.Action && TargetMove==other.TargetMove && Plan.SequenceEqual(other.Plan) &&
        Observations.Count==other.Observations.Count && Observations.Zip(other.Observations).All(pair =>
            pair.First.Frame==pair.Second.Frame && pair.First.Player==pair.Second.Player && pair.First.Phase==pair.Second.Phase &&
            pair.First.State.Fighters.SequenceEqual(pair.Second.State.Fighters) &&
            pair.First.State.Projectiles.SequenceEqual(pair.Second.State.Projectiles) &&
            pair.First.State.NextProjectileId==pair.Second.State.NextProjectileId &&
            pair.First.State.StateVersion==pair.Second.State.StateVersion &&
            pair.First.State.ScriptedPause==pair.Second.State.ScriptedPause &&
            pair.First.State.PendingThrow==pair.Second.State.PendingThrow &&
            pair.First.State.Round==pair.Second.State.Round && pair.First.Timer==pair.Second.Timer);

    public AiPolicyState WithPlan(IEnumerable<AiCommandStep> plan) => this with { Plan=Array.AsReadOnly(plan.ToArray()), PlanIndex=0 };
    public AiPolicyState Observe(AiObservation observation) => this with {
        Observations=Array.AsReadOnly(Observations.Append(observation).TakeLast(121).ToArray()) };
}
public readonly record struct AiDecision(InputBits Bits, AiPolicyState State);
public interface IAiPolicy { AiDecision Decide(AiObservation observation, AiPolicyState committed); }
