using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 03A：SOCD 与「绝对方向 → 相对朝向」解析规则测试。
///
/// 规则（决策 D27）：
/// <list type="bullet">
/// <item>左右同时按下 → 水平中立（相反方向互相抵消）。</item>
/// <item>上下同时按下 → 上优先。</item>
/// <item>录制层保存绝对方向，相对朝向只在解析时用 <see cref="Facing"/> 换算。</item>
/// </list>
/// </summary>
public class DirectionResolverTests
{
    [Fact]
    public void NoInput_ResolvesToNothing()
    {
        RelativeInput result = DirectionResolver.Resolve(InputBits.None, Facing.Right);

        Assert.False(result.Forward);
        Assert.False(result.Back);
        Assert.False(result.Up);
        Assert.False(result.Down);
        Assert.Equal(InputBits.None, result.Attacks);
    }

    [Fact]
    public void FacingRight_RightKeyIsForward()
    {
        RelativeInput result = DirectionResolver.Resolve(InputBits.Right, Facing.Right);

        Assert.True(result.Forward);
        Assert.False(result.Back);
    }

    [Fact]
    public void FacingLeft_RightKeyIsBack_AndLeftKeyIsForward()
    {
        RelativeInput right = DirectionResolver.Resolve(InputBits.Right, Facing.Left);
        Assert.True(right.Back);
        Assert.False(right.Forward);

        RelativeInput left = DirectionResolver.Resolve(InputBits.Left, Facing.Left);
        Assert.True(left.Forward);
        Assert.False(left.Back);
    }

    [Theory]
    [InlineData(Facing.Right)]
    [InlineData(Facing.Left)]
    public void LeftAndRightTogether_ResolveToHorizontalNeutral(Facing facing)
    {
        // SOCD-H：两侧相反方向互相抵消，不论面朝哪边。
        RelativeInput result = DirectionResolver.Resolve(
            InputBits.Left | InputBits.Right, facing);

        Assert.False(result.Forward);
        Assert.False(result.Back);
    }

    [Fact]
    public void UpAndDownTogether_UpWins()
    {
        // SOCD-V：跳跃优先，避免蹲防输入污染跳跃。
        RelativeInput result = DirectionResolver.Resolve(
            InputBits.Up | InputBits.Down, Facing.Right);

        Assert.True(result.Up);
        Assert.False(result.Down);
    }

    [Fact]
    public void DownAlone_ResolvesToDown()
    {
        RelativeInput result = DirectionResolver.Resolve(InputBits.Down, Facing.Right);

        Assert.True(result.Down);
        Assert.False(result.Up);
    }

    [Fact]
    public void SocdDoesNotLeakBetweenAxes()
    {
        // 水平冲突（中立）不应影响垂直方向。
        RelativeInput result = DirectionResolver.Resolve(
            InputBits.Left | InputBits.Right | InputBits.Down, Facing.Right);

        Assert.False(result.Forward);
        Assert.False(result.Back);
        Assert.True(result.Down);
        Assert.False(result.Up);
    }

    [Fact]
    public void AllFourAttacks_PassThrough()
    {
        const InputBits all = InputBits.LightPunch | InputBits.HeavyPunch
            | InputBits.LightKick | InputBits.HeavyKick;

        RelativeInput result = DirectionResolver.Resolve(all, Facing.Right);

        Assert.Equal(all, result.Attacks);
        Assert.False(result.Forward);
        Assert.False(result.Down);
    }

    [Fact]
    public void DirectionBits_AreNotCopiedIntoAttacks()
    {
        RelativeInput result = DirectionResolver.Resolve(
            InputBits.Right | InputBits.LightPunch, Facing.Right);

        // 攻击位只含攻击键，不含方向位——避免后续把「前进」误判成攻击。
        Assert.Equal(InputBits.LightPunch, result.Attacks);
    }
}
