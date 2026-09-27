// 职责：覆盖 DisplaySettingsMath 的四条规则——分辨率列表去重排序过滤、存档分辨率对齐、帧率合法化、全屏模式映射。
// 为什么新建：显示设置的换算没有现成测试；这些规则错了只会在玩家换显示器、手改档案时暴露，必须有 EditMode 测试守着。

using System.Collections.Generic;
using Game.Core.Save;
using Game.Core.Settings;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Core
{
    public sealed class DisplaySettingsMathTests
    {
        [Test]
        public void BuildResolutionList_DedupesSortsAndDropsBelowMinimum()
        {
            List<DisplayResolution> raw = new List<DisplayResolution>
            {
                new DisplayResolution(2560, 1440),
                new DisplayResolution(1920, 1080),
                new DisplayResolution(1920, 1080), // 同宽高不同刷新率
                new DisplayResolution(1024, 768),  // 宽不够
                new DisplayResolution(1280, 720),  // 刚好是下限
                new DisplayResolution(1920, 1200),
                new DisplayResolution(1600, 600),  // 高不够
            };

            List<DisplayResolution> result = DisplaySettingsMath.BuildResolutionList(raw, new DisplayResolution(1920, 1080));

            Assert.That(result, Is.EqualTo(new[]
            {
                new DisplayResolution(1280, 720),
                new DisplayResolution(1920, 1080),
                new DisplayResolution(1920, 1200),
                new DisplayResolution(2560, 1440),
            }));
        }

        [Test]
        public void BuildResolutionList_WhenNothingUsable_ContainsFallback()
        {
            DisplayResolution fallback = new DisplayResolution(1024, 768);

            Assert.That(DisplaySettingsMath.BuildResolutionList(null, fallback), Is.EqualTo(new[] { fallback }));
            Assert.That(
                DisplaySettingsMath.BuildResolutionList(new[] { new DisplayResolution(800, 600) }, fallback),
                Is.EqualTo(new[] { fallback }),
                "全被过滤掉时也要至少含当前分辨率，面板不能是空列表");
        }

        [Test]
        public void ResolveResolution_AlignsToListOrFallsBackToCurrent()
        {
            DisplayResolution current = new DisplayResolution(2560, 1440);
            DisplayResolution[] available = { new DisplayResolution(1280, 720), new DisplayResolution(1920, 1080), current };

            Assert.That(DisplaySettingsMath.ResolveResolution(1920, 1080, available, current), Is.EqualTo(new DisplayResolution(1920, 1080)));
            Assert.That(DisplaySettingsMath.ResolveResolution(0, 0, available, current), Is.EqualTo(current), "0/0 = 原生");
            Assert.That(DisplaySettingsMath.ResolveResolution(1920, 0, available, current), Is.EqualTo(current));
            Assert.That(DisplaySettingsMath.ResolveResolution(3840, 2160, available, current), Is.EqualTo(current), "换了显示器找不到时回当前");
        }

        [TestCase(0, 0)]
        [TestCase(30, 30)]
        [TestCase(60, 60)]
        [TestCase(120, 120)]
        [TestCase(144, 144)]
        [TestCase(240, 240)]
        [TestCase(75, 0)]
        [TestCase(-1, 0)]
        [TestCase(1000, 0)]
        public void SanitizeFrameRate_OnlyAllowsKnownOptions(int input, int expected)
        {
            Assert.That(DisplaySettingsMath.SanitizeFrameRate(input), Is.EqualTo(expected));
        }

        [Test]
        public void ToTargetFrameRate_VSyncOrUnlimitedMapsToMinusOne()
        {
            Assert.That(DisplaySettingsMath.ToTargetFrameRate(true, 60), Is.EqualTo(-1), "垂直同步开着时帧率交给显示器");
            Assert.That(DisplaySettingsMath.ToTargetFrameRate(false, 0), Is.EqualTo(-1));
            Assert.That(DisplaySettingsMath.ToTargetFrameRate(false, 144), Is.EqualTo(144));
            Assert.That(DisplaySettingsMath.ToTargetFrameRate(false, 75), Is.EqualTo(-1), "非法值按不限处理");
        }

        [Test]
        public void ToFullScreenMode_MapsZeroToBorderlessAndOneToWindowed()
        {
            Assert.That(DisplaySettingsMath.ToFullScreenMode(SettingsSaveData.FullScreenModeBorderless), Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(DisplaySettingsMath.ToFullScreenMode(SettingsSaveData.FullScreenModeWindowed), Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(DisplaySettingsMath.ToFullScreenMode(7), Is.EqualTo(FullScreenMode.FullScreenWindow), "非法值按无边框全屏处理");
            Assert.That(DisplaySettingsMath.SanitizeFullScreenMode(-3), Is.EqualTo(SettingsSaveData.FullScreenModeBorderless));
        }

        [Test]
        public void DisplayResolution_ToStringUsesMultiplicationSign()
        {
            Assert.That(new DisplayResolution(1920, 1080).ToString(), Is.EqualTo("1920×1080"));
        }
    }
}
