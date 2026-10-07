// 职责：预约音频播放的生命周期与时间轴。普通 BGM 接口不提供定位时间，扩展其返回值会破坏调用方，因此新增句柄。
using System;
using UnityEngine;

namespace Game.Core.Audio
{
    public sealed class AudioPlayback : IDisposable
    {
        private readonly AudioSource source;
        private readonly Action<AudioPlayback> release;
        private double dspStart;
        private double inputStart;
        private double pausedPosition;
        private double scheduledEnd = double.PositiveInfinity;
        private readonly int initialSample;
        private bool disposed;

        internal AudioPlayback(AudioSource source, double dspStart, double inputStart, double bridgeSampleSpanSeconds, Action<AudioPlayback> release)
        {
            this.source = source;
            this.dspStart = dspStart;
            this.inputStart = inputStart;
            initialSample = source == null ? 0 : source.timeSamples;
            BridgeSampleSpanSeconds = bridgeSampleSpanSeconds;
            this.release = release;
        }

        public bool IsPaused { get; private set; }
        public double Position => IsPaused ? pausedPosition : AudioSettings.dspTime - dspStart;
        public double DspStart => dspStart;
        public double InputStart => inputStart;
        public double BridgeSampleSpanSeconds { get; }
        // 输入事件时间与 DSP 时间的原点不同，预约时记录同一启动点的两种时钟值。
        public double PositionAtInputTime(double timestamp) => IsPaused ? pausedPosition : timestamp - inputStart;

        public void Pause()
        {
            if (disposed) throw new ObjectDisposedException(nameof(AudioPlayback));
            if (IsPaused) return;
            pausedPosition = Position;
            IsPaused = true;
            if (source == null) return;
            // 预滚预约尚未开始时 Pause 不保证取消预约，明确停音并在继续时重新预约。
            if (pausedPosition < 0) source.Stop(); else source.Pause();
        }
        public void Resume(double inputNow)
        {
            if (disposed) throw new ObjectDisposedException(nameof(AudioPlayback));
            if (!IsPaused) return;
            double nextDsp = ResumeOrigin(AudioSettings.dspTime, pausedPosition);
            double nextInput = ResumeOrigin(inputNow, pausedPosition);
            if (source != null && pausedPosition < scheduledEnd)
            {
                if (pausedPosition < 0) { source.timeSamples = initialSample; source.PlayScheduled(nextDsp); }
                else source.UnPause();
                if (pausedPosition >= 0 && !source.isPlaying)
                    throw new InvalidOperationException("暂停的音频源未恢复播放");
                if (!double.IsPositiveInfinity(scheduledEnd)) source.SetScheduledEndTime(nextDsp + scheduledEnd);
            }
            dspStart = nextDsp; inputStart = nextInput; IsPaused = false;
        }
        // 同一播放位置映射到新时钟原点；纯计算供离线验证，不能替代音频设备实测。
        public static double ResumeOrigin(double now, double position)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || double.IsNaN(position) || double.IsInfinity(position) ||
                double.IsInfinity(now - position)) throw new ArgumentOutOfRangeException(nameof(now));
            return now - position;
        }

        /// <summary>按歌曲时间预约停音；句柄仍保留供输入判定尾窗收尾，不受渲染卡顿影响。</summary>
        public void ScheduleEnd(double songSeconds)
        {
            if (disposed) throw new ObjectDisposedException(nameof(AudioPlayback));
            if (double.IsNaN(songSeconds) || double.IsInfinity(songSeconds) || songSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(songSeconds));
            source.SetScheduledEndTime(dspStart + songSeconds);
            scheduledEnd = songSeconds;
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
