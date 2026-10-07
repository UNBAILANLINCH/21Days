// 职责：钉住妖物表（tbyao）的生成物、数据与只读查询——两只 demo 妖在表里、字段齐、clue_items 解析正确、
//   线索都指向真实道具、分层四列（tier / killable / defeat_method / drop_items）解析正确，以及 YaoCatalog 的
//   取行 / 缺 id / 按列反查 / tier 与 defeat_method 的非法取值 / 列白名单校验只做一次。
// 为什么新建：妖物表是 PRP/mirror-core 新加的 Luban 表；ConfigServiceTests 只测框架层装表，不认识具体玩法表的字段。
//   用真实生成的 .bytes（同 InventoryRulesTests / LootServiceTests），不依赖场景。
using System;
using System.Collections.Generic;
using Game.Core.Config;
using Game.Mirror;
using Game.Tests.EditMode.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary>妖物表 EditMode 测试。表是 <c>scripts/gen-tables.ps1</c> 从 <c>Tables/Data/yao/*.json</c> 生成的。</summary>
    public sealed class YaoTableTests
    {
        private global::cfg.Tables tables;
        private YaoCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            catalog = new YaoCatalog(new StubConfigService(tables));
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

        [Test]
        public void DemoRows_TierKillableAndDropsParsed()
        {
            // 聚光灯「怪物分层」（[06]）新加的三列：两只 demo 妖都是 A 底层、可击杀；掉落还没定，写 [] 不算错。
            global::cfg.yao.Yao watchman = tables.TbYao.Get(1);
            Assert.That(watchman.Tier, Is.EqualTo("A"));
            Assert.That(watchman.Killable, Is.True);
            Assert.That(watchman.DropItems, Is.Empty);

            global::cfg.yao.Yao woman = tables.TbYao.Get(2);
            Assert.That(woman.Tier, Is.EqualTo("A"));
            Assert.That(woman.Killable, Is.True);
            Assert.That(woman.DropItems, Is.Empty);
        }

        [Test]
        public void DemoRows_DefeatMethodParsed()
        {
            // 分层第 4 列 defeat_method（06_怪物分层.md:130 表头的「可否击杀 / 怎么杀」列里「怎么杀」那一半）。
            // 两只 demo 妖是占位数据，killable 都是 true，所以填「可击杀（方式没写）」——
            // 与 06_怪物分层.md:132 / :154 那批「可击杀（方式没写）」同一个说法，不引入与 killable 矛盾的取值。
            global::cfg.yao.Yao watchman = tables.TbYao.Get(1);
            Assert.That(watchman.Killable, Is.True);
            Assert.That(watchman.DefeatMethod, Is.EqualTo("可击杀（方式没写）"));

            global::cfg.yao.Yao woman = tables.TbYao.Get(2);
            Assert.That(woman.Killable, Is.True);
            Assert.That(woman.DefeatMethod, Is.EqualTo("可击杀（方式没写）"));
        }

        [Test]
        public void AllRows_TierIsOneOfAbc()
        {
            IReadOnlyList<global::cfg.yao.Yao> rows = tables.TbYao.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.That(rows[i].Tier, Is.EqualTo("A").Or.EqualTo("B").Or.EqualTo("C"),
                    $"妖 {rows[i].Id} 的 tier 只能是 A / B / C（见 Tables/Defines/yao.xml）");
            }
        }

        [Test]
        public void AllRows_DefeatMethodIsInTheFiveValueWhitelist()
        {
            // 白名单与 YaoCatalog.ValidateDefeatMethod 一致，写死在这里是为了让「有人新造了一个词」当场挂测试，
            // 而不是等运行期某次查询才抛。五个取值全部从 06_怪物分层.md 表 3.2–3.5 与该文 :121 R9 归纳。
            string[] allowed = { "可击杀（方式没写）", "暗杀", "特殊条件", "需收服", "不可杀" };
            IReadOnlyList<global::cfg.yao.Yao> rows = tables.TbYao.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.That(allowed, Does.Contain(rows[i].DefeatMethod),
                    $"妖 {rows[i].Id} 的 defeat_method 是 \"{rows[i].DefeatMethod}\"，不在白名单里（见 Tables/Defines/yao.xml）");
            }
        }

        [Test]
        public void AllRows_NotKillableRowsStillSayHowToDefeatOrWhyNot()
        {
            // 两列的分工：killable 答「能不能常规击杀」，defeat_method 答「为什么不能常规杀、有没有替代途径」。
            // killable=false 的行不能把 defeat_method 留空或写成「可击杀（方式没写）」——那就是两列互相矛盾。
            IReadOnlyList<global::cfg.yao.Yao> rows = tables.TbYao.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Killable)
                {
                    continue;
                }

                Assert.That(rows[i].DefeatMethod, Is.Not.Empty.And.Not.EqualTo("可击杀（方式没写）"),
                    $"妖 {rows[i].Id} 常规击杀不了，defeat_method 要说清是不可杀、需收服、特殊条件还是只能暗杀");
            }
        }

        [Test]
        public void AllRows_DropItemsExistInItemTable()
        {
            // 掉落写进表就得在 tbitem 里存在；空列表表示「掉什么还没定」，自然跳过。
            IReadOnlyList<global::cfg.yao.Yao> rows = tables.TbYao.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                List<int> drops = rows[i].DropItems;
                for (int j = 0; j < drops.Count; j++)
                {
                    Assert.That(tables.TbItem.GetOrDefault(drops[j]), Is.Not.Null,
                        $"妖 {rows[i].Id} 的掉落 {drops[j]} 在 tbitem 里不存在");
                }
            }
        }

        [Test]
        public void Catalog_TryGet_ReturnsBothDemoRows()
        {
            Assert.That(catalog.IsReady, Is.True, "表已经喂进去了");
            Assert.That(catalog.TryGet(1, out global::cfg.yao.Yao watchman), Is.True);
            Assert.That(watchman.DisguiseName, Is.EqualTo("巡夜人"));
            Assert.That(watchman.Flaw, Is.Not.Empty, "旧版遗留列，JSON 里不能缺字段");
            Assert.That(watchman.Obsession, Is.Not.Empty, "旧版遗留列，JSON 里不能缺字段");
            Assert.That(watchman.Clan, Is.Not.Empty, "旧版遗留列，JSON 里不能缺字段");

            Assert.That(catalog.TryGet(2, out global::cfg.yao.Yao woman), Is.True);
            Assert.That(woman.DisguiseName, Is.EqualTo("井边妇人"));
            Assert.That(woman.TrueImage, Is.EqualTo("yao_true_slime"));
            Assert.That(catalog.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Catalog_UnknownId_ReportsNotFoundAndDoesNotThrow()
        {
            const int missing = 987654;

            Assert.That(catalog.TryGet(missing, out global::cfg.yao.Yao yao), Is.False);
            Assert.That(yao, Is.Null);
            Assert.That(catalog.TierOf(missing), Is.Null);
            Assert.That(catalog.ClanOf(missing), Is.Null, "「没有这只妖」给 null，与「有妖但族属留空」的空串区分开");
            Assert.That(catalog.IsSealable(missing), Is.False, "查不到按不可收押处理");
            Assert.That(catalog.CanMask(missing), Is.False, "查不到按不可制面具处理");
            Assert.That(catalog.IsKillable(missing), Is.False, "查不到按不可击杀处理");
            Assert.That(catalog.DefeatMethodOf(missing), Is.Null, "查不到这只妖时「怎么杀」也没有答案");
            Assert.That(catalog.DropItemsOf(missing), Is.Empty);
            Assert.That(() => catalog.Get(missing), Throws.TypeOf<KeyNotFoundException>(),
                "Get 是「表里一定有」的入口，缺 id 要报出来而不是给 null");
        }

        [Test]
        public void Catalog_NewColumns_ReadByYaoId()
        {
            Assert.That(catalog.TierOf(1), Is.EqualTo("A"));
            Assert.That(catalog.TierOf(2), Is.EqualTo("A"));
            Assert.That(catalog.IsKillable(1), Is.True);
            Assert.That(catalog.DropItemsOf(1), Is.Empty);
        }

        [Test]
        public void Catalog_DefeatMethod_ReadByYaoId()
        {
            Assert.That(catalog.DefeatMethodOf(1), Is.EqualTo("可击杀（方式没写）"));
            Assert.That(catalog.DefeatMethodOf(2), Is.EqualTo("可击杀（方式没写）"));
            Assert.That(catalog.DefeatMethodOf(2), Is.EqualTo(tables.TbYao.Get(2).DefeatMethod),
                "读出来的就是表里那一列，本类不加工");
        }

        [Test]
        public void Catalog_DefeatMethodAndKillable_TogetherTellTheExceptionsApart()
        {
            // 这两列的分工是本次加列的理由：查勘使（常态不可击杀、但能用地形隐匿击杀）与籍中吏（不可击杀）
            // 的 killable 都是 false，只看 killable 会把「有条件的杀法」和「没有杀法」看成一回事。
            // 表里暂时没有这两只妖，所以用反射把真源里的两种状态摆进去，验两列能一起把它们分开。
            string originalKillable = tables.TbYao.Get(1).DefeatMethod;
            bool originalBoolean = tables.TbYao.Get(1).Killable;
            try
            {
                SetField(1, "Killable", false);
                SetField(1, "DefeatMethod", "特殊条件");
                SetField(2, "Killable", false);
                SetField(2, "DefeatMethod", "不可杀");
                catalog.Invalidate();

                Assert.That(catalog.IsKillable(1), Is.False);
                Assert.That(catalog.DefeatMethodOf(1), Is.EqualTo("特殊条件"), "常态杀不了，但有替代途径");
                Assert.That(catalog.IsKillable(2), Is.False);
                Assert.That(catalog.DefeatMethodOf(2), Is.EqualTo("不可杀"), "没有任何途径");
            }
            finally
            {
                SetField(1, "Killable", originalBoolean);
                SetField(1, "DefeatMethod", originalKillable);
                SetField(2, "Killable", true);
                SetField(2, "DefeatMethod", "可击杀（方式没写）");
                catalog.Invalidate();
            }

            Assert.That(catalog.DefeatMethodOf(1), Is.EqualTo("可击杀（方式没写）"), "换回来之后照常可读");
        }

        [Test]
        public void Catalog_SealableMaskClanQueries_MatchTheTable()
        {
            Assert.That(catalog.IsSealable(1), Is.True);
            Assert.That(catalog.CanMask(1), Is.False);
            Assert.That(catalog.IsSealable(2), Is.True);
            Assert.That(catalog.CanMask(2), Is.True);
            Assert.That(catalog.ClanOf(2), Is.EqualTo("占位族属"));
            Assert.That(catalog.ClanOf(2), Is.EqualTo(tables.TbYao.Get(2).Clan));
        }

        [Test]
        public void Catalog_IdLists_FollowTableOrderAndTheColumnValue()
        {
            // 断言与直接查表的结果一致，而不是写死 [1, 2]：加一只妖只加数据，这条测试不该跟着改。
            List<int> sealable = new List<int>();
            List<int> masks = new List<int>();
            List<int> killable = new List<int>();
            IReadOnlyList<global::cfg.yao.Yao> rows = tables.TbYao.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Sealable)
                {
                    sealable.Add(rows[i].Id);
                }

                if (rows[i].Mask)
                {
                    masks.Add(rows[i].Id);
                }

                if (rows[i].Killable)
                {
                    killable.Add(rows[i].Id);
                }
            }

            Assert.That(catalog.SealableIds, Is.EqualTo(sealable));
            Assert.That(catalog.MaskIds, Is.EqualTo(masks));
            Assert.That(catalog.KillableIds, Is.EqualTo(killable));
            Assert.That(catalog.SealableIds, Is.EqualTo(new[] { 1, 2 }), "两只 demo 妖都可收押");
        }

        [Test]
        public void Catalog_BrokenTier_ThrowsPointingAtTheId()
        {
            // 表里 tier 只允许 A / B / C；写坏了要当场报出来，不能悄悄退化成某一层。
            // 抛出点是「Invalidate 之后第一次读表」那一次（列白名单只校验一次，见 YaoCatalog 类文档），
            // 所以这里断言的是 TierOf 这个入口整体会抛，而不是它内部单独又校验了一遍。
            string original = tables.TbYao.Get(1).Tier;
            SetField(1, "Tier", "D");
            try
            {
                catalog.Invalidate();
                ArgumentException error = Assert.Throws<ArgumentException>(() => catalog.TierOf(1));
                Assert.That(error.Message, Does.Contain("1"), "报错要点名是哪只妖");
                Assert.That(error.Message, Does.Contain("D"), "报错要带上表里的原值");
                Assert.That(error.Message, Does.Contain("yao.xml"), "报错要指到列定义的出处");
            }
            finally
            {
                SetField(1, "Tier", original);
                catalog.Invalidate();
            }

            Assert.That(catalog.TierOf(1), Is.EqualTo("A"), "换回来之后照常可读");
        }

        [Test]
        public void Catalog_BrokenDefeatMethod_ThrowsPointingAtTheId()
        {
            // defeat_method 只认白名单五个值；写坏了要当场报出来，不能悄悄按某一类处理。
            string original = tables.TbYao.Get(1).DefeatMethod;
            SetField(1, "DefeatMethod", "随便杀");
            try
            {
                catalog.Invalidate();
                ArgumentException error = Assert.Throws<ArgumentException>(() => catalog.DefeatMethodOf(1));
                Assert.That(error.Message, Does.Contain("1"), "报错要点名是哪只妖");
                Assert.That(error.Message, Does.Contain("随便杀"), "报错要带上表里的原值");
                Assert.That(error.Message, Does.Contain("yao.xml"), "报错要指到列定义的出处");
            }
            finally
            {
                SetField(1, "DefeatMethod", original);
                catalog.Invalidate();
            }

            Assert.That(catalog.DefeatMethodOf(1), Is.EqualTo("可击杀（方式没写）"), "换回来之后照常可读");
        }

        [Test]
        public void Catalog_TypesetMarkInDefeatMethod_IsRejected()
        {
            // 真源在表里用过全角括号、也用过顿号（06_怪物分层.md:159「持有篮子时不可击杀；『此后』可击杀」），
            // 但白名单要的是归纳后的取值，不是排版记号：把原文那一格照抄进数据要当场报错，
            // 不能靠「看起来差不多」放行，否则以后每个人填的写法都不一样，这一列就没法当枚举用。
            string original = tables.TbYao.Get(2).DefeatMethod;
            SetField(2, "DefeatMethod", "持有篮子时不可击杀；「此后」可击杀");
            try
            {
                catalog.Invalidate();
                ArgumentException error = Assert.Throws<ArgumentException>(() => catalog.DefeatMethodOf(2));
                Assert.That(error.Message, Does.Contain("06_怪物分层.md"), "报错要指到归纳取值的出处");
            }
            finally
            {
                SetField(2, "DefeatMethod", original);
                catalog.Invalidate();
            }

            Assert.That(catalog.DefeatMethodOf(2), Is.EqualTo("可击杀（方式没写）"));
        }

        [Test]
        public void Catalog_SecondReadAfterValidation_DoesNotRevalidate()
        {
            // B13：列白名单校验只做一次。第一次读表校验过之后，再改坏数据但不 Invalidate，读取照旧用缓存、
            // 不会重新报错——这正是收口后的语义（本表是只读生成物，运行期没有旁路改写）。
            // Invalidate 之后必须重新校验，否则「校验只做一次」就变成了「校验只做第一次」。
            string original = tables.TbYao.Get(1).Tier;
            try
            {
                Assert.That(catalog.TierOf(1), Is.EqualTo("A"), "先把表读起来、校验过一遍");
                SetField(1, "Tier", "Z");

                Assert.That(catalog.TierOf(1), Is.EqualTo("Z"), "校验只做一次，改坏数据不会让已读过的表重新校验");
                Assert.That(catalog.IsReady, Is.True, "缓存还在，表仍算就绪");

                catalog.Invalidate();
                Assert.That(() => catalog.IsReady, Throws.TypeOf<ArgumentException>(),
                    "Invalidate 之后重读要重新校验");
            }
            finally
            {
                SetField(1, "Tier", original);
                catalog.Invalidate();
            }

            Assert.That(catalog.TierOf(1), Is.EqualTo("A"));
        }

        [Test]
        public void Catalog_SubTierMark_IsRejectedAndSaysWhy()
        {
            // 06_怪物分层.md:126 的「A·下 / B·下」是「原文写在上一层条目下一级」的排版记号，不是第四个层级；
            // 本表不建模（结论见交付说明）。有人照抄进来时要报清楚，别让它变成一层新的 A 层。
            string original = tables.TbYao.Get(2).Tier;
            SetField(2, "Tier", "A·下");
            try
            {
                catalog.Invalidate();
                ArgumentException error = Assert.Throws<ArgumentException>(() => catalog.TierOf(2));
                Assert.That(error.Message, Does.Contain("A·下"));
                Assert.That(error.Message, Does.Contain("06_怪物分层.md"), "报错要指到排版记号的出处");
            }
            finally
            {
                SetField(2, "Tier", original);
                catalog.Invalidate();
            }

            Assert.That(catalog.TierOf(2), Is.EqualTo("A"));
        }

        // cfg.yao.Yao 的字段是 readonly，测试里要改只能反射写回，用完立刻还原。
        private void SetField(int yaoId, string field, object value)
        {
            typeof(global::cfg.yao.Yao)
                .GetField(field)
                .SetValue(tables.TbYao.Get(yaoId), value);
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>，不拉 Addressables。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
