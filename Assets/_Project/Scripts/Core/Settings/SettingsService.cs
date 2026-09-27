// 职责：ISettingsService 的唯一实现——读写独立档案 "settings"、把显示项应用到屏幕、把音量推给音频服务、快照与回滚。
// 为什么新建：ISettingsService 是契约，实现必须分开放；换算规则已拆到 DisplaySettingsMath（可测），
//   Unity 显示 API 已收进 IDisplayBackend（可替换），这里只剩编排，工程内没有现成文件能承担。
// 分辨率基准来自用户 2026-09-26 的决定：1080p / 16:9 基准、最低 1280×720、窗口化可拖拽 + 无边框全屏、
//   分辨率列表取自显示器。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Audio;
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Platform;
using Game.Core.Save;
using UnityEngine;
using VContainer;

namespace Game.Core.Settings
{
    /// <summary>
    /// 设置服务。注册在 <c>JsonSaveService</c> 之后、<c>AudioService</c> 之前：
    /// 读档案要存档服务已就绪，音频服务初始化时要读 <see cref="Current"/> 里的音量。
    /// <para>
    /// <see cref="Current"/> 在整个生命周期里是**同一个实例**，读档与 <see cref="Restore"/> 都是原地覆盖字段，
    /// 这样音频服务与面板手里的引用永远指向最新值。
    /// </para>
    /// </summary>
    public sealed class SettingsService : ISettingsService, IGameService
    {
        /// <summary>独立档案名，落盘为存档根目录下的 <c>profile-settings.json</c>。</summary>
        public const string ProfileName = "settings";

        private readonly ISaveService saves;
        private readonly IPlatformService platform;
        private readonly IDisplayBackend display;
        private readonly Func<IAudioService> audioProvider;
        private readonly SettingsSaveData current = new SettingsSaveData();

        private List<DisplayResolution> availableResolutions;

        /// <summary>上一次真正调过 SetResolution 的参数；没调过时 hasAppliedResolution 为 false。用来做到「只在值变化时调」。</summary>
        private bool hasAppliedResolution;
        private DisplayResolution appliedResolution;
        private FullScreenMode appliedMode;

        /// <summary>
        /// 容器用的构造。音频服务通过 resolver **延迟**取：AudioService 构造时要注入本服务，
        /// 这里再构造注入 IAudioService 就成了环形依赖——为绕开构造期环才这么做，替代方案见 code review 2026-09-26。
        /// </summary>
        [Inject]
        public SettingsService(ISaveService saves, IPlatformService platform, IObjectResolver resolver)
            : this(saves, platform, new UnityDisplayBackend(), CreateAudioProvider(resolver))
        {
        }

        /// <summary>
        /// 测试用构造：显示后端换成假实现就不会真改编辑器分辨率；<paramref name="audioProvider"/> 可为 null（不推音量）。
        /// </summary>
        public SettingsService(ISaveService saves, IPlatformService platform, IDisplayBackend display, Func<IAudioService> audioProvider)
        {
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.display = display ?? throw new ArgumentNullException(nameof(display));
            this.audioProvider = audioProvider;
        }

        public SettingsSaveData Current => current;

        public DisplayResolution NativeResolution => display.NativeResolution;

        public IReadOnlyList<DisplayResolution> AvailableResolutions
        {
            get
            {
                // 显示器列表在一次运行里基本不变，取一次缓存下来（Screen.resolutions 每次调用都分配数组）。
                if (availableResolutions == null)
                {
                    availableResolutions = DisplaySettingsMath.BuildResolutionList(display.QueryResolutions(), NativeResolution);
                }

                return availableResolutions;
            }
        }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            // 档案不存在时 ReadProfileAsync 返回 new T()，即默认设置；损坏时它会挪走现场并同样返回默认值。
            SettingsSaveData loaded = await saves.ReadProfileAsync<SettingsSaveData>(ProfileName, Sanitize, ct);
            current.CopyFrom(loaded);
            ApplyDisplay();
            Log.Info($"SettingsService 就绪：分辨率 {DescribeResolution()}，全屏模式 {current.FullScreenMode}，"
                     + $"垂直同步 {current.VSync}，帧率上限 {current.TargetFrameRate}");
        }

