// 职责：验证音效预加载、限频、取消与释放；已有音量数学测试不覆盖资源所有权和迟到播放。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Audio;
using Game.Core.Assets;
using Game.Core.Audio;
using Game.Core.Timing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using VContainer;

namespace Game.Tests.EditMode.Core
{
    public sealed class GameplayAudioTests
    {
        private IObjectResolver container;
        private GameplayAudio feedback;
        private Assets assets;
        private Sound sound;
        private Clock clock;
        private AudioClip clip;
        [SetUp] public void SetUp()
        {
            clip = AudioClip.Create("FeedbackTest", 4410, 1, 44100, false);
            assets = new Assets(clip); sound = new Sound(); clock = new Clock();
            var builder = new ContainerBuilder();
            builder.RegisterInstance<IAssetService>(assets);
            builder.RegisterInstance<IAudioService>(sound);
            builder.RegisterInstance<ISfxVoiceService>(sound);
            builder.RegisterInstance<IClock>(clock);
            container = builder.Build(); feedback = new GameplayAudio(container);
        }
        [TearDown] public void TearDown()
        {
            feedback.Dispose(); container.Dispose(); UnityEngine.Object.DestroyImmediate(clip);
        }
        [Test] public void Preload_RepeatPlayIsThrottled_ThenDisposeReleasesEveryHandle()
        {
            feedback.InitializeAsync(default).GetAwaiter().GetResult();
            feedback.InitializeAsync(default).GetAwaiter().GetResult();
            Assert.That(assets.Loaded, Is.EqualTo(22));
            feedback.Play("aware"); feedback.Play("aware");
            Assert.That(sound.Played, Is.EqualTo(1));
            clock.Now = 1f; feedback.Play("aware");
            Assert.That(sound.Played, Is.EqualTo(2));
            feedback.Dispose(); feedback.Play("aware");
            Assert.That(assets.Released, Is.EqualTo(assets.Loaded));
            Assert.That(sound.Played, Is.EqualTo(2));
        }
        [Test] public void CancelledLoad_DisposesLateHandle_AndNeverPlays()
        {
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Assert.Throws<OperationCanceledException>(() => feedback.InitializeAsync(cancelled.Token).GetAwaiter().GetResult());
            }
            feedback.Play("aware");
            Assert.That(assets.Released, Is.EqualTo(assets.Loaded));
            Assert.That(sound.Played, Is.Zero);
        }
        private sealed class Clock : IClock
        {
            public float Now { get; set; }
            public DateTime UtcNow => DateTime.MinValue;
            public float GameTime => Now;
            public float UnscaledTime => Now;
            public float DeltaTime => 0f;
            public float UnscaledDeltaTime => 0f;
        }
        private sealed class Sound : IAudioService, ISfxVoiceService
        {
            public int Played { get; private set; }
            public float MasterVolume { get; set; }
            public float BgmVolume { get; set; }
            public float SfxVolume { get; set; }
            public void PlaySfx(AudioClip value, float volume = 1f) { Assert.That(value, Is.Not.Null); Played++; }
            public UniTask PlaySfxAsync(string key, float volume = 1f, CancellationToken ct = default) => throw new NotSupportedException();
            public UniTask PlayBgmAsync(string key, float fadeSeconds = .5f, CancellationToken ct = default) => throw new NotSupportedException();
            public void StopBgm(float fadeSeconds = .5f) { }
            public AudioPlayback PlayScheduledClip(AudioClip value, float startSeconds, float delaySeconds) => throw new NotSupportedException();
            public SfxVoice PlayVoice(AudioClip value, bool loop, float volume = 1f) => null;
        }
        private sealed class Assets : IAssetService
        {
            private readonly AudioClip clip;
            public int Loaded { get; private set; }
            public int Released { get; private set; }
            public Assets(AudioClip clip) { this.clip = clip; }
            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object
            {
                Loaded++;
                return UniTask.FromResult(new AssetHandle<T>(Addressables.ResourceManager.CreateCompletedOperation(clip as T, null), _ => Released++));
            }
            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default) where T : UnityEngine.Object => throw new NotSupportedException();
            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default) => throw new NotSupportedException();
            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException();
            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default) => throw new NotSupportedException();
        }
    }
}
