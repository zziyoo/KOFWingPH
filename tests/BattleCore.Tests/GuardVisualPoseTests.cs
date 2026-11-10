using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Presentation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

public class GuardVisualPoseTests
{
    [Fact]
    public void BlockPoseLatchesAtEventAndClearsOnExit()
    {
        var pose = new GuardVisualPose();
        pose.Observe(FighterLogicState.Standing, FighterLogicState.Blockstun, false, CommandDirection.DownBack, true);
        Assert.True(pose.Crouching);
        pose.Observe(FighterLogicState.Blockstun, FighterLogicState.Blockstun, false, CommandDirection.Back, false);
        Assert.True(pose.Crouching);
        pose.Observe(FighterLogicState.Blockstun, FighterLogicState.Standing, false, CommandDirection.Back, false);
        Assert.False(pose.Crouching);
        pose.Observe(FighterLogicState.Standing, FighterLogicState.Blockstun, false, CommandDirection.Back, true);
        Assert.False(pose.Crouching);
    }

    [Fact]
    public void FrozenDefenderUsesExistingPostureAndNewBlocksReplaceOldPose()
    {
        var pose = new GuardVisualPose();
        pose.Observe(FighterLogicState.Crouching, FighterLogicState.Blockstun, true, CommandDirection.Back, true);
        Assert.True(pose.Crouching);
        pose.Observe(FighterLogicState.Blockstun, FighterLogicState.Blockstun, false, CommandDirection.Back, true);
        Assert.False(pose.Crouching);
        pose.Observe(FighterLogicState.Blockstun, FighterLogicState.Blockstun, false, CommandDirection.DownBack, true);
        Assert.True(pose.Crouching);
        pose.Reset();
        Assert.False(pose.Crouching);
    }
}
