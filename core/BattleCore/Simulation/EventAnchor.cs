using NewKOF.BattleCore.Numerics;
namespace NewKOF.BattleCore.Simulation;

/// <summary>Read-only presentation position associated with a committed event.</summary>
public readonly record struct EventAnchor(int EventId, FixedPoint X, FixedPoint Y);
