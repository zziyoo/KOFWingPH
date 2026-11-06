using Godot;

namespace NewKOF;

/// <summary>
/// 开发期验收辅助：仅在使用命令行参数 --shot=路径 启动时启用。
/// 在指定帧数后保存一张截图并退出，用于自动化验证画面内容。
/// 不参与任何战斗逻辑；正常运行游戏时完全无副作用。
/// </summary>
public partial class CaptureHelper : Node
{
    private string? _targetPath;
    private int _framesToWait = 30;
    private int _elapsed;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot="))
                _targetPath = arg["--shot=".Length..];
            else if (arg.StartsWith("--shotframes=") && int.TryParse(arg["--shotframes=".Length..], out int f))
                _framesToWait = f;
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
        if (_targetPath is null) return;

        _elapsed++;
        if (_elapsed < _framesToWait) return;

        Image image = GetViewport().GetTexture().GetImage();
        Error err = image.SavePng(_targetPath);
        GD.Print(err == Error.Ok
            ? $"[CaptureHelper] 截图已保存：{_targetPath}"
            : $"[CaptureHelper] 截图失败：{err}");

        _targetPath = null;
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
