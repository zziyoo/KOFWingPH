namespace NewKOF.BattleCore.Input;

/// <summary>
/// 单帧输入位。用于表达某一名玩家在某一逻辑帧的按键状态。
/// 阶段 01 只保留最基础的位，尚未接入方向归一化、SOCD 与命令识别。
/// </summary>
[Flags]
public enum InputBits : ushort
{
    None = 0,

    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,

    LightPunch = 1 << 4,
    HeavyPunch = 1 << 5,
    LightKick = 1 << 6,
    HeavyKick = 1 << 7,
}

/// <summary>
/// 一个逻辑帧内两名玩家的输入。不可变值类型。
/// 输入是外部注入的，绝不由模拟内部采样设备产生，因此同一份输入日志可完整重放。
/// </summary>
public readonly record struct FrameInput(int Frame, InputBits Player1, InputBits Player2)
{
    public static FrameInput Empty(int frame) => new(frame, InputBits.None, InputBits.None);

    public InputBits ForPlayer(int playerIndex) => playerIndex switch
    {
        0 => Player1,
        1 => Player2,
        _ => throw new ArgumentOutOfRangeException(nameof(playerIndex), playerIndex, "阶段 01 仅支持 1v1（玩家索引 0/1）。"),
    };
}
