// 职责：预约音频播放的生命周期与时间轴。普通 BGM 接口不提供定位时间，扩展其返回值会破坏调用方，因此新增句柄。
using System;
using UnityEngine;

namespace Game.Core.Audio
{
    public sealed class AudioPlayback : IDisposable
    {
        private readonly AudioSource source;
        private readonly Action<AudioPlayback> release;
        private readonly double dspStart;
        private readonly double inputStart;
        private bool disposed;

        internal AudioPlayback(AudioSource source, double dspStart, double inputStart, double bridgeSampleSpanSeconds, Action<AudioPlayback> release)
        {
            this.source = source;
            this.dspStart = dspStart;
            this.inputStart = inputStart;
            BridgeSampleSpanSeconds = bridgeSampleSpanSeconds;
            this.release = release;
        }

        public double Position => AudioSettings.dspTime - dspStart;
        public double DspStart => dspStart;
        public double InputStart => inputStart;
        public double BridgeSampleSpanSeconds { get; }
        // 输入事件时间与 DSP 时间的原点不同，预约时记录同一启动点的两种时钟值。
        public double PositionAtInputTime(double timestamp) => timestamp - inputStart;

        /// <summary>按歌曲时间预约停音；句柄仍保留供输入判定尾窗收尾，不受渲染卡顿影响。</summary>
        public void ScheduleEnd(double songSeconds)
        {
            if (disposed) throw new ObjectDisposedException(nameof(AudioPlayback));
            if (double.IsNaN(songSeconds) || double.IsInfinity(songSeconds) || songSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(songSeconds));
            source.SetScheduledEndTime(dspStart + songSeconds);
        }

        // 片段结束后保留时间轴供判定尾窗收尾；释放仍由 Dispose 统一负责。
        public void StopAudio() { if (!disposed && source != null) source.Stop(); }

        internal void SetVolume(float volume)
        {
            if (!disposed && source != null) source.volume = volume;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (source != null)
            {
                source.Stop();
                UnityEngine.Object.Destroy(source.gameObject);
            }
            release(this);
        }
    }
}
