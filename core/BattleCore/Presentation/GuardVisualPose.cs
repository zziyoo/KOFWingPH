using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;

namespace NewKOF.BattleCore.Presentation;

/// <summary>Visual-only guard stance latched on the committed block event.</summary>
public sealed class GuardVisualPose
{
    public bool Crouching { get; private set; }

    public void Observe(FighterLogicState before, FighterLogicState after, bool frozen,
        CommandDirection? directionAtBlock, bool blocked)
    {
        if (after != FighterLogicState.Blockstun) { Reset(); return; }
        if (blocked)
            Crouching = frozen ? before == FighterLogicState.Crouching
                : directionAtBlock is CommandDirection.Down or CommandDirection.DownBack or CommandDirection.DownForward;
    }

    public void Reset() => Crouching = false;
}
