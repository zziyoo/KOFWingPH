namespace NewKOF.BattleCore.Commands;

/// <summary>
/// 一次成功识别的结果。不可变，只描述「识别到了什么、在哪一帧完成」，
/// <b>不包含</b>招式发动、取消、硬直等任何战斗语义（本阶段只做识别）。
/// </summary>
public sealed class CommandMatch
{
    internal CommandMatch(
        string commandId,
        int playerIndex,
        int recognizedFrame,
        int sequenceStartFrame,
        int sequenceEndFrame,
        int buttonFrame,
        int priority,
        int sequenceLength,
        int expiresAtFrame,
        int[] directionFrames)
    {
        CommandId = commandId;
        PlayerIndex = playerIndex;
        RecognizedFrame = recognizedFrame;
        SequenceStartFrame = sequenceStartFrame;
        SequenceEndFrame = sequenceEndFrame;
        ButtonFrame = buttonFrame;
        Priority = priority;
        SequenceLength = sequenceLength;
        ExpiresAtFrame = expiresAtFrame;
        DirectionFrames = Array.AsReadOnly(directionFrames);
    }

    /// <summary>指令 ID，如 <c>236A</c>。</summary>
    public string CommandId { get; }

    /// <summary>玩家索引（0 = P1，1 = P2）。</summary>
    public int PlayerIndex { get; }

    /// <summary>识别发生的逻辑帧号（= 输出该结果的帧）。</summary>
    public int RecognizedFrame { get; }

    /// <summary>方向序列第一个元素进入的帧号。</summary>
    public int SequenceStartFrame { get; }

    /// <summary>方向序列最后一个元素进入的帧号（真正的「指令完成帧」）。</summary>
    public int SequenceEndFrame { get; }

    /// <summary>触发指令的按键按下帧号；纯方向指令为 -1。</summary>
    public int ButtonFrame { get; }

    /// <summary>匹配到的指令优先级（冲突排序第一键）。</summary>
    public int Priority { get; }

    /// <summary>匹配到的方向序列长度（冲突排序第二键，长者优先）。</summary>
    public int SequenceLength { get; }

    /// <summary>该结果在缓冲中的有效期（含此帧）；超过后不再可用。</summary>
    public int ExpiresAtFrame { get; }

    /// <summary>方向序列每个元素各自进入的帧号（诊断用，长度 == <see cref="SequenceLength"/>）。</summary>
    public IReadOnlyList<int> DirectionFrames { get; }

    /// <summary>该结果在 <paramref name="frame"/> 是否已过期。</summary>
    public bool IsExpiredAt(int frame) => frame > ExpiresAtFrame;
}
