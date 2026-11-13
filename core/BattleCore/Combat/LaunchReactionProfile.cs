using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>Optional source-authored launch, ground impact, rebound and recovery timing.</summary>
public sealed record LaunchReactionProfile
{
    public LaunchReactionProfile(int impactFrames, FixedPoint bounceHorizontalRetention,
        FixedPoint bounceVerticalVelocity, FixedPoint bounceGravity, int lieDownFrames, int wakeupFrames,
        int riseFrames, int lieImpactFrames, int transitionGravitySteps)
    {
        if (impactFrames < 1 || riseFrames < 1 || lieDownFrames < 2 || wakeupFrames < 1 ||
            lieImpactFrames < 1 || lieImpactFrames >= lieDownFrames)
            throw new ArgumentException("Launch reaction durations must permit each recovery phase.");
        if (bounceHorizontalRetention < FixedPoint.Zero || bounceHorizontalRetention > FixedPoint.One)
            throw new ArgumentOutOfRangeException(nameof(bounceHorizontalRetention));
        if (bounceGravity <= FixedPoint.Zero || bounceGravity > FixedPoint.FromInt(MovementDefinition.MaxGravityUnits))
            throw new ArgumentOutOfRangeException(nameof(bounceGravity));
        if (bounceVerticalVelocity <= bounceGravity * 2 || bounceVerticalVelocity > FixedPoint.FromInt(AttackDefinition.MaxLaunchUnits))
            throw new ArgumentOutOfRangeException(nameof(bounceVerticalVelocity));
        if (transitionGravitySteps is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(transitionGravitySteps));

        ImpactFrames = impactFrames;
        BounceHorizontalRetention = bounceHorizontalRetention;
        BounceVerticalVelocity = bounceVerticalVelocity;
        BounceGravity = bounceGravity;
        LieDownFrames = lieDownFrames;
        WakeupFrames = wakeupFrames;
        RiseFrames = riseFrames;
        LieImpactFrames = lieImpactFrames;
        TransitionGravitySteps = transitionGravitySteps;
    }

    public int ImpactFrames { get; }
    public FixedPoint BounceHorizontalRetention { get; }
    public FixedPoint BounceVerticalVelocity { get; }
    public FixedPoint BounceGravity { get; }
    public int LieDownFrames { get; }
    public int WakeupFrames { get; }
    public int RiseFrames { get; }
    public int LieImpactFrames { get; }
    public int TransitionGravitySteps { get; }
}
