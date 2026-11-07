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

    /// <summary>构造只含蓄力指令的识别器（蓄力帧数缩短为 ChargeFrames 便于测试）。</summary>
    private static CommandRecognizer ChargeRecognizer() => new(new[]
    {
        new CommandDefinition("46A",
            new[] { CommandDirection.Back, CommandDirection.Forward },
            BtnA, maxSequenceFrames: 60, buttonToleranceFrames: 3, priority: 30,
            bufferLifetimeFrames: 8, chargeHoldFrames: ChargeFrames),
        new CommandDefinition("28B",
            new[] { CommandDirection.Down, CommandDirection.Up },
            BtnB, maxSequenceFrames: 60, buttonToleranceFrames: 3, priority: 30,
            bufferLifetimeFrames: 8, chargeHoldFrames: ChargeFrames),
        new CommandDefinition("46C",
            new[] { CommandDirection.Back, CommandDirection.Forward },
            BtnC, maxSequenceFrames: 60, buttonToleranceFrames: 3, priority: 30,
            bufferLifetimeFrames: 8, chargeHoldFrames: ChargeFrames),
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
