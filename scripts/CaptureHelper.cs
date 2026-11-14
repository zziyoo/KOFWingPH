using Godot;

namespace NewKOF;

/// <summary>
/// 开发期验收辅助：仅在使用命令行参数启动时启用；正常运行零副作用。
///
/// <para><b>参数（全部按「渲染帧」计数，不是逻辑帧）</b></para>
/// <list type="table">
/// <item><term>--shot=&lt;path&gt;</term><description>截图保存路径，指定后才启用本节点</description></item>
/// <item><term>--shotframes=&lt;n&gt;</term><description>第 n 个<b>渲染帧</b>截图并退出</description></item>
/// <item><term>--renderkey=&lt;frame&gt;:&lt;down|up&gt;:&lt;KeyName&gt;</term>
///       <description>第 frame 个<b>渲染帧</b>注入合成按键，可重复；
///       日志同时打印当时的<b>逻辑帧号</b>，便于对照</description></item>
/// <item><term>--maxfps=&lt;n&gt;</term>
///       <description>把 <c>Engine.MaxFps</c> 夹到 n（仅本次运行）。验收脚本用它锁 60，
///       使渲染帧 ≈ 逻辑帧（差一个固定启动偏移），按键时序可复现；
///       不指定则不改动引擎帧率。</description></item>
/// <item><term>--key=...</term><description>旧名，等价于 <c>--renderkey=</c>（保留兼容，会告警一次）</description></item>
/// </list>
///
/// <para>
/// <b>为什么不按逻辑帧注入：</b>本节点的 <c>_Process</c> 只知道自己被调用了多少次
/// （渲染帧），不知道调度器这次推进了几个逻辑帧。把渲染帧计数说成逻辑帧编号是错的。
/// 真正「按逻辑帧」的输入回放应走重放系统（把逐帧输入日志直接喂给
/// <see cref="BattleCore.Input.IInputSource"/>），不在本工具范围内。
/// </para>
///
/// <para>
/// 注入走 <see cref="Input.ParseInputEvent"/>，与真实键盘同一条路径：
/// <c>_Input</c> → <see cref="KeyboardInputMap"/> → <see cref="BattleCore.Input.InputBuffer"/>。
/// 启用 <c>--capture-input-only</c> 时直接进入同一对局输入处理，避免桌面事件干扰。
/// </para>
/// </summary>
public partial class CaptureHelper : Node
{
    private string? _targetPath;
    private int _renderFramesToWait = 30;
    private int _renderFrame;
    private string? _captureDir;
    private int _captureStart, _captureEnd;
    private readonly List<(int Render, int Logic, Image Image)> _captured = new();

    private readonly List<KeyStep> _keyScript = new();

