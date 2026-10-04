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

        internal AudioPlayback(AudioSource source, double dspStart, double inputStart, Action<AudioPlayback> release)
        {
            this.source = source;
            this.dspStart = dspStart;
            this.inputStart = inputStart;
            this.release = release;
        }

        public double Position => AudioSettings.dspTime - dspStart;
        // 输入事件时间与 DSP 时间的原点不同，预约时记录同一启动点的两种时钟值。
        public double PositionAtInputTime(double timestamp) => timestamp - inputStart;

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
