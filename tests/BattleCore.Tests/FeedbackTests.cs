using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Simulation;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 10D（D126）：<b>打击反馈</b>——权威事件到瞬时特效的唯一映射、按
/// <c>(scope, EventId)</c> 去重，以及确定性受控的命中抖动。
/// </summary>
public class FeedbackTests
{
    [Fact]
    public void EffectsUseIndependentCommittedAgesAndCanFinishAfterTerminal()
    {
        Assert.Equal(0, FeedbackTiming.Progress(24, 24));
        Assert.Equal(4f / 9, FeedbackTiming.Progress(24, 28));
        Assert.Equal(1f / 9, FeedbackTiming.Progress(27, 28));
        Assert.Equal(1, FeedbackTiming.Progress(24, 33));
        Assert.Equal(1, FeedbackTiming.Progress(24, 24, 0.15));
    }

    [Fact]
    public void ThrowImpactUsesGroundAnchorForEitherSeat()
    {
        var state = NewKOF.BattleCore.State.BattleState.CreateDefault();
        var right = new FeedbackCue(FeedbackKind.ThrowImpact, 0, 1, 60, 1, BattleEventType.ThrowHit);
        var left = right with { SourceFighterId = 1, TargetFighterId = 0 };
        var a = FeedbackGeometry.Anchor(right, state);
        var b = FeedbackGeometry.Anchor(left, state);
        Assert.Equal(a.X, -b.X);
        Assert.Equal(12, a.Y);
        Assert.Equal(a.Y, b.Y);
    }
    private static BattleEvent Event(
        BattleEventType type, int frame = 10, int seq = 0,
        int source = 0, int target = 1, int amount = 0) =>
        new(frame, frame * 16 + seq, type, source, target, null, amount);

    [Theory]
    [InlineData(BattleEventType.Hit, FeedbackKind.Hit)]
    [InlineData(BattleEventType.Blocked, FeedbackKind.Blocked)]
    [InlineData(BattleEventType.ThrowHit, FeedbackKind.ThrowImpact)]
    [InlineData(BattleEventType.ThrowClash, FeedbackKind.ThrowClash)]
    [InlineData(BattleEventType.StrikeClash, FeedbackKind.StrikeClash)]
    [InlineData(BattleEventType.ProjectileHit, FeedbackKind.ProjectileImpact)]
    [InlineData(BattleEventType.ProjectileBlocked, FeedbackKind.ProjectileBlocked)]
    [InlineData(BattleEventType.ProjectileClash, FeedbackKind.ProjectileClash)]
    [InlineData(BattleEventType.Knockout, FeedbackKind.Knockout)]
    [InlineData(BattleEventType.DoubleKnockout, FeedbackKind.DoubleKnockout)]
    [InlineData(BattleEventType.TimeOver, FeedbackKind.TimeOver)]
    public void VisualEvents_MapToTheirFeedback(BattleEventType type, FeedbackKind expected)
    {
        Assert.Equal(expected, FeedbackDirector.Map(type));

        var director = new FeedbackDirector();
        Assert.True(director.TryConsume(1, Event(type), out FeedbackCue cue));
        Assert.Equal(expected, cue.Kind);
        Assert.Equal(type, cue.SourceType);
    }

    [Theory]
    [InlineData(BattleEventType.MoveStarted)]
    [InlineData(BattleEventType.AttackActive)]
    [InlineData(BattleEventType.HitstunStarted)]
    [InlineData(BattleEventType.BlockstunStarted)]
    [InlineData(BattleEventType.ThrowAttempt)]
    [InlineData(BattleEventType.RoundEnded)]
    public void NonVisualEvents_ProduceNoFeedback(BattleEventType type)
    {
        Assert.Equal(FeedbackKind.None, FeedbackDirector.Map(type));

        var director = new FeedbackDirector();
        Assert.False(director.TryConsume(1, Event(type), out _));
        Assert.Equal(0, director.EmittedCount);
    }

