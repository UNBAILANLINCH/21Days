// 职责：一次「打开设置面板 → 改 → 应用 / 返回」的编辑会话——回滚点、脏标记、音量实时生效、显示项延后到「应用」。
// 为什么新建：复用——ISettingsService 只提供 Snapshot / Restore / Apply 原语，不知道「这一次打开面板」的边界；
//   扩展——这套规则若写进 SettingsController，就和 UIService / 面板实例绑死，EditMode 测不了「返回未应用则回滚、
//   应用则保存一次」。拆成不碰 Unity 对象的纯逻辑类，控制器只做「面板事件 → 会话方法」的转接。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Save;
using Game.Core.Settings;
using UnityEngine;

namespace Game.Core.UI
{
    /// <summary>
    /// 设置编辑会话。规则：
    /// <list type="bullet">
    /// <item><see cref="Begin"/> 取回滚点；</item>
    /// <item>三路音量改完立即 <see cref="ISettingsService.ApplyAudio"/>（拖动实时能听到）；</item>
    /// <item>显示项只改 <see cref="ISettingsService.Current"/>，不立即应用（换分辨率会闪屏，等玩家点「应用」）；</item>
    /// <item><see cref="ApplyAsync"/> 应用显示 + 存盘一次，并把回滚点挪到应用后的值；</item>
    /// <item><see cref="End"/>：自上次回滚点以来改过（脏）就 <see cref="ISettingsService.Restore"/> 回去，音量一并回滚。</item>
    /// </list>
    /// </summary>
    public sealed class SettingsEditSession
    {
        private readonly ISettingsService settings;
        private SettingsSaveData snapshot;

        public SettingsEditSession(ISettingsService settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>会话是否进行中（<see cref="Begin"/> 之后、<see cref="End"/> 之前）。</summary>
        public bool IsActive { get; private set; }

        /// <summary>自回滚点以来是否改过任何一项。</summary>
        public bool IsDirty { get; private set; }

        /// <summary>开一次会话：取回滚点、清脏标记。</summary>
        public void Begin()
        {
            snapshot = settings.Snapshot();
            IsDirty = false;
            IsActive = true;
        }

        public void SetMasterVolume(float value)
        {
            settings.Current.MasterVolume = Mathf.Clamp01(value);
            AudioChanged();
        }

        public void SetBgmVolume(float value)
        {
            settings.Current.BgmVolume = Mathf.Clamp01(value);
            AudioChanged();
        }

        public void SetSfxVolume(float value)
        {
            settings.Current.SfxVolume = Mathf.Clamp01(value);
            AudioChanged();
        }

        /// <summary>分辨率；0×0 表示跟随原生。只记值，「应用」时才生效。</summary>
        public void SetResolution(int width, int height)
        {
            settings.Current.ResolutionWidth = width;
            settings.Current.ResolutionHeight = height;
            IsDirty = true;
        }

        /// <summary>0 无边框全屏 / 1 窗口化。只记值。</summary>
        public void SetFullScreenMode(int mode)
        {
            settings.Current.FullScreenMode = mode;
            IsDirty = true;
        }

        public void SetVSync(bool on)
        {
            settings.Current.VSync = on;
            IsDirty = true;
        }

        /// <summary>0 = 不限。只记值。</summary>
        public void SetTargetFrameRate(int frameRate)
        {
            settings.Current.TargetFrameRate = frameRate;
            IsDirty = true;
        }

        /// <summary>应用显示设置并存盘一次；成功后回滚点挪到当前值（之后再「返回」不会撤掉已应用的部分）。</summary>
        public async UniTask ApplyAsync(CancellationToken ct = default)
        {
            settings.ApplyDisplay();
            await settings.SaveAsync(ct);
            snapshot = settings.Snapshot();
            IsDirty = false;
        }

        /// <summary>结束会话。脏则回滚到回滚点并重新应用音量；返回是否发生了回滚。幂等。</summary>
        public bool End()
        {
            if (!IsActive)
            {
                return false;
            }

            IsActive = false;
            if (!IsDirty || snapshot == null)
            {
                return false;
            }

            settings.Restore(snapshot);
            settings.ApplyAudio();
            IsDirty = false;
            return true;
        }

        private void AudioChanged()
        {
            settings.ApplyAudio();
            IsDirty = true;
        }
    }
}
