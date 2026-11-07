namespace NewKOF.BattleCore.Input;

/// <summary>
/// 两阶段输入源：<b>先准备，模拟成功后再提交</b>。
///
/// <para>
/// 存在原因：<see cref="InputBuffer.Take"/> 的「取走即提交」语义会在
/// <c>BattleSimulation.Step()</c> 抛异常时留下「输入已提交、战斗帧没提交」的幻影帧，
/// 重试同一帧还会因为帧号不连续而失败。把取输入拆成两步后：
/// </para>
/// <list type="number">
/// <item><see cref="Prepare"/> 只<b>计算</b>该帧输入，不改历史、不清边沿；</item>
/// <item>调用方执行模拟；</item>
/// <item>模拟成功后调用 <see cref="Commit"/>，此时才写历史并消费按下边沿。</item>
/// </list>
///
/// <para>
/// <b>契约：</b><see cref="Commit"/> 必须只接受该次 <see cref="Prepare"/> 返回的输入，
/// 且不得在其中抛异常——调用方在它之前已经把战斗状态推进了。
/// 模拟失败时调用方<b>直接丢弃</b>这次 <see cref="Prepare"/> 的结果，不做任何回滚，
/// 因此下一次 <see cref="Prepare"/> 同一帧号必须得到同一输入（幂等）。
/// </para>
/// </summary>
public interface IInputSource
{
    /// <summary>计算某个逻辑帧的输入，不改变任何已提交状态。</summary>
    /// <param name="frame">即将模拟的逻辑帧号。</param>
    FrameInput Prepare(int frame);

    /// <summary>模拟成功后提交该帧输入：写入历史并消费已闩锁的按下边沿。</summary>
    /// <param name="input">本帧实际用于模拟的输入，必须来自同一帧的 <see cref="Prepare"/>。</param>
    void Commit(FrameInput input);
}
