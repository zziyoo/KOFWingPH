using NewKOF.BattleCore.State;

namespace NewKOF.BattleCore.Simulation;

/// <summary>
/// Time Over 的<b>剩余体力比例</b>裁决（阶段 09B，D108）。纯函数、无浮点、无 Godot 依赖。
///
/// <para>比较 <c>P1.Health / P1.MaxHealth</c> 与 <c>P2.Health / P2.MaxHealth</c>
/// 时用安全整数交叉相乘：<c>P1.Health × P2.MaxHealth</c> vs <c>P2.Health × P1.MaxHealth</c>。
/// 两个 int 的乘积在 long 范围内（最大约 2^62 &lt; long.MaxValue），并用 <c>checked</c>
/// 兜底；绝不引入浮点误差，也不受文化区域影响。</para>
///
/// <para>比例完全相同（含 0:0、满血对满血）→ <see cref="RoundOutcome.Draw"/>。</para>
/// </summary>
public static class TimeOverResolver
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// 体力或上限为负、上限 ≤ 0、或体力超过上限。
    /// </exception>
    public static RoundOutcome Resolve(
        int p1Health, int p1MaxHealth, int p2Health, int p2MaxHealth)
    {
        Validate(p1Health, p1MaxHealth, "P1");
        Validate(p2Health, p2MaxHealth, "P2");

        long left = checked((long)p1Health * p2MaxHealth);
        long right = checked((long)p2Health * p1MaxHealth);

        return left > right ? RoundOutcome.P1Win
            : left < right ? RoundOutcome.P2Win
            : RoundOutcome.Draw;
    }

    private static void Validate(int health, int maxHealth, string side)
    {
        if (maxHealth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHealth), maxHealth,
                $"{side} 的体力上限必须为正。");
        if (health < 0 || health > maxHealth)
            throw new ArgumentOutOfRangeException(nameof(health), health,
                $"{side} 的体力必须落在 0..{maxHealth}。");
    }
}
