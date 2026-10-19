// 职责：可停止、暂停的独立音效声部；短音效池没有所有权，AudioPlayback 专管歌曲双时钟，均不能复用。
using System;
using UnityEngine;
namespace Game.Core.Audio
{
    public sealed class SfxVoice : IDisposable
    {
        private AudioSource source;
        private readonly Action<SfxVoice> release;
        private readonly float volume;
        internal SfxVoice(AudioSource source, float volume, Action<SfxVoice> release)
        { this.source = source; this.volume = volume; this.release = release; }
        public bool IsDisposed => source == null;
        public void SetPaused(bool paused)
        {
            if (source == null) return;
            if (paused) source.Pause(); else source.UnPause();
        }
        internal void SetVolume(float level) { if (source != null) source.volume = level * volume; }
        public void Dispose()
        {
            if (source == null) return;
            source.Stop(); source.clip = null;
            UnityEngine.Object.Destroy(source.gameObject);
            source = null;
            release(this);
        }
    }
}
