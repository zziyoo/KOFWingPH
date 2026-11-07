using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;

namespace NewKOF.BattleCore.Tests;

/// <summary>
/// 阶段 04 最终补修 P1：权威识别快照的<b>深层不可变性</b>。
///
/// <para>
/// 缺陷（修复前实测可复现）：<see cref="RecognitionState.Advance"/> 把工作
/// <see cref="List{T}"/> 直接存进 <c>IReadOnlyList{T}</c> 属性——接口只限制编译期视图，
/// 外部可强转回 <see cref="List{T}"/> 修改权威状态；<see cref="RecognitionState.WithResultConsumed"/>
/// 暴露裸 <c>BufferedResult[]</c>（可经 <c>IList{T}</c>/数组写入）；<see cref="RecognitionState.Empty"/>
/// 共享 <c>Array.Empty</c>。识别状态随 <see cref="FighterState"/> 进入 <see cref="BattleState"/>，
/// 这是权威快照完整性问题。</para>
///
/// <para>
/// 修复（D61）：五个公开集合在构造点一律包装为单实例
/// <see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/>
/// （与 D34 的 <c>InputBuffer.History</c> 同一先例）：强转 <c>List{T}</c>/数组为 null；
/// <c>IList{T}</c> / 非泛型 <c>IList</c> 写操作抛 <see cref="NotSupportedException"/> 且不生效；
/// 内部构造仍用可变 List（保留高效流程），只在快照落定点包装一次。</para>
/// </summary>
public class SnapshotImmutabilityTests
{
    // ─────────────────────────────────────────────────────────────
    // 工具：构造「五集合全部非平凡」的识别快照
    // ─────────────────────────────────────────────────────────────

    /// <summary>236A（普通）+ 46A（蓄力 2 帧）。</summary>
    private static CommandDefinition[] Definitions() => new[]
    {
        new CommandDefinition("236A",
            new[] { CommandDirection.Down, CommandDirection.DownForward, CommandDirection.Forward },
            InputBits.LightPunch, priority: 10),
        new CommandDefinition("46A",
            new[] { CommandDirection.Back, CommandDirection.Forward },
            InputBits.LightPunch, priority: 30,
            chargeHoldFrames: 2, chargeReleaseToleranceFrames: 3),
    };

    /// <summary>
    /// 逐帧构造：帧 0-2 完成 236A（缓冲 + 手势锁 + 按键消费），
    /// 帧 3-4 蓄「后」，帧 5 释放到中立（蓄力挂起存活，按键尚在容差窗口内）。
    /// 五个公开集合全部非平凡。
    /// </summary>
    private static RecognitionState StateWithEverything()
    {
        InputBits[] log =
        {
            InputBits.Down,                                     // 0
            InputBits.Down | InputBits.Right,                   // 1
            InputBits.Right | InputBits.LightPunch,             // 2: 236A 完成
            InputBits.Left,                                     // 3: 后（蓄力）
            InputBits.Left,                                     // 4: 后（持续）
            InputBits.None,                                     // 5: 释放（挂起 {2,5}；按键@2 距当前 3 帧，仍在容差内）
        };
        RecognitionState state = RecognitionState.Empty;
        CommandDefinition[] definitions = Definitions();
        for (int f = 0; f < log.Length; f++)
            state = state.Advance(log[f], Facing.Right, f, 0, definitions);
        return state;
    }

    /// <summary>对任意识别快照发起一切可想到的外部修改尝试（吞掉异常，只管改没改）。</summary>
    private static void AttemptEveryMutation(RecognitionState snapshot)
    {
        TryMutate(snapshot.Directions, () => new DirectionEntry(-1, CommandDirection.Up, -1));
        TryMutate(snapshot.Presses, () => new PressRecord(-1, InputBits.LightPunch, false));
        TryMutate(snapshot.Buffer, () => default(BufferedResult));
        TryMutate(snapshot.ConsumedEndFrames, () => -1);
        TryMutate(snapshot.ChargePendings, () => new ChargePending(true, 1, 1));
    }

