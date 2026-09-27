// 职责：显示设置的纯计算——分辨率列表去重排序过滤、存档分辨率对齐到列表、帧率上限合法化、全屏模式映射。
// 为什么新建：这些规则是显示设置里唯一会算错又不好肉眼验的部分，必须 EditMode 可测；
//   写进 SettingsService（要碰 Screen / 读写档案）就只能进 Play 模式靠眼睛看。

using System.Collections.Generic;
using Game.Core.Save;
using UnityEngine;

namespace Game.Core.Settings
{
    /// <summary>显示设置换算。纯函数，不碰任何 Unity 运行时状态。</summary>
    public static class DisplaySettingsMath
    {
        /// <summary>最低支持宽度。用户 2026-09-26 定：最低 1280×720。</summary>
        public const int MinWidth = 1280;

        /// <summary>最低支持高度。</summary>
        public const int MinHeight = 720;

        private static readonly int[] frameRateOptions = { 0, 30, 60, 120, 144, 240 };

        /// <summary>帧率上限的合法取值，0 = 不限。面板下拉框直接用这份。</summary>
        public static IReadOnlyList<int> FrameRateOptions => frameRateOptions;

        /// <summary>
        /// 把显示器报上来的分辨率整理成面板可用的列表：丢掉低于 1280×720 的、按宽高去重（不同刷新率算同一项）、
        /// 按宽再按高升序。整理完是空的（取不到或全都太小）时返回只含 <paramref name="fallback"/> 的列表。
        /// </summary>
        public static List<DisplayResolution> BuildResolutionList(IEnumerable<DisplayResolution> raw, DisplayResolution fallback)
        {
            List<DisplayResolution> result = new List<DisplayResolution>();
            if (raw != null)
            {
                HashSet<DisplayResolution> seen = new HashSet<DisplayResolution>();
                foreach (DisplayResolution resolution in raw)
                {
                    if (resolution.Width < MinWidth || resolution.Height < MinHeight)
                    {
                        continue;
                    }

                    if (seen.Add(resolution))
                    {
                        result.Add(resolution);
                    }
                }
            }

            result.Sort(Compare);
            if (result.Count == 0)
            {
                result.Add(fallback);
            }

            return result;
        }

        /// <summary>
        /// 把存档里的宽高对齐到列表：宽或高 ≤ 0（原生）、或列表里找不到（换了显示器）时返回 <paramref name="current"/>。
        /// </summary>
        public static DisplayResolution ResolveResolution(int width, int height, IReadOnlyList<DisplayResolution> available, DisplayResolution current)
        {
            if (width <= 0 || height <= 0 || available == null)
            {
                return current;
            }

            DisplayResolution wanted = new DisplayResolution(width, height);
            for (int i = 0; i < available.Count; i++)
            {
                if (available[i].Equals(wanted))
                {
                    return wanted;
                }
            }

            return current;
        }

        /// <summary>帧率上限合法化：只允许 0 / 30 / 60 / 120 / 144 / 240，其它值（含负数）回 0（不限）。</summary>
        public static int SanitizeFrameRate(int frameRate)
        {
            for (int i = 0; i < frameRateOptions.Length; i++)
            {
                if (frameRateOptions[i] == frameRate)
                {
                    return frameRate;
                }
            }

            return 0;
        }

        /// <summary>存档里的全屏模式合法化：只认 0（无边框全屏）/ 1（窗口化），其它回 0。</summary>
        public static int SanitizeFullScreenMode(int mode)
        {
            return mode == SettingsSaveData.FullScreenModeWindowed
                ? SettingsSaveData.FullScreenModeWindowed
                : SettingsSaveData.FullScreenModeBorderless;
        }

        /// <summary>存档取值 → Unity 全屏模式：0 → FullScreenWindow（无边框全屏），1 → Windowed，其它按 0 处理。</summary>
        public static FullScreenMode ToFullScreenMode(int mode)
        {
            return SanitizeFullScreenMode(mode) == SettingsSaveData.FullScreenModeWindowed
                ? FullScreenMode.Windowed
                : FullScreenMode.FullScreenWindow;
        }

        /// <summary>
        /// 最终要给 <c>Application.targetFrameRate</c> 的值：垂直同步开着时帧率交给显示器（-1），
        /// 关着时 0 = 不限（-1），其它按合法化后的上限。
        /// </summary>
        public static int ToTargetFrameRate(bool vSync, int frameRate)
        {
            if (vSync)
            {
                return -1;
            }

            int sanitized = SanitizeFrameRate(frameRate);
            return sanitized == 0 ? -1 : sanitized;
        }

        private static int Compare(DisplayResolution a, DisplayResolution b)
        {
            int byWidth = a.Width.CompareTo(b.Width);
            return byWidth != 0 ? byWidth : a.Height.CompareTo(b.Height);
        }
    }
}
