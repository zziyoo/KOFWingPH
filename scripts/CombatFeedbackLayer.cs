using Godot;
using NewKOF.BattleCore.Presentation;

namespace NewKOF;

/// <summary>
/// <b>打击反馈层</b>（阶段 10D，D126）：把 <see cref="FeedbackCue"/> 画成短命的几何火花。
///
/// <para><b>纯表现</b>：不读也不写任何权威状态；所有节点按渲染帧衰减后 <c>QueueFree()</c>，
/// 生命周期用固定帧数（不是 delta），因此同一输入序列的截图可复现。</para>
///
/// <para>层被放在角色之上、HUD 之下，因此火花不会遮住调试文字。</para>
/// </summary>
public partial class CombatFeedbackLayer : Node2D
{
    private sealed class Spark
    {
        public ColorRect Node = null!;
        public int Life;
        public int MaxLife;
        public Vector2 Velocity;
        public Vector2 BaseSize;
    }

    private readonly List<Spark> _sparks = new();

    /// <summary>总开关（F4 调试切换）；关闭时不再生成新火花并清空现存。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>当前存活火花数（诊断用）。</summary>
    public int ActiveSparks => _sparks.Count;

    /// <summary>在指定世界坐标生成一个火花。</summary>
    public void Spawn(Vector2 position, Color color, int size, Vector2 velocity, int lifetimeFrames = 8)
    {
        if (!Enabled)
            return;

        var rect = new ColorRect
        {
            Color = color,
            Size = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(rect);
        rect.Position = position - new Vector2(size / 2f, size / 2f);

        _sparks.Add(new Spark
        {
            Node = rect,
            Life = lifetimeFrames,
            MaxLife = lifetimeFrames,
            Velocity = velocity,
            BaseSize = new Vector2(size, size),
        });
    }

    /// <summary>清空全部火花（新回合/重开/关闭特效）。</summary>
    public void Clear()
    {
        foreach (Spark spark in _sparks)
        {
            if (IsInstanceValid(spark.Node))
                spark.Node.QueueFree();
        }
        _sparks.Clear();
    }

    public override void _Process(double delta)
    {
        if (_sparks.Count == 0)
            return;

        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            Spark spark = _sparks[i];
            if (!IsInstanceValid(spark.Node))
            {
                _sparks.RemoveAt(i);
                continue;
            }

            spark.Life--;
            if (spark.Life <= 0)
            {
                spark.Node.QueueFree();
                _sparks.RemoveAt(i);
                continue;
            }

            spark.Node.Position += spark.Velocity;
            spark.Velocity *= 0.82f;

            float t = (float)spark.Life / spark.MaxLife;      // 1 → 0
            spark.Node.Modulate = new Color(1f, 1f, 1f, t);
            spark.Node.Size = spark.BaseSize * (0.6f + 0.6f * t);
        }
    }
}
