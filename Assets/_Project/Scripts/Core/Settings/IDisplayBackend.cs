// 职责：SettingsService 与 Unity 显示 API（Screen / QualitySettings / Application）之间的一层薄接缝。
// 为什么新建：EditMode 测试要验 ApplyDisplay 的判断逻辑，但不能真的去改编辑器的分辨率、垂直同步和帧率；
//   把这几个静态 API 收到一个可替换的小接口后，测试换成记录调用的假实现即可。没有现成接口能承担。

using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Settings
{
    /// <summary>显示相关的 Unity 静态 API。正式实现是 <see cref="UnityDisplayBackend"/>，测试里换假实现。</summary>
    public interface IDisplayBackend
    {
        /// <summary>显示器当前（原生）分辨率。存档为 0/0 或找不到时用它。</summary>
        DisplayResolution NativeResolution { get; }

        /// <summary>对应 <c>QualitySettings.vSyncCount</c>。</summary>
        int VSyncCount { get; set; }

        /// <summary>对应 <c>Application.targetFrameRate</c>，-1 = 平台默认（不限）。</summary>
        int TargetFrameRate { get; set; }

        /// <summary>显示器报上来的全部分辨率（可能含重复宽高、不同刷新率），未整理。</summary>
        IEnumerable<DisplayResolution> QueryResolutions();

        /// <summary>切分辨率与全屏模式。</summary>
        void SetResolution(int width, int height, FullScreenMode mode);
    }
}
