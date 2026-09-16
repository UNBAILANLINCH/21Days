// 职责：埋点专用的时间与帧号来源，把 Stopwatch 与 Time.frameCount 挡在服务之外。
// 为什么新建：先看了 Core/Timing/IClock 能不能直接用，两点用不了——
//   1) 它没有帧号，而 f（Time.frameCount）是契约必有字段，「同一帧内连续发生」全靠它；
//   2) 它的时间是 float 秒，会话跑上几个小时后 float 的有效位就不够再分出毫秒了，而 t 是毫秒整数。
//   给 IClock 加这两个成员也不合适：那是全框架的时间接口，加一个只有埋点用的 FrameCount 会让
//   每个实现（将来的服务器校时版）都被迫实现一个跟它无关的东西。所以另起一个最小接口。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点时钟。只有两个成员，为的是 EditMode 测试能喂假时钟、假帧号，
    /// 不依赖真实的 Unity 帧循环就能验证「序号递增、t 单调不减、限流按秒生效」。
    /// </summary>
    public interface ITelemetryClock
    {
        /// <summary>单调递增的毫秒计数。起点任意（只用来算差值），要求不受 timeScale、系统改时间影响。</summary>
        long MillisecondsNow { get; }

        /// <summary>当前帧号（对应 <c>Time.frameCount</c>）。</summary>
        int FrameCount { get; }
    }
}
