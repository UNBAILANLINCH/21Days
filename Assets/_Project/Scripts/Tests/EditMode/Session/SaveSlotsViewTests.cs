// 职责：钉住选槽面板的纯格式化与判定——游玩时长 mm:ss、行文案三态、标题、行可选与删除可见。
// 为什么新建：这些是 SaveSlotsView 上的静态纯函数，不需要预制体就能测；按「测试类 = <被测类>Tests」单独成文件。

using System;
using System.Globalization;
using Game.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode.Session
{
    public sealed class SaveSlotsViewTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 9, 26, 3, 4, 0, DateTimeKind.Utc);

        [TestCase(0f, "00:00")]
        [TestCase(59.9f, "00:59")]
        [TestCase(61f, "01:01")]
        [TestCase(3725f, "62:05")]
        [TestCase(-5f, "00:00")]
        public void FormatPlaytime_FormatsMinutesAndSeconds(float seconds, string expected)
        {
            Assert.That(SaveSlotsView.FormatPlaytime(seconds), Is.EqualTo(expected));
        }

        [Test]
        public void FormatPlaytime_WhenNaNOrInfinity_ReturnsZero()
        {
            Assert.That(SaveSlotsView.FormatPlaytime(float.NaN), Is.EqualTo("00:00"));
            Assert.That(SaveSlotsView.FormatPlaytime(float.PositiveInfinity), Is.EqualTo("00:00"));
        }

        [Test]
        public void FormatPrimary_ThreeStates()
        {
            var available = new SlotInfo(1, SlotState.Available, SavedAt, 10f, "寻找失踪的猫");
            Assert.That(SaveSlotsView.FormatPrimary(available), Is.EqualTo("寻找失踪的猫"));
            Assert.That(SaveSlotsView.FormatPrimary(SlotInfo.Empty(2)), Is.EqualTo("新游戏"));
            Assert.That(SaveSlotsView.FormatPrimary(SlotInfo.Unavailable(3)), Is.EqualTo("不可用"));
        }

        [Test]
        public void FormatPrimary_WhenAvailableWithoutProgress_ShowsPlaceholder()
        {
            var info = new SlotInfo(1, SlotState.Available, SavedAt, 0f, null);
            Assert.That(SaveSlotsView.FormatPrimary(info), Is.EqualTo("进度未知"));
        }

        [Test]
        public void FormatSecondary_WhenAvailable_ShowsLocalTimeAndPlaytime()
        {
            var info = new SlotInfo(1, SlotState.Available, SavedAt, 125f, "进度");
            string expectedTime = SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            Assert.That(SaveSlotsView.FormatSecondary(info), Is.EqualTo(expectedTime + " · 游玩时长 02:05"));
        }

        [Test]
        public void FormatSecondary_WhenEmptyOrUnavailable_ReturnsEmpty()
        {
            Assert.That(SaveSlotsView.FormatSecondary(SlotInfo.Empty(1)), Is.Empty);
            Assert.That(SaveSlotsView.FormatSecondary(SlotInfo.Unavailable(2)), Is.Empty);
        }

        [Test]
        public void FormatTitleAndSlotLabel()
        {
            Assert.That(SaveSlotsView.FormatTitle(SlotsMode.Load), Is.EqualTo("选择存档"));
            Assert.That(SaveSlotsView.FormatTitle(SlotsMode.NewGame), Is.EqualTo("新游戏"));
            Assert.That(SaveSlotsView.FormatSlotLabel(3), Is.EqualTo("槽 3"));
        }

        [Test]
        public void IsRowSelectable_LoadModeOnlyAvailable_NewGameModeAll()
        {
            Assert.That(SaveSlotsView.IsRowSelectable(SlotState.Available, SlotsMode.Load), Is.True);
            Assert.That(SaveSlotsView.IsRowSelectable(SlotState.Empty, SlotsMode.Load), Is.False);
            Assert.That(SaveSlotsView.IsRowSelectable(SlotState.Unavailable, SlotsMode.Load), Is.False, "坏槽读档模式不可选");
            Assert.That(SaveSlotsView.IsRowSelectable(SlotState.Empty, SlotsMode.NewGame), Is.True);
            Assert.That(SaveSlotsView.IsRowSelectable(SlotState.Available, SlotsMode.NewGame), Is.True);
            Assert.That(SaveSlotsView.IsRowSelectable(SlotState.Unavailable, SlotsMode.NewGame), Is.True, "新游戏模式允许覆盖坏槽");
        }

        [Test]
        public void IsDeleteVisible_OnlyAvailable()
        {
            Assert.That(SaveSlotsView.IsDeleteVisible(SlotState.Available), Is.True);
            Assert.That(SaveSlotsView.IsDeleteVisible(SlotState.Empty), Is.False);
            Assert.That(SaveSlotsView.IsDeleteVisible(SlotState.Unavailable), Is.False);
        }
    }
}