    private static void TryMutate<T>(IReadOnlyList<T> collection, Func<T> makeItem)
    {
        if (collection is List<T> list)
        {
            list.Clear();
            list.Add(makeItem());
        }

        if (collection is T[] array)
        {
            if (array.Length > 0)
                array[0] = makeItem();
        }

        if (collection is IList<T> ilist)
        {
            if (!ilist.IsReadOnly)
            {
                ilist.Clear();
                ilist.Add(makeItem());
            }
        }

        if (collection is System.Collections.IList nonGeneric)
        {
            if (!nonGeneric.IsReadOnly && !nonGeneric.IsFixedSize)
            {
                nonGeneric.Clear();
                nonGeneric.Add(makeItem());
            }
        }
    }

    /// <summary>断言五集合均不可被外部修改（强转 null + 只读接口 + 内容保持）。</summary>
    private static void AssertImmutable(RecognitionState snapshot)
    {
        // 强转可写类型必须失败（不得暴露底层 List/数组）。
        Assert.Null(snapshot.Directions as List<DirectionEntry>);
        Assert.Null(snapshot.Directions as DirectionEntry[]);
        Assert.Null(snapshot.Presses as List<PressRecord>);
        Assert.Null(snapshot.Presses as PressRecord[]);
        Assert.Null(snapshot.Buffer as List<BufferedResult>);
        Assert.Null(snapshot.Buffer as BufferedResult[]);
        Assert.Null(snapshot.ConsumedEndFrames as List<int>);
        Assert.Null(snapshot.ConsumedEndFrames as int[]);
        Assert.Null(snapshot.ChargePendings as List<ChargePending>);
        Assert.Null(snapshot.ChargePendings as ChargePending[]);

        // 只读接口：写操作必须抛 NotSupportedException 且内容保持。
        AssertReadOnly("Directions", snapshot.Directions);
        AssertReadOnly("Presses", snapshot.Presses);
        AssertReadOnly("Buffer", snapshot.Buffer);
        AssertReadOnly("ConsumedEndFrames", snapshot.ConsumedEndFrames);
        AssertReadOnly("ChargePendings", snapshot.ChargePendings);
    }

