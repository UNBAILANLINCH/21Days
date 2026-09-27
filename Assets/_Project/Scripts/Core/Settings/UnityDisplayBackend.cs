// 职责：IDisplayBackend 的正式实现——直接转调 Screen / QualitySettings / Application。
// 为什么新建：IDisplayBackend 是为测试开的接缝，正式实现必须单独放；逻辑都在 SettingsService / DisplaySettingsMath，
//   这里只做转调，不写判断。
// 不写平台宏：这些 API 在手机上是空操作或被系统覆盖，允许直接调（是否跳过 SetResolution 由 SettingsService 按
//   IPlatformService.IsTouchPrimary 决定），符合 project-root.md「平台差异只在框架层」。

using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Settings
{
    /// <summary>显示 API 的正式转调。</summary>
    public sealed class UnityDisplayBackend : IDisplayBackend
    {
        public DisplayResolution NativeResolution
        {
            get
            {
                Resolution current = Screen.currentResolution;
                return new DisplayResolution(current.width, current.height);
            }
        }

        public int VSyncCount
        {
            get => QualitySettings.vSyncCount;
            set => QualitySettings.vSyncCount = value;
        }

        public int TargetFrameRate
        {
            get => Application.targetFrameRate;
            set => Application.targetFrameRate = value;
        }

        public IEnumerable<DisplayResolution> QueryResolutions()
        {
            Resolution[] resolutions = Screen.resolutions;
            List<DisplayResolution> result = new List<DisplayResolution>(resolutions.Length);
            for (int i = 0; i < resolutions.Length; i++)
            {
                result.Add(new DisplayResolution(resolutions[i].width, resolutions[i].height));
            }

            return result;
        }

        public void SetResolution(int width, int height, FullScreenMode mode)
        {
            Screen.SetResolution(width, height, mode);
        }
    }
}
