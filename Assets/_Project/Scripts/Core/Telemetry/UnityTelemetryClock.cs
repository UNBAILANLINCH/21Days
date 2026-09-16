// 职责：ITelemetryClock 的本机实现——Stopwatch 计时 + Time.frameCount 取帧号。
// 为什么新建：接口要有一个默认实现才能注册进容器；这个实现除了转发没有别的职责，
//   塞进 ITelemetryClock.cs 会让「接口」与「某一个实现」绑死（照 Timing/IClock 与 LocalClock 的分法）。

using System.Diagnostics;
using UnityEngine;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 本机时钟。计时用 <see cref="Stopwatch"/> 而不是 <c>Time.realtimeSinceStartup</c>：
    /// 后者是 float 秒（长会话丢精度），前者是单调的 long 毫秒，也不受系统改时间影响。
    /// <para>**只能在主线程读**：<c>Time.frameCount</c> 在别的线程上访问会抛异常。
    /// 埋点本来就只在主线程打（Application.logMessageReceived 的非线程版也会排到主线程）。</para>
    /// </summary>
    public sealed class UnityTelemetryClock : ITelemetryClock
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public long MillisecondsNow => stopwatch.ElapsedMilliseconds;

        public int FrameCount => Time.frameCount;
    }
}
