// 职责：钉住妖物表（tbyao）的生成物与数据——两只 demo 妖在表里、字段齐、clue_items 解析正确、线索都指向真实道具（PRD V12 数据侧）。
// 为什么新建：妖物表是 PRP/mirror-core 新加的 Luban 表；ConfigServiceTests 只测框架层装表，不认识具体玩法表的字段。
//   用真实生成的 .bytes（同 InventoryRulesTests / LootServiceTests），不依赖场景。
using System.Collections.Generic;
using Game.Core.Config;
using Game.Tests.EditMode.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary>妖物表 EditMode 测试。表是 <c>scripts/gen-tables.ps1</c> 从 <c>Tables/Data/yao/*.json</c> 生成的。</summary>
    public sealed class YaoTableTests
    {
        private global::cfg.Tables tables;

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
        }

        [Test]
        public void TbYao_HasBothDemoRows()
        {
            // 只断言「至少两行」：加一只妖只加数据（PRD V12），不该逼着改这条测试。
            Assert.That(tables.TbYao.DataList.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(tables.TbYao.GetOrDefault(1), Is.Not.Null, "妖 1「巡夜人」");
            Assert.That(tables.TbYao.GetOrDefault(2), Is.Not.Null, "妖 2「井边妇人」");
        }

        [Test]
        public void Yao1_NightWatchman_FieldsAndNoClueRequirement()
        {
            global::cfg.yao.Yao yao = tables.TbYao.Get(1);

            Assert.That(yao.Id, Is.EqualTo(1));
            Assert.That(yao.DisguiseName, Is.EqualTo("巡夜人"));
            Assert.That(yao.TrueImage, Is.EqualTo("yao_true_demon"));
            Assert.That(yao.ClueItems, Is.Empty, "巡夜人一照就见");
        }

        [Test]
        public void Yao2_WellWoman_RequiresOldLetter()
        {
            global::cfg.yao.Yao yao = tables.TbYao.Get(2);

            Assert.That(yao.DisguiseName, Is.EqualTo("井边妇人"));
            Assert.That(yao.TrueImage, Is.EqualTo("yao_true_slime"));
            Assert.That(yao.ClueItems, Is.EqualTo(new[] { 1005 }));
        }

        [Test]
        public void AllRows_DisplayFieldsFilled()
        {
            IReadOnlyList<global::cfg.yao.Yao> rows = tables.TbYao.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                global::cfg.yao.Yao yao = rows[i];
                Assert.That(yao.Id, Is.GreaterThan(0), "妖 id 从 1 起（MirrorSubject 用 0 表示没填）");
                Assert.That(yao.DisguiseName, Is.Not.Empty, $"妖 {yao.Id} 缺化形名");
                Assert.That(yao.TrueName, Is.Not.Empty, $"妖 {yao.Id} 缺真形名");
                Assert.That(yao.TrueDesc, Is.Not.Empty, $"妖 {yao.Id} 缺真形描述");
                Assert.That(yao.TrueImage, Is.Not.Empty, $"妖 {yao.Id} 缺真形图地址");
                Assert.That(yao.Flaw, Is.Not.Null);
                Assert.That(yao.Obsession, Is.Not.Null);
                Assert.That(yao.Clan, Is.Not.Null);
                Assert.That(yao.ClueItems, Is.Not.Null);
            }
        }

        [Test]
        public void AllRows_ClueItemsExistInItemTable()
        {
            IReadOnlyList<global::cfg.yao.Yao> rows = tables.TbYao.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                List<int> clues = rows[i].ClueItems;
                for (int j = 0; j < clues.Count; j++)
                {
                    Assert.That(tables.TbItem.GetOrDefault(clues[j]), Is.Not.Null,
                        $"妖 {rows[i].Id} 的线索 {clues[j]} 在 tbitem 里不存在");
                }
            }
        }

        [Test]
        public void DemoRows_SealableAndMaskParsed()
        {
            // 本波只存不用；钉住布尔列能解析，免得后续波次读到默认 false 才发现 JSON 写错。
            Assert.That(tables.TbYao.Get(1).Sealable, Is.True);
            Assert.That(tables.TbYao.Get(1).Mask, Is.False);
            Assert.That(tables.TbYao.Get(2).Sealable, Is.True);
            Assert.That(tables.TbYao.Get(2).Mask, Is.True);
        }
    }
}
