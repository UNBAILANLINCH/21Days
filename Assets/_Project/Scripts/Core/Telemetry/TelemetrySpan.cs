// 职责：一段耗时统计的句柄——Dispose 时按实际耗时埋一条带 ms 的事件。
// 为什么新建：契约要求「可能超过一帧的异步流程用 BeginSpan 自动记 ms」，这需要一个能配合 using 的类型。
//   写成 class 的话每开一段就是一次堆分配（异步流程在加载期会密集开合），所以必须是 struct；
//   struct 又不能塞进 ITelemetryService.cs（一个文件一个类型），因此单独成文件。

using System;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 耗时段。<c>using</c> 对结构体不会装箱（编译器直接对变量调 Dispose，不走 IDisposable 接口）：
    /// <code>
    /// using (telemetry.BeginSpan(TelemetryKeys.Asset, "scene_load"))
    /// {
    ///     await LoadAsync();
    /// }   // 这里自动埋一条 core.asset/scene_load {"ms":312}
    /// </code>
    /// <para>埋点关掉时 <see cref="ITelemetryService.BeginSpan"/> 返回 <c>default</c>，此时 Dispose 是空操作。</para>
    /// </summary>
    public readonly struct TelemetrySpan : IDisposable
    {
        private readonly ITelemetryService service;
        private readonly ITelemetryClock clock;
        private readonly string module;
        private readonly string name;
        private readonly long startMs;

        internal TelemetrySpan(ITelemetryService service, ITelemetryClock clock, string module, string name, long startMs)
        {
            this.service = service;
            this.clock = clock;
            this.module = module;
            this.name = name;
            this.startMs = startMs;
        }

        /// <summary>结束这一段并埋点。重复 Dispose 会重复埋点，所以别把同一个 span 传来传去。</summary>
        public void Dispose()
        {
            if (service == null || clock == null)
            {
                return;
            }

            long elapsed = clock.MillisecondsNow - startMs;
            service.Track(module, name, (TelemetryKeys.Props.Ms, elapsed));
        }
    }
}
