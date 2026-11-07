namespace NewKOF.BattleCore.Combat;

/// <summary>
/// 攻击高度类别（阶段 06B 防御表使用；06A 随攻击数据一并定义）。
///
/// <para><b>明确规则（D77）</b>：攻击高度是<b>招式数据的显式配置</b>，
/// 不由 Hitbox 的世界 Y 坐标推断。防御表：</para>
/// <list type="table">
/// <item><term>High</term><description>只能站立防御（蹲防失败）。</description></item>
/// <item><term>Mid</term><description>站防与蹲防皆可。</description></item>
/// <item><term>Low</term><description>只能蹲防（站防失败）。</description></item>
/// </list>
/// </summary>
public enum AttackHeight
{
    /// <summary>需要站立防御的攻击（蹲防失败）。</summary>
    High = 0,

    /// <summary>站防与蹲防皆可的攻击。</summary>
    Mid = 1,

    /// <summary>只能蹲防的攻击（站防失败）。</summary>
    Low = 2,
}
