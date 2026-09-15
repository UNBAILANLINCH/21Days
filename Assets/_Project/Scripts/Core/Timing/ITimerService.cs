// 职责：延时与周期回调的注册接口。
// 为什么新建：延时调度和每帧调度是两件事（architecture.md 第 6 节借鉴的手法 4），
// 每帧走 VContainer 的 ITickable，延时必须有自己的接口；工程内没有可复用的实现。

using System;

namespace Game.Core.Timing
{
    /// <summary>
    /// 定时器服务。时间来源只走 IClock，因此可以用假时钟做确定性测试。
    /// 不要用 Interval(0) 冒充每帧回调——每帧请实现 VContainer 的 ITickable。
    /// </summary>
    public interface ITimerService
    {
        /// <summary>延时 seconds 秒后触发一次。返回的句柄 Dispose 即取消。</summary>
        /// <param name="unscaled">true 时用不受 timeScale 影响的时间，暂停中也会走。</param>
        TimerHandle Delay(float seconds, Action callback, bool unscaled = false);

        /// <summary>每 seconds 秒触发一次，直到句柄 Dispose 或作用域销毁。seconds 必须大于 0。</summary>
        TimerHandle Interval(float seconds, Action callback, bool unscaled = false);
    }
}
