// 职责：校验单轮 DSP/realtime 桥接的连续性。规则判定不读取时钟；音频服务不决定玩法中断，故独立纯逻辑检查。
using System;
using Game.Core.Simulation;

namespace Game.Rhythm
{
    public sealed class RhythmClockGuard
    {
        private readonly double dspStart;
        private readonly double inputStart;
        private readonly double toleranceSeconds;
        private double lastDsp = double.NegativeInfinity;
        private double lastRealtime = double.NegativeInfinity;

        public RhythmClockGuard(double dspStart, double inputStart, double toleranceSeconds)
        {
            if (!RhythmRules.Finite(dspStart) || !RhythmRules.Finite(inputStart) ||
                !RhythmRules.Finite(toleranceSeconds) || toleranceSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(toleranceSeconds));
            this.dspStart = dspStart; this.inputStart = inputStart; this.toleranceSeconds = toleranceSeconds;
        }

        public bool Check(double realtime, double dsp, double sampleSpanSeconds, out string reason)
        {
            reason = null;
            if (!RhythmRules.Finite(realtime) || !RhythmRules.Finite(dsp) ||
                !RhythmRules.Finite(sampleSpanSeconds) || sampleSpanSeconds < 0) reason = "invalid_clock";
            else if (dsp < lastDsp || realtime < lastRealtime) reason = "clock_reversed";
            // 单次读取被抢占时只扩大该样本的不确定度；阈值不是物理精度或判定窗口。
            else if (GameMath.Abs((realtime - inputStart) - (dsp - dspStart)) > toleranceSeconds + sampleSpanSeconds * 0.5)
                reason = "clock_discontinuity";
            if (reason != null) return false;
            lastDsp = dsp; lastRealtime = realtime;
            return true;
        }
    }
}
