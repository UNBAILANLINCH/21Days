// 职责：可手动推进的假埋点时钟，让埋点测试完全脱离 Unity 的帧循环。
// 为什么新建：限流是按「秒」判定的，用真实时间测就得 sleep 一秒，还会因为机器快慢闪烁；
//   帧号在 EditMode 下也不会自己往前走。工程里 TimerServiceTests 的 FakeClock 实现的是 IClock
//   （float 秒、没有帧号），接口对不上，复用不了。被两个测试文件共用，所以没做成私有嵌套类。

using Game.Core.Telemetry;

namespace Game.Tests.EditMode.Telemetry
{
    /// <summary>时间与帧号都由测试自己推。默认从 0 毫秒、第 0 帧开始。</summary>
    internal sealed class FakeTelemetryClock : ITelemetryClock
    {
        public long MillisecondsNow { get; private set; }

        public int FrameCount { get; private set; }

        /// <summary>推进时间，并把帧号往前走 <paramref name="frames"/> 帧。</summary>
        public void Advance(long milliseconds, int frames = 1)
        {
            MillisecondsNow += milliseconds;
            FrameCount += frames;
        }
    }
}
