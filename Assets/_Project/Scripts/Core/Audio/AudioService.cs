// 职责：IAudioService 的唯一实现——建 AudioRoot（1 个 BGM 源 + SFX 声部池）、三路音量与存档同步、BGM 淡入淡出。
// 为什么新建：IAudioService 是契约，实现必须分开放；音量换算已经拆到 AudioVolumeMath（纯函数、可测），
//   这里只剩「跟 Unity 和存档打交道」的部分，工程内没有现成文件能承担。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Save;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;

namespace Game.Core.Audio
{
    /// <summary>
    /// 音频服务。
    /// <para>
    /// **三路音量怎么落地**：<c>AudioConfig.Mixer</c> 留空时（本波的默认情况）用 AudioSource 的
    /// volume 相乘实现——BGM 源音量 = Master × Bgm，每个 SFX 声部音量 = Master × Sfx，
    /// <c>PlayOneShot</c> 的 volume 参数再乘一次。接上 Mixer 之后改走分贝：
    /// 各 AudioSource 音量恒为 1，音量由 Mixer 的暴露参数控制。
    /// </para>
    /// <para>
    /// **音量与存档**：三个属性读写的就是 <c>SettingsSaveData</c> 分区，setter 立刻生效但**不落盘**；
    /// 落盘是设置界面的事（拖滑块时每帧写文件是灾难）。
    /// </para>
    /// </summary>
    public sealed class AudioService : IAudioService, IGameService, IDisposable
    {
        /// <summary>接上 Mixer 之后要在 Mixer 里暴露的参数名。没接 Mixer 时这三个常量用不到。</summary>
        private const string MasterVolumeParameter = "MasterVolume";
        private const string BgmVolumeParameter = "BgmVolume";
        private const string SfxVolumeParameter = "SfxVolume";

        private const float DefaultFadeSecondsFallback = 0.5f;
        private const int DefaultSfxVoices = 8;

        private readonly IAssetService assets;
        private readonly ISaveService saves;
        private readonly AudioConfig config;

        /// <summary>PlaySfxAsync 加载出来、还没播完的句柄。Dispose 时要兜底释放。</summary>
        private readonly HashSet<AssetHandle<AudioClip>> pendingSfxHandles = new HashSet<AssetHandle<AudioClip>>();

        private CancellationTokenSource lifetimeCts;
        private SettingsSaveData settings;
        private GameObject root;
        private AudioSource bgmSource;
        private AudioSource[] sfxSources;
        private int nextSfxVoice;
        private AssetHandle<AudioClip> bgmHandle;
        private string bgmKey;
        private MotionHandle bgmFade;

        /// <summary>
        /// BGM 请求的序号。<see cref="PlayBgmAsync"/> 与 <see cref="StopBgmAsync"/> 一进来就自增并记下自己的号，
        /// 每个 await 之后比一次——号对不上说明自己已经被更新的请求取代，除了还回自己加载出来的句柄之外
        /// 什么都不做。三个共享字段（<see cref="bgmSource"/>、<see cref="bgmHandle"/>、<see cref="bgmFade"/>）
        /// 永远只由**最新**的那个请求驱动。
        /// </summary>
        private int bgmRequestId;

        private bool disposed;

        public AudioService(IAssetService assets, ISaveService saves, AudioConfig config)
        {
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            this.config = config;
        }

        public float MasterVolume
        {
            get => settings == null ? 1f : settings.MasterVolume;
            set
            {
                if (settings == null)
                {
                    return;
                }

                settings.MasterVolume = Mathf.Clamp01(value);
                ApplyVolumes();
            }
        }

        public float BgmVolume
        {
            get => settings == null ? 1f : settings.BgmVolume;
            set
            {
                if (settings == null)
                {
                    return;
                }

                settings.BgmVolume = Mathf.Clamp01(value);
                ApplyVolumes();
            }
        }

        public float SfxVolume
        {
            get => settings == null ? 1f : settings.SfxVolume;
            set
            {
                if (settings == null)
                {
                    return;
                }

                settings.SfxVolume = Mathf.Clamp01(value);
                ApplyVolumes();
            }
        }

        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (root != null)
            {
                return UniTask.CompletedTask;
            }

            lifetimeCts = new CancellationTokenSource();

            // 音量初值来自存档分区：Get<T> 首次访问会用默认值创建，所以这里不用判存档存不存在。
            settings = saves.Get<SettingsSaveData>();

            root = new GameObject("AudioRoot");
            UnityEngine.Object.DontDestroyOnLoad(root);

            bgmSource = root.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;

            int voices = config == null ? DefaultSfxVoices : Mathf.Max(1, config.SfxVoices);
            sfxSources = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                AudioSource source = root.AddComponent<AudioSource>();
                source.loop = false;
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                sfxSources[i] = source;
            }

            ApplyVolumes();

            // 退出前先把音频句柄还给资源服务。VContainer 按**注册顺序**释放，IAssetService 排在前面，
            // 等释放到这里它已经销毁了，会误报「还有句柄没释放」——那条 Warn 要留给真泄漏。
            Application.quitting += ReleaseAllHandles;

