using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>An authored takeoff and landing inside a committed attack instance.</summary>
public sealed class MoveAirMotion
{
    public MoveAirMotion(int takeoffFrame, int landingFrame, FixedPoint forwardVelocity,
        FixedPoint upwardVelocity, FixedPoint gravity, FixedPoint horizontalRetention)
    {
        if (takeoffFrame < 0 || landingFrame <= takeoffFrame)
            throw new ArgumentOutOfRangeException(nameof(landingFrame));
        FixedPoint velocityLimit = MovementDefinition.MaxJumpVelocityUnits;
        if (forwardVelocity < -velocityLimit || forwardVelocity > velocityLimit ||
            upwardVelocity <= FixedPoint.Zero || upwardVelocity > velocityLimit)
            throw new ArgumentOutOfRangeException(nameof(upwardVelocity));
        if (gravity <= FixedPoint.Zero || gravity > (FixedPoint)MovementDefinition.MaxGravityUnits)
            throw new ArgumentOutOfRangeException(nameof(gravity));
        if (horizontalRetention < FixedPoint.Zero || horizontalRetention > FixedPoint.One)
            throw new ArgumentOutOfRangeException(nameof(horizontalRetention));
        var freeMotionTicks = (long)landingFrame - takeoffFrame - 1;
        if (2 * (Int128)upwardVelocity.RawValue <= (Int128)gravity.RawValue * freeMotionTicks)
            throw new ArgumentException("The configured flight crosses the floor before its landing frame.");

        TakeoffFrame = takeoffFrame;
        LandingFrame = landingFrame;
        ForwardVelocity = forwardVelocity;
        UpwardVelocity = upwardVelocity;
        Gravity = gravity;
        HorizontalRetention = horizontalRetention;
    }

    public int TakeoffFrame { get; }
    public int LandingFrame { get; }
    public FixedPoint ForwardVelocity { get; }
    public FixedPoint UpwardVelocity { get; }
    public FixedPoint Gravity { get; }
    public FixedPoint HorizontalRetention { get; }
    public bool InFlight(int frame) => frame >= TakeoffFrame && frame < LandingFrame;
}
