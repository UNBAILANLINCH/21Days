// 职责：周期采样 fps / 帧耗时 / 内存，以及单帧超阈值时补一条帧尖峰。
// 为什么新建：契约 2.2 明确「不许自己埋每帧事件，需要每帧数据时用 core.perf 的采样」，
//   那就必须有一个东西替所有人做这件事。TimerService 管的是「到点回调」，加一个只给埋点用的
//   周期采样进去会让它同时管两套语义；所以按它的先例（RegisterEntryPoint + ITickable）另起一个。

using System;
using Game.Core.Timing;
using UnityEngine.Profiling;
using VContainer.Unity;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 性能采样器。由 VContainer 的 EntryPoint 每帧驱动（<see cref="ITickable"/>），
    /// EditMode 测试里可以手动调 <see cref="Tick"/>。
    /// <para>
    /// 两件事：<b>周期采样</b>（默认 5 秒一条 <c>core.perf/sample</c>，fps 是区间平均而不是瞬时——
    /// 瞬时值抖得厉害，聚合出来没法看趋势）；<b>帧尖峰</b>（单帧超阈值立刻补一条 <c>core.perf/spike</c>）。
    /// </para>
    /// <para>
    /// 尖峰本身要限流：一次卡顿常常连着几十帧都超阈值，每帧一条会把日志刷爆、真正的线索沉底。
    /// 所以同一次卡顿只打一条，等帧耗时回到阈值以下才允许再打。
    /// </para>
    /// <para>采样间隔配成 0 时整个采样器不工作（连尖峰也不测）——这是配置里那句「设 0 关闭周期采样」的落地。</para>
    /// </summary>
    public sealed class PerformanceSampler : ITickable
    {
        private const double BytesPerMegabyte = 1024d * 1024d;

        private readonly ITelemetryService telemetry;
        private readonly TelemetryOptions options;
        private readonly IClock clock;

        private float accumulatedSeconds;
        private int accumulatedFrames;
        private bool inSpike;

        public PerformanceSampler(ITelemetryService telemetry, TelemetryOptions options, IClock clock)
        {
            this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Tick()
        {
            float interval = options.SampleIntervalSeconds;
            if (interval <= 0f)
            {
                return;
            }

            // 采样器是 EntryPoint，容器一建好就开始 Tick，可能早于 TelemetryService.InitializeAsync；
            // 抢在 session_start 前面写出去的事件会被分析脚本算进上一段会话，所以等会话头写完再开工。
            if (!telemetry.SessionStarted)
            {
                return;
            }

            // 用不受 timeScale 影响的帧时长：暂停菜单把 timeScale 调成 0 时，卡顿照样要能测出来
            float delta = clock.UnscaledDeltaTime;
            CheckSpike(delta * 1000f);

            accumulatedSeconds += delta;
            accumulatedFrames++;
            if (accumulatedSeconds < interval)
            {
                return;
            }

            float fps = accumulatedFrames / accumulatedSeconds;
            float averageFrameMs = (accumulatedSeconds * 1000f) / accumulatedFrames;
            telemetry.Track(
                TelemetryKeys.Perf,
                TelemetryKeys.PerfEvents.Sample,
                (TelemetryKeys.Props.Fps, fps),
                (TelemetryKeys.Props.FrameMs, averageFrameMs),
                (TelemetryKeys.Props.GcMb, ManagedMegabytes()),
                (TelemetryKeys.Props.MemMb, TotalMegabytes()));

            accumulatedSeconds = 0f;
            accumulatedFrames = 0;
        }

        private void CheckSpike(float frameMs)
        {
            float threshold = options.SpikeThresholdMs;
            if (threshold <= 0f)
            {
                return;
            }

            if (frameMs < threshold)
            {
                inSpike = false;
                return;
            }

            if (inSpike)
            {
                return;
            }

            inSpike = true;

            // 用 W 级：卡顿在 Console 里就该显眼，而且把最低级别调到 W 排查性能时它还在
            telemetry.TrackWarn(
                TelemetryKeys.Perf,
                TelemetryKeys.PerfEvents.Spike,
                TelemetryProps.Of(
                    (TelemetryKeys.Props.FrameMs, frameMs),
                    (TelemetryKeys.Props.GcMb, ManagedMegabytes()),
                    (TelemetryKeys.Props.MemMb, TotalMegabytes())));
        }

        /// <summary>托管堆占用 MB。传 false 不强制 GC——采样本身不能制造卡顿。</summary>
        private static double ManagedMegabytes()
        {
            return GC.GetTotalMemory(false) / BytesPerMegabyte;
        }

        /// <summary>
        /// 进程总分配 MB（Unity 原生分配器口径，包含贴图、网格这些不在托管堆上的东西）。
        /// <para>
        /// <see cref="Profiler"/> 在正式包里也能调，但正式包的 Profiler 是关的，这个值的口径与编辑器/开发包
        /// 不一样（可能偏小甚至为 0）。所以这个数**只在同一种包里纵向比**，别拿编辑器的数去对真机的数。
        /// </para>
        /// </summary>
        private static double TotalMegabytes()
        {
            return Profiler.GetTotalAllocatedMemoryLong() / BytesPerMegabyte;
        }
    }
}
