// 职责：钉住自动保存闸门的四个不存条件与待处理请求的合并 / 取走语义。
// 为什么新建：SaveGateRules 是 Session 模块新加的纯规则，按「测试类 = <被测类>Tests」单独成文件。

using Game.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode.Session
{
    public sealed class SaveGateRulesTests
    {
        [Test]
        public void CanSave_WhenGameplayAndNothingBlocking_ReturnsTrue()
        {
            Assert.That(SaveGateRules.CanSave(true, false, false, false), Is.True);
        }

        [Test]
        public void CanSave_WhenNotGameplay_ReturnsFalse()
        {
            Assert.That(SaveGateRules.CanSave(false, false, false, false), Is.False, "标题页 / 切换中不存");
        }

        [Test]
        public void CanSave_WhenDialogueRunning_ReturnsFalse()
        {
            Assert.That(SaveGateRules.CanSave(true, true, false, false), Is.False, "对白进行中不存");
        }

        [Test]
        public void CanSave_WhenPanelOpen_ReturnsFalse()
        {
            Assert.That(SaveGateRules.CanSave(true, false, true, false), Is.False, "面板 / 弹窗开着不存");
        }

        [Test]
        public void CanSave_WhenBattleResultPending_ReturnsFalse()
        {
            Assert.That(SaveGateRules.CanSave(true, false, false, true), Is.False, "战斗终局待消费不存");
        }

        [Test]
        public void Request_Twice_MergesIntoOneWithLatestReason()
        {
            SaveGateRules.PendingSave pending = default;
            Assert.That(pending.HasRequest, Is.False, "默认值 = 没有请求");

            pending = SaveGateRules.Request(pending, "quest_activated");
            pending = SaveGateRules.Request(pending, "crate");

            Assert.That(pending.HasRequest, Is.True);
            Assert.That(pending.Reason, Is.EqualTo("crate"), "合并后原因记最新一次");
        }

        [Test]
        public void Request_WithEmptyReason_KeepsPreviousReason()
        {
            SaveGateRules.PendingSave pending = SaveGateRules.Request(default, "dialogue");
            pending = SaveGateRules.Request(pending, null);

            Assert.That(pending.Reason, Is.EqualTo("dialogue"));
        }

        [Test]
        public void Take_ReturnsReasonAndClears()
        {
            SaveGateRules.PendingSave pending = SaveGateRules.Request(default, "scene");

            pending = SaveGateRules.Take(pending, out string reason);

            Assert.That(reason, Is.EqualTo("scene"));
            Assert.That(pending.HasRequest, Is.False);
        }

        [Test]
        public void Take_WhenNothingPending_ReturnsNullReason()
        {
            SaveGateRules.PendingSave pending = SaveGateRules.Take(default, out string reason);

            Assert.That(reason, Is.Null);
            Assert.That(pending.HasRequest, Is.False);
        }
    }
}
