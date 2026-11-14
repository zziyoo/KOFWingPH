using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Numerics;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Simulation;

/// <summary>Advances an opted-in launch reaction once on the unfrozen world clock.</summary>
internal static class ScriptedLaunchLanding
{
    public static FighterState Advance(FighterState fighter, bool groundTechPressed)
    {
        LaunchReactionState reaction = fighter.LaunchReaction!.Value;
        LaunchReactionProfile profile = reaction.Profile;
        if (profile.GroundTech is { } tech && reaction.Phase is LaunchReactionPhase.Shake or LaunchReactionPhase.Fall)
        {
            reaction = reaction with { TechInputTicks = reaction.TechInputTicks > 0 ? reaction.TechInputTicks - 1
                : groundTechPressed ? tech.InputBufferFrames : 0 };
        }

        switch (reaction.Phase)
        {
            case LaunchReactionPhase.Shake when profile.StartInFall:
                return AdvanceAir(fighter, reaction with { Phase = LaunchReactionPhase.Fall, Ticks = 1 },
                    fighter.VelocityY - fighter.HitLaunchGravity!.Value);
            case LaunchReactionPhase.Shake:
                return AdvanceAir(fighter, reaction with { Phase = LaunchReactionPhase.Lift, Ticks = 1 },
                    fighter.VelocityY);
            case LaunchReactionPhase.Lift:
                return AdvanceAir(fighter, reaction with { Phase = LaunchReactionPhase.Rise, Ticks = 1 },
                    fighter.VelocityY - fighter.HitLaunchGravity!.Value * profile.TransitionGravitySteps);
            case LaunchReactionPhase.Rise:
            case LaunchReactionPhase.Fall:
                if (reaction.GroundCrossed)
                {
                    if (reaction.TechInputTicks > 0 && fighter.Health > 0 && profile.GroundTech is { } groundTech)
                    {
                        FighterState start = AtGround(fighter, reaction with { Phase = LaunchReactionPhase.Tech, Ticks = 1,
                            GroundCrossed = false, TechInputTicks = 0 }, groundTech.DurationFrames);
                        FixedPoint velocity = fighter.Facing == Facing.Right ? -groundTech.InitialSpeed : groundTech.InitialSpeed;
                        return start with { PositionX = start.PositionX + velocity, VelocityX = velocity };
                    }
                    return AtGround(fighter, reaction with { Phase = LaunchReactionPhase.Impact, Ticks = 1,
                        GroundCrossed = false, ImpactHorizontalVelocity = fighter.VelocityX, TechInputTicks = 0 }, profile.ImpactFrames);
                }

                bool startFall = reaction.Phase == LaunchReactionPhase.Rise && reaction.Ticks >= profile.RiseFrames;
                return AdvanceAir(fighter, startFall ? reaction with { Phase = LaunchReactionPhase.Fall, Ticks = 1 }
                    : reaction with { Ticks = reaction.Ticks + 1 }, fighter.VelocityY - fighter.HitLaunchGravity!.Value);
            case LaunchReactionPhase.Impact:
                if (reaction.Ticks < profile.ImpactFrames)
                    return AtGround(fighter, reaction with { Ticks = reaction.Ticks + 1 }, profile.ImpactFrames - reaction.Ticks);

                // The impact state's position freeze also applies on the transition tick.
                return fighter with
                {
                    LogicState = FighterLogicState.AirHitstun,
                    StunRemainingFrames = 0,
                    VelocityX = reaction.ImpactHorizontalVelocity * profile.BounceHorizontalRetention,
                    VelocityY = profile.BounceVerticalVelocity - profile.BounceGravity,
                    LaunchReaction = reaction with { Phase = LaunchReactionPhase.Bounce, Ticks = 1,
                        ImpactHorizontalVelocity = FixedPoint.Zero },
                };
            case LaunchReactionPhase.Bounce:
                if (reaction.GroundCrossed)
                    return AtGround(fighter, reaction with { Phase = LaunchReactionPhase.LyingImpact, Ticks = 1,
                        GroundCrossed = false }, profile.LieDownFrames);
                return AdvanceAir(fighter, reaction with { Ticks = reaction.Ticks + 1 },
                    fighter.VelocityY - profile.BounceGravity);
            case LaunchReactionPhase.LyingImpact:
                if (reaction.Ticks >= profile.LieImpactFrames)
                    return AtGround(fighter, reaction with { Phase = LaunchReactionPhase.Lying, Ticks = 1 },
                        profile.LieDownFrames - profile.LieImpactFrames);
                return AtGround(fighter, reaction with { Ticks = reaction.Ticks + 1 }, profile.LieDownFrames - reaction.Ticks);
            case LaunchReactionPhase.Lying:
                if (reaction.Ticks >= profile.LieDownFrames - profile.LieImpactFrames)
                    return AtGround(fighter, reaction with { Phase = LaunchReactionPhase.Wakeup, Ticks = 1 }, profile.WakeupFrames);
                return AtGround(fighter, reaction with { Ticks = reaction.Ticks + 1 },
                    profile.LieDownFrames - profile.LieImpactFrames - reaction.Ticks);
            case LaunchReactionPhase.Wakeup:
                if (reaction.Ticks < profile.WakeupFrames)
                    return AtGround(fighter, reaction with { Ticks = reaction.Ticks + 1 }, profile.WakeupFrames - reaction.Ticks);
                return fighter with { LogicState = FighterLogicState.Standing, StunRemainingFrames = 0, LaunchReaction = null };
            case LaunchReactionPhase.Tech:
                GroundTechProfile recovery = profile.GroundTech!;
                if (reaction.Ticks >= recovery.DurationFrames)
                    return fighter with { LogicState = FighterLogicState.Standing, StunRemainingFrames = 0, LaunchReaction = null };
                FixedPoint slide = reaction.Ticks < recovery.SlideFrames
                    ? fighter.VelocityX * recovery.VelocityRetention : FixedPoint.Zero;
                return fighter with { PositionX = fighter.PositionX + slide, VelocityX = slide,
                    StunRemainingFrames = recovery.DurationFrames - reaction.Ticks,
                    LaunchReaction = reaction with { Ticks = reaction.Ticks + 1 } };
            default:
                throw new InvalidOperationException($"Unsupported launch reaction phase {reaction.Phase}.");
        }
    }

    private static FighterState AdvanceAir(FighterState fighter, LaunchReactionState reaction, FixedPoint velocityY)
    {
        FixedPoint y = fighter.PositionY + velocityY;
        bool groundCrossed = velocityY < FixedPoint.Zero && y <= FixedPoint.Zero;
        return fighter with
        {
            PositionX = fighter.PositionX + fighter.VelocityX,
            PositionY = groundCrossed ? FixedPoint.Zero : y,
            VelocityY = velocityY,
            LaunchReaction = reaction with { GroundCrossed = groundCrossed },
        };
    }

    private static FighterState AtGround(FighterState fighter, LaunchReactionState reaction, int remaining) => fighter with
    {
        LogicState = reaction.LogicState,
        PositionY = FixedPoint.Zero,
        VelocityX = FixedPoint.Zero,
        VelocityY = FixedPoint.Zero,
        StunRemainingFrames = remaining,
        LandingKnockdownFrames = 0,
        LandingWakeupFrames = 0,
        HitLaunchGravity = null,
        LaunchReaction = reaction,
    };
}
