using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

public sealed record GroundTechProfile
{
    public GroundTechProfile(int inputBufferFrames, int durationFrames, int slideFrames,
        FixedPoint initialSpeed, FixedPoint velocityRetention)
    {
        if (inputBufferFrames is < 1 or > 60 || slideFrames < 1 || slideFrames >= durationFrames || durationFrames > 600)
            throw new ArgumentException("Ground tech requires bounded input, slide and animation durations.");
        if (initialSpeed <= FixedPoint.Zero || initialSpeed > FixedPoint.FromInt(AttackDefinition.MaxLaunchUnits))
            throw new ArgumentOutOfRangeException(nameof(initialSpeed));
        if (velocityRetention <= FixedPoint.Zero || velocityRetention >= FixedPoint.One)
            throw new ArgumentOutOfRangeException(nameof(velocityRetention));
        InputBufferFrames = inputBufferFrames;
        DurationFrames = durationFrames;
        SlideFrames = slideFrames;
        InitialSpeed = initialSpeed;
        VelocityRetention = velocityRetention;
    }

    public int InputBufferFrames { get; }
    public int DurationFrames { get; }
    public int SlideFrames { get; }
    public FixedPoint InitialSpeed { get; }
    public FixedPoint VelocityRetention { get; }
}

/// <summary>Optional source-authored launch, ground impact, rebound and recovery timing.</summary>
public sealed record LaunchReactionProfile
{
    public LaunchReactionProfile(int impactFrames, FixedPoint bounceHorizontalRetention,
        FixedPoint bounceVerticalVelocity, FixedPoint bounceGravity, int lieDownFrames, int wakeupFrames,
        int riseFrames, int lieImpactFrames, int transitionGravitySteps, bool startInFall = false,
        GroundTechProfile? groundTech = null, bool suppressOwnerProjectilesDuringFall = false)
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
        if (suppressOwnerProjectilesDuringFall && !startInFall)
            throw new ArgumentException("The air follow-up restriction requires a reaction that starts in fall.",
                nameof(suppressOwnerProjectilesDuringFall));

        ImpactFrames = impactFrames;
        BounceHorizontalRetention = bounceHorizontalRetention;
        BounceVerticalVelocity = bounceVerticalVelocity;
        BounceGravity = bounceGravity;
        LieDownFrames = lieDownFrames;
        WakeupFrames = wakeupFrames;
        RiseFrames = riseFrames;
        LieImpactFrames = lieImpactFrames;
        TransitionGravitySteps = transitionGravitySteps;
        StartInFall = startInFall;
        GroundTech = groundTech;
        SuppressOwnerProjectilesDuringFall = suppressOwnerProjectilesDuringFall;
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
    public bool StartInFall { get; }
    public GroundTechProfile? GroundTech { get; }
    public bool SuppressOwnerProjectilesDuringFall { get; }
}
