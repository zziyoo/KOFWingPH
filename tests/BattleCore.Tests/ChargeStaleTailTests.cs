using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 03B 蓄力边界最终补修：释放后<b>不得借用释放前的旧方向条目</b>完成尾段。
///
/// <para>
/// 缺陷（审查基线 A1）：释放窗口检查只有上界
/// （<c>endFrame - ReleasedAtFrame &gt; tolerance → 拒绝</c>），没有下界。
/// 释放前的旧 Forward 条目满足 <c>endFrame - ReleasedAtFrame &lt; 0</c>，
/// 只要按键边沿还在 <c>ButtonToleranceFrames</c> 内，就能借旧条目幽灵完成蓄力——
/// 即使本次释放后从未重新输入完成方向。
/// </para>
///
/// <para>
/// 规则（D54）：蓄力尾段（序列最后一个方向）的进入帧必须满足
/// <c>ReleasedAtFrame ≤ endFrame ≤ ReleasedAtFrame + ChargeReleaseToleranceFrames</c>——
/// 尾段必须属于<b>本次释放之后</b>的新手势。修复前第 1 项测试失败，修复后全部通过。
/// </para>
/// </summary>
public class ChargeStaleTailTests
{
    private const InputBits Left = InputBits.Left;
    private const InputBits Right = InputBits.Right;
    private const InputBits BtnA = InputBits.LightPunch;

