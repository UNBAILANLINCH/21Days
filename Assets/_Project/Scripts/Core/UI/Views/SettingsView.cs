// 职责：设置面板——音频三条滑条、显示四项（分辨率 / 全屏模式 / 垂直同步 / 帧率上限）、语言占位、应用 / 返回；只显示与抛事件，不注入服务。
// 为什么新建：复用——工程里没有任何带滑条 / 下拉框的面板；扩展——暂停菜单是「导航」面板，设置是「编辑」面板，
//   两者生命周期不同（设置有回滚点，可从标题等其它入口打开），塞进 PauseMenuView 职责说不通（roadmap E3）。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Save;
using Game.Core.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 设置面板。预制体 <c>Assets/_Project/Prefabs/UI/SettingsView.prefab</c>，Addressables 地址 <c>SettingsView</c>。
    /// <para>
    /// 用法：<see cref="SettingsController"/> 打开后调 <see cref="Bind"/> 把当前值摆上去（不触发变化事件），
    /// 之后玩家每动一个控件就抛对应的 <c>OnXxxChanged</c>；「应用」「返回」各抛一个事件。
    /// 何时生效、何时回滚全由控制器决定，本面板不碰 <see cref="ISettingsService"/>。
    /// </para>
    /// </summary>
    public sealed class SettingsView : UIView
    {
        /// <summary>帧率上限下拉框的选项值，0 表示不限。与 <see cref="FrameRateLabels"/> 一一对应。</summary>
        private static readonly int[] FrameRateValues = { 0, 30, 60, 120, 144, 240 };

        private static readonly string[] FrameRateLabels = { "不限", "30", "60", "120", "144", "240" };

        /// <summary>全屏模式下拉框的选项，下标即 <c>SettingsSaveData.FullScreenMode</c>（0 无边框全屏 / 1 窗口化）。</summary>
        private static readonly string[] FullScreenLabels = { "无边框全屏", "窗口化" };

        private const string LanguageLabel = "简体中文";

        [Header("音频")]
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Slider sfxSlider;

        [Header("显示（触屏为主的平台整块隐藏）")]
        [Tooltip("显示区根节点。")]
        [SerializeField] private GameObject displayRoot;
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private TMP_Dropdown fullScreenDropdown;
        [SerializeField] private Toggle vsyncToggle;
        [Tooltip("帧率上限；垂直同步开着时禁用。")]
        [SerializeField] private TMP_Dropdown frameRateDropdown;

        [Header("语言（占位，只有一项且禁用）")]
        [SerializeField] private TMP_Dropdown languageDropdown;

        [Header("按钮")]
        [SerializeField] private Button applyButton;
        [Tooltip("「返回」；同时应拖到基类的 Default Selected 上。")]
        [SerializeField] private Button backButton;

        private readonly List<DisplayResolution> resolutions = new List<DisplayResolution>();
        private readonly List<string> optionBuffer = new List<string>();

        /// <summary>主音量滑条变化，0～1。</summary>
        public event Action<float> OnMasterVolumeChanged;

        /// <summary>音乐音量滑条变化，0～1。</summary>
        public event Action<float> OnBgmVolumeChanged;

        /// <summary>音效音量滑条变化，0～1。</summary>
        public event Action<float> OnSfxVolumeChanged;

        /// <summary>分辨率选中项变化。</summary>
        public event Action<DisplayResolution> OnResolutionChanged;

        /// <summary>全屏模式变化（0 无边框全屏 / 1 窗口化）。</summary>
        public event Action<int> OnFullScreenModeChanged;

        /// <summary>垂直同步开关变化。</summary>
        public event Action<bool> OnVSyncChanged;

        /// <summary>帧率上限变化（0 = 不限）。</summary>
        public event Action<int> OnFrameRateChanged;

        /// <summary>「应用」被点。</summary>
        public event Action OnApply;

        /// <summary>「返回」被点。</summary>
        public event Action OnBack;

        /// <summary>面板已被关闭（不论谁关的），在 <see cref="OnCloseAsync"/> 里触发一次。</summary>
        public event Action OnClosed;

        public override UILayer Layer => UILayer.Panel;

        public override bool IsFullScreen => true;

        /// <summary>Esc 关掉等同「返回」：控制器在 <see cref="OnClosed"/> 里按未应用处理（回滚）。</summary>
        public override bool CloseOnCancel => true;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            UnhookAll();
            if (masterSlider != null) masterSlider.onValueChanged.AddListener(RaiseMaster);
            if (bgmSlider != null) bgmSlider.onValueChanged.AddListener(RaiseBgm);
            if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(RaiseSfx);
            if (resolutionDropdown != null) resolutionDropdown.onValueChanged.AddListener(RaiseResolution);
            if (fullScreenDropdown != null) fullScreenDropdown.onValueChanged.AddListener(RaiseFullScreen);
            if (vsyncToggle != null) vsyncToggle.onValueChanged.AddListener(RaiseVSync);
            if (frameRateDropdown != null) frameRateDropdown.onValueChanged.AddListener(RaiseFrameRate);
            if (applyButton != null) applyButton.onClick.AddListener(RaiseApply);
            if (backButton != null) backButton.onClick.AddListener(RaiseBack);

            // 静态选项在这里填一次；随数据变的选项（分辨率）在 Bind 里填。
            FillOptions(fullScreenDropdown, FullScreenLabels);
            FillOptions(frameRateDropdown, FrameRateLabels);
            FillOptions(languageDropdown, new[] { LanguageLabel });
            if (languageDropdown != null) languageDropdown.interactable = false;
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            UnhookAll();

            // 面板可能被 UIService 从外部关掉（Esc 走 CloseTopAsync），通知控制器按「未应用」收尾。
            Action closed = OnClosed;
            OnClosed = null;
            OnMasterVolumeChanged = null;
            OnBgmVolumeChanged = null;
            OnSfxVolumeChanged = null;
            OnResolutionChanged = null;
            OnFullScreenModeChanged = null;
            OnVSyncChanged = null;
            OnFrameRateChanged = null;
            OnApply = null;
            OnBack = null;
            closed?.Invoke();
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 把当前设置摆到控件上。全部用 <c>SetValueWithoutNotify</c>，不会触发任何 <c>OnXxxChanged</c>。
        /// 分辨率为 0×0（跟随原生）或不在列表里时，预选与 <paramref name="native"/> 相同的一项，找不到就选最后一项。
        /// <paramref name="native"/> 由控制器从 <c>ISettingsService.NativeResolution</c> 取，本面板不读 Unity 显示 API。
        /// </summary>
        public void Bind(SettingsSaveData data, IReadOnlyList<DisplayResolution> available, DisplayResolution native)
        {
            if (data == null)
            {
                return;
            }

            if (masterSlider != null) masterSlider.SetValueWithoutNotify(data.MasterVolume);
            if (bgmSlider != null) bgmSlider.SetValueWithoutNotify(data.BgmVolume);
            if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(data.SfxVolume);

            resolutions.Clear();
            optionBuffer.Clear();
            int count = available == null ? 0 : available.Count;
            for (int i = 0; i < count; i++)
            {
                resolutions.Add(available[i]);
                optionBuffer.Add(available[i].ToString());
            }

            if (resolutionDropdown != null)
            {
                resolutionDropdown.ClearOptions();
                resolutionDropdown.AddOptions(optionBuffer);
                resolutionDropdown.interactable = count > 0;
                if (count > 0)
                {
                    resolutionDropdown.SetValueWithoutNotify(FindResolutionIndex(resolutions, data.ResolutionWidth, data.ResolutionHeight, native));
                }
            }

            if (fullScreenDropdown != null)
            {
                fullScreenDropdown.SetValueWithoutNotify(Mathf.Clamp(data.FullScreenMode, 0, FullScreenLabels.Length - 1));
            }

            if (vsyncToggle != null) vsyncToggle.SetIsOnWithoutNotify(data.VSync);
            if (frameRateDropdown != null)
            {
                frameRateDropdown.SetValueWithoutNotify(FindFrameRateIndex(data.TargetFrameRate));
            }

            RefreshFrameRateInteractable(data.VSync);
            if (languageDropdown != null) languageDropdown.SetValueWithoutNotify(0);
        }

        /// <summary>显示 / 隐藏整个显示区（触屏为主的平台隐藏：手机分辨率与帧率由系统管）。</summary>
        public void SetDisplaySectionVisible(bool visible)
        {
            if (displayRoot != null)
            {
                displayRoot.SetActive(visible);
            }
        }

        /// <summary>帧率值 → 下拉框下标；不在选项里的值落到「不限」。纯函数，EditMode 可测。</summary>
        public static int FindFrameRateIndex(int frameRate)
        {
            for (int i = 0; i < FrameRateValues.Length; i++)
            {
                if (FrameRateValues[i] == frameRate)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// 存档宽高 → 分辨率下拉框下标。纯函数，EditMode 可测。
        /// 规则：列表里有完全相同的一项 → 选它；否则（0×0 = 跟随原生，或换了显示器找不到）→ 选与 <paramref name="native"/> 相同的一项；
        /// 原生也不在列表里 → 选最后一项（列表按宽高升序，即最大的那项）；列表为空 → 0。不往列表里追加项。
        /// </summary>
        public static int FindResolutionIndex(IReadOnlyList<DisplayResolution> list, int width, int height, DisplayResolution native)
        {
            if (list == null || list.Count == 0)
            {
                return 0;
            }

            int nativeIndex = -1;
            for (int i = 0; i < list.Count; i++)
            {
                DisplayResolution r = list[i];
                if (r.Width == width && r.Height == height)
                {
                    return i;
                }

                if (r.Equals(native))
                {
                    nativeIndex = i;
                }
            }

            return nativeIndex >= 0 ? nativeIndex : list.Count - 1;
        }

        private void RefreshFrameRateInteractable(bool vsync)
        {
            // 垂直同步开着时帧率由刷新率决定，帧率上限不起作用，禁用免得玩家以为改了有用。
            if (frameRateDropdown != null)
            {
                frameRateDropdown.interactable = !vsync;
            }
        }

        private void FillOptions(TMP_Dropdown dropdown, string[] labels)
        {
            if (dropdown == null)
            {
                return;
            }

            optionBuffer.Clear();
            optionBuffer.AddRange(labels);
            dropdown.ClearOptions();
            dropdown.AddOptions(optionBuffer);
        }

        private void UnhookAll()
        {
            if (masterSlider != null) masterSlider.onValueChanged.RemoveListener(RaiseMaster);
            if (bgmSlider != null) bgmSlider.onValueChanged.RemoveListener(RaiseBgm);
            if (sfxSlider != null) sfxSlider.onValueChanged.RemoveListener(RaiseSfx);
            if (resolutionDropdown != null) resolutionDropdown.onValueChanged.RemoveListener(RaiseResolution);
            if (fullScreenDropdown != null) fullScreenDropdown.onValueChanged.RemoveListener(RaiseFullScreen);
            if (vsyncToggle != null) vsyncToggle.onValueChanged.RemoveListener(RaiseVSync);
            if (frameRateDropdown != null) frameRateDropdown.onValueChanged.RemoveListener(RaiseFrameRate);
            if (applyButton != null) applyButton.onClick.RemoveListener(RaiseApply);
            if (backButton != null) backButton.onClick.RemoveListener(RaiseBack);
        }

        private void RaiseMaster(float value) => OnMasterVolumeChanged?.Invoke(value);

        private void RaiseBgm(float value) => OnBgmVolumeChanged?.Invoke(value);

        private void RaiseSfx(float value) => OnSfxVolumeChanged?.Invoke(value);

        private void RaiseResolution(int index)
        {
            if (index >= 0 && index < resolutions.Count)
            {
                OnResolutionChanged?.Invoke(resolutions[index]);
            }
        }

        private void RaiseFullScreen(int index) => OnFullScreenModeChanged?.Invoke(index);

        private void RaiseVSync(bool on)
        {
            RefreshFrameRateInteractable(on);
            OnVSyncChanged?.Invoke(on);
        }

        private void RaiseFrameRate(int index)
        {
            if (index >= 0 && index < FrameRateValues.Length)
            {
                OnFrameRateChanged?.Invoke(FrameRateValues[index]);
            }
        }

        private void RaiseApply() => OnApply?.Invoke();

        private void RaiseBack() => OnBack?.Invoke();
    }
}
