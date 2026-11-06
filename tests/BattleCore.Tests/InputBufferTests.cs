using NewKOF.BattleCore.Input;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 03A：输入缓冲语义测试（纯 C#，不依赖 Godot）。
///
/// 核心要证明的两件事：
/// <list type="number">
/// <item><b>电平</b>：按住就一直带位，松开下一帧消失。</item>
/// <item><b>边沿</b>：两次逻辑帧之间的「按下」必定出现在且仅出现在下一个逻辑帧，
///       不会被整批逻辑帧重复消费，也不会因为中间没有逻辑帧而丢失。</item>
/// </list>
/// </summary>
public class InputBufferTests
{
    [Fact]
    public void Take_FirstFrame_ReturnsCurrentHeldState()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.Left);

        FrameInput input = buffer.Take(0);

        Assert.Equal(0, input.Frame);
        Assert.Equal(InputBits.Right, input.Player1);
        Assert.Equal(InputBits.Left, input.Player2);
    }

    [Fact]
    public void HeldKey_StaysSetAcrossConsecutiveFrames()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Up, InputBits.None);

        // 一次采样，连续三个逻辑帧：属于「保持」，三帧都应带 Up。
        Assert.Equal(InputBits.Up, buffer.Take(0).Player1);
        Assert.Equal(InputBits.Up, buffer.Take(1).Player1);
        Assert.Equal(InputBits.Up, buffer.Take(2).Player1);
    }

    [Fact]
    public void Release_ClearsBitFromNextFrame()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Up, InputBits.None);
        buffer.Take(0);

        buffer.Sample(InputBits.None, InputBits.None);

        Assert.Equal(InputBits.None, buffer.Take(1).Player1);
    }

    [Fact]
    public void TapBetweenLogicFrames_AppearsInExactlyOneFrame()
    {
        var buffer = new InputBuffer();

        // 两次逻辑帧之间：按下 → 松开，期间没有任何 Take。
        buffer.Sample(InputBits.Right, InputBits.None);
        buffer.Sample(InputBits.None, InputBits.None);

        // 按下边沿必须被保留到下一个逻辑帧，且只出现一次。
        Assert.Equal(InputBits.Right, buffer.Take(0).Player1);
        Assert.Equal(InputBits.None, buffer.Take(1).Player1);
        Assert.Equal(InputBits.None, buffer.Take(2).Player1);
    }

    [Fact]
    public void MultiFrameBatch_DoesNotDuplicateOneShotPress()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.LightPunch, InputBits.None);
        buffer.Sample(InputBits.None, InputBits.None);

        // 一个渲染帧内推进 5 个逻辑帧
        int framesWithPunch = 0;
        for (int frame = 0; frame < 5; frame++)
        {
            if ((buffer.Take(frame).Player1 & InputBits.LightPunch) != 0)
                framesWithPunch++;
        }

        Assert.Equal(1, framesWithPunch);
    }

    [Fact]
    public void TwoTapsBetweenFrames_AreCoalescedAndCounted()
    {
        var buffer = new InputBuffer();

        buffer.Sample(InputBits.HeavyKick, InputBits.None); // 第一次按下
        buffer.Sample(InputBits.None, InputBits.None);      // 松开
        buffer.Sample(InputBits.HeavyKick, InputBits.None); // 第二次按下
        buffer.Sample(InputBits.None, InputBits.None);      // 再松开

        // 电平语义只能表达「这一帧有按」，两次按下被合并；
        // 合并行为不是静默的：计数可观测。
        Assert.Equal(1, buffer.CoalescedPressCount);

        Assert.Equal(InputBits.HeavyKick, buffer.Take(0).Player1);
        Assert.Equal(InputBits.None, buffer.Take(1).Player1);
    }

    [Fact]
    public void PlayersAreIndependent()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.Left);

        FrameInput both = buffer.Take(0);
        Assert.Equal(InputBits.Right, both.Player1);
        Assert.Equal(InputBits.Left, both.Player2);

        // 只改 P2，P1 的按下/松开不受影响
        buffer.Sample(InputBits.Right, InputBits.Down);
        FrameInput next = buffer.Take(1);
        Assert.Equal(InputBits.Right, next.Player1);
        Assert.Equal(InputBits.Down, next.Player2);
    }

    [Fact]
    public void MultiKeyCombination_IsPreserved()
    {
        var buffer = new InputBuffer();
        buffer.Sample(
            InputBits.Up | InputBits.LightPunch | InputBits.HeavyKick,
            InputBits.Down | InputBits.LightKick);

        FrameInput input = buffer.Take(0);

        Assert.Equal(InputBits.Up | InputBits.LightPunch | InputBits.HeavyKick, input.Player1);
        Assert.Equal(InputBits.Down | InputBits.LightKick, input.Player2);
    }

    [Fact]
    public void HeldBy_ReportsRawHeldState_WithoutPendingEdge()
    {
        var buffer = new InputBuffer();
        buffer.Sample(InputBits.Right, InputBits.None);
        buffer.Sample(InputBits.None, InputBits.None);

        // 物理上已松开……
        Assert.Equal(InputBits.None, buffer.HeldBy(0));

        // ……但边沿仍在，下一逻辑帧依然会带上它。
        Assert.Equal(InputBits.Right, buffer.Take(0).Player1);
    }

    [Fact]
    public void History_RecordsEveryTakenFrameInOrder()
    {
        var buffer = new InputBuffer();
        for (int frame = 0; frame < 10; frame++)
            buffer.Take(frame);

        Assert.Equal(10, buffer.History.Count);
        for (int i = 0; i < 10; i++)
            Assert.Equal(i, buffer.History[i].Frame);

        Assert.Equal(9, buffer.LastInput!.Value.Frame);
    }

    [Fact]
    public void Take_RejectsNonContiguousFrame()
    {
        var buffer = new InputBuffer();
        buffer.Take(0);

        // 跳帧会让输入错位，必须立即失败而不是静默产出错帧数据。
        var ex = Assert.Throws<InvalidOperationException>(() => buffer.Take(2));
        Assert.Contains("连续", ex.Message);
    }

    [Fact]
    public void Take_RejectsNegativeFrame()
    {
        var buffer = new InputBuffer();
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Take(-1));
    }

    [Fact]
    public void LastInput_IsNullBeforeAnyFrame()
    {
        var buffer = new InputBuffer();
        Assert.Null(buffer.LastInput);
        Assert.Empty(buffer.History);
    }
}
