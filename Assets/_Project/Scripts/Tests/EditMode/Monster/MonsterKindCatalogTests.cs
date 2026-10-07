// 职责：钉住按种类取数值这条路——不同种类取到不同数值、种类优先于全局、未知种类的明确行为（抛 + 宽容版），
//   以及种类表的数据本身（两个 demo 种类、yao_id 指向真实的妖、跨表的三列能读到）。
// 为什么新建：MonsterRulesTests 只测确定性规则（巡逻 / 感知 / 快照），不认识配置表；
//   本文件是「表 → 只读视图 → 规则」这条新链路的测试，一个被测类一个测试类。
// 用真实生成的 .bytes（同 YaoTableTests / LootServiceTests），不依赖场景与 Addressables。
using System.Collections.Generic;
using Game.Core.Config;
using Game.Mirror;
using Game.Monster;
using Game.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    /// <summary><see cref="MonsterKindCatalog"/> 与 <see cref="MonsterKind"/> 的 EditMode 测试。</summary>
    public sealed class MonsterKindCatalogTests
    {
        // 与 Tables/Data/monster_species/ 下的 demo 数据一致；改表要同步改这里。
        private const int NightWatchmanKind = 1001;
        private const int WellWomanKind = 1002;

        private global::cfg.Tables tables;
        private YaoCatalog yao;
        private MonsterConfig config;
        private MonsterKindCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            yao = new YaoCatalog(new StubConfigService(tables));
            config = ScriptableObject.CreateInstance<MonsterConfig>();
            catalog = new MonsterKindCatalog(new StubConfigService(tables), yao, config.HostileRadius);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void Catalog_HasBothDemoKinds()
        {
            Assert.That(catalog.IsReady, Is.True, "真实表应能通过校验");
            Assert.That(catalog.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(catalog.TryGet(NightWatchmanKind, out _), Is.True, "种类 1001 巡夜人");
            Assert.That(catalog.TryGet(WellWomanKind, out _), Is.True, "种类 1002 井边妇人");
        }

        [Test]
        public void Get_TwoDifferentKinds_ReturnsDifferentValues()
        {
            MonsterKind nightWatchman = catalog.Get(NightWatchmanKind);
            MonsterKind wellWoman = catalog.Get(WellWomanKind);

            Assert.That(nightWatchman.Name, Is.Not.EqualTo(wellWoman.Name));
            Assert.That(nightWatchman.MaxHealth, Is.EqualTo(3));
            Assert.That(wellWoman.MaxHealth, Is.EqualTo(2), "井边妇人比巡夜人脆");
            Assert.That(nightWatchman.AlertRadius, Is.EqualTo(6f));
            Assert.That(wellWoman.AlertRadius, Is.EqualTo(4.5f), "井边妇人视野更窄");
            Assert.That(wellWoman.AlertFillSeconds, Is.LessThan(nightWatchman.AlertFillSeconds), "井边妇人更警觉");
            Assert.That(wellWoman.HostileLoseSeconds, Is.GreaterThan(nightWatchman.HostileLoseSeconds), "井边妇人更执着");
        }

        [Test]
        public void Get_UnknownKind_ThrowsKeyNotFound()
        {
            // 未知种类的策略是「显式抛」，不是静默兜底：见 MonsterKindCatalog 类文档。
            Assert.That(() => catalog.Get(999999), Throws.TypeOf<KeyNotFoundException>()
                .With.Message.Contains("999999"));
        }

        [Test]
        public void TryGet_UnknownKind_ReturnsFalseWithoutThrowing()
        {
            Assert.That(catalog.TryGet(999999, out MonsterKind kind), Is.False);
            Assert.That(kind, Is.Null);
        }

        [Test]
        public void Kind_CrossTableColumns_ComeFromYaoTable()
        {
            MonsterKind nightWatchman = catalog.Get(NightWatchmanKind);

            global::cfg.yao.Yao row = yao.Get(nightWatchman.YaoId);
            Assert.That(nightWatchman.Tier, Is.EqualTo(row.Tier));
            Assert.That(nightWatchman.IsKillable, Is.EqualTo(row.Killable));
            Assert.That(nightWatchman.DefeatMethod, Is.EqualTo(row.DefeatMethod));
            Assert.That(nightWatchman.DropItemIds, Is.EqualTo(row.DropItems),
                "掉落不在这张表里复制一份，直接转问 tbyao");
        }

        [Test]
        public void Kind_WithoutYaoCatalog_CrossTableColumnsDegradeQuietly()
        {
            // 妖物表不可用（例如单独跑怪物模块的测试）时不能崩：跨表三列退化成「查不到」。
            var withoutYao = new MonsterKindCatalog(new StubConfigService(tables), null, config.HostileRadius);

            MonsterKind kind = withoutYao.Get(NightWatchmanKind);

            Assert.That(kind.Tier, Is.Null);
            Assert.That(kind.IsKillable, Is.False);
            Assert.That(kind.DefeatMethod, Is.Null);
            Assert.That(kind.DropItemIds, Is.Empty);
        }

        [Test]
        public void TableRead_WhenHostileRadiusNotBelowAlertRadius_Throws()
        {
            // 全局敌对半径被调到比种类橙区半径还大：读表那一次就要报出来，
            // 否则「红区优先」会把橙区整段吞掉，表现为「警戒永远攒不起来」。
            var broken = new MonsterKindCatalog(new StubConfigService(tables), yao, 99f);

            Assert.That(() => broken.Count, Throws.TypeOf<System.ArgumentException>()
                .With.Message.Contains("alert_radius"));
        }

        [Test]
        public void Invalidate_AfterTableRead_ReadsAgain()
        {
            Assert.That(catalog.Count, Is.GreaterThanOrEqualTo(2));

            catalog.Invalidate();

            Assert.That(catalog.Count, Is.GreaterThanOrEqualTo(2), "清缓存后能重新读表并重新校验");
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 YaoTableTests / LootServiceTests 的最小假实现）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new System.NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
