// 职责：钉住世界表（TbScene / TbRegion / TbPortal）的生成物、数据与只读查询——三张表读得进来、
//   区域成对同构、传送点自洽、未实装的场景被 WorldCatalog 的校验器报出来，以及「改坏一行必须报错」的负对照。
// 为什么新建：世界表是 roadmap A4「多场景流转」新加的三张 Luban 表；ConfigServiceTests 只测框架层装表，
//   不认识 scene / region / portal 的列。数据用真实生成物（同 YaoTableTests），不依赖场景。
using System;
using System.Collections.Generic;
using Game.Core.Config;
using Game.World;
using Game.Tests.EditMode.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.World
{
    /// <summary>世界表 EditMode 测试。表是 <c>scripts/gen-tables.ps1</c> 从 <c>Tables/Data/world/**/*.json</c> 生成的。</summary>
    public sealed class WorldTableTests
    {
        private global::cfg.Tables tables;
        private WorldCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            catalog = new WorldCatalog(new TableStubConfigService(tables));
        }

        // ---------------------------------------------------------------- 表能翻译

        [Test]
        public void Tables_HaveTheTwoIsomorphicMaps()
        {
            Assert.That(tables.TbScene.DataList.Count, Is.GreaterThanOrEqualTo(2), "人间与妖界两张主图");
            Assert.That(tables.TbRegion.DataList.Count, Is.GreaterThan(0));
            Assert.That(tables.TbPortal.DataList.Count, Is.GreaterThan(0));

            Assert.That(catalog.TryGetScene("human_jingyang", out global::cfg.world.Scene human), Is.True);
            Assert.That(human.DisplayName, Is.EqualTo("泾阳"));
            Assert.That(human.World, Is.EqualTo(global::cfg.world.WorldId.Human));
            Assert.That(human.WorldId, Is.EqualTo("jingyang"));

            Assert.That(catalog.TryGetScene("yao_fangshi", out global::cfg.world.Scene yao), Is.True);
            Assert.That(yao.DisplayName, Is.EqualTo("镜中妖界长安·坊市"));
            Assert.That(yao.World, Is.EqualTo(global::cfg.world.WorldId.Yao));
            Assert.That(yao.WorldId, Is.EqualTo("mirror_changan"));
        }

        [Test]
        public void Catalog_SceneLists_ReadInTableOrder()
        {
            IReadOnlyList<global::cfg.world.Region> regions = catalog.RegionsOf("human_jingyang");
            Assert.That(regions.Count, Is.EqualTo(4));
            Assert.That(regions[0].RegionId, Is.EqualTo("jingyang_street"), "街面是地图上第一格（10_两界与场景结构.md:133 R5 的层序）");
            Assert.That(regions[3].RegionId, Is.EqualTo("jingyang_well"));

            global::cfg.world.Scene scene = catalog.GetScene("human_jingyang");
            Assert.That(scene.RegionNames.Count, Is.EqualTo(scene.RegionIds.Count), "名称与区域一一对应");
            Assert.That(scene.SpawnPoints, Does.Contain(scene.DefaultSpawnId), "默认出生点必须在出生点列表里");
        }

        [Test]
        public void Catalog_TwoMaps_AreIsomorphic()
        {
            // 10_两界与场景结构.md:132 R4：两张俯视图格局完全对应。四格主图必须两两互指。
            Assert.That(catalog.MirrorRegionOf("jingyang_street"), Is.EqualTo("fangshi_street"));
            Assert.That(catalog.MirrorRegionOf("fangshi_street"), Is.EqualTo("jingyang_street"));
            Assert.That(catalog.MirrorRegionOf("jingyang_platform"), Is.EqualTo("fangshi_platform"));
            Assert.That(catalog.MirrorRegionOf("jingyang_yamen"), Is.EqualTo("fangshi_lunhuitang"));
            Assert.That(catalog.MirrorRegionOf("jingyang_well"), Is.EqualTo("fangshi_dongtingfu"));

            // 单位置区域（堤坝、坟地这些不在俯视图上的地点）没有对面。
            Assert.That(catalog.MirrorRegionOf("jingyang_dike"), Is.Empty);
        }

        [Test]
        public void Catalog_Portals_AreNamedOnBothScenes()
        {
            Assert.That(catalog.TryGetPortal("portal_mirror_jingyang_to_fangshi", out global::cfg.world.Portal forward), Is.True);
            Assert.That(forward.SceneKey, Is.EqualTo("human_jingyang"));
            Assert.That(forward.TargetScene, Is.EqualTo("yao_fangshi"));
            Assert.That(forward.AnchorId, Is.EqualTo("jingyang_street"));

            // 场景侧也登记了同一个传送点（两边对得上，不是只在传送点表里写了一行）。
            global::cfg.world.Scene scene = catalog.GetScene("human_jingyang");
            Assert.That(scene.PortalIds, Does.Contain("portal_mirror_jingyang_to_fangshi"));
            Assert.That(catalog.PortalsOf("human_jingyang").Count, Is.EqualTo(scene.PortalIds.Count));
        }

        [Test]
        public void Catalog_UnknownScene_ReportsNotFoundAndDoesNotThrow()
        {
            Assert.That(catalog.TryGetScene("nowhere", out global::cfg.world.Scene scene), Is.False);
            Assert.That(scene, Is.Null);
            Assert.That(catalog.SceneAddressOf("nowhere"), Is.Empty);
            Assert.That(catalog.IsSceneImplemented("nowhere"), Is.False, "查不到按未实装处理");
            Assert.That(catalog.CanLoadScene("nowhere"), Is.False);
            Assert.That(catalog.RegionsOf("nowhere"), Is.Empty);
            Assert.That(catalog.PortalsOf("nowhere"), Is.Empty);
            Assert.That(catalog.MirrorRegionOf("nowhere"), Is.Empty);
            Assert.That(() => catalog.GetScene("nowhere"), Throws.TypeOf<KeyNotFoundException>(),
                "GetScene 是「表里一定有」的入口，缺键要报出来而不是给 null");
        }

        [Test]
        public void Catalog_UnknownRegionAndPortal_ReportNotFound()
        {
            Assert.That(catalog.TryGetRegion("nowhere", out global::cfg.world.Region region), Is.False);
            Assert.That(region, Is.Null);
            Assert.That(catalog.TryGetPortal("nowhere", out global::cfg.world.Portal portal), Is.False);
            Assert.That(portal, Is.Null);
        }

        [Test]
        public void PortalTriggerKindMap_MatchesTheTableEnum()
        {
            // 运行期枚举是手写的那一份（PortalTriggerKind.cs），必须与表里的取值逐项对齐；
            // 表侧改了取值而这份没跟，Here 当场报出来。
            Assert.That((int)PortalTriggerKind.EnterRange, Is.EqualTo((int)global::cfg.world.PortalTriggerKind.EnterRange));
            Assert.That((int)PortalTriggerKind.Interact, Is.EqualTo((int)global::cfg.world.PortalTriggerKind.Interact));
            Assert.That(PortalTriggerKindMap.FromTable(global::cfg.world.PortalTriggerKind.EnterRange),
                Is.EqualTo(PortalTriggerKind.EnterRange));
            Assert.That(PortalTriggerKindMap.FromTable(global::cfg.world.PortalTriggerKind.Interact),
                Is.EqualTo(PortalTriggerKind.Interact));
        }

        [Test]
        public void PortalTriggerKindMap_UnknownValue_Throws()
        {
            // 负对照：表里出现白名单外的取值时不能悄悄退化成某一种。
            Assert.That(() => PortalTriggerKindMap.FromTable((global::cfg.world.PortalTriggerKind)99),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // ---------------------------------------------------------------- 未实装的场景与地址

        [Test]
        public void AllRegions_SceneScopeIsConsistentWithSceneKey()
        {
            // 区域形态与 scene_key 的三种对应关系（见 world.xml 的 SceneScope）：
            // 主图 / 战斗关延伸在某张图上，单场景专属不挂任何图。
            foreach (global::cfg.world.Region region in tables.TbRegion.DataList)
            {
                if (region.SceneScope == global::cfg.world.SceneScope.SingleScene)
                {
                    Assert.That(region.SceneKey, Is.Empty, $"{region.RegionId} 是单场景专属地点，不该挂 scene_key");
                }
                else
                {
                    Assert.That(region.SceneKey, Is.Not.Empty, $"{region.RegionId} 必须落在某张图上");
                    Assert.That(catalog.TryGetScene(region.SceneKey, out _), Is.True,
                        $"{region.RegionId} 的 scene_key「{region.SceneKey}」不在 TbScene 里");
                }
            }
        }

        [Test]
        public void StageExtensionRegions_AreOnlyConnectedByThePiecesTheDesignGives()
        {
            // 战斗关延伸区域（10:146 R18）目前原文明说「与两张俯视图怎么拼没定」（10:207 Q3），
            // 所以它们登记了 scene_key 却还没有传送点；这条测试把这个现状钉住，
            // 将来谁把它们接起来，会在这里看见「该更新这条测试」而不是悄悄多了几条导线。
            var withPortal = new List<string>();
            foreach (global::cfg.world.Region region in tables.TbRegion.DataList)
            {
                if (region.SceneScope != global::cfg.world.SceneScope.StageExtension)
                {
                    continue;
                }

                Assert.That(region.SceneKey, Is.Not.Empty, $"{region.RegionId} 得说清属于哪张图");
                withPortal.Add(region.RegionId);
            }

            Assert.That(withPortal, Is.Not.Empty, "阶段1 / 3 / 六 / 九的战斗关区域要登记在案（10:146 R18）");
            Assert.That(catalog.PortalsOf("human_jingyang").Count, Is.EqualTo(1),
                "现阶段人间只有一条两界转场导线，战斗关区域之间还没有传送点");
        }

        [Test]
        public void NotImplementedScenes_AreReportedSeparatelyFromProblems()
        {
            // 「Addressables 里还没有这些场景」是现状、不是表写坏了，所以它不该进 Problems。
            // 接线波（2026-10-07）把两张主图都实装了，所以现在要**两个状态各验一遍**：
            // 真表现状是「都实装」，未实装那一支用假表造（原来那条断言靠的是「真表恰好都没实装」，
            // 数据一变它就不再验任何规则了）。
            WorldValidationResult real = catalog.Validate();

            Assert.That(real.Passed, Is.True, real.Describe());
            Assert.That(real.NotImplementedSceneKeys, Is.Empty,
                "两张主图都已在 Addressables 里实装（human_jingyang / yao_fangshi）");
            Assert.That(catalog.IsSceneImplemented("human_jingyang"), Is.True);
            Assert.That(catalog.IsSceneImplemented("yao_fangshi"), Is.True);

            // 另一支：未实装的场景仍然要单独报出来、仍然不进 Problems。
            WorldCatalog mixed = WorldTestSupport.CatalogWithFakeScenes(
                new WorldTestSupport.FakeScene("fake_live", true, "FakeLiveAddress", "fake_spawn", "fake_spawn"),
                new WorldTestSupport.FakeScene("fake_dark", false, string.Empty, "fake_spawn", "fake_spawn"));
            WorldValidationResult mixedResult = mixed.Validate();

            Assert.That(mixedResult.Passed, Is.True, mixedResult.Describe());
            Assert.That(mixedResult.NotImplementedSceneKeys, Is.EquivalentTo(new[] { "fake_dark" }),
                "未实装是状态不是错误：单独报出来，且不阻断");
            Assert.That(mixed.IsSceneImplemented("fake_dark"), Is.False);
            Assert.That(mixed.IsSceneImplemented("fake_live"), Is.True);
        }

        [Test]
        public void NotImplementedScene_HasNoAddress()
        {
            // 未实装的场景不许写地址：写了就等于宣称 Addressables 里有它。
            // 真表这一遍是「都实装」那一支；未实装那一支的**负对照**（写了地址要报）用假表直接问校验器。
            foreach (global::cfg.world.Scene scene in tables.TbScene.DataList)
            {
                if (scene.Implemented)
                {
                    Assert.That(scene.SceneAddress, Is.Not.Empty, $"{scene.SceneKey} 标了已实装就必须有地址");
                }
                else
                {
                    Assert.That(scene.SceneAddress, Is.Empty, $"{scene.SceneKey} 未实装，地址必须是空的");
                }
            }

            Assert.That(catalog.SceneAddressOf("human_jingyang"), Is.EqualTo("human_jingyang"),
                "接线波登记后地址与场景键同名（Addressables Scenes 组里的地址）");
            Assert.That(catalog.CanLoadScene("human_jingyang"), Is.True, "已实装且地址非空 = 可加载");

            WorldValidationResult bad = WorldCatalogValidator.Validate(WorldTestSupport.TablesWithFakeScenes(
                new WorldTestSupport.FakeScene("fake_unimplemented", false, "SomeAddress", "fake_spawn", "fake_spawn")));

            Assert.That(bad.Passed, Is.False, "未实装却写了地址必须报出来");
            Assert.That(bad.Describe(), Does.Contain("fake_unimplemented"));
            Assert.That(bad.Describe(), Does.Contain("scene_address"));
        }

        // ---------------------------------------------------------------- 负对照：改坏一行必须报出来

        [Test]
        public void BrokenMirror_OneSided_IsReported()
        {
            // 负对照：两界同构断了一半（对面不互指）必须报出来，否则「建筑复用」省不下工作量（10:196）。
            string original = tables.TbRegion.Get("fangshi_street").MirrorRegionId;
            SetRegionField("fangshi_street", "MirrorRegionId", "jingyang_well");
            try
            {
                AssertMissingMirrorProblems();
            }
            finally
            {
                SetRegionField("fangshi_street", "MirrorRegionId", original);
            }

            Assert.That(catalog.Validate().Passed, Is.True, "换回来之后照常通过");
        }

        [Test]
        public void BrokenMirror_EmptyOnMainMapRegion_IsReported()
        {
            // 负对照：主图区域不写对面也要报。
            string original = tables.TbRegion.Get("jingyang_yamen").MirrorRegionId;
            SetRegionField("jingyang_yamen", "MirrorRegionId", string.Empty);
            try
            {
                AssertMissingMirrorProblems();
            }
            finally
            {
                SetRegionField("jingyang_yamen", "MirrorRegionId", original);
            }
        }

        [Test]
        public void BrokenMirror_PointsIntoTheSameWorld_IsReported()
        {
            // 负对照：mirror_region_id 指向同一界——「对面世界」是这条关系的全部意义。
            string original = tables.TbRegion.Get("jingyang_street").MirrorRegionId;
            SetRegionField("jingyang_street", "MirrorRegionId", "jingyang_yamen");
            SetRegionField("jingyang_yamen", "MirrorRegionId", "jingyang_street");
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "同一个世界"), Is.True, result.Describe());
            }
            finally
            {
                SetRegionField("jingyang_street", "MirrorRegionId", original);
                SetRegionField("jingyang_yamen", "MirrorRegionId", "fangshi_lunhuitang");
            }
        }

        [Test]
        public void BrokenImplementation_TrueWithoutAddress_IsReported()
        {
            // 负对照：标了已实装却没写地址。
            // **两个字段一起摆**：接线波之后真表这一行本来就是 implemented=true + 地址非空，
            // 只翻 implemented（原来那样）是个空操作，这条就什么也没验到；把地址清掉才是要验的形状。
            bool originalImplemented = tables.TbScene.Get("human_jingyang").Implemented;
            string originalAddress = tables.TbScene.Get("human_jingyang").SceneAddress;
            SetSceneField("human_jingyang", "Implemented", true);
            SetSceneField("human_jingyang", "SceneAddress", string.Empty);
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "scene_address 是空的"), Is.True, result.Describe());
            }
            finally
            {
                SetSceneField("human_jingyang", "Implemented", originalImplemented);
                SetSceneField("human_jingyang", "SceneAddress", originalAddress);
            }

            Assert.That(WorldCatalogValidator.Validate(tables).Passed, Is.True, "还原之后照常通过");
        }

        [Test]
        public void BrokenImplementation_FalseWithAddress_IsReported()
        {
            // 负对照：未实装却写了一个地址——**这正是「不要把不存在的地址写进表就完事」那条要求**。
            // 同上一处：真表这一行现在是 implemented=true，只改地址是**合法**形状，必须把 implemented 一起压回 false。
            bool originalImplemented = tables.TbScene.Get("human_jingyang").Implemented;
            string originalAddress = tables.TbScene.Get("human_jingyang").SceneAddress;
            SetSceneField("human_jingyang", "Implemented", false);
            SetSceneField("human_jingyang", "SceneAddress", "SomeSceneThatDoesNotExist");
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "未实装的场景不许写地址"), Is.True, result.Describe());
                Assert.That(Contains(result, "SomeSceneThatDoesNotExist"), Is.True, "报错要点名表里的原值");
            }
            finally
            {
                SetSceneField("human_jingyang", "Implemented", originalImplemented);
                SetSceneField("human_jingyang", "SceneAddress", originalAddress);
            }

            Assert.That(WorldCatalogValidator.Validate(tables).Passed, Is.True, "还原之后照常通过");
        }

        [Test]
        public void BrokenImplementation_ImplementedWithoutDefaultSpawn_IsReported()
        {
            // 负对照：实装了却没有默认出生点——新开局与读档恢复都没有落点。
            bool originalImplemented = tables.TbScene.Get("human_jingyang").Implemented;
            string originalAddress = tables.TbScene.Get("human_jingyang").SceneAddress;
            string originalDefault = tables.TbScene.Get("human_jingyang").DefaultSpawnId;
            SetSceneField("human_jingyang", "Implemented", true);
            SetSceneField("human_jingyang", "SceneAddress", "SomeScene");
            SetSceneField("human_jingyang", "DefaultSpawnId", string.Empty);
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "没有 default_spawn_id"), Is.True, result.Describe());
            }
            finally
            {
                SetSceneField("human_jingyang", "Implemented", originalImplemented);
                SetSceneField("human_jingyang", "SceneAddress", originalAddress);
                SetSceneField("human_jingyang", "DefaultSpawnId", originalDefault);
            }
        }

        [Test]
        public void BrokenReferences_AreReported()
        {
            // 负对照：区域 / 传送点 id 写错（表里对不上）必须报出来。
            List<string> originalRegionIds = tables.TbScene.Get("human_jingyang").RegionIds;
            originalRegionIds.Add("nowhere_region");
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "nowhere_region"), Is.True, result.Describe());
            }
            finally
            {
                originalRegionIds.Remove("nowhere_region");
            }

            List<string> originalPortalIds = tables.TbScene.Get("human_jingyang").PortalIds;
            originalPortalIds.Add("nowhere_portal");
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "nowhere_portal"), Is.True, result.Describe());
            }
            finally
            {
                originalPortalIds.Remove("nowhere_portal");
            }

            Assert.That(catalog.Validate().Passed, Is.True, "换回来之后照常通过");
        }

        [Test]
        public void BrokenPortal_UnknownSpawnTarget_IsReported()
        {
            // 负对照：传送点的目标出生点在目标场景里不存在。
            string original = tables.TbPortal.Get("portal_mirror_jingyang_to_fangshi").TargetSpawnId;
            SetPortalField("portal_mirror_jingyang_to_fangshi", "TargetSpawnId", "nowhere_spawn");
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "nowhere_spawn"), Is.True, result.Describe());
            }
            finally
            {
                SetPortalField("portal_mirror_jingyang_to_fangshi", "TargetSpawnId", original);
            }
        }

        [Test]
        public void BrokenPortal_EnterRangeClaimingMirrorArrival_IsReported()
        {
            // 负对照：进入范围就触发却自称两界转场——两界过镜要玩家主动（10:148 R20）。
            global::cfg.world.PortalTriggerKind originalKind =
                tables.TbPortal.Get("portal_mirror_fangshi_to_jingyang").TriggerKind;
            SetPortalField("portal_mirror_fangshi_to_jingyang", "TriggerKind", global::cfg.world.PortalTriggerKind.EnterRange);
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "trigger_kind=EnterRange"), Is.True, result.Describe());
            }
            finally
            {
                SetPortalField("portal_mirror_fangshi_to_jingyang", "TriggerKind", originalKind);
            }
        }

        [Test]
        public void BrokenPortal_SameAnchorAsTarget_IsReported()
        {
            // 负对照：出口与目标出生点是同一个，玩家会在原地打转。
            string originalScene = tables.TbPortal.Get("portal_mirror_jingyang_to_fangshi").TargetScene;
            string originalSpawn = tables.TbPortal.Get("portal_mirror_jingyang_to_fangshi").TargetSpawnId;
            SetPortalField("portal_mirror_jingyang_to_fangshi", "TargetScene", "human_jingyang");
            SetPortalField("portal_mirror_jingyang_to_fangshi", "TargetSpawnId", "jingyang_street");
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "原地打转"), Is.True, result.Describe());
            }
            finally
            {
                SetPortalField("portal_mirror_jingyang_to_fangshi", "TargetScene", originalScene);
                SetPortalField("portal_mirror_jingyang_to_fangshi", "TargetSpawnId", originalSpawn);
            }
        }

        [Test]
        public void BrokenRegion_MainMapMissingSceneKey_IsReported()
        {
            // 负对照：主图区域不写 scene_key。
            string originalSceneKey = tables.TbRegion.Get("jingyang_well").SceneKey;
            SetRegionField("jingyang_well", "SceneKey", string.Empty);
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "必须写 scene_key"), Is.True, result.Describe());
            }
            finally
            {
                SetRegionField("jingyang_well", "SceneKey", originalSceneKey);
            }
        }

        [Test]
        public void BrokenRegion_SingleSceneCarryingSceneKey_IsReported()
        {
            // 负对照：单场景专属地点（堤坝、坟地这些只在一界出现的）不许挂到某张图上。
            string originalSceneKey = tables.TbRegion.Get("jingyang_dike").SceneKey;
            SetRegionField("jingyang_dike", "SceneKey", "human_jingyang");
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "必须是空串"), Is.True, result.Describe());
            }
            finally
            {
                SetRegionField("jingyang_dike", "SceneKey", originalSceneKey);
            }
        }

        [Test]
        public void BrokenScene_RegionNameCountMismatch_IsReported()
        {
            // 负对照：region_ids 与 region_names 对不上。
            List<string> originalNames = tables.TbScene.Get("human_jingyang").RegionNames;
            originalNames.RemoveAt(0);
            try
            {
                WorldValidationResult result = WorldCatalogValidator.Validate(tables);
                Assert.That(result.Passed, Is.False);
                Assert.That(Contains(result, "一一对应"), Is.True, result.Describe());
            }
            finally
            {
                originalNames.Insert(0, "街面·角色可移动范围");
            }
        }

        [Test]
        public void BrokenData_StopsTheCatalogFromReading()
        {
            // 负对照：表坏了之后 **WorldCatalog 整张表都读不出来**（不是只坏那一行），
            // 免得半份数据被当成正常表用。
            string original = tables.TbRegion.Get("fangshi_street").MirrorRegionId;
            SetRegionField("fangshi_street", "MirrorRegionId", "nowhere_region");
            try
            {
                catalog.Invalidate();

                // 用块体 lambda：() => catalog.IsReady 的 lambda 体是「成员访问表达式」，
                // 在「委托返回 void」的重载下不是合法语句（CS0201）。
                // 这条断言是「读表那次校验会抛」，抛点在 IsReady 内部，所以照样落在 IsReady 上。
                ArgumentException error = Assert.Throws<ArgumentException>(() => { _ = catalog.IsReady; });
                Assert.That(error.Message, Does.Contain("world.xml"), "报错要指到列定义的出处");
            }
            finally
            {
                SetRegionField("fangshi_street", "MirrorRegionId", original);
                catalog.Invalidate();
            }

            Assert.That(catalog.IsReady, Is.True, "换回来之后照常可读");
            Assert.That(catalog.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Catalog_NotInitialized_ReportsNotReadyInsteadOfThrowing()
        {
            // 负对照：配置服务还没初始化完时，IsReady 是 false 而不是抛异常（同 YaoCatalog 的容错）。
            var notReady = new WorldCatalog(new NotReadyConfigService());
            Assert.That(notReady.IsReady, Is.False);
            Assert.That(notReady.Count, Is.EqualTo(0));
            Assert.That(notReady.TryGetScene("human_jingyang", out global::cfg.world.Scene scene), Is.False);
            Assert.That(scene, Is.Null);
            Assert.That(notReady.AllScenes, Is.Empty);
            Assert.That(notReady.Validate().Passed, Is.False, "读不到表时校验要给出一条问题，而不是假装通过");
        }

        // ---------------------------------------------------------------- 辅助

        private void AssertMissingMirrorProblems()
        {
            // 「两界同构断了一半」有几种写法（不写、写了个不存在的、对方不互指、指到同一界），
            // 断言只要求其中任意一条被报出来，别把测试钉在某一句文案上。
            WorldValidationResult result = WorldCatalogValidator.Validate(tables);
            Assert.That(result.Passed, Is.False);
            Assert.That(Contains(result, "mirror_region_id") || Contains(result, "对应关系") || Contains(result, "同一个世界"),
                Is.True, result.Describe());
        }

        private static bool Contains(WorldValidationResult result, string fragment)
        {
            for (int i = 0; i < result.Problems.Count; i++)
            {
                if (result.Problems[i].Message.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        // 生成代码的字段是 readonly，测试里要改只能反射写回，用完立刻还原。
        private void SetSceneField(string sceneKey, string field, object value) =>
            SetField(typeof(global::cfg.world.Scene), tables.TbScene.Get(sceneKey), field, value);

        private void SetRegionField(string regionId, string field, object value) =>
            SetField(typeof(global::cfg.world.Region), tables.TbRegion.Get(regionId), field, value);

        private void SetPortalField(string portalId, string field, object value) =>
            SetField(typeof(global::cfg.world.Portal), tables.TbPortal.Get(portalId), field, value);

        private static void SetField(Type type, object row, string field, object value) =>
            type.GetField(field).SetValue(row, value);

        /// <summary>只递一份现成的 <c>cfg.Tables</c>，不拉 Addressables。</summary>
        private sealed class TableStubConfigService : IConfigService
        {
            public TableStubConfigService(global::cfg.Tables tables) => Tables = tables;

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>模拟「配置服务还没初始化完」——访问 Tables 就抛，与 ConfigService.EnsureInitialized 一致。</summary>
        private sealed class NotReadyConfigService : IConfigService
        {
            public global::cfg.Tables Tables =>
                throw new InvalidOperationException("配置表还没初始化完。");

            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
