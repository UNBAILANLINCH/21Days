// 职责：ItemUseRules / ItemUseSpec 的 EditMode 回归——命中（回效果标识与消耗件数）、未知 id、件数不够、
//   使用表那一行写坏、空 / null 表，以及「判定不改背包」这条纯规则约束。
// 为什么新建：对应被测类一个测试类（unity-tests.md）；使用是独立于展示与合成的一件事。
//   使用表与效果标识都手搓：效果标识现在还没有白名单（05_皮面具与道具.md:219「使用要不要按键、从哪用」未定），
//   被测的是「查得到 → 报标识；查不到 → 明确失败」这条规则，不依赖任何具体标识。

using System.Collections.Generic;
using Game.Inventory;
using NUnit.Framework;

namespace Game.Tests.EditMode.Inventory
{
    public sealed class ItemUseRulesTests
    {
        // 与 xlsx 里新增的示例行对齐：1101 普通皮（一次性）、1103 空白皮（持有型，用出去即借身份）。
        private const int CommonSkin = 1101;
        private const int BlankSkin = 1103;
        private const int UnknownId = 9999;

        private List<ItemUseSpec> specs;

        [SetUp]
        public void SetUp()
        {
            specs = new List<ItemUseSpec>
            {
                new ItemUseSpec(CommonSkin, "shop.enter_with_skin"),
                new ItemUseSpec(BlankSkin, "disguise.blank_skin"),
            };
        }

        [Test]
        public void TryUse_WhenTheItemIsUsableAndOwned_ReturnsOkWithTheEffectId()
        {
            var items = new Dictionary<int, int> { { CommonSkin, 2 } };

            ItemUseResult result = ItemUseRules.TryUse(items, CommonSkin, specs);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.Ok));
            Assert.That(result.IsOk, Is.True);
            Assert.That(result.EffectId, Is.EqualTo("shop.enter_with_skin"));
            Assert.That(result.ConsumeCount, Is.EqualTo(1), "默认用一次扣一件");
            Assert.That(result.OwnedCount, Is.EqualTo(2));
        }

        [Test]
        public void TryUse_WhenTheSpecConsumesMoreThanOne_ReportsItsConsumeCount()
        {
            var items = new Dictionary<int, int> { { BlankSkin, 3 } };
            var triple = new List<ItemUseSpec> { new ItemUseSpec(BlankSkin, "disguise.blank_skin", 3) };

            ItemUseResult result = ItemUseRules.TryUse(items, BlankSkin, triple);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.Ok));
            Assert.That(result.ConsumeCount, Is.EqualTo(3));
        }

        [Test]
        public void TryUse_WhenTheItemIdIsUnknown_ReturnsUnknownItem()
        {
            var items = new Dictionary<int, int> { { UnknownId, 5 } };

            ItemUseResult result = ItemUseRules.TryUse(items, UnknownId, specs);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.UnknownItem));
            Assert.That(result.IsOk, Is.False);
            Assert.That(result.EffectId, Is.Empty, "未知 id 不该回出一个效果标识");
            Assert.That(result.ConsumeCount, Is.Zero);
            Assert.That(result.OwnedCount, Is.Zero);
        }

        [Test]
        public void TryUse_WhenTheItemIdIsNotPositive_ReturnsUnknownItem()
        {
            Assert.That(ItemUseRules.TryUse(new Dictionary<int, int>(), 0, specs).Status, Is.EqualTo(ItemUseStatus.UnknownItem));
            Assert.That(ItemUseRules.TryUse(new Dictionary<int, int>(), -1, specs).Status, Is.EqualTo(ItemUseStatus.UnknownItem));
        }

        [Test]
        public void TryUse_WhenTheSpecListIsEmptyOrNull_ReturnsUnknownItem()
        {
            var items = new Dictionary<int, int> { { CommonSkin, 1 } };

            Assert.That(ItemUseRules.TryUse(items, CommonSkin, new List<ItemUseSpec>()).Status, Is.EqualTo(ItemUseStatus.UnknownItem));
            Assert.That(ItemUseRules.TryUse(items, CommonSkin, null).Status, Is.EqualTo(ItemUseStatus.UnknownItem));
        }

        [Test]
        public void TryUse_WhenOwnedIsBelowTheConsumeCount_ReturnsNotEnoughWithTheOwnedCount()
        {
            var items = new Dictionary<int, int> { { CommonSkin, 0 } };
            var twoEach = new List<ItemUseSpec> { new ItemUseSpec(CommonSkin, "shop.enter_with_skin", 2) };

            ItemUseResult result = ItemUseRules.TryUse(items, CommonSkin, twoEach);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.NotEnough));
            Assert.That(result.OwnedCount, Is.Zero);
            Assert.That(result.EffectId, Is.EqualTo("shop.enter_with_skin"), "件数不够时仍要能报出「差的是什么效果」，方便提示");
        }

        [Test]
        public void TryUse_WhenTheBackpackDictionaryIsNull_ReturnsNotEnough()
        {
            ItemUseResult result = ItemUseRules.TryUse(null, CommonSkin, specs);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.NotEnough));
            Assert.That(result.OwnedCount, Is.Zero);
        }

        [Test]
        public void TryUse_WhenTheOwnedEntryIsAbsent_ReturnsNotEnough()
        {
            var items = new Dictionary<int, int> { { BlankSkin, 1 } };

            ItemUseResult result = ItemUseRules.TryUse(items, CommonSkin, specs);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.NotEnough));
        }

        [Test]
        public void TryUse_WhenTheSpecHasNoEffectId_ReturnsInvalidSpec()
        {
            var broken = new List<ItemUseSpec> { new ItemUseSpec(CommonSkin, string.Empty) };

            ItemUseResult result = ItemUseRules.TryUse(new Dictionary<int, int> { { CommonSkin, 1 } }, CommonSkin, broken);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.InvalidSpec));
            Assert.That(result.IsOk, Is.False);
        }

        [Test]
        public void TryUse_WhenTheSpecConsumeCountIsNotPositive_ReturnsInvalidSpec()
        {
            var broken = new List<ItemUseSpec> { new ItemUseSpec(CommonSkin, "shop.enter_with_skin", 0) };

            ItemUseResult result = ItemUseRules.TryUse(new Dictionary<int, int> { { CommonSkin, 1 } }, CommonSkin, broken);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.InvalidSpec));
        }

        [Test]
        public void TryUse_DoesNotTouchTheBackpackDictionary()
        {
            var items = new Dictionary<int, int> { { CommonSkin, 2 } };

            ItemUseResult result = ItemUseRules.TryUse(items, CommonSkin, specs);

            Assert.That(result.Status, Is.EqualTo(ItemUseStatus.Ok));
            Assert.That(items[CommonSkin], Is.EqualTo(2), "判定不扣件数：执行与落盘归调用方（ItemUseRules 文件头）");
            Assert.That(items.Count, Is.EqualTo(1));
        }

        [Test]
        public void ItemUseSpec_WithPositiveFields_IsValid()
        {
            Assert.That(new ItemUseSpec(BlankSkin, "disguise.blank_skin").IsValid, Is.True);
            Assert.That(new ItemUseSpec(0, "disguise.blank_skin").IsValid, Is.False);
            Assert.That(new ItemUseSpec(BlankSkin, null).IsValid, Is.False);
            Assert.That(new ItemUseSpec(BlankSkin, "disguise.blank_skin", -1).IsValid, Is.False);
        }
    }
}
