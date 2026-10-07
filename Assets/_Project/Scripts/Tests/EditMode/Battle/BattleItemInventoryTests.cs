// 职责：钉住战斗背包口适配器——string ↔ int id 换算、只认消耗品、未拥有 / 已拥有 / 已用过三种情况的判定与道具格、用一次扣 1。
// 为什么新建：BattleItemInventory 是本波新增的跨模块适配器（turnbased-module-guide「接线清单·背包口」）；一个被测类一个测试类。
//   背包用 BattleFakes.Backpack（int id），账本与会话用真实 TurnBased 类型。
using System.Collections.Generic;
using Game.Battle;
using Game.Core.Simulation;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleItemInventoryTests
    {
        private const int Potion = 1004;
        private const int Sword = 1002;
        private const int Herb = 1006;

        private BattleFakes.Backpack backpack;
        private BattleItemInventory inventory;

        [SetUp]
        public void SetUp()
        {
            backpack = new BattleFakes.Backpack();
            inventory = new BattleItemInventory(backpack);
        }

        [Test]
        public void Owns_OwnedConsumable_IsTrue()
        {
            backpack.Add(Potion, 2, consumable: true);

            Assert.That(inventory.Owns("1004"), Is.True);
        }

        [Test]
        public void Owns_NotOwnedOrZeroOrNotConsumable_IsFalse()
        {
            // 负对照：没有、件数 0、不是消耗品（剑）、id 写坏，一律当「未拥有」（07:42 不显示）。
            backpack.Add(Herb, 0, consumable: true).Add(Sword, 1, consumable: false);

            Assert.That(inventory.Owns("1004"), Is.False);
            Assert.That(inventory.Owns("1006"), Is.False);
            Assert.That(inventory.Owns("1002"), Is.False);
            Assert.That(inventory.Owns("item.yao"), Is.False);
            Assert.That(inventory.Owns(" 1004"), Is.False);
            Assert.That(inventory.Owns("-1004"), Is.False);
            Assert.That(inventory.Owns(null), Is.False);
        }

        [Test]
        public void IdConversion_RoundTripsInvariantDecimal()
        {
            Assert.That(BattleItemInventory.ToKey(Potion), Is.EqualTo("1004"));
            Assert.That(BattleItemInventory.TryParse("1004", out int id), Is.True);
            Assert.That(id, Is.EqualTo(Potion));
            Assert.That(BattleItemInventory.TryParse("0", out _), Is.False, "tbitem 主键从 1 起");
        }

        [Test]
        public void ListSlots_OwnedConsumablesOnly_SortedById()
        {
            backpack.Add(Herb, 3, consumable: true).Add(Potion, 2, consumable: true).Add(Sword, 1, consumable: false);
            var slots = new List<BattleItemSlot>();

            inventory.ListSlots(new BattleItemLedger(true), slots);

            Assert.That(slots.Count, Is.EqualTo(2), "剑不是消耗品，不进道具格");
            Assert.That(slots[0].ItemId, Is.EqualTo("1004"));
            Assert.That(slots[0].Count, Is.EqualTo(2));
            Assert.That(slots[0].CanUse, Is.True);
            Assert.That(slots[1].ItemId, Is.EqualTo("1006"));
            Assert.That(slots[1].DisplayName, Is.EqualTo("道具1006"));
        }

        [Test]
        public void ThreeCases_NotOwnedOwnedUsed_InASession()
        {
            // 未拥有 → 不显示、规则拒 NotOwned；已拥有 → 高亮、能用、扣 1；已用过（背包里还剩）→ 置暗、规则拒 UsedThisBattle。
            backpack.Add(Potion, 2, consumable: true);
            BattleSession session = StartSession();
            var slots = new List<BattleItemSlot>();

            Assert.That(session.TryUseItem("1006").Reject, Is.EqualTo(ItemUseReject.NotOwned), "未拥有");
            inventory.ListSlots(session.Items, slots);
            Assert.That(slots.Exists(s => s.ItemId == "1006"), Is.False, "未拥有不显示");
            Assert.That(slots.Find(s => s.ItemId == "1004").CanUse, Is.True, "已拥有：高亮");

            Assert.That(session.TryUseItem("1004").Allowed, Is.True, "已拥有：能用");
            Assert.That(inventory.TryConsume("1004"), Is.True);
            Assert.That(backpack.CountOf(Potion), Is.EqualTo(1), "用一次扣 1");

            inventory.ListSlots(session.Items, slots);
            BattleItemSlot used = slots.Find(s => s.ItemId == "1004");
            Assert.That(used.UsedThisBattle, Is.True, "已用过：置暗");
            Assert.That(used.Count, Is.EqualTo(1));
            Assert.That(used.CanUse, Is.False);
            Assert.That(session.TryUseItem("1004").Reject, Is.EqualTo(ItemUseReject.UsedThisBattle), "已用过：被拒");
        }

        [Test]
        public void ListSlots_UsedUpThisBattle_StillShownDimmed()
        {
            // 最后一件用掉（扣到 0）：本场仍显示、置暗（07:42「用过了则置暗」），而不是「未拥有则不显示」；
            // 规则侧此时先判「未拥有」（ItemUseRules 的判定顺序），同样不能再用。
            backpack.Add(Potion, 1, consumable: true);
            BattleSession session = StartSession();
            var slots = new List<BattleItemSlot>();
            session.TryUseItem("1004");
            inventory.TryConsume("1004");

            inventory.ListSlots(session.Items, slots);

            BattleItemSlot used = slots.Find(s => s.ItemId == "1004");
            Assert.That(used.ItemId, Is.EqualTo("1004"), "仍在道具格里");
            Assert.That(used.Count, Is.EqualTo(0));
            Assert.That(used.UsedThisBattle, Is.True);
            Assert.That(used.CanUse, Is.False);
            Assert.That(session.TryUseItem("1004").Allowed, Is.False);
        }

        [Test]
        public void TryConsume_NotOwnedOrBadId_IsFalseAndBackpackUntouched()
        {
            backpack.Add(Potion, 1, consumable: true);

            Assert.That(inventory.TryConsume("1006"), Is.False);
            Assert.That(inventory.TryConsume("abc"), Is.False);
            Assert.That(backpack.CountOf(Potion), Is.EqualTo(1));
        }

        private BattleSession StartSession()
        {
            var kernel = new TurnBasedKernel(BattleSettings.PlaceholderDefault, new XorShiftRandomStream(1UL), inventory);
            Assert.That(kernel.TryStartBattle(
                new BattleEntryRequest(false, false, true, false, MonsterAlertState.Alert, BattleInitiator.PlayerAttacked),
                new PlayerBattleSnapshot(10, 10), new BossBattleSnapshot(10, 10, 0), out BattleSession session, out _), Is.True);
            return session;
        }
    }
}