    /// <summary>一条按键注入计划；<paramref name="RenderFrame"/> 是<b>渲染帧</b>号。</summary>
    private readonly record struct KeyStep(int RenderFrame, bool Down, Key Keycode);

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot="))
                _targetPath = arg["--shot=".Length..];
            else if (arg.StartsWith("--shotframes=") && int.TryParse(arg["--shotframes=".Length..], out int f))
                _renderFramesToWait = f;
            else if (arg.StartsWith("--renderkey="))
                ParseKeyStep(arg["--renderkey=".Length..]);
            else if (arg.StartsWith("--capture-dir="))
                _captureDir = arg["--capture-dir=".Length..];
            else if (arg.StartsWith("--capture-start=") && int.TryParse(arg["--capture-start=".Length..], out int start))
                _captureStart = start;
            else if (arg.StartsWith("--capture-end=") && int.TryParse(arg["--capture-end=".Length..], out int end))
                _captureEnd = end;
            else if (arg.StartsWith("--maxfps=")
                     && int.TryParse(arg["--maxfps=".Length..], out int maxFps) && maxFps > 0)
            {
                // 仅调试验收：锁定渲染帧率，让「渲染帧 → 逻辑帧」的映射可复现。
                Engine.MaxFps = maxFps;
            }
            else if (arg.StartsWith("--key="))
            {
                GD.PushWarning("[CaptureHelper] --key= 已改名为 --renderkey=（语义：渲染帧，不是逻辑帧）。");
                ParseKeyStep(arg["--key=".Length..]);
            }
        }

        if (_targetPath is null)
        {
            SetProcess(false);
            return;
        }

        if (_captureDir is not null)
        {
            if (_captureStart < 1 || _captureEnd < _captureStart || _captureEnd >= _renderFramesToWait || _captureEnd - _captureStart >= 90)
            {
                GD.PushError("Frame capture requires 1 <= start <= end < shotframes and at most 90 images.");
                _captureDir = null;
                return;
            }
            System.IO.Directory.CreateDirectory(_captureDir);
            RenderingServer.FramePostDraw += CaptureRenderedFrame;
        }

        GD.Print($"[CaptureHelper] 将在第 {_renderFramesToWait} 个渲染帧截图到 {_targetPath}" +
                 "（渲染帧计数，不是逻辑帧）");
    }

    public override void _ExitTree()
    {
        if (_captureDir is not null) RenderingServer.FramePostDraw -= CaptureRenderedFrame;
        foreach (var capture in _captured) capture.Image.Dispose();
        _captured.Clear();
    }

    private void CaptureRenderedFrame()
    {
        if (_captureDir is null || _renderFrame < _captureStart || _renderFrame > _captureEnd) return;
        int logic = LogicFrame();
        _captured.Add((_renderFrame, logic, GetViewport().GetTexture().GetImage()));
    }

    public override void _Process(double delta)
    {
        _renderFrame++;
        InjectScheduledKeys();

        if (_captureDir is not null && _renderFrame == _captureEnd + 1)
        {
            foreach (var capture in _captured)
            {
                string path = System.IO.Path.Combine(_captureDir, $"render-{capture.Render:D5}-state-{capture.Logic:D5}.png");
                Error result = capture.Image.SavePng(path);
                GD.Print($"[FrameCapture] render={capture.Render} state={capture.Logic} path={path} result={result}");
                capture.Image.Dispose();
            }
            _captured.Clear();
        }

        if (_targetPath is null) return;

        if (_renderFrame < _renderFramesToWait) return;

        Image image = GetViewport().GetTexture().GetImage();
        Error err = image.SavePng(_targetPath);
        GD.Print(err == Error.Ok
            ? $"[CaptureHelper] 截图已保存：{_targetPath}（渲染帧 {_renderFrame}，逻辑帧 {LogicFrame()}）"
            : $"[CaptureHelper] 截图失败：{err}");

        _targetPath = null;
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    /// <summary>解析 <c>&lt;renderFrame&gt;:&lt;down|up&gt;:&lt;KeyName&gt;</c>。</summary>
    private void ParseKeyStep(string spec)
    {
        string[] parts = spec.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int renderFrame)
            || !Enum.TryParse(parts[2], ignoreCase: true, out Key keycode))
        {
            GD.PushWarning($"[CaptureHelper] 无法解析按键参数：{spec}");
            return;
        }

        _keyScript.Add(new KeyStep(
            renderFrame,
            parts[1].Equals("down", StringComparison.OrdinalIgnoreCase),
            keycode));
    }

    /// <summary>在预定的<b>渲染帧</b>注入合成按键事件，并在日志里标出当时的逻辑帧。</summary>
    private void InjectScheduledKeys()
    {
        for (int i = 0; i < _keyScript.Count; i++)
        {
            KeyStep step = _keyScript[i];
            if (step.RenderFrame != _renderFrame) continue;

            using var key = new InputEventKey
            {
                Keycode = step.Keycode,
                PhysicalKeycode = step.Keycode,
                Pressed = step.Down,
            };
            if (GetParent() is BattleScene { CaptureInputOnly: true } battle)
                battle.HandleCaptureInput(key);
            else
                Input.ParseInputEvent(key);

            GD.Print($"[CaptureHelper] 渲染帧 {_renderFrame} 注入 {step.Keycode} {(step.Down ? "按下" : "松开")}" +
                     $"（当时逻辑帧 {LogicFrame()}）");
        }
    }

    /// <summary>当前逻辑帧号；场景根不是 <see cref="BattleScene"/> 时返回 -1。</summary>
    private int LogicFrame() => (GetParent() as BattleScene)?.CurrentLogicFrame ?? -1;
}
