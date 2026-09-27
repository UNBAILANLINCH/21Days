// 职责：钉住标题路由的两条纯判定——新游戏落第一个空槽 / 无空槽返回 0、「继续」按最近槽启用。
// 为什么新建：SessionTitleRules 是 Session 模块新加的纯规则，按「测试类 = <被测类>Tests」单独成文件。

using System;
using System.Collections.Generic;
using Game.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode.Session
{
    public sealed class SessionTitleRulesTests
    {
        private static SlotInfo Available(int slot) =>
            new SlotInfo(slot, SlotState.Available, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc), 10f, "进度");

        [Test]
        public void PickNewGameSlot_WhenSomeEmpty_ReturnsFirstEmptyInOrder()
        {
            var infos = new List<SlotInfo> { Available(1), SlotInfo.Unavailable(2), SlotInfo.Empty(3), SlotInfo.Empty(4) };
            Assert.That(SessionTitleRules.PickNewGameSlot(infos), Is.EqualTo(3));
        }

        [Test]
        public void PickNewGameSlot_WhenAllEmpty_ReturnsSlotOne()
        {
            var infos = new List<SlotInfo> { SlotInfo.Empty(1), SlotInfo.Empty(2), SlotInfo.Empty(3) };
            Assert.That(SessionTitleRules.PickNewGameSlot(infos), Is.EqualTo(1));
        }

        [Test]
        public void PickNewGameSlot_WhenNoEmpty_ReturnsZero()
        {
            var infos = new List<SlotInfo> { Available(1), SlotInfo.Unavailable(2), Available(3) };
            Assert.That(SessionTitleRules.PickNewGameSlot(infos), Is.EqualTo(0), "坏槽不算空槽，没有空槽要开选槽面板");
        }

        [Test]
        public void PickNewGameSlot_WhenNullOrEmptyList_ReturnsZero()
        {
            Assert.That(SessionTitleRules.PickNewGameSlot(null), Is.EqualTo(0));
            Assert.That(SessionTitleRules.PickNewGameSlot(new List<SlotInfo>()), Is.EqualTo(0));
        }

        [Test]
        public void ShouldShowContinue_WhenLatestSlotZero_ReturnsFalse()
        {
            Assert.That(SessionTitleRules.ShouldShowContinue(0), Is.False);
        }

        [Test]
        public void ShouldShowContinue_WhenLatestSlotPositive_ReturnsTrue()
        {
            Assert.That(SessionTitleRules.ShouldShowContinue(2), Is.True);
        }
    }
}
