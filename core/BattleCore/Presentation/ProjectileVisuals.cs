using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Presentation;

/// <summary>Presentation age follows the lifetime consumed by each authoritative projectile.</summary>
public sealed class ProjectileVisuals
{
    private readonly Dictionary<int, int> _initialLife = new();

    public void Observe(BattleState state)
    {
        foreach (int id in _initialLife.Keys.ToArray())
            if (!state.Projectiles.Any(p => p.ProjectileId == id))
                _initialLife.Remove(id);
        foreach (var projectile in state.Projectiles)
            _initialLife.TryAdd(projectile.ProjectileId, projectile.RemainingLifetime);
    }

    public int Age(ProjectileState projectile) => _initialLife[projectile.ProjectileId] - projectile.RemainingLifetime;
    public static bool FacesLeft(ProjectileState projectile) => projectile.SpawnFacing is { } facing
        ? facing == Facing.Left : projectile.VelocityX < FixedPoint.Zero;
    public void Reset() => _initialLife.Clear();
}
