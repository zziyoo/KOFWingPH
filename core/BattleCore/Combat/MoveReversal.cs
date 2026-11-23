using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Combat;

/// <summary>An inclusive move-tick window for a facing-relative Clsn1 reversal.</summary>
public sealed class MoveReversal
{
    public MoveReversal(int firstFrame, int lastFrame, IReadOnlyList<FixedRect> boxes, int sourceHitstop, int targetHitstop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstFrame);
        if (lastFrame < firstFrame)
            throw new ArgumentOutOfRangeException(nameof(lastFrame), "A reversal must end at or after its first tick.");
        ArgumentNullException.ThrowIfNull(boxes);
        if (boxes.Count == 0)
            throw new ArgumentException("A reversal requires at least one collision box.", nameof(boxes));
        if (sourceHitstop is < 0 or > AttackDefinition.MaxHitstopFrames)
            throw new ArgumentOutOfRangeException(nameof(sourceHitstop));
        if (targetHitstop is < 0 or > AttackDefinition.MaxHitstopFrames)
            throw new ArgumentOutOfRangeException(nameof(targetHitstop));

        FirstFrame = firstFrame;
        LastFrame = lastFrame;
        Boxes = GroundHurtboxes.CopyLocalBoxes(boxes, nameof(boxes));
        SourceHitstop = sourceHitstop;
        TargetHitstop = targetHitstop;
    }

    public int FirstFrame { get; }
    public int LastFrame { get; }
    public IReadOnlyList<FixedRect> Boxes { get; }
    public int SourceHitstop { get; }
    public int TargetHitstop { get; }
}

/// <summary>A proposed reversal of one opposing strike in the common pre-settlement view.</summary>
public readonly record struct MoveReversalContact(int SourceId, int TargetId, int TargetAttackIndex,
    int SourceHitstop, int TargetHitstop);

/// <summary>An opposing Helper entity's live attack box in the common pre-settlement view.</summary>
public readonly record struct ProjectileReversalCandidate(int ProjectileId, FixedRect Box);

/// <summary>Identifies the reversed Helper and the two independent freeze clocks.</summary>
public readonly record struct ProjectileReversalContact(int SourceId, int ProjectileId, int SourceHitstop, int TargetHitstop);

public static class MoveReversalResolver
{
    /// <summary>Matches reversal boxes against the opponent's eligible live melee strike boxes in world space.</summary>
    public static MoveReversalContact? Detect(FighterState first, FighterState second,
        IReadOnlyList<MoveReversal> firstReversals, IReadOnlyList<MoveReversal> secondReversals,
        IReadOnlyList<ActiveHitbox> firstStrikes, IReadOnlyList<ActiveHitbox> secondStrikes)
    {
        MoveReversalContact? firstContact = FindMeleeContact(first, second, firstReversals, secondStrikes);
        MoveReversalContact? secondContact = FindMeleeContact(second, first, secondReversals, firstStrikes);
        return first.Id < second.Id ? firstContact ?? secondContact : secondContact ?? firstContact;
    }

    /// <summary>Matches against eligible Helper HitDefs captured after entity movement.</summary>
    public static ProjectileReversalContact? DetectProjectile(FighterState source,
        IReadOnlyList<MoveReversal> reversals, IReadOnlyList<ProjectileReversalCandidate> projectiles) =>
        FindContact(source, reversals, projectiles, static projectile => projectile.Box) is { } match
            ? new ProjectileReversalContact(source.Id, match.Target.ProjectileId, match.Reversal.SourceHitstop,
                match.Reversal.TargetHitstop) : null;

    private static MoveReversalContact? FindMeleeContact(FighterState source, FighterState target,
        IReadOnlyList<MoveReversal> reversals, IReadOnlyList<ActiveHitbox> targetStrikes) =>
        FindContact(source, reversals, targetStrikes, static strike => strike.Box) is { } match
            ? new MoveReversalContact(source.Id, target.Id, match.Target.AttackIndex, match.Reversal.SourceHitstop,
                match.Reversal.TargetHitstop) : null;

    private static (MoveReversal Reversal, T Target)? FindContact<T>(FighterState source,
        IReadOnlyList<MoveReversal> reversals, IReadOnlyList<T> targets, Func<T, FixedRect> boxOf)
    {
        if (!source.IsInMove || targets.Count == 0)
            return null;

        bool facingRight = source.Facing == Facing.Right;
        foreach (MoveReversal reversal in reversals)
        {
            if (source.MoveElapsedFrames < reversal.FirstFrame || source.MoveElapsedFrames > reversal.LastFrame)
                continue;

            foreach (FixedRect local in reversal.Boxes)
            {
                var world = new FixedRect(
                    source.PositionX + (facingRight ? local.MinX : -local.MaxX),
                    source.PositionX + (facingRight ? local.MaxX : -local.MinX),
                    source.PositionY + local.MinY,
                    source.PositionY + local.MaxY);
                foreach (T target in targets)
                    if (world.IntersectsOrTouches(boxOf(target)))
                        return (reversal, target);
            }
        }

        return null;
    }
}
