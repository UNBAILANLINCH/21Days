// 职责：逻辑 tick 的只读时间来源——现在是第几个 tick、一个 tick 多长、逻辑一共跑了多久。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何固定步长推进设施。Core/Timing/IClock 报的是**渲染帧时间**
//      （DeltaTime 每帧不等长、受 timeScale 影响、跟机器性能走），ITimerService 管的是「过多少秒回调」，
//      VContainer 的 ITickable 是**渲染帧回调**——三者都不是逻辑 tick，拿哪个都做不出可重放的推进。
//   2. 扩展不行：不能往 IClock 上加 Tick / FixedDeltaTime。那等于把「渲染帧时间」和「逻辑 tick」
//      并进同一个抽象，而这是重放系统里最容易写混的两个概念：玩法代码随手读一个 DeltaTime，
//      不确定性就进了逻辑，重放当场对不上，还查不出来。放进 Core/Simulation/ 而不是 Core/Timing/，
//      就是让这两件事在目录层面先分开——注入的时候就得先想清楚自己要的是哪一种时间。

namespace Game.Core.Simulation
{
    /// <summary>
    /// 逻辑时钟（只读）。玩法逻辑要时间只认这个接口，不读 <see cref="Game.Core.Timing.IClock"/>，
    /// 更不读 <c>UnityEngine.Time</c>——录制的输入要能重放出同一份结果，逻辑里就不能有渲染帧时间。
    /// <para>
    /// <see cref="Tick"/> 的语义是「**下一个要执行的 tick 序号**」，也等于「已经跑完的 tick 数」：
    /// 第一个 tick 执行时它是 0，等这一 tick 的所有 <see cref="ISimulationStep"/> 都跑完才变成 1。
    /// 所以在 <c>Step</c> 里读到的 <see cref="Tick"/> 与 <see cref="SimulationContext.Tick"/> 永远相等。
    /// </para>
    /// <para>推进由 <see cref="SimulationRunner"/> 独占，注入方拿到的只有读。</para>
    /// </summary>
    public interface ILogicClock
    {
        /// <summary>当前 tick 序号，从 0 开始。</summary>
        long Tick { get; }

        /// <summary>一个 tick 的固定时长秒数（tickRate 为 60 时是 1/60）。整局不变。</summary>
        float FixedDeltaTime { get; }

        /// <summary>逻辑累计秒数，等于 Tick × FixedDeltaTime。不受 timeScale、掉帧、暂停影响。</summary>
        float SimTime { get; }
    }
}
