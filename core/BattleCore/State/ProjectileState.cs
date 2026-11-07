using NewKOF.BattleCore.Numerics;

namespace NewKOF.BattleCore.State;

/// <summary>
/// 一个<b>飞行道具实体</b>（阶段 08D，D99）。它是 <see cref="BattleState"/> 的一部分，
/// 与角色一样是权威数据：位置、速度、剩余寿命全部使用定点数与逻辑帧，
/// Godot 只读、<b>不得</b>自行模拟。
///
/// <para><b>身份</b>：<see cref="ProjectileId"/> 由 <see cref="BattleState.NextProjectileId"/> 单调分配
/// （随快照携带、溢出显式失败），不使用 GUID / 时钟 / 随机数 / Godot Node ID；
/// 同一快照重放必得同一串 ID。发射者进入收招、被击中、被投中都不会删除已有实体——
/// 实体只认自己的 <see cref="SourceMoveId"/> 与寿命，不依赖发射者当前的动作。</para>
/// </summary>
public readonly record struct ProjectileState(
    int ProjectileId,
    int OwnerFighterId,
    string TypeId,
    string SourceMoveId,
    FixedPoint PositionX,
    FixedPoint PositionY,
    FixedPoint VelocityX,
    FixedPoint VelocityY,
    int RemainingLifetime)
{
    /// <summary>校验基本不变式（非法实体不允许进入快照）。</summary>
    public void Validate()
    {
        if (ProjectileId < 1)
            throw new InvalidOperationException($"投掷物 ID 必须 ≥ 1，收到 {ProjectileId}。");
        if (OwnerFighterId is < 0 or > 1)
            throw new InvalidOperationException(
                $"投掷物 {ProjectileId} 的发射者 ID 非法：{OwnerFighterId}（只支持 0/1）。");
        if (string.IsNullOrWhiteSpace(TypeId))
            throw new InvalidOperationException($"投掷物 {ProjectileId} 缺少类型 ID。");
        if (string.IsNullOrWhiteSpace(SourceMoveId))
            throw new InvalidOperationException($"投掷物 {ProjectileId} 缺少来源招式 ID。");
        if (RemainingLifetime < 1)
            throw new InvalidOperationException(
                $"投掷物 {ProjectileId} 的剩余寿命为 {RemainingLifetime}（存活实体必须 ≥ 1，寿命归零应被移除）。");
    }
}