    [Fact]
    public void SameEvent_IsEmittedOnlyOnce()
    {
        var director = new FeedbackDirector();
        BattleEvent hit = Event(BattleEventType.Hit, frame: 12, seq: 3, amount: 30);

        Assert.True(director.TryConsume(1, hit, out FeedbackCue first));
        Assert.Equal(30, first.Amount);
        Assert.Equal(0, first.SourceFighterId);
        Assert.Equal(1, first.TargetFighterId);

        // 同一事件被重复读取（每个渲染帧都会读 LastEvents）不得重复出特效。
        for (int i = 0; i < 5; i++)
            Assert.False(director.TryConsume(1, hit, out _));

        Assert.Equal(1, director.EmittedCount);
        Assert.Equal(5, director.SuppressedCount);
    }

    [Fact]
    public void DifferentScope_SameEventId_AreDistinct()
    {
        var director = new FeedbackDirector();
        BattleEvent hit = Event(BattleEventType.Hit, frame: 5, seq: 1);

        Assert.True(director.TryConsume(1, hit, out FeedbackCue round1));
        Assert.True(director.TryConsume(2, hit, out FeedbackCue round2));
        Assert.NotEqual(round1.Identity, round2.Identity);
        Assert.Equal(2, director.EmittedCount);
    }

    [Fact]
    public void Reset_AllowsReEmission()
    {
        var director = new FeedbackDirector();
        BattleEvent hit = Event(BattleEventType.Hit);

        Assert.True(director.TryConsume(1, hit, out _));
        Assert.False(director.TryConsume(1, hit, out _));

        director.Reset();
        Assert.Equal(0, director.EmittedCount);
        Assert.True(director.TryConsume(1, hit, out _));
    }

    [Fact]
    public void DifferentEventTypesInSameFrame_EachGetFeedback()
    {
        var director = new FeedbackDirector();
        int emitted = 0;

        for (int seq = 0; seq < 4; seq++)
        {
            if (director.TryConsume(1, Event(BattleEventType.Hit, frame: 7, seq: seq), out _))
                emitted++;
        }

        Assert.Equal(4, emitted);
    }

    // ─────────────────────────────────────────────────────────────
    // HitShake
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Shake_IsInactiveUntilTriggered()
    {
        var shake = new HitShake();
        Assert.False(shake.IsActive);
        Assert.Equal((0, 0), shake.Tick());
    }

    [Fact]
    public void Shake_ProducesDeterministicPattern()
    {
        var a = new HitShake();
        var b = new HitShake();
        a.Trigger(2);
        b.Trigger(2);

        for (int i = 0; i < 20; i++)
            Assert.Equal(a.Tick(), b.Tick());
    }

    [Fact]
    public void Shake_DecaysToZero_AndStops()
    {
        var shake = new HitShake();
        shake.Trigger(3);

        int total = 3 * 6 + 1; // magnitude × 固定序列长度 + 收尾
        for (int i = 0; i < total; i++)
            shake.Tick();

        Assert.False(shake.IsActive);
        Assert.Equal((0, 0), shake.Tick());
    }

    [Fact]
    public void Shake_ClampsMagnitude_AndDoesNotAccumulate()
    {
        var shake = new HitShake();
        shake.Trigger(100);
        Assert.Equal(8, shake.Magnitude);

        shake.Trigger(1);          // 取较大值，不叠加
        Assert.Equal(8, shake.Magnitude);

        shake.Reset();
        Assert.False(shake.IsActive);
        Assert.Equal(0, shake.Magnitude);
    }

    [Fact]
    public void Shake_OffsetsStaySmall()
    {
        var shake = new HitShake();
        shake.Trigger(8);

        for (int i = 0; i < 48; i++)
        {
            (int x, int y) = shake.Tick();
            Assert.InRange(x, -8, 8);
            Assert.InRange(y, -8, 8);
        }
    }
}
