// 职责：InventoryRules 的 EditMode 回归——排序（类别再 id）、三档筛选、表里查不到时的「#id」占位、空字典 / 空表，
//   以及聚光灯新增的四个类别（皮 / 面具 / 钥匙 / 文书）的解析、分组与中文标签。
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

        [Test]
        public void CategoryLabel_ReturnsLabelsForSkinMaskPassDocument()
        {
            Assert.That(InventoryRules.CategoryLabel(global::cfg.EItemCategory.Skin), Is.EqualTo("皮"));
            Assert.That(InventoryRules.CategoryLabel(global::cfg.EItemCategory.Mask), Is.EqualTo("面具"));
            Assert.That(InventoryRules.CategoryLabel(global::cfg.EItemCategory.Pass), Is.EqualTo("钥匙"));
            Assert.That(InventoryRules.CategoryLabel(global::cfg.EItemCategory.Document), Is.EqualTo("文书"));
        }

        [Test]
        public void TbItem_WhenRowsUseTheNewCategories_ParsesThemFromTheGeneratedBytes()
        {
            // 生成物链路：Tables/Data/item.xlsx → tbitem.bytes → cfg.TbItem。
            // 皮 / 面具 / 钥匙 / 文书四个新类别各取一行，确认枚举值真被解析出来（不是回落到 0）。
            global::cfg.Item skin = table.Get(1101);
            Assert.That(skin.Name, Is.EqualTo("普通皮"));
            Assert.That(skin.Category, Is.EqualTo(global::cfg.EItemCategory.Skin));

            global::cfg.Item mask = table.Get(1201);
            Assert.That(mask.Name, Is.EqualTo("查勘使面具"));
            Assert.That(mask.Category, Is.EqualTo(global::cfg.EItemCategory.Mask));
            Assert.That(mask.Quality, Is.EqualTo(global::cfg.EItemQuality.Rare));

            global::cfg.Item pass = table.Get(1701);
            Assert.That(pass.Name, Is.EqualTo("进入废弃官署的钥匙"));
            Assert.That(pass.Category, Is.EqualTo(global::cfg.EItemCategory.Pass));

            global::cfg.Item document = table.Get(1601);
            Assert.That(document.Name, Is.EqualTo("名簿"));
            Assert.That(document.Category, Is.EqualTo(global::cfg.EItemCategory.Document));
            Assert.That(document.Desc, Does.Contain("名簿"));

            // 跨阶段关键道具（牙）与合成原料（水痕）走的是旧类别，确认没被新类别带偏。
            Assert.That(table.Get(1301).Category, Is.EqualTo(global::cfg.EItemCategory.Key));
            Assert.That(table.Get(1401).Category, Is.EqualTo(global::cfg.EItemCategory.Material));
        }

        [Test]
        public void Build_WithNewCategories_SortsThemAfterKey()
        {
            // 枚举值 1..8 就是「全部」档的排序键，新类别只能往后接，不能插进已有四类中间。
            var items = new Dictionary<int, int>
            {
                { 1601, 1 }, { 1701, 1 }, { 1201, 1 }, { 1101, 1 }, { 1006, 1 }, { 1005, 1 }, { 1004, 1 }, { 1001, 1 },
            };

            InventoryRules.Build(items, table, InventoryFilter.All, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1001, 1004, 1005, 1006, 1101, 1201, 1701, 1601 }));
        }

        [Test]
        public void Build_WithItemsFilter_KeepsSkinMaskAndPassButNotDocument()
        {
            var items = new Dictionary<int, int>
            {
                { 1101, 1 }, { 1201, 1 }, { 1701, 1 }, { 1601, 1 }, { 1005, 1 },
            };

            InventoryRules.Build(items, table, InventoryFilter.Items, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1101, 1201, 1701 }), "皮 / 面具 / 钥匙归「物品」档，文书不归");
        }

        [Test]
        public void Build_WithCluesFilter_KeepsDocumentTogetherWithClues()
        {
            var items = new Dictionary<int, int> { { 1005, 1 }, { 1601, 1 }, { 1101, 1 } };

            InventoryRules.Build(items, table, InventoryFilter.Clues, result);

            Assert.That(Ids(), Is.EqualTo(new[] { 1005, 1601 }), "文书与线索同为「信息」类，暂并在一档");
            Assert.That(result[1].Category, Is.EqualTo(global::cfg.EItemCategory.Document));
        }

        [Test]
        public void Matches_WhenCategoryIsNewAndFilterIsItemsOrClues_OnlyTheMappedOneAccepts()
        {
            Assert.That(InventoryRules.Matches(global::cfg.EItemCategory.Skin, InventoryFilter.Items), Is.True);
            Assert.That(InventoryRules.Matches(global::cfg.EItemCategory.Mask, InventoryFilter.Items), Is.True);
            Assert.That(InventoryRules.Matches(global::cfg.EItemCategory.Pass, InventoryFilter.Items), Is.True);
            Assert.That(InventoryRules.Matches(global::cfg.EItemCategory.Document, InventoryFilter.Clues), Is.True);
            Assert.That(InventoryRules.Matches(global::cfg.EItemCategory.Document, InventoryFilter.Items), Is.False);
            Assert.That(InventoryRules.Matches(global::cfg.EItemCategory.Skin, InventoryFilter.Clues), Is.False);
            Assert.That(InventoryRules.Matches(global::cfg.EItemCategory.Document, InventoryFilter.All), Is.True);
        }

        private int[] Ids()
        {
            var ids = new int[result.Count];
            for (int i = 0; i < result.Count; i++) ids[i] = result[i].Id;
            return ids;
        }
    }
}