        public void ApplyDisplay()
        {
            Sanitize(current);

            // 触屏为主的平台（手机）分辨率与全屏由系统决定，SetResolution 要么无效要么会把画面弄糊，跳过；
            // 垂直同步与帧率上限在手机上仍然有意义（省电），照常设。这里判的是 IPlatformService，不写平台宏。
            if (!platform.IsTouchPrimary)
            {
                DisplayResolution target = DisplaySettingsMath.ResolveResolution(
                    current.ResolutionWidth, current.ResolutionHeight, AvailableResolutions, NativeResolution);
                FullScreenMode mode = DisplaySettingsMath.ToFullScreenMode(current.FullScreenMode);
                if (!hasAppliedResolution || !appliedResolution.Equals(target) || appliedMode != mode)
                {
                    // 只在值变化时调：窗口化下玩家拖过窗口大小，改音量之类的操作不该把窗口弹回去。
                    display.SetResolution(target.Width, target.Height, mode);
                    hasAppliedResolution = true;
                    appliedResolution = target;
                    appliedMode = mode;
                }
            }

            display.VSyncCount = current.VSync ? 1 : 0;
            display.TargetFrameRate = DisplaySettingsMath.ToTargetFrameRate(current.VSync, current.TargetFrameRate);
        }

        public void ApplyAudio()
        {
            IAudioService audio = audioProvider?.Invoke();
            if (audio == null)
            {
                return;
            }

            // 音频服务读的就是 Current，这里赋一遍是为了走它的 setter：夹取 + 立即刷新各声部音量。
            audio.MasterVolume = current.MasterVolume;
            audio.BgmVolume = current.BgmVolume;
            audio.SfxVolume = current.SfxVolume;
        }

        public UniTask SaveAsync(CancellationToken ct = default)
        {
            return saves.WriteProfileAsync(ProfileName, current, ct);
        }

        public SettingsSaveData Snapshot()
        {
            return current.Clone();
        }

        public void Restore(SettingsSaveData snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            current.CopyFrom(snapshot);
            ApplyDisplay();
            ApplyAudio();
        }

        /// <summary>
        /// 合法化（读档后跑一次，每次 ApplyDisplay 前也跑一遍）：档案是玩家机器上的文本，手改或旧版本写出的越界值
        /// 在这里纠正，不当成损坏——损坏会把整份档案挪走，玩家的其它设置也跟着丢。
        /// </summary>
        private static void Sanitize(SettingsSaveData data)
        {
            data.MasterVolume = Mathf.Clamp01(data.MasterVolume);
            data.BgmVolume = Mathf.Clamp01(data.BgmVolume);
            data.SfxVolume = Mathf.Clamp01(data.SfxVolume);
            if (string.IsNullOrEmpty(data.Language))
            {
                data.Language = new SettingsSaveData().Language;
            }

            if (data.ResolutionWidth <= 0 || data.ResolutionHeight <= 0)
            {
                data.ResolutionWidth = 0;
                data.ResolutionHeight = 0;
            }

            data.FullScreenMode = DisplaySettingsMath.SanitizeFullScreenMode(data.FullScreenMode);
            data.TargetFrameRate = DisplaySettingsMath.SanitizeFrameRate(data.TargetFrameRate);
        }

        private static Func<IAudioService> CreateAudioProvider(IObjectResolver resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            IAudioService cached = null;
            return () => cached ?? (cached = resolver.Resolve<IAudioService>());
        }

        private string DescribeResolution()
        {
            return current.ResolutionWidth <= 0
                ? "原生 " + NativeResolution
                : current.ResolutionWidth + "×" + current.ResolutionHeight;
        }
    }
}
