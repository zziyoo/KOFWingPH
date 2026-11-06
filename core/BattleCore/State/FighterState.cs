namespace NewKOF.BattleCore.State;

/// <summary>角色朝向。阶段 01 固定，不做自动转身。</summary>
public enum Facing
{
    Right = 1,
    Left = -1,
}

/// <summary>
/// 单名角色的初始/当前数据。
/// 阶段 01 刻意只包含“身份 + 初始数据”，不包含招式、状态机、碰撞或受击信息。
/// </summary>
public readonly record struct FighterState(
    int Id,
    int MaxHealth,
    int Health,
    int PositionX,
    int PositionY,
    Facing Facing)
{
    public static FighterState Create(int id, int maxHealth, int positionX, Facing facing) =>
        new(id, maxHealth, maxHealth, positionX, 0, facing);

    /// <summary>校验基础不变式。用于尽早暴露非法初态，而不是让错误无声传播。</summary>
    public void Validate()
    {
        if (Id < 0)
            throw new InvalidOperationException($"角色 Id 不能为负：{Id}。");
        if (MaxHealth <= 0)
            throw new InvalidOperationException($"角色 {Id} 的 MaxHealth 必须为正：{MaxHealth}。");
        if (Health < 0 || Health > MaxHealth)
            throw new InvalidOperationException($"角色 {Id} 的 Health 越界：{Health}/{MaxHealth}。");
    }
}
