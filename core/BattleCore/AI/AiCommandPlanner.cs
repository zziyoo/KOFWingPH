using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.AI;

public static class AiCommandPlanner
{
    /// <summary>Maximum per-frame script size, including button releases.</summary>
    public const int MaxPlanFrames = 4096;
    private const int MaxDirectionEntries = 64;

    /// <summary>
    /// Builds the fastest relative-direction script from the definition, with attack buttons
    /// released before and after the gesture. An empty result explicitly means unsupported:
    /// timing, recognizer history, or the bounded script budget cannot accommodate the command.
    /// Move selection and allowed-state checks remain the caller's responsibility.
    /// </summary>
    public static IReadOnlyList<AiCommandStep> Plan(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var sequence = command.DirectionSequence;
        bool charge = command.ChargeHoldFrames > 0;
        int matchedDirections = charge ? sequence.Count - 1 : sequence.Count;
        long frameCount = 2L + Math.Max(1, sequence.Count) +
            (charge ? (long)command.ChargeHoldFrames - 1 : 0);
        if (frameCount > MaxPlanFrames || matchedDirections > MaxDirectionEntries)
            return Array.Empty<AiCommandStep>();

        if (!charge && sequence.Count - 1 > command.MaxSequenceFrames)
            return Array.Empty<AiCommandStep>();

        if (charge)
        {
            int tailSpan = sequence.Count - 2;
            // Recognition retains direction entries according to the ordinary history horizon,
            // even though the charge hold itself is independent of MaxSequenceFrames.
            if (tailSpan > command.ChargeReleaseToleranceFrames ||
                tailSpan > (long)command.MaxSequenceFrames + command.ButtonToleranceFrames)
                return Array.Empty<AiCommandStep>();
            for (int i = 1; i < sequence.Count; i++)
                if (sequence[i] == sequence[0]) // Re-entering the hold direction clears pending charge.
                    return Array.Empty<AiCommandStep>();
        }

        var steps = new List<AiCommandStep>((int)frameCount);
        // A neutral first direction needs a distinct preceding entry even at zero button tolerance.
        var releaseDirection = sequence.Count > 0 && sequence[0] == CommandDirection.Neutral
            ? CommandDirection.Forward : CommandDirection.Neutral;
        steps.Add(new AiCommandStep(releaseDirection, InputBits.None));
        if (sequence.Count == 0)
            steps.Add(new AiCommandStep(CommandDirection.Neutral, command.RequiredButtons));
        else
            for (int i = 0; i < sequence.Count; i++)
            {
                int heldFrames = charge && i == 0 ? command.ChargeHoldFrames : 1;
                for (int frame = 0; frame < heldFrames; frame++)
                    steps.Add(new AiCommandStep(sequence[i],
                        i == sequence.Count - 1 ? command.RequiredButtons : InputBits.None));
            }
        steps.Add(new AiCommandStep(CommandDirection.Neutral, InputBits.None));
        return Array.AsReadOnly(steps.ToArray());
    }

    /// <summary>Converts one relative step using the authoritative facing of its execution frame.</summary>
    public static InputBits ToInput(AiCommandStep step, Facing facing)
    {
        if (facing is not (Facing.Right or Facing.Left))
            throw new ArgumentOutOfRangeException(nameof(facing));
        InputBits forward = facing == Facing.Right ? InputBits.Right : InputBits.Left;
        InputBits back = facing == Facing.Right ? InputBits.Left : InputBits.Right;
        InputBits direction = step.Direction switch
        {
            CommandDirection.Neutral => InputBits.None,
            CommandDirection.Forward => forward,
            CommandDirection.Back => back,
            CommandDirection.Up => InputBits.Up,
            CommandDirection.Down => InputBits.Down,
            CommandDirection.UpForward => InputBits.Up | forward,
            CommandDirection.UpBack => InputBits.Up | back,
            CommandDirection.DownForward => InputBits.Down | forward,
            CommandDirection.DownBack => InputBits.Down | back,
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };
        return direction | (step.Buttons & DirectionResolver.AttackButtons);
    }
}
