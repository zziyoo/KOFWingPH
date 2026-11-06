using Godot;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;

namespace NewKOF;

/// <summary>
/// 战斗场景的表现层根节点。
///
/// 职责边界：
/// - 只读 <see cref="BattleSession.CurrentState"/>，绝不写回权威状态。
/// - 不参与任何权威判定；Godot 物理系统不决定战斗结果。
/// - <b>不自己实现时间累积与补帧</b>：全部委托给
///   <see cref="FixedStepScheduler"/>（纯 C#、可独立测试）。
///   本类只负责把 <c>delta</c> 交给调度器、把状态映射成画面。
///
/// 阶段 02 修改原因（阶段 01 缺陷）：
/// 旧实现用 <c>(int)(delta*1e6)</c> 截断 + 整数 16666us 间隔，
/// 导致逻辑帧总数随渲染帧率漂移（240FPS 跑 600 秒少 5 帧）。
/// 现改为 double 秒累加 + 精确步长判定，且补帧上限显式可配。
/// </summary>
public partial class BattleScene : Node2D
{
    /// <summary>战斗会话：调度器 + 模拟 + 权威状态，全部在内核层。</summary>
    private BattleSession _session = null!;

    private Node2D? _fighter1Node;
    private Node2D? _fighter2Node;
    private Label? _hudLabel;

    /// <summary>
    /// 单次渲染最多补的逻辑帧数。超过则欠帧被计入
    /// <see cref="FixedStepScheduler.DroppedStepCount"/> 并显式告警。
    /// </summary>
    private const int MaxStepsPerAdvance = 5;

    /// <summary>确保积压告警只输出一次，避免每帧刷屏。</summary>
    private bool _backlogWarningIssued;

    public override void _Ready()
    {
        _fighter1Node = GetNodeOrNull<Node2D>("Fighter1");
        _fighter2Node = GetNodeOrNull<Node2D>("Fighter2");
        _hudLabel = GetNodeOrNull<Label>("Hud/HudLabel");

        _session = new BattleSession(
            initialState: BattleState.CreateDefault(),
            scheduler: new FixedStepScheduler(MaxStepsPerAdvance));

        SyncViewFromState();
    }

    public override void _Process(double delta)
    {
        // 阶段 02：输入仍为空（输入链路属后续阶段）。调度与帧号配对在内核内完成。
        int steps = _session.Advance(delta, inputProvider: null);

        if (steps > 0)
            SyncViewFromState();

        // 只在“首次发生丢弃”时告警一次，避免每帧刷屏。
        long dropped = _session.Scheduler.DroppedStepCount;
        if (dropped > 0 && !_backlogWarningIssued)
        {
            _backlogWarningIssued = true;
            GD.PushWarning(
                $"[BattleScene] 帧积压超过上限 {MaxStepsPerAdvance}，已丢弃 {dropped} 个逻辑帧。" +
                "请检查渲染性能。");
        }
    }

    /// <summary>把权威状态映射到画面。表现层只读。</summary>
    private void SyncViewFromState()
    {
        BattleState state = _session.CurrentState;

        // 1 逻辑单位 = 1 像素；屏幕原点对齐到视口中线，地面 Y = 560。
        const float originX = 640f;
        const float groundY = 560f;

        // 角色节点是 Node2D 容器（子节点 ColorRect 从容器原点向上 120px 绘制），
        // 因此容器 Y 设为地面高度即可让色块底边贴合地面。
        if (_fighter1Node is not null)
            _fighter1Node.Position = new Vector2(originX + state.Fighter(0).PositionX, groundY);

        if (_fighter2Node is not null)
            _fighter2Node.Position = new Vector2(originX + state.Fighter(1).PositionX, groundY);

        if (_hudLabel is not null)
            _hudLabel.Text = $"Logic Frame: {state.Frame}";
    }
}
