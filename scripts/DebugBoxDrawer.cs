using Godot;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;

namespace NewKOF;

/// <summary>
/// 判定框调试绘制节点（阶段 06A，D76）：Hitbox 红色半透明 / Hurtbox 蓝色半透明 /
/// Pushbox 绿色轮廓。<b>几何全部来自 BattleCore 权威查询</b>（<see cref="FighterBoxes"/>），
/// 表现层只做世界坐标 → 屏幕坐标的显示换算，绝不根据色块重新计算命中。
///
/// <para>作为独立子节点挂在 <c>BattleScene</c> 最后（渲染在最上层——
/// BattleScene 自身的 _Draw 会被不透明的全屏 Background 子节点盖住）。</para>
/// </summary>
public partial class DebugBoxDrawer : Node2D
{
    private BattleSession? _session;
    private FightRoster? _roster;

    /// <summary>调试显示开关（F2 切换）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>绑定会话与配置（只读消费权威状态）。</summary>
    public void Bind(BattleSession session, FightRoster roster)
    {
        _session = session;
        _roster = roster;
    }

    /// <summary>权威状态变化后由场景调用（触发一次重绘）。</summary>
    public void Refresh() => QueueRedraw();

    public override void _Draw()
    {
        if (!Enabled || _session is null || _roster is null)
            return;

        BattleState state = _session.CurrentState;
        for (int i = 0; i < 2; i++)
        {
            FighterState fighter = state.Fighter(i);
            FighterDefinition definition = _roster.Fighter(i);

            // Hurtbox：蓝色半透明（受击面）。
            foreach (FixedRect hurtbox in FighterBoxes.GetHurtboxes(fighter, definition))
                DrawRect(ToScreenRect(hurtbox), new Color(0.3f, 0.55f, 1f, 0.30f));

            // Pushbox：绿色轮廓（实体占位；权威推挤仍由内核 D73 负责）。
            GroundBodyWidths? widths = _roster.FacingGroundSeparation.HasValue ? definition.GroundBodyWidths : null;
            DrawRect(ToScreenRect(FighterBoxes.GetPushbox(fighter, widths)), new Color(0.2f, 1f, 0.3f, 0.9f),
                filled: false, width: 2f);

            // Hitbox：红色半透明（仅 Active 生效帧存在）。
            foreach (FixedRect hitbox in FighterBoxes.GetActiveHitboxes(fighter, definition))
                DrawRect(ToScreenRect(hitbox), new Color(1f, 0.15f, 0.1f, 0.45f));
        }

        // 飞行道具（08D，D99）：橙色半透明 = 权威碰撞盒；位置完全来自快照，表现层不模拟。
        foreach (ProjectileState projectile in state.Projectiles)
        {
            ProjectileDefinition? definition = ResolveProjectile(projectile);
            if (definition is null)
                continue;

            FixedRect box = new(
                projectile.PositionX - definition.Hit.HalfWidth,
                projectile.PositionX + definition.Hit.HalfWidth,
                projectile.PositionY - definition.Hit.HalfHeight,
                projectile.PositionY + definition.Hit.HalfHeight);

            DrawRect(ToScreenRect(box), new Color(1f, 0.6f, 0.1f, 0.55f));
            DrawRect(ToScreenRect(box), new Color(1f, 0.85f, 0.2f, 0.95f), filled: false, width: 2f);
        }
    }

    /// <summary>按来源身份查投掷物定义（只读展示用；规则仍在内核）。</summary>
    private ProjectileDefinition? ResolveProjectile(ProjectileState projectile)
    {
        if (_roster is null)
            return null;

        MoveDefinition? move = _roster.Fighter(projectile.OwnerFighterId).FindMoveById(projectile.SourceMoveId);
        if (move is null)
            return null;

        for (int i = 0; i < move.Projectiles.Count; i++)
        {
            if (move.Projectiles[i].ProjectileTypeId == projectile.TypeId)
                return move.Projectiles[i];
        }

        return null;
    }

    /// <summary>世界坐标（Y 向上、地面 0）→ 屏幕矩形（Godot Y 向下）。</summary>
    private static Rect2 ToScreenRect(FixedRect rect)
    {
        const float originX = 640f;
        const float groundY = 560f;
        float left = originX + (float)rect.MinX.ToDouble();
        float top = groundY - (float)rect.MaxY.ToDouble();
        float width = (float)(rect.MaxX - rect.MinX).ToDouble();
        float height = (float)(rect.MaxY - rect.MinY).ToDouble();
        return new Rect2(left, top, width, height);
    }
}
