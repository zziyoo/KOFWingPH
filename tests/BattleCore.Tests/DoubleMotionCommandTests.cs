using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 03B-2：双重方向 / 超必杀指令。
///
/// 覆盖：236236A / 214214B / 623623A 成功、缺少第二段失败、顺序错误失败、
/// 第二段超时失败、普通 236 与双重 236236 的优先级竞争、新手势在旧手势消费后识别、
/// 无按键边沿不触发。复用 03B-1 最终补修后的同帧冲突裁决规则（D47）。
/// </summary>
public class DoubleMotionCommandTests
{
    private const InputBits Down = InputBits.Down;
    private const InputBits Left = InputBits.Left;
    private const InputBits Right = InputBits.Right;
    private const InputBits BtnA = InputBits.LightPunch;
    private const InputBits BtnB = InputBits.HeavyPunch;

    // ─────────────────────────────────────────────────────────────
    // 成功识别
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DoubleQuarterCircleForward_Recognizes236236A()
    {
        // ↓ ↘ → ↓ ↘ → + A
        CommandRecognizer recognizer = DoubleRecognizer();
        Run(recognizer, Script(
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (1, Right | BtnA)));

        CommandMatch match = Single(recognizer);
        Assert.Equal("236236A", match.CommandId);
        Assert.Equal(6, match.SequenceLength);
        Assert.Equal(10, match.SequenceEndFrame);
        Assert.Equal(12, match.ButtonFrame);
        Assert.Equal(12, match.RecognizedFrame);
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10 }, match.DirectionFrames);
    }

    [Fact]
    public void DoubleQuarterCircleBack_Recognizes214214B()
    {
        // ↓ ↙ ← ↓ ↙ ← + B（朝右时 ↙ = Down|Left，← = Left）
        CommandRecognizer recognizer = DoubleRecognizer();
        Run(recognizer, Script(
            (2, Down),
            (2, Down | Left),
            (2, Left),
            (2, Down),
            (2, Down | Left),
            (2, Left),
            (1, Left | BtnB)));

        CommandMatch match = Single(recognizer);
        Assert.Equal("214214B", match.CommandId);
        Assert.Equal(6, match.SequenceLength);
    }

    [Fact]
    public void DoubleDragonPunch_Recognizes623623A()
    {
        // → ↓ ↘ → ↓ ↘ + A
        CommandRecognizer recognizer = DoubleRecognizer();
        Run(recognizer, Script(
            (2, Right),
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (2, Down),
            (2, Down | Right),
            (1, Down | Right | BtnA)));

        CommandMatch match = Single(recognizer);
        Assert.Equal("623623A", match.CommandId);
        Assert.Equal(6, match.SequenceLength);
    }

    // ─────────────────────────────────────────────────────────────
    // 失败场景
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DoubleQuarterCircle_MissingSecondSegment_DoesNotMatch()
    {
        // 只有 ↓ ↘ → + A（单段），双重指令不匹配。
        CommandRecognizer recognizer = DoubleRecognizer();
        Run(recognizer, Script(
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (1, Right | BtnA)));

        // 单段不匹配双重；但若默认表里也有单段 236A，会匹配单段。这里只用双重定义。
        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void DoubleQuarterCircle_WrongOrder_DoesNotMatch()
    {
        // ↓ ↘ → → ↘ ↓（第二段顺序颠倒）不匹配。
        CommandRecognizer recognizer = DoubleRecognizer();
        Run(recognizer, Script(
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (2, Right),
            (2, Down | Right),
            (2, Down),
            (1, Down | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void DoubleQuarterCircle_SecondSegmentTimeout_DoesNotMatch()
    {
        // 第一段 ↓ ↘ →，第二段迟迟不来（中间大量空闲），首尾跨度超 MaxSequenceFrames。
        CommandRecognizer recognizer = DoubleRecognizer();
        Run(recognizer, Script(
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (30, InputBits.None),   // 超时
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (1, Right | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void DoubleMotion_NoButtonEdge_DoesNotTrigger()
    {
        // 完整 236236 方向但无攻击键边沿（A 一直按住，无 0→1），不触发。
        CommandRecognizer recognizer = DoubleRecognizer();
        Run(recognizer, Script(
            (2, Down | BtnA),
            (2, Down | Right | BtnA),
            (2, Right | BtnA),
            (2, Down | BtnA),
            (2, Down | Right | BtnA),
            (2, Right | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 与单段指令的优先级竞争
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DoubleVsSingle_SameFrameConflict_DoubleWinsByPriority()
    {
        // 同时定义单段 236A（优先级 10）与双重 236236A（优先级 15）。
        // 完整 236236 + A：双重指令在同一帧胜出，且不产生单段 236A 幽灵结果。
        var single = new CommandDefinition(
            "236A",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnA, priority: 10);

        var dual = new CommandDefinition(
            "236236A",
            new[]
            {
                CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward,
                CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward,
            },
            BtnA, priority: 15);

        var recognizer = new CommandRecognizer(new[] { single, dual });
        Run(recognizer, Script(
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (1, Right | BtnA)));

        IReadOnlyList<CommandMatch> matches = recognizer.BufferedMatches(0);
        // 只有双重指令胜出，单段 236A 不得借同一手势幽灵触发。
        Assert.Single(matches);
        Assert.Equal("236236A", matches[0].CommandId);
    }

    [Fact]
    public void DoubleCompleted_NewGesture_StillRecognizesSingle()
    {
        // 完成双重 236236A 后，中间插入中立间隔，再独立地做单段 236A（重新走 ↓↘→），
        // 第二段不会被第一段末尾的 → 拼成双重指令，仍能识别为单段 236A。
        var single = new CommandDefinition(
            "236A",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnA, priority: 10, bufferLifetimeFrames: 100);

        var dual = new CommandDefinition(
            "236236A",
            new[]
            {
                CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward,
                CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward,
            },
            BtnA, priority: 15, maxSequenceFrames: 40, bufferLifetimeFrames: 100);

        var recognizer = new CommandRecognizer(new[] { single, dual });
        Run(recognizer, Script(
            // 第一段：236236A
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (1, Right | BtnA),   // → 236236A
            // 中立间隔，分隔两个手势
            (5, InputBits.None),
            // 第二段：独立 236A
            (2, Down),
            (2, Down | Right),
            (2, Right),
            (1, Right | BtnA))); // → 236A

        IReadOnlyList<CommandMatch> matches = recognizer.BufferedMatches(0);
        Assert.Equal(2, matches.Count);
        Assert.Equal("236236A", matches[0].CommandId);
        Assert.Equal("236A", matches[1].CommandId);
    }

    // ─────────────────────────────────────────────────────────────
    // 真正的超时边界：方向连续，仅首尾跨度超限（03B-2 最终补修 P2）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DoubleQuarterCircle_ExactMaxSpan_Matches()
    {
        // 方向进入帧 0,8,16,24,32,40；首尾跨度恰好 40 == MaxSequenceFrames，应成功。
        CommandRecognizer recognizer = DoubleRecognizer();
        RunDirectionFrames(
            recognizer,
            directionFrames: new[] { 0, 8, 16, 24, 32, 40 },
            buttonFrame: 42);

        CommandMatch match = Single(recognizer);
        Assert.Equal("236236A", match.CommandId);
        Assert.Equal(0, match.SequenceStartFrame);
        Assert.Equal(40, match.SequenceEndFrame);
        Assert.Equal(42, match.ButtonFrame);
    }

    [Fact]
    public void DoubleQuarterCircle_OneFrameOverMaxSpan_DoesNotMatch()
    {
        // 同上但末方向 @41：首尾跨度 41 > 40，必须因超时失败。
        // 方向序列本身完整连续、按键也在容差内——失败只能来自跨度判断。
        // 变异验证：移除跨度检查后本用例会失败。
        CommandRecognizer recognizer = DoubleRecognizer();
        RunDirectionFrames(
            recognizer,
            directionFrames: new[] { 0, 8, 16, 24, 32, 41 },
            buttonFrame: 43);

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 按指定的方向进入帧构造逐帧输入，末段一直保持到 <paramref name="buttonFrame"/>，
    /// 并在该帧叠加 A 键（末方向 + 按键）。
    /// </summary>
    private static void RunDirectionFrames(
        CommandRecognizer recognizer,
        int[] directionFrames,
        int buttonFrame)
    {
        InputBits[] directions =
        {
            Down, Down | Right, Right, Down, Down | Right, Right,
        };

        var log = new InputBits[buttonFrame + 1];
        for (int i = 0; i < directionFrames.Length; i++)
        {
            int from = directionFrames[i];
            int to = i + 1 < directionFrames.Length ? directionFrames[i + 1] : buttonFrame + 1;
            for (int f = from; f < to; f++)
                log[f] = directions[i];
        }

        log[buttonFrame] = directions[^1] | BtnA;
        Run(recognizer, log);
    }

    /// <summary>只含双重指令的识别器（不含单段，便于隔离测试）。</summary>
    private static CommandRecognizer DoubleRecognizer() => new(new[]
    {
        new CommandDefinition("236236A",
            new[]
            {
                CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward,
                CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward,
            },
            BtnA, maxSequenceFrames: 40, buttonToleranceFrames: 3, priority: 15,
            bufferLifetimeFrames: 8),
        new CommandDefinition("214214B",
            new[]
            {
                CommandDirection.Down, CommandDirection.DownBack, CommandDirection.Back,
                CommandDirection.Down, CommandDirection.DownBack, CommandDirection.Back,
            },
            BtnB, maxSequenceFrames: 40, buttonToleranceFrames: 3, priority: 15,
            bufferLifetimeFrames: 8),
        new CommandDefinition("623623A",
            new[]
            {
                CommandDirection.Forward, CommandDirection.Down, CommandDirection.DownForward,
                CommandDirection.Forward, CommandDirection.Down, CommandDirection.DownForward,
            },
            BtnA, maxSequenceFrames: 40, buttonToleranceFrames: 3, priority: 25,
            bufferLifetimeFrames: 8),
    });

    private static CommandMatch Single(CommandRecognizer recognizer, int player = 0)
    {
        IReadOnlyList<CommandMatch> matches = recognizer.BufferedMatches(player);
        Assert.Single(matches);
        return matches[0];
    }

    private static InputBits[] Script(params (int Frames, InputBits Bits)[] segments)
    {
        var frames = new List<InputBits>();
        foreach ((int count, InputBits bits) in segments)
        {
            for (int i = 0; i < count; i++)
                frames.Add(bits);
        }

        return frames.ToArray();
    }

    private static void Run(CommandRecognizer recognizer, InputBits[] player1)
    {
        for (int frame = 0; frame < player1.Length; frame++)
        {
            recognizer.Observe(new FrameInput(frame, player1[frame], InputBits.None), Facing.Right, Facing.Left);
        }
    }
}
