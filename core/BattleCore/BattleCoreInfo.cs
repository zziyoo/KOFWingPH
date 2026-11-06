namespace NewKOF.BattleCore;

/// <summary>
/// 核心模拟模块的元信息。
/// 该程序集不得引用 Godot、不得依赖任何图形窗口、不得读写文件系统。
/// </summary>
public static class BattleCoreInfo
{
    /// <summary>核心内核版本。规则/数据格式变化时递增。</summary>
    public const int CoreVersion = 1;

    /// <summary>固定逻辑帧率（Hz）。权威模拟只按逻辑帧推进，与渲染帧率无关。</summary>
    public const int LogicFrameRate = 60;

    public const string AssemblyName = "NewKOF.BattleCore";
}
