// 职责：验证三店正式奖励、旧档补发、防重复发放和三件套缺件门槛。
// 现有店内测试不经过背包映射，故单独覆盖组合模块的奖励规则。
using System.Collections.Generic;
using Game.Forging;
using Game.ShopPlaytest;
using NUnit.Framework;
namespace Game.Tests.EditMode.ShopPlaytest
{
    public sealed class ShopRewardRulesTests
    {
        [Test]
        public void CompletedStores_GrantExactItemsOnceEvenAfterLocalRetry()
        {
            var progress = new ShopProgressSaveData { TheaterCompleted = true, ClothesReceived = true,
                Forging = new ForgingProgressData { Submitted = true } };
            var items = new Dictionary<int, int>();
            System.Func<int, int, bool> grant = (id, count) => { items[id] = items.ContainsKey(id) ? items[id] + count : count; return true; };
            ShopRewardRules.GrantCompleted(progress, grant);
            ShopRewardRules.GrantCompleted(progress, grant);
            Assert.That(items.Count, Is.EqualTo(5));
            Assert.That(items[ShopRewardRules.CostumeId], Is.EqualTo(1));
            Assert.That(items[ShopRewardRules.ClothesId], Is.EqualTo(1));
            Assert.That(items[ShopRewardRules.ClaspId], Is.EqualTo(1));
            Assert.That(items[ShopRewardRules.BeltHookId], Is.EqualTo(2));
            Assert.That(items[ShopRewardRules.RingId], Is.EqualTo(3));
            progress.TheaterCompleted = false;
            ShopRewardRules.GrantCompleted(progress, grant);
            progress.TheaterCompleted = true;
            ShopRewardRules.GrantCompleted(progress, grant);
            Assert.That(items[ShopRewardRules.CostumeId], Is.EqualTo(1));
            Assert.That(ShopRewardRules.MissingDisguise(items), Is.Empty);
            items[ShopRewardRules.RingId] = 2;
            Assert.That(ShopRewardRules.MissingDisguise(items), Is.EqualTo("金饰"));
        }

        [Test]
        public void FailedGrant_DoesNotMarkPaidAndCanRetry()
        {
            var progress = new ShopProgressSaveData { ClothesReceived = true };
            ShopRewardRules.GrantCompleted(progress, (id, count) => false);
            Assert.That(progress.RewardedItems, Is.Empty);
            ShopRewardRules.GrantCompleted(progress, (id, count) => true);
            Assert.That(progress.RewardedItems, Is.EqualTo(new[] { ShopRewardRules.ClothesId }));
        }

        [Test]
        public void FreshSlot_HasNoRewardsAndMissingAllDisguiseParts()
        {
            var progress = new ShopProgressSaveData();
            int calls = 0;
            ShopRewardRules.GrantCompleted(progress, (id, count) => { calls++; return true; });
            Assert.That(calls, Is.Zero);
            Assert.That(ShopRewardRules.MissingDisguise(new Dictionary<int, int>()), Is.EqualTo("行头三样、黑色长衫、金饰"));
        }

        [Test]
        public void MigratedAndSerializedProgress_KeepsOneTimeRewardLedger()
        {
            var old = new ShopProgressSaveData { ClothesReceived = true };
            old.Migrate(2);
            int grants = 0;
            System.Func<int, int, bool> grant = (id, count) => { grants++; return true; };
            ShopRewardRules.GrantCompleted(old, grant);
            var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<ShopProgressSaveData>(Newtonsoft.Json.JsonConvert.SerializeObject(old));
            ShopRewardRules.GrantCompleted(loaded, grant);
            Assert.That(grants, Is.EqualTo(1));
            Assert.That(loaded.ClothesReceived, Is.True);
        }

        [TestCase(ShopRewardRules.CostumeId, "行头三样")]
        [TestCase(ShopRewardRules.ClothesId, "黑色长衫")]
        [TestCase(ShopRewardRules.ClaspId, "金襟扣")]
        [TestCase(ShopRewardRules.BeltHookId, "金带钩")]
        [TestCase(ShopRewardRules.RingId, "金环扣")]
        public void RewardItem_IsPresentInGeneratedTableAndInventory(int id, string name)
        {
            var table = Game.Core.Config.ConfigService.BuildTables(Game.Tests.EditMode.Core.ConfigServiceTests.ReadAllTableBytes()).TbItem;
            var entries = new List<Game.Inventory.InventoryEntry>();
            Game.Inventory.InventoryRules.Build(new Dictionary<int, int> { { id, 1 } }, table, Game.Inventory.InventoryFilter.Items, entries);
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Name, Is.EqualTo(name));
            Assert.That(entries[0].Category, Is.EqualTo(global::cfg.EItemCategory.Key));
        }
    }
}
