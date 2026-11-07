using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 03B-1：拳皇式基础指令识别。
///
/// 覆盖点（对应任务书）：236A / 623A / 214B / 66、错误顺序、超时、按键提前/延迟、
/// 方向保持、同一次按键不重复触发、236 与 623 竞争、同优先级稳定、
/// P1/P2 独立、面向左、输入中换边、SOCD、不同渲染帧率下同一逻辑输入日志结果一致、
/// 识别结果缓冲时效。
///
/// 所有断言都检查<b>具体指令 ID、完成帧、按键帧、优先级</b>，不满足于「没抛异常」。
/// </summary>
public class CommandRecognitionTests
{
    private const InputBits Down = InputBits.Down;
    private const InputBits Up = InputBits.Up;
    private const InputBits Left = InputBits.Left;
    private const InputBits Right = InputBits.Right;
    private const InputBits BtnA = InputBits.LightPunch;
    private const InputBits BtnB = InputBits.HeavyPunch;

    // ─────────────────────────────────────────────────────────────
    // 基础指令
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void QuarterCircleForward_Recognizes236A_WithExactFramesAndMetadata()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (3, Down | Right),
            (3, Right),
            (1, Right | BtnA)));

        CommandMatch match = Single(recognizer);

        Assert.Equal(CommandCatalog.QuarterCircleForwardLightPunch, match.CommandId);
        Assert.Equal(0, match.PlayerIndex);
        Assert.Equal(9, match.RecognizedFrame);
        Assert.Equal(0, match.SequenceStartFrame);
        Assert.Equal(6, match.SequenceEndFrame);
        Assert.Equal(9, match.ButtonFrame);
        Assert.Equal(10, match.Priority);
        Assert.Equal(3, match.SequenceLength);
        Assert.Equal(new[] { 0, 3, 6 }, match.DirectionFrames);
        Assert.Equal(17, match.ExpiresAtFrame); // 9 + 8
    }

    [Fact]
    public void DragonPunch_Recognizes623A()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Right),
            (3, Down),
            (3, Down | Right),
            (1, Down | Right | BtnA)));

        CommandMatch match = Single(recognizer);
        Assert.Equal(CommandCatalog.DragonPunchLightPunch, match.CommandId);
        Assert.Equal(6, match.SequenceEndFrame);
        Assert.Equal(9, match.ButtonFrame);
        Assert.Equal(9, match.RecognizedFrame);
        Assert.Equal(20, match.Priority);
        Assert.Equal(new[] { 0, 3, 6 }, match.DirectionFrames);
    }

    [Fact]
    public void QuarterCircleBack_Recognizes214B()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (3, Down | Left),
            (3, Left),
            (1, Left | BtnB)));

        CommandMatch match = Single(recognizer);
        Assert.Equal(CommandCatalog.QuarterCircleBackHeavyPunch, match.CommandId);
        Assert.Equal(0, match.SequenceStartFrame);
        Assert.Equal(6, match.SequenceEndFrame);
        Assert.Equal(9, match.ButtonFrame);
        Assert.Equal(new[] { 0, 3, 6 }, match.DirectionFrames);
    }

    [Fact]
    public void DoubleTapForward_Recognizes66_OnCompletionFrameWithoutButton()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Right),
            (3, InputBits.None),
            (5, Right)));

        CommandMatch match = Single(recognizer);
        Assert.Equal(CommandCatalog.DoubleTapForward, match.CommandId);
        Assert.Equal(6, match.SequenceEndFrame);
        Assert.Equal(6, match.RecognizedFrame);
        Assert.Equal(-1, match.ButtonFrame);
        Assert.Equal(0, match.Priority);
        Assert.Equal(new[] { 0, 3, 6 }, match.DirectionFrames);

        // 继续按住不应重复触发：纯方向指令只在末方向「进入」的那一帧成立。
        Assert.Single(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 不匹配 / 超时
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void WrongDirectionOrder_DoesNotMatch()
    {
        // ↓ → ↘ + A：方向集合对了，顺序错了。
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (3, Right),
            (3, Down | Right),
            (1, Down | Right | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
        Assert.Null(recognizer.LastMatch(0));
    }

    [Fact]
    public void SequenceExceedingMaxFrames_TimesOut()
    {
        // 首方向 @0，末方向 @30，默认上限 20 帧 → 超时，按键再准也不匹配。
        CommandRecognizer recognizer = Run(Default(), Script(
            (10, Down),
            (20, Down | Right),
            (2, Right),
            (1, Right | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void MotionWithoutRequiredButton_DoesNotMatch()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (3, Down | Right),
            (3, Right)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ButtonHeldSinceBeforeMotion_ProducesNoEdgeAndDoesNotMatch()
    {
        // A 从第 0 帧就按住：没有新的按下边沿，不能触发 236A。
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down | BtnA),
            (3, Down | Right | BtnA),
            (3, Right | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 按键容差 / 方向保持
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ButtonPressedThreeFramesBeforeCompletion_Matches()
    {
        // 完成帧 6，按键在 3，帧差 3 == 容差上限。
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (1, Down | BtnA),
            (2, Down | Right),
            (3, Right)));

        CommandMatch match = Single(recognizer);
        Assert.Equal(6, match.SequenceEndFrame);
        Assert.Equal(3, match.ButtonFrame);
        Assert.Equal(6, match.RecognizedFrame);
    }

    [Fact]
    public void ButtonPressedFourFramesBeforeCompletion_DoesNotMatch()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (2, Down),
            (1, Down | BtnA),
            (1, Down),
            (2, Down | Right),
            (3, Right)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void ButtonPressedThreeFramesAfterCompletion_Matches()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (3, Down | Right),
            (3, Right),
            (1, Right | BtnA),
            (1, Right)));

        CommandMatch match = Single(recognizer);
        Assert.Equal(6, match.SequenceEndFrame);
        Assert.Equal(9, match.ButtonFrame);
        Assert.Equal(9, match.RecognizedFrame);
    }

    [Fact]
    public void ButtonPressedFourFramesAfterCompletion_DoesNotMatch()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (3, Down | Right),
            (4, Right),
            (1, Right | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void EachDirectionHeldTenFrames_StillMatchesWithinMaxSpan()
    {
        // 三个方向各按 10 帧：首尾跨度恰好 20，仍在默认上限内；按住帧不计入序列元素。
        CommandRecognizer recognizer = Run(Default(), Script(
            (10, Down),
            (10, Down | Right),
            (2, Right),
            (1, Right | BtnA)));

        CommandMatch match = Single(recognizer);
        Assert.Equal(new[] { 0, 10, 20 }, match.DirectionFrames);
        Assert.Equal(20, match.SequenceEndFrame);
        Assert.Equal(22, match.ButtonFrame);
    }

    [Fact]
    public void SameButtonPress_DoesNotRetriggerWhileHeldInsideToleranceWindow()
    {
        // 完成帧 6 按下 A；7..9 继续按住，仍在 3 帧容差内。
        // 若「一次边沿只消费一次」失效，同一按键会在 7/8/9 反复产出 236A。
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (3, Down | Right),
            (1, Right | BtnA),    // 6：识别
            (3, Right | BtnA)));  // 7..9：仍在容差窗口内

        Assert.Single(recognizer.BufferedMatches(0));
        Assert.Equal(6, recognizer.LastMatch(0)!.RecognizedFrame);
    }

    [Fact]
    public void SameButtonPress_DoesNotRetriggerWhileHeldOrAfterBufferExpires()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
            (3, Down),
            (3, Down | Right),
            (3, Right),
            (1, Right | BtnA),   // 9：识别
            (6, Right | BtnA),   // 10..15：按住，无新边沿
            (1, Right),          // 16：松开
            (1, Right | BtnA))); // 17：新边沿，但离完成帧 6 已 11 帧 → 不匹配

        Assert.Single(recognizer.BufferedMatches(0));
        Assert.Equal(9, recognizer.LastMatch(0)!.RecognizedFrame);
    }

    // ─────────────────────────────────────────────────────────────
    // 冲突与优先级
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DragonPunchAndFireballBothMatch_623WinsByPriority()
    {
        // →↓↘→ + A：236 以最后一帧的 → 结束；623 以两帧前的 ↘ 结束，仍在容差内。
        CommandRecognizer recognizer = Run(Default(), Script(
            (2, Right),
            (2, Down),
            (2, Down | Right),
            (2, Right | BtnA)));

        CommandMatch match = Single(recognizer);
        Assert.Equal(CommandCatalog.DragonPunchLightPunch, match.CommandId);
        Assert.Equal(20, match.Priority);
        Assert.Equal(4, match.SequenceEndFrame); // 623 的末方向是 ↘
        Assert.Equal(6, match.ButtonFrame);
        Assert.Equal(6, match.RecognizedFrame);
    }

    [Fact]
    public void EqualPriorityAndLength_TieBreaksByOrdinalId_IndependentOfDefinitionOrder()
    {
        static CommandDefinition Def(string id) => new(
            id,
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            id == "236A" ? BtnA : BtnB,
            priority: 5,
            bufferLifetimeFrames: 8);

        InputBits[] script = Script(
            (3, Down),
            (3, Down | Right),
            (3, Right),
            (1, Right | BtnA | BtnB));

        var forwardOrder = new CommandRecognizer(new[] { Def("236A"), Def("236B") });
        Run(forwardOrder, script);

        var reverseOrder = new CommandRecognizer(new[] { Def("236B"), Def("236A") });
        Run(reverseOrder, script);

        Assert.Equal("236A", Single(forwardOrder).CommandId);
        Assert.Equal("236A", Single(reverseOrder).CommandId);
    }

    [Fact]
    public void HigherPriorityShortCommand_BeatsLongerLowerPriorityCommand()
    {
        var shortCommand = new CommandDefinition(
            "SHORT",
            new[] { CommandDirection.Down, CommandDirection.DownForward },
            BtnA,
            priority: 100);

        var longCommand = new CommandDefinition(
            "LONG",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnA,
            priority: 10);

        var recognizer = new CommandRecognizer(new[] { longCommand, shortCommand });
        Run(recognizer, Script(
            (3, Down),
            (3, Down | Right),
            (1, Right | BtnA)));

        CommandMatch match = Single(recognizer);
        Assert.Equal("SHORT", match.CommandId);
        Assert.Equal(100, match.Priority);
    }

    [Fact]
    public void DuplicateCommandIds_AreRejected()
    {
        static CommandDefinition Def() => new("236A", new[] { CommandDirection.Down }, BtnA);

        Assert.Throws<ArgumentException>(() => new CommandRecognizer(new[] { Def(), Def() }));
    }

    // ─────────────────────────────────────────────────────────────
    // 朝向 / 换边 / 双人独立
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FacingLeft_QuarterCircleForwardStillRecognizes236A()
    {
        CommandRecognizer recognizer = Run(Default(), Script(
                (3, Down),
                (3, Down | Left),
                (3, Left),
                (1, Left | BtnA)),
            player1Facing: Facing.Left);

        CommandMatch match = Single(recognizer);
        Assert.Equal(CommandCatalog.QuarterCircleForwardLightPunch, match.CommandId);
        Assert.Equal(new[] { 0, 3, 6 }, match.DirectionFrames);
    }

    [Fact]
    public void SideSwitchDuringInput_KeepsHistoricalFacingPerFrame()
    {
        // 前两段在朝右下完成（↓↘），末段已换到朝左，用「左」表达前进。
        // 若用最终朝向左重算历史，第 2 帧的「下+右」会变成「下后」→ 指令断裂。
        InputBits[] script = Script(
            (2, Down),
            (2, Down | Right),
            (1, Left),
            (1, Left | BtnA));

        Facing[] facings =
        {
            Facing.Right, Facing.Right, Facing.Right, Facing.Right, Facing.Left, Facing.Left,
        };

        CommandRecognizer recognizer = Run(Default(), script, player1Facing: facings);

        CommandMatch match = Single(recognizer);
        Assert.Equal(CommandCatalog.QuarterCircleForwardLightPunch, match.CommandId);
        Assert.Equal(new[] { 0, 2, 4 }, match.DirectionFrames);
    }

    [Fact]
    public void TwoPlayers_RecognizeIndependentlyInSameFrames()
    {
        InputBits[] p1 = Script(
            (3, Down),
            (3, Down | Right),
            (3, Right),
            (1, Right | BtnA));

        // P2 朝左：同样的绝对输入（↓、↓+右、右）在相对朝向里是 ↓↙←。
        InputBits[] p2 = Script(
            (3, Down),
            (3, Down | Right),
            (3, Right),
            (1, Right | BtnB));

        var recognizer = new CommandRecognizer(CommandCatalog.CreateDefault());
        for (int frame = 0; frame < p1.Length; frame++)
        {
            recognizer.Observe(
                new FrameInput(frame, p1[frame], p2[frame]),
                player1Facing: Facing.Right,
                player2Facing: Facing.Left);
        }

        CommandMatch match1 = Single(recognizer, player: 0);
        CommandMatch match2 = Single(recognizer, player: 1);

        Assert.Equal(CommandCatalog.QuarterCircleForwardLightPunch, match1.CommandId);
        Assert.Equal(CommandCatalog.QuarterCircleBackHeavyPunch, match2.CommandId);
        Assert.Equal(9, match1.ButtonFrame);
        Assert.Equal(9, match2.ButtonFrame);
    }

    // ─────────────────────────────────────────────────────────────
    // SOCD
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void HorizontalSocdCancel_Prevents236A()
    {
        // 左右一直同时按住：水平方向恒为中立，↓↘→ 退化成 ↓↓↓。
        CommandRecognizer recognizer = Run(Default(), Script(
            (2, Down | Left | Right),
            (2, Down | Left | Right),
            (2, Left | Right),
            (1, Left | Right | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    [Fact]
    public void VerticalSocdUpPriority_Prevents236A()
    {
        // 上下同时按住时上优先：↓↘→ 退化成 ↑↗↗。
        CommandRecognizer recognizer = Run(Default(), Script(
            (2, Down | Up),
            (2, Down | Up | Right),
            (2, Up | Right),
            (1, Up | Right | BtnA)));

        Assert.Empty(recognizer.BufferedMatches(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 缓冲时效
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RecognizedCommand_StaysBufferedUntilExpiry_ThenIsRemoved()
    {
        // 识别帧 9，有效期 8 → ExpiresAtFrame = 17。
        var recognizer = Default();
        Run(recognizer, Script(
            (3, Down),
            (3, Down | Right),
            (3, Right),
            (1, Right | BtnA)));

        Feed(recognizer, fromFrame: 10, count: 8, Right); // 10..17

        Assert.Single(recognizer.BufferedMatches(0));
        Assert.Equal(17, recognizer.LastMatch(0)!.ExpiresAtFrame);

        Feed(recognizer, fromFrame: 18, count: 1, Right); // 18 > 17

        Assert.Empty(recognizer.BufferedMatches(0));
        // 展示用的「最近一次」仍保留，便于 HUD 观察。
        Assert.NotNull(recognizer.LastMatch(0));
    }

    // ─────────────────────────────────────────────────────────────
    // 帧率无关性（同一逻辑输入日志 → 同一识别结果）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SameLogicInputLog_ProducesIdenticalResults_DifferentRenderRates()
    {
        FrameInput[] log = BuildRateTestLog();

        static CommandDefinition Def(string id, InputBits button, params CommandDirection[] sequence) => new(
            id,
            sequence,
            button,
            priority: 10,
            bufferLifetimeFrames: 1_000);

        CommandDefinition[] definitions =
        {
            Def("236A", BtnA, CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward),
            Def("214B", BtnB, CommandDirection.Down, CommandDirection.DownBack, CommandDirection.Back),
        };

        (CommandRecognizer at30, BattleSession session30) = RunAtFps(1.0 / 30.0, log, definitions, targetFrame: 100);
        (CommandRecognizer at144, BattleSession session144) = RunAtFps(1.0 / 144.0, log, definitions, targetFrame: 100);

        Assert.True(session30.CurrentState.Frame >= 100);
        Assert.True(session144.CurrentState.Frame >= 100);

        AssertSameMatches(at30.BufferedMatches(0), at144.BufferedMatches(0));
        AssertSameMatches(at30.BufferedMatches(1), at144.BufferedMatches(1));

        // 日志里 P1 在 20..29 做 236A，P2（朝左）在 60..69 做 214B。
        CommandMatch p1 = Single(at30, player: 0);
        CommandMatch p2 = Single(at30, player: 1);
        Assert.Equal("236A", p1.CommandId);
        Assert.Equal(26, p1.SequenceEndFrame); // 方向条目 20/23/26
        Assert.Equal("214B", p2.CommandId);
        Assert.Equal(66, p2.SequenceEndFrame); // 方向条目 60/63/66
    }

    private static FrameInput[] BuildRateTestLog()
    {
        const int length = 100;
        var log = new FrameInput[length];
        for (int frame = 0; frame < length; frame++)
            log[frame] = FrameInput.Empty(frame);

        // P1 朝右：236A（↓ @20，↓↘ @23，→ @26，A @29）。
        Set(log, 20, 3, InputBits.Down, InputBits.None);
        Set(log, 23, 3, InputBits.Down | InputBits.Right, InputBits.None);
        Set(log, 26, 3, InputBits.Right, InputBits.None);
        log[29] = new FrameInput(29, InputBits.Right | BtnA, InputBits.None);
        Set(log, 30, 5, InputBits.Right, InputBits.None);

        // P2 朝左：原始 ↓、↓+右、右 → 相对 ↓↙←，B @69。
        Set(log, 60, 3, InputBits.None, InputBits.Down);
        Set(log, 63, 3, InputBits.None, InputBits.Down | InputBits.Right);
        Set(log, 66, 3, InputBits.None, InputBits.Right);
        log[69] = new FrameInput(69, InputBits.None, InputBits.Right | BtnB);

        return log;
    }

    private static (CommandRecognizer Recognizer, BattleSession Session) RunAtFps(
        double deltaSeconds,
        FrameInput[] log,
        CommandDefinition[] definitions,
        int targetFrame)
    {
        var recognizer = new CommandRecognizer(definitions);
        var session = new BattleSession(
            frameCommitted: (input, state) =>
                recognizer.Observe(input, state.Fighter(0).Facing, state.Fighter(1).Facing));

        FrameInput Provider(int frame) =>
            frame < log.Length ? log[frame] : FrameInput.Empty(frame);

        int guard = 0;
        while (session.CurrentState.Frame < targetFrame && guard++ < 100_000)
            session.Advance(deltaSeconds, inputProvider: Provider);

        return (recognizer, session);
    }

    private static void AssertSameMatches(
        IReadOnlyList<CommandMatch> expected,
        IReadOnlyList<CommandMatch> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].CommandId, actual[i].CommandId);
            Assert.Equal(expected[i].PlayerIndex, actual[i].PlayerIndex);
            Assert.Equal(expected[i].RecognizedFrame, actual[i].RecognizedFrame);
            Assert.Equal(expected[i].SequenceStartFrame, actual[i].SequenceStartFrame);
            Assert.Equal(expected[i].SequenceEndFrame, actual[i].SequenceEndFrame);
            Assert.Equal(expected[i].ButtonFrame, actual[i].ButtonFrame);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────────

    private static CommandRecognizer Default() => new(CommandCatalog.CreateDefault());

    private static CommandMatch Single(CommandRecognizer recognizer, int player = 0)
    {
        IReadOnlyList<CommandMatch> matches = recognizer.BufferedMatches(player);
        Assert.Single(matches);
        return matches[0];
    }

    /// <summary>把「每段帧数 + 该段输入位」展开成逐帧输入。</summary>
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

    /// <summary>按帧喂入 P1 输入（P2 无输入），默认朝右。</summary>
    private static CommandRecognizer Run(CommandRecognizer recognizer, InputBits[] player1) =>
        Run(recognizer, player1, Facing.Right);

    /// <summary>按帧喂入 P1 输入，使用固定朝向。</summary>
    private static CommandRecognizer Run(
        CommandRecognizer recognizer,
        InputBits[] player1,
        Facing player1Facing) =>
        Run(recognizer, player1, Enumerable.Repeat(player1Facing, player1.Length).ToArray());

    /// <summary>按帧喂入 P1 输入与逐帧朝向（用于换边场景）。</summary>
    private static CommandRecognizer Run(
        CommandRecognizer recognizer,
        InputBits[] player1,
        Facing[] player1Facing)
    {
        for (int frame = 0; frame < player1.Length; frame++)
        {
            recognizer.Observe(
                new FrameInput(frame, player1[frame], InputBits.None),
                player1Facing[frame],
                player2Facing: Facing.Left);
        }

        return recognizer;
    }

    /// <summary>从给定帧号起连续喂入相同输入（用于观察缓冲过期）。</summary>
    private static void Feed(CommandRecognizer recognizer, int fromFrame, int count, InputBits bits)
    {
        for (int i = 0; i < count; i++)
        {
            recognizer.Observe(
                new FrameInput(fromFrame + i, bits, InputBits.None),
                Facing.Right,
                Facing.Left);
        }
    }

    private static void Set(FrameInput[] log, int from, int count, InputBits p1, InputBits p2)
    {
        for (int i = 0; i < count; i++)
            log[from + i] = new FrameInput(from + i, p1, p2);
    }

    // ─────────────────────────────────────────────────────────────
    // 阶段 03B-1 补修：P1 幽灵指令 / P2 校验与连续性 / 换边时序
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LosingCandidateEdge_DoesNotGhostTriggerOnNextFrame()
    {
        // 236A（优先级 20）与 236B（优先级 10）共享同一方向序列，只差按键。
        var fireballA = new CommandDefinition(
            "236A",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnA,
            maxSequenceFrames: 20,
            buttonToleranceFrames: 3,
            priority: 20,
            bufferLifetimeFrames: 8);

        var fireballB = new CommandDefinition(
            "236B",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnB,
            maxSequenceFrames: 20,
            buttonToleranceFrames: 3,
            priority: 10,
            bufferLifetimeFrames: 8);

        var recognizer = new CommandRecognizer(new[] { fireballA, fireballB });

        // 帧 0 ↓；帧 1 ↘+B；帧 2 →+A；帧 3 →（无新攻击按键）。
        // 帧 2 上 236A 与 236B 同时成立（B 在帧 1 仍处于 3 帧容差内），236A 胜出。
        // 帧 3 必须不能利用帧 1 残留的 B 边沿再触发 236B。
        Run(recognizer, new[]
        {
            Down,               // 0
            Down | Right | BtnB, // 1
            Right | BtnA,       // 2
            Right,              // 3
        });

        IReadOnlyList<CommandMatch> matches = recognizer.BufferedMatches(0);
        Assert.Single(matches);
        Assert.Equal("236A", matches[0].CommandId);
        Assert.Equal(2, matches[0].RecognizedFrame);
    }

    [Fact]
    public void IndependentGestureAfterConflict_StillProducesLegalCommand()
    {
        var fireballA = new CommandDefinition(
            "236A",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnA,
            priority: 20);

        var fireballB = new CommandDefinition(
            "236B",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnB,
            priority: 10);

        var recognizer = new CommandRecognizer(new[] { fireballA, fireballB });

        // 第一个手势：↓ ↘ → + A（帧 0..3，A 在帧 3）→ 236A 胜出。
        // 第二个手势：独立地重新 ↓ ↘ → + B（帧 4..7，B 在帧 7），
        // 必须仍能被识别为 236B —— 这是两个独立手势，不是同一手势的落败候选。
        Run(recognizer, new[]
        {
            Down,               // 0
            Down | Right,       // 1
            Right,              // 2
            Right | BtnA,       // 3 → 236A
            Down,               // 4
            Down | Right,       // 5
            Right,              // 6
            Right | BtnB,       // 7 → 236B
        });

        IReadOnlyList<CommandMatch> matches = recognizer.BufferedMatches(0);
        Assert.Equal(2, matches.Count);
        Assert.Equal("236A", matches[0].CommandId);
        Assert.Equal("236B", matches[1].CommandId);
        Assert.Equal(3, matches[0].RecognizedFrame);
        Assert.Equal(7, matches[1].RecognizedFrame);
    }

    [Fact]
    public void LosingCandidateSuppression_IsIndependentOfDefinitionOrder()
    {
        var fireballA = new CommandDefinition(
            "236A",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnA,
            priority: 20);

        var fireballB = new CommandDefinition(
            "236B",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            BtnB,
            priority: 10);

        InputBits[] input =
        {
            Down,
            Down | Right | BtnB,
            Right | BtnA,
            Right,
        };

        var orderAB = new CommandRecognizer(new[] { fireballA, fireballB });
        Run(orderAB, input);

        var orderBA = new CommandRecognizer(new[] { fireballB, fireballA });
        Run(orderBA, input);

        Assert.Equal("236A", Single(orderAB).CommandId);
        Assert.Equal("236A", Single(orderBA).CommandId);
    }

    [Fact]
    public void DragonPunchVsFireballConflict_StillChooses623()
    {
        // 回归：236/623 竞争在补修后不得退化（→↓↘→ + A，623 胜出）。
        CommandRecognizer recognizer = Run(Default(), Script(
            (2, Right),
            (2, Down),
            (2, Down | Right),
            (2, Right | BtnA)));

        CommandMatch match = Single(recognizer);
        Assert.Equal(CommandCatalog.DragonPunchLightPunch, match.CommandId);
    }

    [Fact]
    public void Observe_RejectsDuplicateFrame_BeforeMutatingState()
    {
        var recognizer = Default();
        recognizer.Observe(new FrameInput(0, InputBits.None, InputBits.None), Facing.Right, Facing.Left);

        Assert.Throws<InvalidOperationException>(
            () => recognizer.Observe(new FrameInput(0, InputBits.None, InputBits.None), Facing.Right, Facing.Left));

        // 失败后继续送入正确帧，识别器仍正常工作。
        recognizer.Observe(new FrameInput(1, Down, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(2, Down, InputBits.None), Facing.Right, Facing.Left);
    }

    [Fact]
    public void Observe_RejectsSkippedFrame_AndContinuesAfterCorrectFrame()
    {
        var recognizer = Default();
        recognizer.Observe(new FrameInput(0, InputBits.None, InputBits.None), Facing.Right, Facing.Left);

        Assert.Throws<InvalidOperationException>(
            () => recognizer.Observe(new FrameInput(2, InputBits.None, InputBits.None), Facing.Right, Facing.Left));

        // 失败后送入正确帧（1）可继续。
        recognizer.Observe(new FrameInput(1, Down, InputBits.None), Facing.Right, Facing.Left);
    }

    [Fact]
    public void Observe_RejectsBackwardsFrame()
    {
        var recognizer = Default();
        recognizer.Observe(new FrameInput(2, InputBits.None, InputBits.None), Facing.Right, Facing.Left);

        Assert.Throws<InvalidOperationException>(
            () => recognizer.Observe(new FrameInput(1, InputBits.None, InputBits.None), Facing.Right, Facing.Left));
    }

    [Fact]
    public void Observe_AllowsNonZeroInitialFrame_ThenRequiresContiguous()
    {
        // 非零初始帧（快照恢复场景）：100、101、102 正常。
        var recognizer = Default();
        recognizer.Observe(new FrameInput(100, InputBits.None, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(101, Down, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(102, Down, InputBits.None), Facing.Right, Facing.Left);

        // 回退到 101 拒绝。
        Assert.Throws<InvalidOperationException>(
            () => recognizer.Observe(new FrameInput(101, Down, InputBits.None), Facing.Right, Facing.Left));
    }

    [Fact]
    public void CommandDefinition_RejectsAdjacentDuplicateDirections()
    {
        Assert.Throws<ArgumentException>(() => new CommandDefinition(
            "DUP",
            new[] { CommandDirection.Down, CommandDirection.Down, CommandDirection.Forward },
            BtnA));
    }

    [Fact]
    public void CommandDefinition_RejectsInvalidEnumValue()
    {
        var invalid = (CommandDirection)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandDefinition(
            "BAD",
            new[] { CommandDirection.Down, invalid },
            BtnA));
    }

    [Fact]
    public void CommandDefinition_CopiesDirectionSequence_IgnoresLaterSourceMutation()
    {
        var source = new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward };
        var definition = new CommandDefinition("236A", source, BtnA);

        source[0] = CommandDirection.Up;

        Assert.Equal(CommandDirection.Down, definition.DirectionSequence[0]);
    }

    [Fact]
    public void CommandDefinition_RejectsEmptySequence()
    {
        Assert.Throws<ArgumentException>(() => new CommandDefinition(
            "EMPTY",
            Array.Empty<CommandDirection>(),
            BtnA));
    }

    [Fact]
    public void Observe_RejectsNegativeFrame()
    {
        var recognizer = Default();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => recognizer.Observe(new FrameInput(-1, InputBits.None, InputBits.None), Facing.Right, Facing.Left));
    }

    // ─────────────────────────────────────────────────────────────
    // P4：换边朝向时序 —— 输入按「当帧开始时的朝向」解析
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FrameCommittedObserver_ReceivesFrameStartState_AlignedWithInputFrame()
    {
        // 观察者收到的「帧开始状态」必须与输入帧号对齐：
        // state.Frame == input.Frame（不是帧结束后的 Frame+1）。
        var observed = new List<(int StateFrame, int InputFrame, Facing Facing)>();

        var session = new BattleSession(
            initialState: BattleState.CreateDefault(),
            frameCommitted: (input, state) =>
                observed.Add((state.Frame, input.Frame, state.Fighter(0).Facing)));

        // 推进一帧：默认 P1 朝右，模拟不改朝向。
        session.StepExact(1);

        (int stateFrame, int inputFrame, Facing facing) = Assert.Single(observed);
        Assert.Equal(inputFrame, stateFrame);   // 帧开始状态，与输入同帧
        Assert.Equal(0, stateFrame);            // 首帧
        Assert.Equal(Facing.Right, facing);     // 当帧开始朝向 = 朝右
    }

    [Fact]
    public void SideSwitchAcrossFrames_ResolvesEachFrameWithItsOwnFacing()
    {
        // 帧 0/1 朝右做 ↓、↓↘；帧 2 起翻转为朝左，用「左」表达前进完成 →
        // 识别为 236A。证明识别器按「当帧」朝向解析，而非用最终朝向重算历史。
        // （与既有 SideSwitchDuringInput 互补：这里显式断言面向右的两帧不被误解析。）
        CommandRecognizer recognizer = Default();

        recognizer.Observe(new FrameInput(0, Down, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(1, Down | Right, InputBits.None), Facing.Right, Facing.Left);
        recognizer.Observe(new FrameInput(2, Left, InputBits.None), Facing.Left, Facing.Right);
        recognizer.Observe(new FrameInput(3, Left | BtnA, InputBits.None), Facing.Left, Facing.Right);

        CommandMatch match = Single(recognizer);
        Assert.Equal(CommandCatalog.QuarterCircleForwardLightPunch, match.CommandId);
        Assert.Equal(new[] { 0, 1, 2 }, match.DirectionFrames);
    }
}
