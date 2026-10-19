// 职责：真实音频源的音量、暂停与释放；纯规则测试无法验证 Unity 声部生命周期。
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Audio;
using Game.Core.Save;
using Game.Core.Settings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class GameplayAudioPlaybackTests
    {
        [UnityTest]
        public IEnumerator OwnedLoop_PausesChangesVolumeAndStops()
        {
            var assets = new AddressablesAssetService(null, null);
            var audio = new AudioService(assets, new Settings(), null, null);
            audio.InitializeAsync(default).GetAwaiter().GetResult();
            var clip = AudioClip.Create("VoiceTest", 4410, 1, 44100, false);
            SfxVoice voice = null;
            try
            {
                audio.MasterVolume = 1f; audio.SfxVolume = 1f;
                voice = audio.PlayVoice(clip, true);
                Assert.That(voice, Is.Not.Null);
                var root = GameObject.Find("AudioRoot");
                var source = root.GetComponentInChildren<AudioSource>();
                foreach (var candidate in root.GetComponentsInChildren<AudioSource>())
                    if (candidate.clip == clip) source = candidate;
                Assert.That(source.clip, Is.SameAs(clip));
                yield return null;
                Assert.That(source.isPlaying, Is.True);
                Assert.That(source.loop, Is.True);
                voice.SetPaused(true);
                Assert.That(source.isPlaying, Is.False);
                voice.SetPaused(false);
                yield return null;
                Assert.That(source.isPlaying, Is.True);
                audio.SfxVolume = .25f;
                Assert.That(source.volume, Is.EqualTo(.25f).Within(.001f));
                voice.Dispose(); voice.Dispose();
                yield return null;
                Assert.That(source == null, Is.True);
                Assert.That(voice.IsDisposed, Is.True);
            }
            finally
            {
                if (voice != null) voice.Dispose();
                audio.Dispose(); assets.Dispose(); Object.Destroy(clip);
            }
        }
        private sealed class Settings : ISettingsService
        {
            public SettingsSaveData Current { get; } = new SettingsSaveData();
            public IReadOnlyList<DisplayResolution> AvailableResolutions => new DisplayResolution[0];
            public DisplayResolution NativeResolution => default;
            public void ApplyAudio() { }
            public void ApplyDisplay() { }
            public UniTask SaveAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public SettingsSaveData Snapshot() => Current.Clone();
            public void Restore(SettingsSaveData snapshot) => Current.CopyFrom(snapshot);
        }
    }
}
