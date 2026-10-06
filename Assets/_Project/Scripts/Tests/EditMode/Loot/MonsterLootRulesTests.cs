// 职责：钉住怪物掉落的纯规则——按 item id 列表累加、跳过非法 id、空列表 / 空数据不崩；以及
//   「掉落 id 列表能解析成 item 引用」这条链路的入口（列表来自妖物表，本文件用真实表核对每个 id 都查得到）。
// 为什么新建：LootRulesTests 只覆盖物资箱的「按键幂等 + 累加」，怪物掉落没有箱子键、语义不同；
//   一个被测类一个测试类。
using System.Collections.Generic;
using Game.Core.Config;
using Game.Loot;
using Game.Tests.EditMode.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Loot
{
    public sealed class MonsterLootRulesTests
    {
        private LootSaveData data;

        [SetUp]
        public void SetUp() => data = new LootSaveData();

        [Test]
        public void Grant_DropList_AddsEveryItemOnce()
        {
            Assert.That(MonsterLootRules.Grant(data, 3, new[] { 1001, 1006 }), Is.True);

            Assert.That(data.Items[1001], Is.EqualTo(1));
            Assert.That(data.Items[1006], Is.EqualTo(1));
            Assert.That(data.Items.Count, Is.EqualTo(2));
            Assert.That(data.CollectedCrates, Is.Empty, "怪物掉落不写箱子键");
        }

        [Test]
        public void Grant_SameKindTwice_Accumulates()
        {
            MonsterLootRules.Grant(data, 3, new[] { 1001 });
            MonsterLootRules.Grant(data, 3, new[] { 1001 });

            Assert.That(data.Items[1001], Is.EqualTo(2), "同一只种类会死很多次，每次都要掉");
        }

        [Test]
        public void Grant_EmptyOrNullDropList_ReturnsFalseWithoutChangingData()
        {
            Assert.That(MonsterLootRules.Grant(data, 3, new List<int>()), Is.False);
            Assert.That(MonsterLootRules.Grant(data, 3, null), Is.False);
            Assert.That(data.Items, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Grant_InvalidYaoId_ReturnsFalse(int yaoId)
        {
            Assert.That(MonsterLootRules.Grant(data, yaoId, new[] { 1001 }), Is.False);
            Assert.That(data.Items, Is.Empty);
        }

        [Test]
        public void Grant_NullData_ReturnsFalse()
        {
            Assert.That(MonsterLootRules.Grant(null, 3, new[] { 1001 }), Is.False);
        }

        [Test]
        public void Grant_NonPositiveItemIds_AreSkipped()
        {
            Assert.That(MonsterLootRules.Grant(data, 3, new[] { 0, 1001, -5 }), Is.True);

            Assert.That(data.Items.Count, Is.EqualTo(1));
            Assert.That(data.Items[1001], Is.EqualTo(1));
        }

        [Test]
        public void Drops_FindsTheItemInTheList()
        {
            IReadOnlyList<int> drops = new[] { 1002, 1006 };

            Assert.That(MonsterLootRules.Drops(drops, 1006), Is.True);
            Assert.That(MonsterLootRules.Drops(drops, 1001), Is.False);
            Assert.That(MonsterLootRules.Drops(null, 1001), Is.False);
            Assert.That(MonsterLootRules.Drops(drops, 0), Is.False);
        }

        // 真实数据核对：妖物表每一行的 drop_items 里每个 id 都要能在 tbitem 里查到。
        // 策划往 tbyao 里填掉落时，填错 id 会在这里当场亮（表是生成物，这条测试是唯一的守门人）。
        // 现在 demo 数据两行的 drop_items 都是空列表，所以这条测试跑的是「空列表不算错」那一支——
        // 真正的价值在后续波次往 yao 表填掉落之后。
        [Test]
        public void EveryDropItemInYaoTable_ResolvesToARealItem()
        {
            global::cfg.Tables tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            var unknown = new List<string>();
            foreach (global::cfg.yao.Yao yao in tables.TbYao.DataList)
            {
                if (yao.DropItems == null) continue;
                foreach (int itemId in yao.DropItems)
                {
                    if (tables.TbItem.GetOrDefault(itemId) == null)
                    {
                        unknown.Add($"yao {yao.Id} 的 drop_items 里的 {itemId}");
                    }
                }
            }

            // 只断言「没有查不到的」：现在两行 demo 妖的 drop_items 都是空列表，这条跑的是「空列表不算错」，
            // 真正的价值在后续波次往 yao 表填掉落之后（那时填错 id 会在这里亮）。
            Assert.That(unknown, Is.Empty, "掉落 id 在 tbitem 里查不到：" + string.Join("、", unknown));
        }
    }
}
