using Godot;

namespace NewKOF;

/// <summary>
/// 开发期验收辅助：仅在使用命令行参数 --shot=路径 启动时启用。
/// 在指定帧数后保存一张截图并退出，用于自动化验证画面内容。
/// 不参与任何战斗逻辑；正常运行游戏时完全无副作用。
///
/// <para>
/// 另支持 <c>--key=&lt;frame&gt;:&lt;down|up&gt;:&lt;KeyName&gt;</c>（可重复），
/// 在指定帧通过 <see cref="Input.ParseInputEvent"/> 注入合成按键事件。
/// 它走的是 Godot 正常输入事件管线（与真实键盘同一条路径：
/// <c>_Input</c> → <see cref="KeyboardInputMap"/> → <see cref="InputBuffer"/>），
/// 用于在无人值守环境下验证输入链路；不指定该参数时完全不生效。
/// </para>
/// </summary>
public partial class CaptureHelper : Node
{
    private string? _targetPath;
    private int _framesToWait = 30;
    private int _elapsed;

    private readonly List<KeyStep> _keyScript = new();

    private readonly record struct KeyStep(int Frame, bool Down, Key Keycode);

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot="))
                _targetPath = arg["--shot=".Length..];
            else if (arg.StartsWith("--shotframes=") && int.TryParse(arg["--shotframes=".Length..], out int f))
                _framesToWait = f;
            else if (arg.StartsWith("--key="))
                ParseKeyStep(arg["--key=".Length..]);
        }

        if (_targetPath is null)
        {
            SetProcess(false);
            return;
        }

        GD.Print($"[CaptureHelper] 将在 {_framesToWait} 帧后截图到 {_targetPath}");
    }

    public override void _Process(double delta)
    {
        _elapsed++;
        InjectScheduledKeys();

        if (_targetPath is null) return;

        if (_elapsed < _framesToWait) return;

        Image image = GetViewport().GetTexture().GetImage();
        Error err = image.SavePng(_targetPath);
        GD.Print(err == Error.Ok
            ? $"[CaptureHelper] 截图已保存：{_targetPath}"
            : $"[CaptureHelper] 截图失败：{err}");

        _targetPath = null;
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    /// <summary>解析 <c>&lt;frame&gt;:&lt;down|up&gt;:&lt;KeyName&gt;</c>。</summary>
    private void ParseKeyStep(string spec)
    {
        string[] parts = spec.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int frame)
            || !Enum.TryParse(parts[2], ignoreCase: true, out Key keycode))
        {
            GD.PushWarning($"[CaptureHelper] 无法解析按键参数：{spec}");
            return;
        }

        _keyScript.Add(new KeyStep(frame, parts[1].Equals("down", StringComparison.OrdinalIgnoreCase), keycode));
    }

    /// <summary>在预定帧注入合成按键事件。按帧排序后逐条命中，避免每帧遍历全部。</summary>
    private void InjectScheduledKeys()
    {
        for (int i = 0; i < _keyScript.Count; i++)
        {
            KeyStep step = _keyScript[i];
            if (step.Frame != _elapsed) continue;

            Input.ParseInputEvent(new InputEventKey
            {
                Keycode = step.Keycode,
                PhysicalKeycode = step.Keycode,
                Pressed = step.Down,
            });
        }
    }
}
