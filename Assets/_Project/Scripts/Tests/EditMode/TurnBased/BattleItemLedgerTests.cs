// 职责：道具账本（本场哪些用过了）与背包接口注入的 EditMode 测试。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:42`、:58；
// 背包不归本模块（`docs/design/features-spotlight/09_BOSS战.md:276` 划给 Loot / Inventory），
// 所以「有没有」必须走注入，这里连注入口一起验。

using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BattleItemLedgerTests
    {
        [Test]
        public void TryUse_FirstTime_SucceedsAnd_SecondTimeIsRefused()
        {
            var ledger = new BattleItemLedger(true);

            Assert.That(ledger.TryUse("item.yao", true).Allowed, Is.True);
            Assert.That(ledger.IsUsed("item.yao"), Is.True);
            Assert.That(ledger.UsedCount, Is.EqualTo(1));

            // 负对照：同一场里第二次用同一件道具被拒。
            ItemUseDecision second = ledger.TryUse("item.yao", true);
            Assert.That(second.Allowed, Is.False);
            Assert.That(second.Reject, Is.EqualTo(ItemUseReject.UsedThisBattle));
            Assert.That(ledger.UsedCount, Is.EqualTo(1));
        }

        [Test]
        public void TryUse_NotOwned_RefusesAndDoesNotMarkAsUsed()
        {
            // 负对照：没有这件道具时不能记账，否则玩家捡到之后就用不了了。
            var ledger = new BattleItemLedger(true);

            ItemUseDecision refused = ledger.TryUse("item.yao", false);
            Assert.That(refused.Reject, Is.EqualTo(ItemUseReject.NotOwned));
            Assert.That(ledger.IsUsed("item.yao"), Is.False);

            Assert.That(ledger.TryUse("item.yao", true).Allowed, Is.True);
        }

        [Test]
        public void TryUse_DifferentItems_AreIndependent()
        {
            var ledger = new BattleItemLedger(true);

            Assert.That(ledger.TryUse("item.yao", true).Allowed, Is.True);
            Assert.That(ledger.TryUse("item.jinlongdan", true).Allowed, Is.True);
            Assert.That(ledger.UsedCount, Is.EqualTo(2));
        }

        [Test]
        public void TryUse_EmptyId_IsRefused()
        {
            // 负对照：空 id 不许静默当成一件道具。
            var ledger = new BattleItemLedger(true);

            Assert.That(ledger.TryUse(null, true).Reject, Is.EqualTo(ItemUseReject.InvalidItem));
            Assert.That(ledger.TryUse(string.Empty, true).Reject, Is.EqualTo(ItemUseReject.InvalidItem));
            Assert.That(ledger.UsedCount, Is.EqualTo(0));
        }

        [Test]
        public void CanUse_DoesNotMarkAsUsed()
        {
            var ledger = new BattleItemLedger(true);

            Assert.That(ledger.CanUse("item.yao", true).Allowed, Is.True);
            Assert.That(ledger.IsUsed("item.yao"), Is.False, "只判定不该记账");
        }

        [Test]
        public void Clear_AllowsTheItemAgain()
        {
            var ledger = new BattleItemLedger(true);
            ledger.TryUse("item.yao", true);

            ledger.Clear();

            Assert.That(ledger.UsedCount, Is.EqualTo(0));
            Assert.That(ledger.TryUse("item.yao", true).Allowed, Is.True);
        }

        [Test]
        public void TryUse_WithInventory_AsksTheInventoryAndMarksTheUse()
        {
            var inventory = new FakeBattleItemInventory("item.yao");
            var ledger = new BattleItemLedger(true);

            Assert.That(ledger.TryUse("item.yao", inventory).Allowed, Is.True);
            Assert.That(inventory.QueryCount, Is.EqualTo(1));
            Assert.That(ledger.IsUsed("item.yao"), Is.True);

            // 负对照：背包里没有的道具被拒。
            Assert.That(ledger.TryUse("item.jinlongdan", inventory).Reject, Is.EqualTo(ItemUseReject.NotOwned));
        }

        [Test]
        public void TryUse_NullInventory_TreatsEverythingAsNotOwned()
        {
            // 负对照：没接背包时宁可「什么都没有」，也不要默认送道具。
            var ledger = new BattleItemLedger(true);

            Assert.That(ledger.TryUse("item.yao", null).Reject, Is.EqualTo(ItemUseReject.NotOwned));
        }

        [Test]
        public void TryUse_OncePerBattleDisabled_AllowsRepeatedUse()
        {
            var ledger = new BattleItemLedger(false);

            Assert.That(ledger.TryUse("item.yao", true).Allowed, Is.True);
            Assert.That(ledger.TryUse("item.yao", true).Allowed, Is.True);
        }
    }
}
