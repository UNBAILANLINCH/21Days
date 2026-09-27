// 职责：覆盖 SettingsView 的纯函数——分辨率下拉框预选下标（原生分辨率由参数传入，不读 Unity 显示 API）。
// 为什么新建：FindResolutionIndex 抽成静态纯函数后（code review 2026-09-26）还没有测试；预选错了玩家一打开设置就看到错误的分辨率。

using Game.Core.Settings;
using Game.Core.UI.Views;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    public sealed class SettingsViewTests
    {
        private static readonly DisplayResolution[] List =
        {
            new DisplayResolution(1280, 720),
            new DisplayResolution(1920, 1080),
            new DisplayResolution(2560, 1440),
        };

        [Test]
        public void FindResolutionIndex_WhenExactMatch_SelectsIt()
        {
            Assert.That(SettingsView.FindResolutionIndex(List, 1280, 720, new DisplayResolution(2560, 1440)), Is.EqualTo(0));
        }

        [Test]
        public void FindResolutionIndex_WhenZeroByZero_SelectsNative()
        {
            Assert.That(SettingsView.FindResolutionIndex(List, 0, 0, new DisplayResolution(1920, 1080)), Is.EqualTo(1), "0×0 = 跟随原生");
        }

        [Test]
        public void FindResolutionIndex_WhenNotFound_FallsBackToNativeThenLast()
        {
            Assert.That(SettingsView.FindResolutionIndex(List, 3840, 2160, new DisplayResolution(1920, 1080)), Is.EqualTo(1), "找不到先选原生");
            Assert.That(SettingsView.FindResolutionIndex(List, 3840, 2160, new DisplayResolution(3440, 1440)), Is.EqualTo(2), "原生也不在列表里选最后一项");
        }

        [Test]
        public void FindResolutionIndex_WhenListEmpty_ReturnsZero()
        {
            Assert.That(SettingsView.FindResolutionIndex(new DisplayResolution[0], 0, 0, new DisplayResolution(1920, 1080)), Is.EqualTo(0));
        }
    }
}
