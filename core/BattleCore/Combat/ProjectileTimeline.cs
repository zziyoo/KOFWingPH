using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.Combat;

/// <summary>One attack window on the entity clock, measured from spawn at age zero.</summary>
public sealed class ProjectileHitSpan
{
    public ProjectileHitSpan(int firstFrame, int lastFrame, FixedRect localHitbox, AttackDefinition hit,
        int guardDamage = 0, DynamicAttackDamage? dynamicDamage = null, bool contactOnEdge = true,
        FixedPoint? launchGravity = null, LaunchReactionProfile? launchReaction = null)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (firstFrame < 1 || lastFrame < firstFrame)
            throw new ArgumentException("Entity hit spans must begin after spawn and have a valid inclusive end.");
        if (localHitbox.MaxX <= localHitbox.MinX || localHitbox.MaxY <= localHitbox.MinY)
            throw new ArgumentException("Entity hitbox must have positive area.", nameof(localHitbox));
        if (guardDamage < 0)
            throw new ArgumentOutOfRangeException(nameof(guardDamage));
        if (launchGravity is { } gravity && (!hit.Launches || gravity <= FixedPoint.Zero ||
            gravity > FixedPoint.FromInt(MovementDefinition.MaxGravityUnits)))
            throw new ArgumentException("Entity launch gravity requires a launch and a valid gravity value.", nameof(launchGravity));
        if (launchReaction is not null && launchGravity is null)
            throw new ArgumentException("Entity launch reaction requires launch gravity.", nameof(launchReaction));
        if (hit.HitstopByOutcome is { } stop && (stop.Hit.AttackerFrames != 0 || stop.Block.AttackerFrames != 0))
            throw new ArgumentException("Entity contact can only freeze the defender.", nameof(hit));
        if (hit.DynamicDamage is not null || hit.CounterDamage is not null || hit.ContactGroup != 0 ||
            dynamicDamage is { UseContactDivisor: true } || (dynamicDamage is not null && hit.Damage != 0))
            throw new ArgumentException("Entity damage must use either fixed damage or a separate ungrouped dynamic rule.", nameof(hit));

        FirstFrame = firstFrame;
        LastFrame = lastFrame;
        LocalHitbox = localHitbox;
        Hit = hit;
        GuardDamage = guardDamage;
        DynamicDamage = dynamicDamage;
        ContactOnEdge = contactOnEdge;
        LaunchGravity = launchGravity;
        LaunchReaction = launchReaction;
    }

    public int FirstFrame { get; }
    public int LastFrame { get; }
    public FixedRect LocalHitbox { get; }
    public AttackDefinition Hit { get; }
    public int GuardDamage { get; }
    public DynamicAttackDamage? DynamicDamage { get; }
    public bool ContactOnEdge { get; }
    public FixedPoint? LaunchGravity { get; }
    public LaunchReactionProfile? LaunchReaction { get; }
}

/// <summary>Authoritative entity animation timing, with spawn-frame movement and one lifetime contact.</summary>
public sealed class ProjectileTimeline
{
    public ProjectileTimeline(int movementFrames, IReadOnlyList<ProjectileHitSpan> hitSpans,
        bool requestPauseOnHit = false, int? guardThreatFrames = null)
    {
        ArgumentNullException.ThrowIfNull(hitSpans);
        if (movementFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(movementFrames));
        if (guardThreatFrames is < 1)
            throw new ArgumentOutOfRangeException(nameof(guardThreatFrames));
        var spans = hitSpans.ToArray();
        if (spans.Length == 0)
            throw new ArgumentException("Entity timeline requires at least one hit span.", nameof(hitSpans));
        int previousEnd = 0;
        foreach (ProjectileHitSpan span in spans)
        {
            ArgumentNullException.ThrowIfNull(span);
            if (span.FirstFrame <= previousEnd)
                throw new ArgumentException("Entity hit spans must be ordered and cannot overlap.", nameof(hitSpans));
            previousEnd = span.LastFrame;
        }
        MovementFrames = movementFrames;
        HitSpans = Array.AsReadOnly(spans);
        RequestPauseOnHit = requestPauseOnHit;
        GuardThreatFrames = guardThreatFrames;
    }

    public int MovementFrames { get; }
    public IReadOnlyList<ProjectileHitSpan> HitSpans { get; }
    public bool RequestPauseOnHit { get; }
    public int? GuardThreatFrames { get; }

    internal ProjectileHitSpan? HitAt(int age)
    {
        foreach (ProjectileHitSpan span in HitSpans)
        {
            if (age < span.FirstFrame)
                break;
            if (age <= span.LastFrame)
                return span;
        }
        return null;
    }
}