            Log.Info($"AudioService 就绪：BGM 1 路 + SFX {voices} 路，"
                     + $"音量 Master={MasterVolume:F2} Bgm={BgmVolume:F2} Sfx={SfxVolume:F2}");
            return UniTask.CompletedTask;
        }

        public void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (disposed)
            {
                return;
            }

            // AudioClip 是 UnityEngine.Object，判空只用 == null。
            if (clip == null)
            {
                Log.Warn("PlaySfx 收到空 clip，忽略");
                return;
            }

            if (sfxSources == null || sfxSources.Length == 0)
            {
                Log.Warn($"AudioService 还没初始化，音效 {clip.name} 被丢弃");
                return;
            }

            // 轮转取声部：池用满时最先用过的那一个被再次拿走，正好就是「复用最早的」。
            AudioSource source = sfxSources[nextSfxVoice];
            nextSfxVoice = (nextSfxVoice + 1) % sfxSources.Length;
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        public async UniTask PlaySfxAsync(string key, float volume = 1f, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("音效 key 不能为空", nameof(key));
            }

            // 调用方的 ct 只作用于**加载阶段**：它表达的是「这个状态/面板还要不要这个音效」，
            // 一旦音效已经开始响，把它掐断只会得到半截声音 + 提前释放的句柄。
            AssetHandle<AudioClip> handle;
            using (CancellationTokenSource linked = LinkLifetime(ct))
            {
                handle = await assets.LoadAsync<AudioClip>(key, linked.Token);
            }

            pendingSfxHandles.Add(handle);
            try
            {
                AudioClip clip = handle.Asset;
                PlaySfx(clip, volume);

                // 等它播完再释放句柄。选 Delay 而不是缓存最近 N 个句柄：
                // 后者的「N 多大才够」没有正确答案，短音效会被提前释放，长音效又白占内存；
                // clip.length 是确定的，代价只是一个 UniTask。
                float seconds = clip == null ? 0f : clip.length;
                if (seconds > 0f)
                {
                    // 播放阶段只绑服务自身的生命周期令牌：服务销毁时该停，调用方取消时不该停。
                    // DelayType 要写全限定：LitMotion 里也有一个同名类型，裸写会撞车。
                    await UniTask.Delay(TimeSpan.FromSeconds(seconds),
                        Cysharp.Threading.Tasks.DelayType.UnscaledDeltaTime,
                        cancellationToken: LifetimeToken);
                }
            }
            finally
            {
                pendingSfxHandles.Remove(handle);
                handle.Dispose();
            }
        }

        public async UniTask PlayBgmAsync(string key, float fadeSeconds = 0.5f, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("BGM key 不能为空", nameof(key));
            }

            if (bgmSource == null)
            {
                Log.Warn($"AudioService 还没初始化，BGM {key} 被丢弃");
                return;
            }

            if (bgmKey == key && bgmSource.isPlaying)
            {
                return;
            }

            // 记下自己的号：从这一刻起，后来的切曲 / 停曲请求会把号推高，把这一次作废。
            int myId = ++bgmRequestId;
            float fade = ResolveFade(fadeSeconds);
            using (CancellationTokenSource linked = LinkLifetime(ct))
            {
                AssetHandle<AudioClip> handle = await assets.LoadAsync<AudioClip>(key, linked.Token);

                // 加载期间被新请求取代了：只把自己刚加载、还没挂到 source 上的句柄还回去。
                // 继续往下走的话会去 Stop 别人刚放起来的曲子、Dispose 别人正在用的句柄。
                if (myId != bgmRequestId)
                {
                    handle.Dispose();
                    return;
                }

                try
                {
                    // 只有一路 BGM 源，所以是「先淡出旧的、再淡入新的」而不是真正的交叉淡化。
                    // 真要交叉就得两路源轮流用，为一个占位框架加这个复杂度不值当。
                    if (bgmSource.isPlaying && fade > 0f)
                    {
                        await FadeBgmAsync(0f, fade, linked.Token);

                        // 淡出旧曲这段最长，被取代的概率也最高，再验一次号。
                        if (myId != bgmRequestId)
                        {
                            handle.Dispose();
                            return;
                        }
                    }

                    bgmSource.Stop();
                    if (bgmHandle != null)
                    {
                        bgmHandle.Dispose();
                    }

                    bgmHandle = handle;
                    bgmKey = key;
                    bgmSource.clip = handle.Asset;
                    bgmSource.volume = fade > 0f ? 0f : BgmSourceLevel;
                    bgmSource.Play();

                    if (fade > 0f)
                    {
                        await FadeBgmAsync(BgmSourceLevel, fade, linked.Token);
                    }
                }
                catch
                {
                    // 没能接管这个句柄就要还回去，否则这首曲子的内存永远不释放。
                    if (bgmHandle != handle)
                    {
                        handle.Dispose();
                    }

                    throw;
                }
            }
        }

        public void StopBgm(float fadeSeconds = 0.5f)
        {
            if (disposed || bgmSource == null)
            {
                return;
            }

            StopBgmAsync(ResolveFade(fadeSeconds)).Forget();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Application.quitting -= ReleaseAllHandles;

            if (lifetimeCts != null)
            {
                lifetimeCts.Cancel();
                lifetimeCts.Dispose();
                lifetimeCts = null;
            }

            if (bgmFade.IsActive())
            {
                bgmFade.Cancel();
            }

            ReleaseAllHandles();

            bgmSource = null;
            sfxSources = null;

            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }
        }

        /// <summary>把 BGM 与还没播完的 SFX 句柄全部还给资源服务。幂等：AssetHandle.Dispose 本身就幂等。</summary>
        private void ReleaseAllHandles()
        {
            // 句柄 Dispose 会改这个集合，先拷一份再遍历。
            if (pendingSfxHandles.Count > 0)
            {
                var pending = new AssetHandle<AudioClip>[pendingSfxHandles.Count];
                pendingSfxHandles.CopyTo(pending);
                for (int i = 0; i < pending.Length; i++)
                {
                    pending[i].Dispose();
                }

                pendingSfxHandles.Clear();
            }

            if (bgmHandle != null)
            {
                bgmHandle.Dispose();
                bgmHandle = null;
            }

            bgmKey = null;
        }

        /// <summary>接了 Mixer 时 AudioSource 音量恒为 1（音量交给 Mixer 的分贝参数），否则按线性相乘。</summary>
        private bool UseMixer => config != null && config.Mixer != null;

        private float BgmSourceLevel => UseMixer ? 1f : AudioVolumeMath.Effective(MasterVolume, BgmVolume);

        private float SfxSourceLevel => UseMixer ? 1f : AudioVolumeMath.Effective(MasterVolume, SfxVolume);

        private void ApplyVolumes()
        {
            if (UseMixer)
            {
                // 参数名在 Mixer 资产里 Expose 出来才生效；没暴露时 SetFloat 返回 false，不报错。
                config.Mixer.SetFloat(MasterVolumeParameter, AudioVolumeMath.ToDecibels(MasterVolume));
                config.Mixer.SetFloat(BgmVolumeParameter, AudioVolumeMath.ToDecibels(BgmVolume));
                config.Mixer.SetFloat(SfxVolumeParameter, AudioVolumeMath.ToDecibels(SfxVolume));
            }

            if (bgmSource != null)
            {
                // 调音量时掐掉正在跑的淡入淡出直接落到目标值：
                // 让动画继续会把玩家刚拖到的音量再改回去，看起来像滑块失灵。
                if (bgmFade.IsActive())
                {
                    bgmFade.Cancel();
                }

                bgmSource.volume = BgmSourceLevel;
            }

            if (sfxSources == null)
            {
                return;
            }

            float sfxLevel = SfxSourceLevel;
            for (int i = 0; i < sfxSources.Length; i++)
            {
                if (sfxSources[i] != null)
                {
                    sfxSources[i].volume = sfxLevel;
                }
            }
        }

        private async UniTaskVoid StopBgmAsync(float fade)
        {
            int myId = ++bgmRequestId;
            try
            {
                if (fade > 0f && bgmSource != null && bgmSource.isPlaying)
                {
                    await FadeBgmAsync(0f, fade, LifetimeToken);

                    // 淡出期间又有人切曲了：新曲已经在放，这里再 Stop 就把它掐了。
                    if (myId != bgmRequestId)
                    {
                        return;
                    }
                }

                if (bgmSource != null)
                {
                    bgmSource.Stop();
                    bgmSource.clip = null;
                }

                if (bgmHandle != null)
                {
                    bgmHandle.Dispose();
                    bgmHandle = null;
                }

                bgmKey = null;
            }
            catch (OperationCanceledException)
            {
                // 服务销毁引起的取消属正常路径，Dispose 里已经把句柄释放掉了
            }
        }

        private UniTask FadeBgmAsync(float target, float seconds, CancellationToken ct)
        {
            if (bgmFade.IsActive())
            {
                bgmFade.Cancel();
            }

            // UpdateIgnoreTimeScale：暂停界面里 timeScale 是 0，音乐淡出照样要走完。
            bgmFade = LMotion.Create(bgmSource.volume, target, seconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToVolume(bgmSource);

            // cancelAwaitOnMotionCanceled = false：被下一次切曲或调音量掐断时正常结束，不抛异常。
            return bgmFade.ToUniTask(CancelBehavior.Cancel, false, ct);
        }

        private float ResolveFade(float fadeSeconds)
        {
            if (fadeSeconds >= 0f)
            {
                return fadeSeconds;
            }

            return config == null ? DefaultFadeSecondsFallback : Mathf.Max(0f, config.DefaultBgmFadeSeconds);
        }

        /// <summary>服务自身的生命周期令牌：只有 Dispose 会把它取消。没初始化时是永不取消的默认令牌。</summary>
        private CancellationToken LifetimeToken => lifetimeCts == null ? default : lifetimeCts.Token;

        private CancellationTokenSource LinkLifetime(CancellationToken ct)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, ct);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AudioService),
                    "音频服务已销毁，不能再播放（多半是作用域已经释放了还有代码在跑）");
            }
        }
    }
}