    // ─────────────────────────────────────────────────────────────
    // 缺陷复现（修复前失败）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StaleForwardBeforeCharge_CannotBeBorrowedAfterRelease()
    {
        // 最小复现（A1）：前(0) → 后(1，蓄 1 帧) → 中立(2，释放) → 中立+A(3)。
        // 释放后从未重新进入 Forward；旧 Forward(0) 与按键(3) 恰好相差 3 帧（容差内），
        // 修复前会借旧条目完成 46A（幽灵指令）。
        CommandRecognizer recognizer = Recognizer();
        Run(recognizer, new[]
        {
            Right,           // 0: Forward（释放前的旧条目）
            Left,            // 1: Back（蓄力 1 帧）
            InputBits.None,  // 2: Neutral（释放，挂起 {1, 2}）
            InputBits.None | BtnA, // 3: Neutral + A（无新 Forward）
        });

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void StaleForward_InsufficientHold_AlsoFails()
    {
        // 同款布局但蓄力不足（要求 2 帧、实际 1 帧）：任何路径都不得匹配。
        CommandRecognizer recognizer = Recognizer(holdFrames: 2);
        Run(recognizer, new[]
        {
            Right,                 // 0: Forward（旧条目）
            Left,                  // 1: Back（仅 1 帧 < 2）
            InputBits.None,        // 2: Neutral（释放）
            InputBits.None | BtnA, // 3: Neutral + A
        });

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void LateButtonAfterRelease_NoNewForward_DoesNotMatch()
    {
        // 释放后 4 帧才按 A：既超出释放窗口，也超出对旧 Forward(0) 的按键容差，
        // 两条路都不得复活蓄力。
        CommandRecognizer recognizer = Recognizer();
        Run(recognizer, new[]
        {
            Right,                 // 0: Forward（旧条目）
            Left,                  // 1: Back（蓄力 1 帧）
            InputBits.None,        // 2: Neutral（释放）
            InputBits.None,        // 3
            InputBits.None,        // 4
            InputBits.None,        // 5
            InputBits.None | BtnA, // 6: A（距旧 Forward 6 帧 > 容差 3）
        });

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 正确路径不受影响（含历史里存在旧 Forward 的情况）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ReleaseFrame_ImmediateForwardEntry_Completes()
    {
        // 释放当帧直接进入 Forward（D52 同帧完成）：旧 Forward(0) 不被借用，
        // 新 Forward(2) 才是尾段 → 正常识别。
        CommandRecognizer recognizer = Recognizer();
        Run(recognizer, new[]
        {
            Right,          // 0: Forward（旧条目）
            Left,           // 1: Back（蓄力 1 帧）
            Right | BtnA,   // 2: 释放当帧直接前 + A
        });

        CommandMatch match = Single(recognizer);
        Assert.Equal("46A", match.CommandId);
        Assert.Equal(2, match.SequenceEndFrame);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void ReleaseWindow_WithStaleForwardInHistory(int neutralFrames, bool shouldMatch)
    {
        // 历史里存在旧 Forward(0)，释放后 1/2/3 帧内重新进入 Forward → 匹配；
        // 4 帧 → 窗口过期，且旧条目不得被借用 → 不匹配。
        CommandRecognizer recognizer = Recognizer(holdFrames: 2);

        var frames = new List<InputBits>
        {
            Right,   // 0: Forward（旧条目）
            Left,    // 1: Back（蓄力 2 帧）
            Left,    // 2: Back（持续）
        };
        for (int i = 0; i < neutralFrames; i++)
            frames.Add(InputBits.None);          // 3..：中立（释放）
        frames.Add(Right | BtnA);                // 释放后第 neutralFrames+1 帧前 + A

        Run(recognizer, frames.ToArray());

        if (shouldMatch)
            Assert.Equal("46A", Single(recognizer).CommandId);
        else
            Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // P1/P2 独立 + 换边归属
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StaleForward_P1Rejected_P2CleanChargeStillMatches()
    {
        // P1 带旧 Forward 的失效布局；P2（朝左）独立做干净的蓄力 + 释放窗口内完成。
        // 一方的失败不得影响另一方的正常识别。
        var recognizer = Recognizer();

        // P1：前(0) → 后(1) → 中立(2) → 中立+A(3)（旧 Forward 不得被借用）。
        // P2 朝左：后 = Right，前 = Left。
        // P2：后(1-2) → 中立(3-4) → 前+A(5)（释放后 2 帧内，合法）。
        for (int frame = 0; frame <= 5; frame++)
        {
            InputBits p1 = frame switch
            {
                0 => Right,
                1 => Left,
                3 => InputBits.None | BtnA,
                _ => InputBits.None,
            };
            InputBits p2 = frame switch
            {
                1 or 2 => Right,                 // 后（朝左）
                5 => Left | BtnA,                // 前 + A（朝左）
                _ => InputBits.None,
            };
            recognizer.Observe(new FrameInput(frame, p1, p2), Facing.Right, Facing.Left);
        }

        Assert.Empty(recognizer.BufferedMatches(0));
        Assert.Equal("46A", Single(recognizer, 1).CommandId);
        Assert.Equal(5, recognizer.LastMatch(1)!.SequenceEndFrame);
    }

    [Fact]
    public void SideSwitch_NewFacingForward_IsFreshTail()
    {
        // 前(0，朝右) → 后(1-2) → 换边当帧中立(3，释放) → 朝左按 Left(4)：
        // 相对方向为前，是释放后的新条目 → 合法尾段，识别成功；
        // 旧 Forward(0) 依旧不被借用（起始帧应回到蓄力条目 1）。
        var recognizer = Recognizer(holdFrames: 2);

        recognizer.Observe(new FrameInput(0, Right, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(1, Left, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(2, Left, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(3, InputBits.None, InputBits.None), Facing.Left, Facing.Right);
        recognizer.Observe(new FrameInput(4, Left | BtnA, InputBits.None), Facing.Left, Facing.Right);

        CommandMatch match = Single(recognizer);
        Assert.Equal("46A", match.CommandId);
        Assert.Equal(1, match.SequenceStartFrame);
        Assert.Equal(4, match.SequenceEndFrame);
        Assert.Equal(4, match.ButtonFrame);
    }

    // ─────────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────────

    /// <summary>构造只含 46A 的识别器；审查基线最小复现配置（蓄力 1 帧、释放窗口 3、按键容差 3）。</summary>
    private static CommandRecognizer Recognizer(int holdFrames = 1) => new(new[]
    {
        new CommandDefinition("46A",
            new[] { CommandDirection.Back, CommandDirection.Forward },
            BtnA, maxSequenceFrames: 60, buttonToleranceFrames: 3, priority: 30,
            bufferLifetimeFrames: 8, chargeHoldFrames: holdFrames,
            chargeReleaseToleranceFrames: 3),
    });

    private static CommandMatch Single(CommandRecognizer recognizer, int player = 0)
    {
        IReadOnlyList<CommandMatch> matches = recognizer.BufferedMatches(player);
        Assert.Single(matches);
        return matches[0];
    }

    private static void Run(CommandRecognizer recognizer, InputBits[] player1)
    {
        for (int frame = 0; frame < player1.Length; frame++)
        {
            recognizer.Observe(new FrameInput(frame, player1[frame], InputBits.None), Facing.Right, Facing.Left);
        }
    }
}
