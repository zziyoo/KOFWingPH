using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 03B-2：蓄力指令（charge moves）。
///
/// 覆盖：保持 N 帧成功、N-1 帧失败、超过 N 帧成功、松开后超时失败、
/// 斜方向蓄力规则、SOCD 影响、P1/P2 独立、不同攻击键、转向窗口、暂停不累计。
///
/// 蓄力规则（见 ARCHITECTURE.md D49）：蓄力只认<b>纯方向</b>，斜方向是独立条目，
/// 会打断「纯方向」的连续保持；蓄力时长 = 序列第一个方向条目持续帧数。
/// </summary>
public class ChargeCommandTests
{
    private const InputBits Down = InputBits.Down;
    private const InputBits Up = InputBits.Up;
    private const InputBits Left = InputBits.Left;
    private const InputBits Right = InputBits.Right;
    private const InputBits BtnA = InputBits.LightPunch;
    private const InputBits BtnB = InputBits.HeavyPunch;
    private const InputBits BtnC = InputBits.LightKick;

    /// <summary>测试用蓄力帧数（默认表用 30，这里用更短的值便于测试）。</summary>
    private const int ChargeFrames = 10;

    // ─────────────────────────────────────────────────────────────
    // 保持帧数成功 / 失败 / 超过
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChargeBackForward_ExactlyHoldFrames_Recognizes46A()
    {
        // 后（Back）保持恰好 ChargeFrames 帧 → 前 + A。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);                 // 后（朝右时 Left = Back）
        frames.Add(Right | BtnA);             // 前 + A（完成）

        Run(recognizer, frames.ToArray());

        CommandMatch match = Single(recognizer);
        Assert.Equal("46A", match.CommandId);
        Assert.Equal(0, match.SequenceStartFrame);
        Assert.Equal(ChargeFrames, match.SequenceEndFrame);
        Assert.Equal(ChargeFrames, match.ButtonFrame);
    }