    private static void AssertReadOnly<T>(string name, IReadOnlyList<T> collection)
    {
        Assert.True(collection is System.Collections.IList,
            $"{name} 应实现非泛型 IList（ReadOnlyCollection 包装）以获得只读防护。");

        var ilist = (System.Collections.IList)collection;
        Assert.True(ilist.IsReadOnly || ilist.IsFixedSize,
            $"{name} 的非泛型 IList 既不可写也不可定长。");

        if (collection.Count > 0)
        {
            Assert.Throws<NotSupportedException>(
                () => ilist[0] = default(T)!);
        }

        Assert.Throws<NotSupportedException>(() => ilist.Clear());
        Assert.Throws<NotSupportedException>(() => ilist.Add(default(T)!));
        Assert.Throws<NotSupportedException>(() => ilist.RemoveAt(0));

        // 泛型只读接口同样拒绝写入。
        var generic = (IList<T>)collection;
        Assert.True(generic.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => generic.Clear());
        if (collection.Count > 0)
        {
            Assert.Throws<NotSupportedException>(() => generic[0] = default(T)!);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // A. 公共集合不可修改（修复前：Directions 可强转 List 并被清空）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PublicCollections_AreNotExternallyMutable()
    {
        RecognitionState snapshot = StateWithEverything();

        // 记录修复期望的内容（方向历史 / 挂起等按构造脚本可预期）。
        Assert.Equal(5, snapshot.Directions.Count);
        Assert.Single(snapshot.Presses);
        Assert.Single(snapshot.Buffer);
        Assert.Single(snapshot.ConsumedEndFrames);
        Assert.Equal(2, snapshot.ChargePendings.Count);
        Assert.True(snapshot.ChargePendings[1].HasPending); // 蓄力挂起存活。

        var before = (
            Directions: snapshot.Directions.ToArray(),
            Presses: snapshot.Presses.ToArray(),
            Buffer: snapshot.Buffer.ToArray(),
            Locks: snapshot.ConsumedEndFrames.ToArray(),
            Pendings: snapshot.ChargePendings.ToArray());

        AttemptEveryMutation(snapshot);

        // 内容与长度完全未被修改。
        Assert.Equal(before.Directions, snapshot.Directions.ToArray());
        Assert.Equal(before.Presses, snapshot.Presses.ToArray());
        Assert.Equal(before.Buffer, snapshot.Buffer.ToArray());
        Assert.Equal(before.Locks, snapshot.ConsumedEndFrames.ToArray());
        Assert.Equal(before.Pendings, snapshot.ChargePendings.ToArray());

        AssertImmutable(snapshot);
    }

    [Fact]
    public void EmptyState_IsSharedSafely()
    {
        // Empty 被不同角色 / 不同对局共享：共享实例必须同样不可变。
        RecognitionState empty = RecognitionState.Empty;
        var before = empty.Directions.ToArray();

        AttemptEveryMutation(empty);

        Assert.Empty(empty.Directions);
        Assert.Empty(empty.Buffer);
        Assert.Empty(empty.Presses);
        Assert.Equal(before, empty.Directions.ToArray());
        AssertImmutable(empty);

        // 不同对局的 Advance 均从同一 Empty 出发，互不影响。
        var a = empty.Advance(InputBits.Down, Facing.Right, 0, 0, DemoRoster.CreateKai().Commands);
        var b = empty.Advance(InputBits.Right, Facing.Left, 0, 1, DemoRoster.CreateRei().Commands);
        Assert.NotEqual(a, b);
        Assert.Empty(empty.Directions); // 共享实例仍为空。
    }

    // ─────────────────────────────────────────────────────────────
    // B. WithResultConsumed 新旧状态互不污染
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void WithResultConsumed_OldAndNewStates_BothImmutable()
    {
        RecognitionState oldState = StateWithEverything();
        var oldBufferBefore = oldState.Buffer.ToArray();

        RecognitionState newState = oldState.WithResultConsumed(2);

        // 旧状态结果仍未消费、新状态已消费。
        Assert.False(oldState.Buffer[0].Consumed);
        Assert.True(newState.Buffer[0].Consumed);

        // 对新旧 Buffer 分别发起修改尝试 → 两个状态都不被污染。
        AttemptEveryMutation(oldState);
        AttemptEveryMutation(newState);

        Assert.Equal(oldBufferBefore, oldState.Buffer.ToArray());
        Assert.True(newState.Buffer[0].Consumed);
        AssertImmutable(oldState);
        AssertImmutable(newState);
    }

    // ─────────────────────────────────────────────────────────────
    // C. 旧快照在修改尝试后重放仍确定
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void OldSnapshot_MutationAttempts_DoNotAffectReplay()
    {
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());

        // 推进到帧 5（236A 已识别并消费，蓄力相关历史存在）。
        BattleState mid = BattleState.CreateDefault();
        InputBits[] head =
        {
            InputBits.Down,
            InputBits.Down | InputBits.Right,
            InputBits.Right | InputBits.LightPunch,
            InputBits.Left,
            InputBits.None,
        };
        for (int f = 0; f < head.Length; f++)
            mid = sim.Step(mid, new FrameInput(f, head[f], InputBits.None)).State;

        // 记录旧快照的全部公开识别集合。
        var midP1 = mid.Fighter(0).RecognitionState;
        var midP2 = mid.Fighter(1).RecognitionState;
        var before = (
            Directions: midP1.Directions.ToArray(),
            Presses: midP1.Presses.ToArray(),
            Buffer: midP1.Buffer.ToArray(),
            Locks: midP1.ConsumedEndFrames.ToArray(),
            Pendings: midP1.ChargePendings.ToArray());

        // 对旧快照（双方）发起一切修改尝试。
        AttemptEveryMutation(midP1);
        AttemptEveryMutation(midP2);

        // 旧快照内容保持不变。
        Assert.Equal(before.Directions, mid.Fighter(0).RecognitionState.Directions);
        Assert.Equal(before.Presses, mid.Fighter(0).RecognitionState.Presses);
        Assert.Equal(before.Buffer, mid.Fighter(0).RecognitionState.Buffer);
        Assert.Equal(before.Locks, mid.Fighter(0).RecognitionState.ConsumedEndFrames);
        Assert.Equal(before.Pendings, mid.Fighter(0).RecognitionState.ChargePendings);

        // 从同一旧快照重放两次相同输入 → 结果一致（动作 + 识别快照）。
        BattleState Replay()
        {
            BattleState s = mid;
            for (int f = head.Length; f <= 15; f++)
                s = sim.Step(s, FrameInput.Empty(f)).State;
            return s;
        }

        BattleState run1 = Replay();
        BattleState run2 = Replay();

        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(run1.Fighter(p).LogicState, run2.Fighter(p).LogicState);
            Assert.Equal(run1.Fighter(p).MoveId, run2.Fighter(p).MoveId);
            Assert.Equal(run1.Fighter(p).PhaseRemainingFrames, run2.Fighter(p).PhaseRemainingFrames);
            Assert.Equal(run1.Fighter(p).RecognitionState, run2.Fighter(p).RecognitionState);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // D. 长时间运行与双人隔离
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LongRun_1000Frames_PlayersIsolated_NoPollution()
    {
        // P1 在帧 4 完成 236A 并被消费；P2 一直按住下（蹲）。1000 帧后：
        // P1 回到待机、P2 仍蹲且识别缓冲为空（无幽灵、无跨玩家污染）。
        var sim = new BattleSimulation(DemoRoster.CreateKaiVsRei());
        BattleState state = BattleState.CreateDefault();

        for (int f = 0; f < 1000; f++)
        {
            InputBits p1 = f switch
            {
                <= 1 => InputBits.Down,
                2 => InputBits.Down | InputBits.Right,
                3 => InputBits.Right | InputBits.LightPunch,
                _ => InputBits.None,
            };
            InputBits p2 = InputBits.Down;
            state = sim.Step(state, new FrameInput(f, p1, p2)).State;

            // P2 的缓冲任何时刻都不得出现 P1 的指令。
            Assert.DoesNotContain(
                state.Fighter(1).RecognitionState.Buffer, r => r.Match.CommandId == "236A");
        }

        Assert.Equal(FighterLogicState.Standing, state.Fighter(0).LogicState);
        Assert.Equal(FighterLogicState.Crouching, state.Fighter(1).LogicState);
        Assert.Null(state.Fighter(1).MoveId);
        Assert.Empty(state.Fighter(1).RecognitionState.Buffer);

        // 历史裁剪有界：1000 帧后方向条目不超过兜底上限。
        Assert.True(state.Fighter(0).RecognitionState.Directions.Count <= 64);
        Assert.True(state.Fighter(1).RecognitionState.Directions.Count <= 64);

        // 修改尝试后状态依旧完好（可继续推进）。
        AttemptEveryMutation(state.Fighter(0).RecognitionState);
        AttemptEveryMutation(state.Fighter(1).RecognitionState);
        var next = sim.Step(state, FrameInput.Empty(1000)).State;
        Assert.Equal(1001, next.Frame);
    }

    [Fact]
    public void EqualStates_HaveStableAndConsistentHashes()
    {
        // 相等状态 → 相同哈希；哈希在生命周期内稳定（不可变性依赖）。
        RecognitionState a = StateWithEverything();
        RecognitionState b = StateWithEverything();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        int hashBefore = a.GetHashCode();
        var _ = a.WithResultConsumed(2); // 产生新状态，不影响旧实例。
        Assert.Equal(hashBefore, a.GetHashCode());

        // 相等语义不因只读包装改变：内容相同的包装集合 SequenceEqual 成立。
        Assert.Equal(a.Directions, b.Directions);
    }
}
