namespace NewKOF.BattleCore.Data;

/// <summary>
/// 角色 JSON 配置加载失败（阶段 09D，D114）。消息固定包含
/// <c>来源 / 字段路径 / 原因</c>，例如
/// <c>fighters/Kai.json: moves[2].attacks[0].hitstopFrames 超过允许上限 30</c>。
/// 绝不静默忽略错误招式、自动删除重复项或用默认值掩盖格式错误。
/// </summary>
public sealed class FighterDataException : Exception
{
    public FighterDataException(string sourceName, string fieldPath, string reason, Exception? inner = null)
        : base($"{sourceName}: {fieldPath} {reason}", inner)
    {
        SourceName = sourceName;
        FieldPath = fieldPath;
        Reason = reason;
    }

    /// <summary>来源文件名（如 <c>fighters/Kai.json</c>）。</summary>
    public string SourceName { get; }

    /// <summary>出错字段路径（如 <c>moves[2].attacks[0].hitstopFrames</c>）。</summary>
    public string FieldPath { get; }

    /// <summary>失败原因。</summary>
    public string Reason { get; }
}
