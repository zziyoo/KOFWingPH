using Godot;
using NewKOF.BattleCore.Input;

namespace NewKOF;

/// <summary>
/// 键盘 → <see cref="InputBits"/> 的映射与采样。**表现层唯一接触 Godot 输入 API 的地方。**
///
/// <para>
/// 设计要点：
/// </para>
/// <list type="bullet">
/// <item>按<b>按键事件</b>而不是每渲染帧轮询：这样「同一渲染帧内按下又松开」也能被
///       捕获成边沿，不会因为轮询时机错过。</item>
/// <item>按住状态用「按下的物理键集合」推导，因此一个输入位可以绑定多个键
///       （数字小键盘 + 标点键都映射到 P2 攻击），同时按也不会互相抵消。</item>
/// <item>本类不做任何指令识别、不做 SOCD——那属于核心层
///       （<see cref="DirectionResolver"/>）。</item>
/// </list>
///
/// <para><b>默认键位</b></para>
/// <list type="table">
/// <item><term>P1</term><description>W/A/S/D 移动，J = A（轻拳），K = B（重拳），
///       U = C（轻脚），I = D（重脚）</description></item>
/// <item><term>P2</term><description>↑/↓/←/→ 移动，小键盘 1/2/4/5 或
///       , . / ; = A/B/C/D（笔记本无小键盘时用标点键）</description></item>
/// </list>
/// </summary>
public sealed class KeyboardInputMap
{
    private readonly InputBuffer _buffer;
    private readonly HashSet<Key> _down = new();

    public KeyboardInputMap(InputBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    /// <summary>
    /// 处理一个输入事件；按键事件会被转换并立即提交一次采样。
    /// </summary>
    /// <returns>是否为被本类消费的键盘事件（非键盘或回显事件返回 false）。</returns>
    public bool Handle(InputEvent @event)
    {
        if (@event is not InputEventKey key || key.Echo)
            return false;

        if (key.Pressed)
            _down.Add(key.Keycode);
        else
            _down.Remove(key.Keycode);

        Publish();
        return true;
    }

    /// <summary>
    /// 释放全部按键（窗口失焦时调用）。不丢弃已闩锁的按下边沿：
    /// 玩家确实按过，只是松开事件不会再送达。
    /// </summary>
    public void ReleaseAll()
    {
        if (_down.Count == 0)
            return;
        _down.Clear();
        Publish();
    }

    private void Publish() => _buffer.Sample(Read(0), Read(1));

    private InputBits Read(int player)
    {
        InputBits bits = InputBits.None;
        foreach (Key key in _down)
        {
            if (TryMap(key, out int owner, out InputBits bit) && owner == player)
                bits |= bit;
        }

        return bits;
    }

    /// <summary>物理键 → (玩家索引, 输入位)。未映射的键返回 false。</summary>
    public static bool TryMap(Key key, out int player, out InputBits bit)
    {
        switch (key)
        {
            // ── P1：WASD + J K U I ─────────────────────────────
            case Key.W: player = 0; bit = InputBits.Up; return true;
            case Key.S: player = 0; bit = InputBits.Down; return true;
            case Key.A: player = 0; bit = InputBits.Left; return true;
            case Key.D: player = 0; bit = InputBits.Right; return true;
            case Key.J: player = 0; bit = InputBits.LightPunch; return true;
            case Key.K: player = 0; bit = InputBits.HeavyPunch; return true;
            case Key.U: player = 0; bit = InputBits.LightKick; return true;
            case Key.I: player = 0; bit = InputBits.HeavyKick; return true;

            // ── P2：方向键 + 小键盘（或标点键） ──────────────────
            case Key.Up: player = 1; bit = InputBits.Up; return true;
            case Key.Down: player = 1; bit = InputBits.Down; return true;
            case Key.Left: player = 1; bit = InputBits.Left; return true;
            case Key.Right: player = 1; bit = InputBits.Right; return true;
            case Key.Kp1: player = 1; bit = InputBits.LightPunch; return true;
            case Key.Kp2: player = 1; bit = InputBits.HeavyPunch; return true;
            case Key.Kp4: player = 1; bit = InputBits.LightKick; return true;
            case Key.Kp5: player = 1; bit = InputBits.HeavyKick; return true;

            // 笔记本没有小键盘时的等价键位（同一输入位绑定第二个键）
            case Key.Comma: player = 1; bit = InputBits.LightPunch; return true;
            case Key.Period: player = 1; bit = InputBits.HeavyPunch; return true;
            case Key.Slash: player = 1; bit = InputBits.LightKick; return true;
            case Key.Semicolon: player = 1; bit = InputBits.HeavyKick; return true;

            default: player = -1; bit = InputBits.None; return false;
        }
    }
}
