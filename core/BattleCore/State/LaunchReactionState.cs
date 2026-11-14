using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.State;

public enum LaunchReactionPhase
{
    Shake,
    Lift,
    Rise,
    Fall,
    Impact,
    Bounce,
    LyingImpact,
    Lying,
    Wakeup,
    Tech,
}

/// <summary>The authored reaction phase and its local clock. Shake duration is owned by the hitstop snapshot.</summary>
public readonly record struct LaunchReactionState(LaunchReactionProfile Profile, LaunchReactionPhase Phase,
    int Ticks, bool GroundCrossed = false, FixedPoint ImpactHorizontalVelocity = default, int TechInputTicks = 0,
    int LaunchOwner = -1)
{
    public bool IsLying => Phase is LaunchReactionPhase.Impact or LaunchReactionPhase.Bounce or
        LaunchReactionPhase.LyingImpact or LaunchReactionPhase.Lying or LaunchReactionPhase.Wakeup;

    public bool RestrictsAirFollowupFrom(int owner) => Profile.SuppressOwnerProjectilesDuringFall &&
        LaunchOwner == owner && Phase is LaunchReactionPhase.Shake or LaunchReactionPhase.Fall;

    public FighterLogicState LogicState => Phase switch
    {
        LaunchReactionPhase.Impact or LaunchReactionPhase.LyingImpact or LaunchReactionPhase.Lying => FighterLogicState.Knockdown,
        LaunchReactionPhase.Wakeup or LaunchReactionPhase.Tech => FighterLogicState.Wakeup,
        _ => FighterLogicState.AirHitstun,
    };
}
