using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.AI;

/// <summary>
/// Pure, input-only policy. Restore snapshots with the same immutable profile and tactic.
/// Opponent perception always comes from committed history, even with zero configured delay.
/// </summary>
public sealed class AiController : IAiPolicy
{
    private readonly AiDifficultyProfile _profile;
    private readonly AiTactic _tactic;

    public AiController(AiDifficultyProfile profile, AiTactic tactic = AiTactic.Balanced)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(tactic)) throw new ArgumentOutOfRangeException(nameof(tactic));
        _profile = profile;
        _tactic = tactic;
    }

    public AiDecision Decide(AiObservation current, AiPolicyState committed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(committed);
        if (committed.NextFrame != current.Frame || committed.FighterId != current.Definition.FighterId)
            throw new ArgumentException("AI snapshot does not match observation.");
        if (committed.PlanIndex < 0 || committed.PlanIndex > committed.Plan.Count ||
            committed.Plan.Count > AiCommandPlanner.MaxPlanFrames + 1 || committed.Observations.Count > 121 ||
            committed.Observations.Any(o => o.Frame >= current.Frame || o.Player != current.Player ||
                o.Definition.FighterId != current.Definition.FighterId) ||
            committed.Observations.Zip(committed.Observations.Skip(1), (a, b) => a.Frame >= b.Frame).Any(x => x))
            throw new ArgumentException("Invalid AI snapshot history or command cursor.");
        var next = committed.Observe(current) with { NextFrame = current.Frame + 1 };
        AiDecision Result(InputBits bits) => new(bits, next);
        InputBits Direction(CommandDirection direction) => AiCommandPlanner.ToInput(new(direction, InputBits.None), current.Self.Facing);
        void Clear(string action)
        {
            next = next.WithPlan(Array.Empty<AiCommandStep>()) with { Action = action, TargetMove = null };
        }
        bool Roll(int chance)
        {
            next = next with { RandomState = AiRandom.Next(next.RandomState) };
            return next.RandomState % 1000 < chance;
        }

        if (current.State.Round.Phase != RoundPhase.InProgress ||
            current.Self.Health == 0 || current.Opponent.Health == 0)
        {
            Clear("Terminal");
            return Result(InputBits.None);
        }

        // The public grab window is deliberately separate from ordinary threat perception.
        if (current.Grab is { } grab && grab.Target == current.Player)
        {
            bool attempted = committed.ThreatSinceFrame == grab.StartedFrame &&
                committed.Action is "Tech" or "TechMiss";
            Clear(attempted ? committed.Action : "Grabbed");
            next = next with { ThreatSinceFrame = grab.StartedFrame };
            const InputBits chord = InputBits.LightPunch | InputBits.LightKick;
            if (!attempted && current.Frame - grab.StartedFrame >= _profile.TechReactionFrames &&
                (current.Self.RecognitionState.HeldAttacks & chord) != chord)
            {
                bool tech = Roll(_profile.TechChance);
                next = next with { Action = tech ? "Tech" : "TechMiss" };
                return Result(tech ? chord : InputBits.None);
            }
            return Result(InputBits.None); // Release an old chord before creating a fresh edge.
        }

        var seen = committed.Observations.LastOrDefault(o =>
            o.Frame <= current.Frame - Math.Max(1, _profile.ObservationDelayFrames));
        var height = seen is null ? null : ThreatHeight(seen);
        bool threat = height.HasValue;
        int since = threat ? (committed.ThreatSinceFrame < 0 || committed.Action is "Grabbed" or "Tech" or "TechMiss"
            ? current.Frame : committed.ThreatSinceFrame) : -1;
        next = next with { ThreatSinceFrame = since };

        // Never buffer fresh attacks through hitstop, stun, or an already running move.
        if (current.Self.HitstopRemainingFrames > 0 || !current.CanAct)
        {
            bool interrupted = committed.PlanIndex < committed.Plan.Count;
            Clear(interrupted ? "Interrupted" : "Recover");
            if (interrupted) next = next with { NextDecisionFrame = current.Frame + _profile.ActionCooldownFrames + 1 };
            if (current.Self.LogicState is FighterLogicState.Airborne or FighterLogicState.AirBlockstun &&
                threat && height != AttackHeight.Low && since >= 0 && current.Frame - since >= _profile.ReactionDelayFrames)
            {
                // Air defense is back only and has the same imperfect guard roll as ground defense.
                bool guard = committed.Action == "AirGuard";
                if (current.Frame >= committed.NextDecisionFrame)
                {
                    guard = Roll(_profile.GuardChance);
                    next = next with { NextDecisionFrame = current.Frame + _profile.DecisionIntervalFrames };
                }
                next = next with { Action = guard ? "AirGuard" : "AirObserve" };
                return Result(guard ? Direction(CommandDirection.Back) : InputBits.None);
            }
            if (current.Self.LogicState == FighterLogicState.Blockstun && threat)
                return Result(Direction(height == AttackHeight.Low ? CommandDirection.DownBack : CommandDirection.Back));
            return Result(InputBits.None);
        }

        // Once selected, a command script is stable until it completes or loses control.
        if (next.PlanIndex < next.Plan.Count)
        {
            var step = next.Plan[next.PlanIndex];
            next = next with { PlanIndex = next.PlanIndex + 1 };
            if (next.PlanIndex == next.Plan.Count)
                next = next with { NextDecisionFrame = current.Frame + _profile.ActionCooldownFrames + 1 };
            return Result(AiCommandPlanner.ToInput(step, current.Self.Facing));
        }

        if (seen is null) { Clear("Observe"); return Result(InputBits.None); }
        if (current.Frame < committed.NextDecisionFrame)
        {
            if (committed.Action == "Guard" && threat)
                return Result(Direction(height == AttackHeight.Low ? CommandDirection.DownBack : CommandDirection.Back));
            if (committed.Action == "Approach") return Result(Direction(CommandDirection.Forward));
            // Reposition lasts only one decision interval; distance hysteresis prevents oscillation.
            if (committed.Action == "Reposition") return Result(Direction(CommandDirection.Back));
            return Result(InputBits.None);
        }
        Clear("Observe");
        next = next with { NextDecisionFrame = current.Frame + _profile.DecisionIntervalFrames };
        if (Roll(_profile.MistakeChance)) return Result(InputBits.None);
        if (threat && current.Frame - since >= _profile.ReactionDelayFrames && Roll(_profile.GuardChance))
        {
            next = next with { Action = "Guard" };
            return Result(Direction(height == AttackHeight.Low ? CommandDirection.DownBack : CommandDirection.Back));
        }

        bool punish = seen.Opponent.LogicState == FighterLogicState.Recovery && Roll(_profile.CounterChance);
        bool throwAttempt = Roll(_profile.ThrowChance);
        bool attack = punish || Roll(_profile.AttackChance);
        MoveDefinition? best = null;
        int bestScore = int.MinValue;
        if (attack || throwAttempt)
        {
            foreach (var move in current.Definition.Moves.OrderBy(m => m.MoveId, StringComparer.Ordinal))
            {
                var definition = current.Definition.Commands.Single(c => c.CommandId == move.CommandId);
                if (AiCommandPlanner.Plan(definition).Count == 0) continue;
                bool isThrow = move.Throws.Any(t => t.AllowsTarget(seen.Opponent.LogicState) && seen.Distance <= t.Range);
                if (move.Throws.Count > 0 && (!throwAttempt || !isThrow)) continue;
                if (move.Throws.Count == 0 && !attack) continue;
                int score = Score(move, seen, punish);
                if (score < 0) continue;
                // Bounded per-candidate variation, independent of iteration/hash implementation.
                next = next with { RandomState = AiRandom.Next(next.RandomState) };
                score += (int)(next.RandomState % 17);
                if (score > bestScore) { best = move; bestScore = score; }
            }
        }
        if (best is not null)
        {
            var command = current.Definition.Commands.Single(c => c.CommandId == best.CommandId);
            var plan = AiCommandPlanner.Plan(command).ToList();
            // A buffered command may require the other ground posture on the following frame.
            var posture = best.AllowedFrom == MoveAllowedStates.Crouching ? CommandDirection.Down : CommandDirection.Neutral;
            plan.Add(new(posture, InputBits.None));
            next = next.WithPlan(plan) with { Action = punish ? "Punish" : best.Throws.Count > 0 ? "Throw" : "Attack", TargetMove = best.MoveId };
            var step = next.Plan[0];
            next = next with { PlanIndex = 1 };
            return Result(AiCommandPlanner.ToInput(step, current.Self.Facing));
        }

        // A failed attack roll still closes distance. Defensive retreat is bounded by distance
        // and alternates with approach, so a policy can never back away forever.
        var preferred = _profile.PreferredDistance;
        if (seen.Distance > preferred + FixedPoint.FromInt(12) || committed.Action == "Reposition")
        {
            next = next with { Action = "Approach" };
            return Result(Direction(CommandDirection.Forward));
        }
        if (_tactic == AiTactic.Defensive && seen.Distance < preferred - FixedPoint.FromInt(20))
        {
            next = next with { Action = "Reposition" };
            return Result(Direction(CommandDirection.Back));
        }
        next = next with { Action = "Approach" };
        return Result(Direction(CommandDirection.Forward));
    }

    private int Score(MoveDefinition move, AiObservation seen, bool punish)
    {
        if (move.Throws.Count > 0) return 650 - Math.Min(100, move.StartupFrames);
        bool projectile = move.Projectiles.Count > 0;
        if (move.Attacks.Count == 0 && !projectile) return -1;
        FixedPoint reach = move.Attacks.Select(a => a.OffsetForward + a.HalfWidth + FixedPoint.FromInt(22))
            .DefaultIfEmpty(FixedPoint.Zero).Max();
        bool inRange = seen.Distance <= reach;
        if (!projectile && !inRange) return -1;
        int damage = Math.Min(200, move.Attacks.Select(a => a.Damage).Concat(move.Projectiles.Select(p => p.Hit.Damage)).DefaultIfEmpty().Max());
        int score = 300 + damage - Math.Min(120, move.StartupFrames * 3) - Math.Min(60, move.RecoveryFrames);
        if (projectile) score += seen.Distance > _profile.PreferredDistance + FixedPoint.FromInt(40) ? 230 : -100;
        if (punish && inRange) score += move.StartupFrames < seen.Opponent.PhaseRemainingFrames ? 300 : -120;
        if (_tactic == AiTactic.Offensive) score += Math.Min(80, damage / 2);
        if (_tactic == AiTactic.Defensive && projectile) score += 50;
        return score;
    }

    private static AttackHeight? ThreatHeight(AiObservation seen)
    {
        if (seen.OpponentAttacking && seen.Opponent.MoveId is { } id && seen.OpponentDefinition.FindMoveById(id) is { } move)
        {
            var hit = move.Attacks.FirstOrDefault();
            if (hit is not null) return hit.Height;
            if (move.Projectiles.Count > 0) return move.Projectiles[0].Hit.Height;
        }
        foreach (var projectile in seen.Projectiles.Where(p => p.OwnerFighterId != seen.Player))
        {
            var source = seen.OpponentDefinition.FindMoveById(projectile.SourceMoveId);
            var definition = source?.Projectiles.FirstOrDefault(p => p.ProjectileTypeId == projectile.TypeId);
            if (definition is not null) return definition.Hit.Height;
        }
        return null;
    }
}
