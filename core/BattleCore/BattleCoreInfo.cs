using System.Runtime.CompilerServices;

// 记账方法（CommitStep / RecordDroppedSteps）刻意为 internal：
// 只有 BattleSession 可以提交帧，外部程序集无法绕过它破坏
// 「Frame - 初始帧号 == TotalSteps」不变量。测试需要直接驱动调度器，
// 因此仅对测试程序集开放。
[assembly: InternalsVisibleTo("BattleCore.Tests")]

namespace NewKOF.BattleCore;

/// <summary>
/// 核心模拟模块的元信息。
/// 该程序集不得引用 Godot、不得依赖任何图形窗口、不得读写文件系统。
/// </summary>
public static class BattleCoreInfo
{
    /// <summary>核心内核版本。规则/数据格式变化时递增（阶段 05B：跳跃/落地/场地边界 → 4）。</summary>
    public const int CoreVersion = 4;

    /// <summary>固定逻辑帧率（Hz）。权威模拟只按逻辑帧推进，与渲染帧率无关。</summary>
    public const int LogicFrameRate = 60;

    public const string AssemblyName = "NewKOF.BattleCore";
}
