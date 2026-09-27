// 职责：InventoryRules 的 EditMode 回归——排序（类别再 id）、三档筛选、表里查不到时的「#id」占位、空字典 / 空表。
// 为什么新建：对应被测类一个测试类；背包模块首次落地，没有可扩展的测试文件。
//   物品表走真实生成物（复用 ConfigServiceTests.ReadAllTableBytes），Luban 的 cfg.Item 只能从字节流构造，手搓不划算。

using System.Collections.Generic;
using Game.Core.Config;
using Game.Inventory;
using Game.Tests.EditMode.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Inventory
{
    public sealed class InventoryRulesTests
    {
        private global::cfg.TbItem table;
        private List<InventoryEntry> result;

        [SetUp]
        public void SetUp()
        {
            table = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()).TbItem;
            result = new List<InventoryEntry>();
        }

        [Test]
        public void Build_WithMixedCategories_SortsByCategoryThenId()
        {
            // 1006 关键物、1005 线索、1004 消耗品、1002 / 1001 材料，插入顺序故意打乱。
            var items = new Dictionary<int, int> { { 1006, 1 }, { 1005, 1 }, { 1004, 2 }, { 1002, 1 }, { 1001, 1 } };

            InventoryRules.Build(items, table, InventoryFilter.All, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1001, 1002, 1004, 1005, 1006 }));
            Assert.That(result[2].Count, Is.EqualTo(2));
            Assert.That(result[2].Name, Is.EqualTo("治疗药水"));
        }

        [Test]
        public void Build_WithItemsFilter_KeepsMaterialConsumableAndKey()
        {
            var items = new Dictionary<int, int> { { 1001, 1 }, { 1004, 1 }, { 1005, 1 }, { 1006, 1 } };

            InventoryRules.Build(items, table, InventoryFilter.Items, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1001, 1004, 1006 }));
        }

        [Test]
        public void Build_WithCluesFilter_KeepsOnlyClues()
        {
            var items = new Dictionary<int, int> { { 1001, 1 }, { 1005, 1 }, { 1006, 1 } };

            InventoryRules.Build(items, table, InventoryFilter.Clues, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1005 }));
            Assert.That(result[0].Desc, Does.Contain("镜"));
            Assert.That(result[0].Category, Is.EqualTo(global::cfg.EItemCategory.Clue));
        }

        [Test]
        public void Build_WhenIdMissingFromTable_UsesHashPlaceholderAndSortsLast()
        {
            var items = new Dictionary<int, int> { { 9999, 3 }, { 1006, 1 } };

            InventoryRules.Build(items, table, InventoryFilter.All, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1006, 9999 }));
            Assert.That(result[1].Name, Is.EqualTo("#9999"));
            Assert.That(result[1].Category, Is.EqualTo(InventoryRules.UnknownCategory));
            Assert.That(result[1].Desc, Is.Empty);
        }

        [Test]
        public void Build_WhenIdMissingFromTable_OnlyShowsUnderAll()
        {
            var items = new Dictionary<int, int> { { 9999, 1 } };

            InventoryRules.Build(items, table, InventoryFilter.Items, result);
            Assert.That(result, Is.Empty, "物品档");

            InventoryRules.Build(items, table, InventoryFilter.Clues, result);
            Assert.That(result, Is.Empty, "线索档");
        }

        [Test]
        public void Build_WhenTableIsNull_ShowsEveryIdAsPlaceholder()
        {
            var items = new Dictionary<int, int> { { 1002, 1 }, { 1001, 1 } };

            InventoryRules.Build(items, (global::cfg.TbItem)null, InventoryFilter.All, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1001, 1002 }));
            Assert.That(result[0].Name, Is.EqualTo("#1001"));
        }

        [Test]
        public void Build_WithEmptyOrNullDictionary_ClearsResult()
        {
            result.Add(new InventoryEntry(1, "旧", 1, 0, 0, null));

            InventoryRules.Build(new Dictionary<int, int>(), table, InventoryFilter.All, result);
            Assert.That(result, Is.Empty, "空字典");

            result.Add(new InventoryEntry(1, "旧", 1, 0, 0, null));
            InventoryRules.Build(null, table, InventoryFilter.All, result);
            Assert.That(result, Is.Empty, "null 字典");
        }

        [Test]
        public void Build_WhenCountIsNotPositive_SkipsEntry()
        {
            var items = new Dictionary<int, int> { { 1001, 0 }, { 1002, -1 }, { 1004, 1 } };

            InventoryRules.Build(items, table, InventoryFilter.All, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1004 }));
        }

        [Test]
        public void CategoryLabel_ReturnsChineseLabels()
        {
            Assert.That(InventoryRules.CategoryLabel(global::cfg.EItemCategory.Material), Is.EqualTo("材料"));
            Assert.That(InventoryRules.CategoryLabel(global::cfg.EItemCategory.Consumable), Is.EqualTo("消耗品"));
            Assert.That(InventoryRules.CategoryLabel(global::cfg.EItemCategory.Clue), Is.EqualTo("线索"));
            Assert.That(InventoryRules.CategoryLabel(global::cfg.EItemCategory.Key), Is.EqualTo("关键物"));
            Assert.That(InventoryRules.CategoryLabel(InventoryRules.UnknownCategory), Is.EqualTo("未知"));
        }

        private int[] Ids()
        {
            var ids = new int[result.Count];
            for (int i = 0; i < result.Count; i++) ids[i] = result[i].Id;
            return ids;
        }
    }
}
