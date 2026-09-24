// 职责：世界暂停的对外契约——多持有者引用计数地冻结 Time.timeScale 与逻辑 tick。
// 为什么新建：框架里没有「同时管 timeScale 与逻辑 tick」的暂停入口；SimulationRunner.SetPaused 只管逻辑 tick，
//   对话、暂停菜单若各自改 Time.timeScale 会互相覆盖，必须收口到一个接口（实现见 WorldPauseService）。

using System;

namespace Game.Core.Timing
{
    /// <summary>
    /// 世界暂停服务。
    /// <para>
    /// **持有者引用计数**：每个 owner 调一次 <see cref="Acquire"/> 拿到一枚令牌；只要还有任一持有者没释放令牌，
    /// 世界就保持暂停——<c>Time.timeScale = 0</c>，且逻辑 tick（<c>SimulationRunner</c>）暂停推进。
    /// 最后一个持有者释放时，timeScale 恢复到第一个持有者进来之前的值（不是简单置 1），逻辑 tick 恢复推进。
    /// </para>
    /// <para>
    /// **表现层要用 unscaled 时间**：暂停期间还要动的东西（对话打字机、菜单动效、定时器）
    /// 读 <c>IClock.UnscaledDeltaTime</c> / 用 <c>unscaled</c> 定时器，否则会跟着世界一起停住。
    /// </para>
    /// <para>
    /// **对话、暂停菜单等一切「要让世界停下」的需求都走这里**，不要各自改 <c>Time.timeScale</c>，
    /// 也不要拿 <c>SimulationRunner</c> 的 Driven 模式冒充暂停（那是重放用的）。
    /// </para>
    /// </summary>
    public interface IWorldPauseService
    {
        /// <summary>当前是否有任一持有者在暂停世界。</summary>
        bool IsPaused { get; }

        /// <summary>
        /// 以 <paramref name="owner"/> 的名义请求暂停，返回的令牌 Dispose 即释放。
        /// 同一 owner 重复调用返回同一枚令牌、不重复计数；令牌 Dispose 幂等。
        /// </summary>
        /// <param name="owner">持有者，通常传 <c>this</c>，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> 为 null。</exception>
        IDisposable Acquire(object owner);
    }
}