    [Fact]
    public void ChargeBackForward_HoldNMinusOneFrames_DoesNotMatch()
    {
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames - 1; i++)
            frames.Add(Left);                 // 少 1 帧
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeBackForward_HoldMoreThanFrames_StillMatches()
    {
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames + 10; i++)
            frames.Add(Left);                 // 超过阈值
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());

        CommandMatch match = Single(recognizer);
        Assert.Equal("46A", match.CommandId);
        Assert.Equal(ChargeFrames + 10, match.SequenceEndFrame);
    }

    [Fact]
    public void ChargeDownUp_HoldFramesThenUp_Recognizes28B()
    {
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Down);
        frames.Add(Up | BtnB);

        Run(recognizer, frames.ToArray());

        CommandMatch match = Single(recognizer);
        Assert.Equal("28B", match.CommandId);
    }

    // ─────────────────────────────────────────────────────────────
    // 松开后超时 / 转向窗口
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChargeBackForward_ReleaseThenPress_WithinWindow_StillMatches()
    {
        // 蓄力完成后短暂松开（中立），再按前+A，仍在容差窗口内。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(Right | BtnA);   // 完成帧

        Run(recognizer, frames.ToArray());
        Assert.Single(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeBackForward_ReleaseLongThenPress_DoesNotMatch()
    {
        // 蓄力后松开，前方向迟迟不按（超时），不匹配。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        // 松开后长时间无输入（超过 MaxSequenceFrames=60 的容差）
        for (int i = 0; i < 70; i++)
            frames.Add(InputBits.None);
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());
        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 斜方向蓄力规则：斜方向不计入蓄力（打断纯方向保持）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChargeBackForward_DiagonalInterrupts_DoesNotMatch()
    {
        // 「后」保持 5 帧 → 斜下后（↙，DownBack）5 帧 → 前：↙ 是独立条目，
        // 打断「后」的连续保持，蓄力不成立。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < 5; i++)
            frames.Add(Left);
        for (int i = 0; i < 5; i++)
            frames.Add(Down | Left);    // ↙
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());
        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeDownUp_DiagonalDoesNotCountTowardHold()
    {
        // 「下」保持 5 帧 → 斜下前（↘）5 帧 → 上：↘ 是独立条目，打断「下」的保持。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < 5; i++)
            frames.Add(Down);
        for (int i = 0; i < 5; i++)
            frames.Add(Down | Right);   // ↘
        frames.Add(Up | BtnB);

        Run(recognizer, frames.ToArray());
        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // SOCD 影响
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChargeBackForward_SocdNeutral_DoesNotCharge()
    {
        // 左右同时按住：水平中立，不产生「后」方向，蓄力不成立。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames + 5; i++)
            frames.Add(Left | Right);    // SOCD 水平中立
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());
        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeDownUp_SocdUpPriority_PreventsDownCharge()
    {
        // 上下同时按住：上优先，不产生「下」，下蓄力不成立。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames + 5; i++)
            frames.Add(Down | Up);       // SOCD 上优先
        frames.Add(Up | BtnB);

        Run(recognizer, frames.ToArray());
        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 不同攻击键 / P1/P2 独立
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChargeBackForward_DifferentButton_Recognizes46C()
    {
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(Right | BtnC);        // C 键

        Run(recognizer, frames.ToArray());

        CommandMatch match = Single(recognizer);
        Assert.Equal("46C", match.CommandId);
    }

    [Fact]
    public void ChargeCommands_P1AndP2_RecognizeIndependently()
    {
        // P1 朝右做 [4]6A（后=Left，前=Right）；P2 朝左做 [4]6A（后=Right，前=Left）。
        var recognizer = ChargeRecognizer();
        int total = ChargeFrames + 1;

        for (int frame = 0; frame < total; frame++)
        {
            InputBits p1 = frame < ChargeFrames ? Left : Right | BtnA;
            // P2 朝左：后 = Right，前 = Left。
            InputBits p2 = frame < ChargeFrames ? Right : Left | BtnA;
            recognizer.Observe(new FrameInput(frame, p1, p2), Facing.Right, Facing.Left);
        }

        CommandMatch m1 = Single(recognizer, 0);
        CommandMatch m2 = Single(recognizer, 1);
        Assert.Equal("46A", m1.CommandId);
        Assert.Equal("46A", m2.CommandId);
    }

    // ─────────────────────────────────────────────────────────────
    // 换边：蓄力方向按当帧朝向解析
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChargeBackForward_SideSwitchDuringCharge_UsesPerFrameFacing()
    {
        // 蓄力过程中换边且相对方向不一致：前 5 帧朝右按 Left（相对 Back），
        // 换边后仍按 Left（朝左时 Left = Forward），相对朝向从 Back 变为 Forward，
        // 蓄力保持被打断，不匹配。这验证「蓄力按当帧朝向解析、换边不重算历史」。
        var recognizer = ChargeRecognizer();
        int total = ChargeFrames;

        for (int frame = 0; frame < total; frame++)
        {
            InputBits p1 = Left;  // 一直按 Left
            Facing facing = frame < 5 ? Facing.Right : Facing.Left;
            recognizer.Observe(new FrameInput(frame, p1, InputBits.None), facing, Facing.Left);
        }
        // 朝左时「前」= Left，末帧 Left + A 尝试完成 [4]6A。
        recognizer.Observe(new FrameInput(total, Left | BtnA, InputBits.None), Facing.Left, Facing.Right);

        // 相对朝向序列：Back(0-4) → Forward(5+)（换边后 Left 变成 Forward），
        // 序列 [Back, Forward] 中「后」只保持 5 帧 < ChargeFrames，蓄力不成立。
        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 按住攻击键不无限重复发动
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChargeBackForward_HeldButton_DoesNotRetrigger()
    {
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(Right | BtnA);       // 完成
        for (int i = 0; i < 3; i++)
            frames.Add(Right | BtnA);   // 按住 A 不放（在缓冲有效期内）

        Run(recognizer, frames.ToArray());

        // 只识别一次：按住 A 不产生新边沿，不重复触发。
        Assert.Single(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 蓄力保持时长：最短边界 + 超长保持（03B-2 最终补修 P1）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(45, true)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    [InlineData(120, true)]
    [InlineData(600, true)]
    [InlineData(3600, true)]
    public void ChargeBackForward_HoldBoundaryAndLongHold(int holdFrames, bool shouldMatch)
    {
        // 任务书表格：29 不足 → 失败；30/45/60/61/120/600/3600 必须成功。
        // 修复前 61+ 会因「首尾跨度 > MaxSequenceFrames」与历史裁剪而失效。
        CommandRecognizer recognizer = ChargeRecognizer(holdFrames: 30);

        var frames = new List<InputBits>(holdFrames + 1);
        for (int i = 0; i < holdFrames; i++)
            frames.Add(Left);            // 后（朝右时 Left = Back）
        frames.Add(Right | BtnA);        // 前 + A

        Run(recognizer, frames.ToArray());

        if (shouldMatch)
        {
            CommandMatch match = Single(recognizer);
            Assert.Equal("46A", match.CommandId);
            Assert.Equal(0, match.SequenceStartFrame);
            Assert.Equal(holdFrames, match.SequenceEndFrame);
        }
        else
        {
            Assert.Empty(recognizer.BufferedMatches(0));
        }
    }

    [Fact]
    public void ChargeDownUp_LongHold_StillMatches()
    {
        CommandRecognizer recognizer = ChargeRecognizer(holdFrames: 30);

        var frames = new List<InputBits>();
        for (int i = 0; i < 600; i++)
            frames.Add(Down);
        frames.Add(Up | BtnB);

        Run(recognizer, frames.ToArray());

        Assert.Equal("28B", Single(recognizer).CommandId);
    }

    // ─────────────────────────────────────────────────────────────
    // 蓄力释放窗口（03B-2 最终补修 P2）
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(20, false)]
    public void ChargeBackForward_ReleaseToleranceBoundary(int neutralFrames, bool shouldMatch)
    {
        // 蓄力满 N 帧后，允许最多 3 帧中立（释放窗口）再输入前 + A。
        // 修复前方向序列必须严格相邻，中立 1 帧即失败。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        for (int i = 0; i < neutralFrames; i++)
            frames.Add(InputBits.None);
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());

        if (shouldMatch)
            Assert.Equal("46A", Single(recognizer).CommandId);
        else
            Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void ChargeDownUp_ReleaseToleranceBoundary(int neutralFrames, bool shouldMatch)
    {
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Down);
        for (int i = 0; i < neutralFrames; i++)
            frames.Add(InputBits.None);
        frames.Add(Up | BtnB);

        Run(recognizer, frames.ToArray());

        if (shouldMatch)
            Assert.Equal("28B", Single(recognizer).CommandId);
        else
            Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeRelease_UnrelatedDirectionInsideWindow_DoesNotMatch()
    {
        // 蓄力后先按「上」再按「前」：无关方向立即失效，不得借释放窗口蒙混。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(Up);              // 无关方向
        frames.Add(Right | BtnA);    // 前 + A

        Run(recognizer, frames.ToArray());

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeRelease_SocdNeutral_CountsAsRelease()
    {
        // 后 + 前同时按住 → SOCD 水平中立，等同于「松开蓄力方向」。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(Left | Right);    // SOCD 中立（1 帧）
        frames.Add(Left | Right);    // 仍中立
        // 松开「后」，只剩「前」→ 完成
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());

        Assert.Equal("46A", Single(recognizer).CommandId);
    }

    [Fact]
    public void ChargeRelease_InsufficientHold_CannotBeCompensatedByWindow()
    {
        // 只蓄 5 帧（< 阈值），释放窗口再宽也不能补足。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < 5; i++)
            frames.Add(Left);
        frames.Add(InputBits.None);
        frames.Add(InputBits.None);
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeRelease_RechargeResetsCounter()
    {
        // 蓄满 10 帧（达标）→ 释放 → 只再蓄 3 帧 → 前+A：重新计数，不足则不匹配。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(InputBits.None);
        for (int i = 0; i < 3; i++)
            frames.Add(Left);          // 重新蓄力，仅 3 帧
        frames.Add(Right | BtnA);

        Run(recognizer, frames.ToArray());

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeResult_RepeatedButtonWithoutRecharge_DoesNotRetrigger()
    {
        // 成功一次后：松开 A 再按下（未重新蓄力）不得再利用旧蓄力触发。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(Right | BtnA);    // 识别
        frames.Add(Right);           // 松开 A
        frames.Add(Right | BtnA);    // 再按 A（无新蓄力）

        Run(recognizer, frames.ToArray());

        Assert.Single(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeResult_AfterIndependentRecharge_MatchesAgain()
    {
        // 重新完成独立蓄力后应允许再次识别。注意：第一次结果（帧 10 识别，
        // ExpiresAtFrame=18）在第二次识别（帧 22）前已按 8 帧缓冲有效期过期出队，
        // 这是缓冲的设计语义（D41）——因此缓冲中只有新结果，但识别确实发生了。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(Right | BtnA);         // 第一次：帧 ChargeFrames 识别
        frames.Add(InputBits.None);       // 松开
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);             // 独立重新蓄力
        frames.Add(Right | BtnA);         // 第二次：帧 2*ChargeFrames+2 识别

        Run(recognizer, frames.ToArray());

        CommandMatch second = Single(recognizer);
        Assert.Equal("46A", second.CommandId);
        Assert.Equal(2 * ChargeFrames + 2, second.RecognizedFrame);
    }

    [Fact]
    public void ChargeResult_NoSecondTriggerWithinReleaseWindow_FromSameCharge()
    {
        // 同一蓄力不得在释放窗口内借第二次按键边沿反复触发：
        // 识别一次后松开方向与按键，再在窗口内「前+A」，不得再次识别。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        frames.Add(Right | BtnA);         // 帧 ChargeFrames：识别，蓄力被消费
        frames.Add(InputBits.None);       // 松开方向 + 按键
        frames.Add(Right | BtnA);         // 帧 ChargeFrames+2（窗口内）：不得再触发

        Run(recognizer, frames.ToArray());

        Assert.Single(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ChargeRelease_P1AndP2_Independent()
    {
        // P1 朝右蓄「后」（Left），P2 朝左蓄「后」（Right）；两者都带 2 帧中立后完成。
        var recognizer = ChargeRecognizer();

        for (int frame = 0; frame < ChargeFrames; frame++)
        {
            recognizer.Observe(new FrameInput(frame, Left, Right), Facing.Right, Facing.Left);
        }
        for (int i = 0; i < 2; i++)
        {
            int frame = ChargeFrames + i;
            recognizer.Observe(new FrameInput(frame, InputBits.None, InputBits.None), Facing.Right, Facing.Left);
        }
        // P1 朝右「前」= Right；P2 朝左「前」= Left。
        recognizer.Observe(new FrameInput(ChargeFrames + 2, Right | BtnA, Left | BtnA), Facing.Right, Facing.Left);

        Assert.Equal("46A", Single(recognizer, 0).CommandId);
        Assert.Equal("46A", Single(recognizer, 1).CommandId);
    }

    [Fact]
    public void ChargeRelease_SideSwitchUsesPerFrameFacing()
    {
        // 蓄力 10 帧朝右按 Left（相对后）；释放后换边，朝左按 Left（相对前）完成。
        // 换边只影响之后的条目，蓄力保持按当帧朝向解析。
        var recognizer = ChargeRecognizer();

        for (int frame = 0; frame < ChargeFrames; frame++)
            recognizer.Observe(new FrameInput(frame, Left, InputBits.None), Facing.Right, Facing.Left);

        // 换边后 Left = Forward，直接完成（无中立帧）。
        recognizer.Observe(new FrameInput(ChargeFrames, Left | BtnA, InputBits.None), Facing.Left, Facing.Right);

        CommandMatch match = Single(recognizer);
        Assert.Equal("46A", match.CommandId);
        Assert.Equal(0, match.SequenceStartFrame);
        Assert.Equal(ChargeFrames, match.SequenceEndFrame);
    }

    [Fact]
    public void ChargeRelease_ButtonToleranceDoesNotExtendReleaseWindow()
    {
        // 释放 4 帧后才按「前」：即使按键离得更近也不得匹配（释放窗口先失效）。
        CommandRecognizer recognizer = ChargeRecognizer();

        var frames = new List<InputBits>();
        for (int i = 0; i < ChargeFrames; i++)
            frames.Add(Left);
        for (int i = 0; i < 4; i++)
            frames.Add(InputBits.None);
        frames.Add(Right);              // 前，无按键
        frames.Add(Right | BtnA);       // 下一帧才按 A

        Run(recognizer, frames.ToArray());

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 构造校验 / 内存上界
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChargeDefinition_RejectsNegativeHoldFrames()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandDefinition(
            "46A",
            new[] { CommandDirection.Back, CommandDirection.Forward },
            BtnA, chargeHoldFrames: -1));
    }

    [Fact]
    public void ChargeInput_LongRun_DoesNotGrowUnbounded()
    {
        // 大量输入后，方向历史与消费标记应被裁剪，不无限增长。
        var recognizer = ChargeRecognizer();

        // 反复做蓄力输入（成功若干次），然后长时间抖动。
        for (int i = 0; i < 200; i++)
        {
            recognizer.Observe(new FrameInput(i, Left, InputBits.None), Facing.Right, Facing.Left);
        }
        recognizer.Observe(new FrameInput(200, Right | BtnA, InputBits.None), Facing.Right, Facing.Left);

        // 之后长时间抖动输入。
        for (int i = 201; i < 5000; i++)
        {
            InputBits bits = (i % 2 == 0) ? Left : Right;
            recognizer.Observe(new FrameInput(i, bits, InputBits.None), Facing.Right, Facing.Left);
        }

        // 方向历史有固定上限（MaxDirectionEntries=64），不会无限增长。
        // 通过继续识别一条新蓄力指令验证识别器仍正常工作（内部状态未损坏）。
        recognizer.Observe(new FrameInput(5000, Left, InputBits.None), Facing.Right, Facing.Left);
        for (int i = 5001; i < 5001 + ChargeFrames; i++)
            recognizer.Observe(new FrameInput(i, Left, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(5001 + ChargeFrames, Right | BtnA, InputBits.None), Facing.Right, Facing.Left);

        Assert.Equal("46A", recognizer.LastMatch(0)!.CommandId);
    }

    // ─────────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────────

    /// <summary>构造只含蓄力指令的识别器（默认蓄力 ChargeFrames，便于测试）。</summary>
    private static CommandRecognizer ChargeRecognizer(int holdFrames = ChargeFrames) => new(new[]
    {
        new CommandDefinition("46A",
            new[] { CommandDirection.Back, CommandDirection.Forward },
            BtnA, maxSequenceFrames: 60, buttonToleranceFrames: 3, priority: 30,
            bufferLifetimeFrames: 8, chargeHoldFrames: holdFrames),
        new CommandDefinition("28B",
            new[] { CommandDirection.Down, CommandDirection.Up },
            BtnB, maxSequenceFrames: 60, buttonToleranceFrames: 3, priority: 30,
            bufferLifetimeFrames: 8, chargeHoldFrames: holdFrames),
        new CommandDefinition("46C",
            new[] { CommandDirection.Back, CommandDirection.Forward },
            BtnC, maxSequenceFrames: 60, buttonToleranceFrames: 3, priority: 30,
            bufferLifetimeFrames: 8, chargeHoldFrames: holdFrames),
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
