// 职责：钉住出生点选择的纯规则——各种组合（指名 / 留空 / 不存在 / 场景不存在 / 场景没有默认出生点 /
//   场景一个出生点都没有）分别返回什么，以及「缺失时先回退、回退不了才失败」这条口径的负对照。
// 为什么新建：规则与表适配分了两个类（WorldRules / WorldCatalog），规则要按组合穷举，而真表里只有
//   「有名有默认」这一种组合。所以「真表里没有的形状」一律**手写一份最小假表**（照
//   QuestCatalogTests.Content_TalkToDialogueMissing_Throws 的既有做法）：零出生点、有出生点但没默认出生点，
//   都做成「未实装 + 无地址」的表级合法形状。
//   **不再用「反射清掉真表某一列」那条路**：那要求真表那一行恰好是 implemented=false 才仍然合法，
//   接线波把两张主图改成 implemented=true 之后，同一个动作会先撞上校验器（「实装必须有默认出生点」），
//   而那是它**该拦**的表级错误，不是本用例要测的解析期行为。
using System;
using System.Collections.Generic;
using Game.Core.Config;
using Game.World;
using Game.Tests.EditMode.Core;
using Luban;
using NUnit.Framework;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="WorldRules"/> 的 EditMode 测试。</summary>
    public sealed class WorldRulesTests
    {
        /// <summary>真表里的场景键（Tables/Data/world/scene/human_jingyang.json）。</summary>
        private const string Scene = "human_jingyang";

        private global::cfg.Tables tables;

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
        }

        // ---------------------------------------------------------------- 指名的出生点

        [Test]
        public void Resolve_WhenSpawnExists_UsesIt()
        {
            WorldSpawnTarget target = WorldRules.Resolve(BuildCatalog(), Scene, "jingyang_yamen", WorldRules.ArrivalMirror);

            Assert.That(target.SceneKey, Is.EqualTo(Scene));
            Assert.That(target.SpawnId, Is.EqualTo("jingyang_yamen"));
            Assert.That(target.UsedFallback, Is.False, "指名的出生点存在，不该走回退");
        }

        [Test]
        public void Resolve_WhenSpawnIsEmpty_UsesDefaultAndFlagsFallback()
        {
            WorldSpawnTarget target = WorldRules.Resolve(BuildCatalog(), Scene, string.Empty, WorldRules.ArrivalDefault);

            Assert.That(target.SpawnId, Is.EqualTo("jingyang_street"));
            Assert.That(target.UsedFallback, Is.True, "没指名就是回退到默认出生点");
        }

        [Test]
        public void Resolve_WhenSpawnIsNull_UsesDefault()
        {
            // null 与空串走同一条路（调用方从表里读出来可能是 null）。
            WorldSpawnTarget target = WorldRules.Resolve(BuildCatalog(), Scene, null, WorldRules.ArrivalDefault);
            Assert.That(target.SpawnId, Is.EqualTo("jingyang_street"));
            Assert.That(target.UsedFallback, Is.True);
        }

        [Test]
        public void Resolve_WhenSpawnDoesNotExist_FallsBackToDefault()
        {
            // 这是本规则的核心口径：**指名的出生点不存在时先回退，不直接失败**。
            WorldSpawnTarget target = WorldRules.Resolve(BuildCatalog(), Scene, "nowhere", WorldRules.ArrivalMirror);

            Assert.That(target.SpawnId, Is.EqualTo("jingyang_street"), "回退到默认出生点");
            Assert.That(target.UsedFallback, Is.True);
        }

        // ---------------------------------------------------------------- 失败路径（负对照）
        //
        // 下面几条「没有默认出生点」的形状一律用手写假表（`scene_no_default`，未实装 + 无地址 +
        // 有出生点但无 default_spawn_id —— 表级合法）。
        // **原来这几条是反射清掉真表 human_jingyang 的 default_spawn_id、靠「真表恰好 implemented=false」
        // 让这张表仍然合法**；接线波把两张主图改成 implemented=true 之后，同一个动作会先撞上校验器的
        // 「实装必须有默认出生点」——那是它**该拦**的表级错误，不是本用例要测的解析期行为。
        // 换成假表之后这几条不再依赖真表的实装状态。

        [Test]
        public void TryResolveSpawn_WhenSpawnMissingAndNoDefault_Fails()
        {
            SpawnResolution result = WorldRules.TryResolveSpawn(
                BuildNoDefaultCatalog(), NoDefaultScene, "nowhere", WorldRules.ArrivalMirror);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Target, Is.Null);
            Assert.That(result.Error, Does.Contain("nowhere"), "失败原因要点名是哪个出生点");
            Assert.That(result.Error, Does.Contain(NoDefaultScene), "也要点名是哪个场景");
        }

        [Test]
        public void TryResolveSpawn_WhenSpawnEmptyAndNoDefault_Fails()
        {
            SpawnResolution result = WorldRules.TryResolveSpawn(
                BuildNoDefaultCatalog(), NoDefaultScene, string.Empty, WorldRules.ArrivalDefault);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("没有默认出生点"));
        }

        [Test]
        public void TryResolveSpawn_FailureMessage_MentionsTheArrivalMethod()
        {
            string error = WorldRules.TryResolveSpawn(
                BuildNoDefaultCatalog(), NoDefaultScene, string.Empty, "teleport_v2").Error;
            Assert.That(error, Does.Contain("teleport_v2"), "失败原因要带上到达方式，方便查是哪条导线");
        }

        [Test]
        public void TryResolveSpawn_WhenSceneHasNoSpawnsAtAll_Fails()
        {
            // 负对照：场景一个出生点都没有时，指名与不指名都落不下去。
            // **不碰真表的行**：真表里没有「零出生点」这种形状，而未实装的场景仍然带着 default_spawn_id；
            // 若把共享行的 spawn_points 清空而不清 default_spawn_id，会先撞上 WorldCatalogValidator 的
            // 「default_spawn_id 不在 spawn_points 里」——那是它**该拦**的表级错误，不是本用例要测的解析期行为。
            // 所以这里手写一份最小表，把「零出生点」做成一个表级合法的形状（同样清空 default_spawn_id）。
            WorldCatalog catalog = BuildCatalogFromTables(WriteSceneTable(
                "scene_no_spawns", implemented: false, sceneAddress: string.Empty,
                defaultSpawnId: string.Empty, spawnPoints: new string[0]));

            Assert.That(WorldRules.TryResolveSpawn(catalog, "scene_no_spawns", string.Empty, WorldRules.ArrivalDefault).Success,
                Is.False, "不指名又没有默认出生点 → 失败");
            Assert.That(WorldRules.TryResolveSpawn(catalog, "scene_no_spawns", "anywhere", WorldRules.ArrivalDefault).Success,
                Is.False, "指名的出生点不存在 → 失败");
            Assert.That(catalog.Validate().Passed, Is.True, "这份假表本身是表级合法的（零出生点 + 无默认出生点 + 未实装）");
        }

        [Test]
        public void Resolve_WhenSceneHasSpawnsButNoDefault_SaysSoInsteadOfFallingSilently()
        {
            // 另一格：有出生点、但那一行没写 default_spawn_id。指名不可用时**没人可回退**，
            // 要说清是「没有默认出生点」，而不是静默落到某个坐标。
            WorldCatalog catalog = BuildCatalogFromTables(WriteSceneTable(
                "scene_no_default", implemented: false, sceneAddress: string.Empty,
                defaultSpawnId: string.Empty, spawnPoints: new[] { "only_spawn" }));

            SpawnResolution result = WorldRules.TryResolveSpawn(catalog, "scene_no_default", "nowhere", WorldRules.ArrivalDefault);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("没有默认出生点"));

            // 指名那个存在的出生点时照常能用：错误只发生在「指名不可用 + 无默认」这一格。
            Assert.That(WorldRules.Resolve(catalog, "scene_no_default", "only_spawn", WorldRules.ArrivalDefault).SpawnId,
                Is.EqualTo("only_spawn"));
            Assert.That(WorldRules.TryResolveSpawn(catalog, "scene_no_default", string.Empty, WorldRules.ArrivalDefault).Success,
                Is.False, "不指名、又没有默认出生点 → 失败");
        }

        // ---------------------------------------------------------------- 未知场景 / 参数

        [Test]
        public void Resolve_WhenUnknownScene_ThrowsPointingAtTheTable()
        {
            WorldResolveException error = Assert.Throws<WorldResolveException>(
                () => WorldRules.Resolve(BuildCatalog(), "nowhere_scene", "jingyang_street", WorldRules.ArrivalDefault));

            Assert.That(error.Message, Does.Contain("nowhere_scene"), "报错要点名是哪个场景");
            Assert.That(error.Message, Does.Contain("TbScene"), "报错要指到哪张表");
        }

        [Test]
        public void TryResolveSpawn_WhenSceneKeyEmpty_FailsInsteadOfThrowing()
        {
            SpawnResolution result = WorldRules.TryResolveSpawn(BuildCatalog(), string.Empty, "jingyang_street", WorldRules.ArrivalDefault);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("目标场景键"));
        }

        [Test]
        public void Resolve_WhenCatalogIsNull_ThrowsArgumentNull()
        {
            Assert.That(() => WorldRules.Resolve(null, Scene, "jingyang_street", WorldRules.ArrivalDefault),
                Throws.TypeOf<ArgumentNullException>());
        }

        // ---------------------------------------------------------------- 到达方式

        [Test]
        public void Resolve_ArrivalMethod_DoesNotChangeTheChosenSpawn()
        {
            // 口径钉死：到达方式**不是**查询条件（world.xml 的 arrival_method 列注释）。
            // 目标出生点由传送点那一行显式指定；两界转场在真源里只有一条（10:148 R20），
            // 「从哪个传送点来」决定落点没有依据。将来策划拍板要按到达方式分岔，这条测试会先挂。
            WorldCatalog catalog = BuildCatalog();
            foreach (string arrival in new[] { WorldRules.ArrivalDefault, WorldRules.ArrivalMirror, "whatever" })
            {
                WorldSpawnTarget target = WorldRules.Resolve(catalog, Scene, "jingyang_yamen", arrival);
                Assert.That(target.SpawnId, Is.EqualTo("jingyang_yamen"), $"到达方式「{arrival}」不该改变指名的落点");

                WorldSpawnTarget fallback = WorldRules.Resolve(catalog, Scene, "nowhere", arrival);
                Assert.That(fallback.SpawnId, Is.EqualTo("jingyang_street"), $"到达方式「{arrival}」不该改变回退的落点");
            }
        }

        // ---------------------------------------------------------------- 辅助

        /// <summary>用当前这份真表建一个目录。</summary>
        private WorldCatalog BuildCatalog() => new WorldCatalog(new RulesStubConfigService(tables));

        /// <summary>「有出生点、但没有 default_spawn_id」的假场景键（表级合法：未实装 + 无地址 + 无默认出生点）。</summary>
        private const string NoDefaultScene = "scene_no_default";

        /// <summary>「有出生点、没默认出生点」的表级合法假表（负对照用，不碰真表的行）。</summary>
        private static WorldCatalog BuildNoDefaultCatalog() => BuildCatalogFromTables(WriteSceneTable(
            NoDefaultScene, implemented: false, sceneAddress: string.Empty,
            defaultSpawnId: string.Empty, spawnPoints: new[] { "only_spawn" }));

        /// <summary>用手写的假表建一个目录：只给 TbScene 一行，区域表与传送点表都是空表。</summary>
        private static WorldCatalog BuildCatalogFromTables(byte[] sceneTableBytes)
        {
            Dictionary<string, byte[]> bytes = ConfigServiceTests.ReadAllTableBytes();
            bytes["world_tbscene"] = sceneTableBytes;
            bytes["world_tbregion"] = WriteEmptyTable();
            bytes["world_tbportal"] = WriteEmptyTable();
            return new WorldCatalog(new RulesStubConfigService(ConfigService.BuildTables(bytes)));
        }

        /// <summary>
        /// 按 <c>cfg.world.TbScene</c> / <c>Scene</c> 构造函数的读取顺序手写一张只含一行的场景表。
        /// 生成代码的字段顺序变了这里要跟着改（届时这条测试会先在反序列化处炸，不会静默误判）。
        /// </summary>
        private static byte[] WriteSceneTable(string sceneKey, bool implemented, string sceneAddress,
            string defaultSpawnId, string[] spawnPoints)
        {
            var buf = new ByteBuf();
            buf.WriteSize(1);
            buf.WriteString(sceneKey);
            buf.WriteString("假场景");
            buf.WriteInt((int)global::cfg.world.WorldId.Human);
            buf.WriteString("fake_world");
            buf.WriteBool(implemented);
            buf.WriteString(sceneAddress);
            buf.WriteSize(0); // region_ids
            buf.WriteSize(spawnPoints.Length);
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                buf.WriteString(spawnPoints[i]);
            }

            buf.WriteString(defaultSpawnId);
            buf.WriteSize(0); // portal_ids
            buf.WriteSize(0); // region_names
            buf.WriteString("测试用假场景");
            return buf.CopyData();
        }

        /// <summary>一张零行的表（Luban 的行数前缀写成 0）。</summary>
        private static byte[] WriteEmptyTable()
        {
            var buf = new ByteBuf();
            buf.WriteSize(0);
            return buf.CopyData();
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>。</summary>
        private sealed class RulesStubConfigService : IConfigService
        {
            public RulesStubConfigService(global::cfg.Tables tables) => Tables = tables;

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
