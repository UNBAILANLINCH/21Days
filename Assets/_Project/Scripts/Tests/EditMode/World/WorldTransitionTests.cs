// 职责：钉住待处理转场服务——一次只挂一个、重复请求后到覆盖并 Warn、消费即清空、四类失败原因各报各的。
// 为什么新建：这是 PRP/world-scenes §2.1 核心决策的落点（一个状态 + 一个待处理转场），
//   而它最容易出的两种错是「连点传送点就崩」与「失败的请求还留在待处理位上，下次进图站到上一张图的落点」。
using System;
using System.Text.RegularExpressions;
using Game.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="WorldTransition"/> / <see cref="WorldTransitionRequest"/> 的 EditMode 测试。</summary>
    public sealed class WorldTransitionTests
    {
        /// <summary>假表里那张「已实装且地址非空」的场景——真表现状是两张图都没实装，成功路径只能自己造。</summary>
        private const string ReadyScene = "fake_ready_scene";

        private const string ReadyAddress = "FakeReadySceneAddress";

        /// <summary>真表目录：两张场景都存在，但都 implemented=false（真表现状）。</summary>
        private WorldTransition real;

        /// <summary>假表目录：一张已实装、地址非空的场景。</summary>
        private WorldTransition ready;

        [SetUp]
        public void SetUp()
        {
            real = new WorldTransition(WorldTestSupport.RealCatalog());
            ready = new WorldTransition(WorldTestSupport.CatalogWithFakeScenes(
                new WorldTestSupport.FakeScene(ReadyScene, true, ReadyAddress, "sp_a", "sp_a", "sp_b")));
        }

        // ---------------------------------------------------------------- 一次只挂一个

        [Test]
        public void Request_ThenCurrent_KeepsTheSinglePendingTarget()
        {
            ready.Request(new WorldTransitionRequest(ReadyScene, "sp_b", WorldRules.ArrivalMirror, "portal_x"));

            Assert.That(ready.HasPending, Is.True);
            Assert.That(ready.Current.SceneKey, Is.EqualTo(ReadyScene));
            Assert.That(ready.Current.SpawnId, Is.EqualTo("sp_b"), "落点原样挂着，取用时才算");
            Assert.That(ready.OverwrittenCount, Is.EqualTo(0), "第一次请求不算覆盖");
        }

        [Test]
        public void Request_Twice_LaterOverwritesEarlierAndWarns()
        {
            ready.Request(new WorldTransitionRequest(ReadyScene, "sp_a"));

            // 连点传送点：**不抛异常**，只留一条 Warn（PRP §2.1：玩家连点不该崩）。
            LogAssert.Expect(LogType.Warning, new Regex("转场请求被覆盖"));
            ready.Request(new WorldTransitionRequest(ReadyScene, "sp_b"));

            Assert.That(ready.OverwrittenCount, Is.EqualTo(1));
            Assert.That(ready.Current.SpawnId, Is.EqualTo("sp_b"), "后到的那条说了算");
            Assert.That(ready.HasPending, Is.True, "覆盖之后仍然只有一个待处理目标");

            WorldTransitionResolution consumed = ready.TryConsume();
            Assert.That(consumed.Success, Is.True);
            Assert.That(consumed.Request.SpawnId, Is.EqualTo("sp_b"), "取到的是后到那条，被顶掉的那条再也取不到");
        }

        [Test]
        public void Request_Once_DoesNotWarn()
        {
            // 负对照：只有重复请求才 Warn，单次请求不该刷日志（否则真出问题时淹没在噪声里）。
            ready.Request(new WorldTransitionRequest(ReadyScene));

            Assert.That(ready.OverwrittenCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Request_Null_Throws()
        {
            Assert.That(() => ready.Request(null), Throws.TypeOf<ArgumentNullException>());
        }

        // ---------------------------------------------------------------- 消费即清空

        [Test]
        public void TryConsume_ImplementedScene_ReturnsTargetAndClearsPending()
        {
            ready.Request(new WorldTransitionRequest(ReadyScene, "sp_b", WorldRules.ArrivalMirror, "portal_x"));

            WorldTransitionResolution consumed = ready.TryConsume();

            Assert.That(consumed.Success, Is.True);
            Assert.That(consumed.Failure, Is.EqualTo(WorldTransitionFailure.None));
            Assert.That(consumed.Error, Is.Empty, "成功不带失败原因");
            Assert.That(consumed.Request.SceneKey, Is.EqualTo(ReadyScene));

            // 清空的三种表现（重进时不会误用旧目标）。
            Assert.That(ready.HasPending, Is.False);
            Assert.That(ready.Current, Is.Null);
            Assert.That(ready.TryConsume().Failure, Is.EqualTo(WorldTransitionFailure.NoPending));
        }

        [Test]
        public void TryConsume_FailedRequest_IsAlsoCleared()
        {
            // 负对照的另一半：失败的请求**也不能留在待处理位上**，否则下一次进图会拿一个已作废的目标。
            // 这里用「表里没有的场景」当失败源，不用「未实装的场景」：后者会随真表的 implemented 列变化
            // （接线波把两张主图都实装了），那样这条用例验的就不再是「失败也清空」这条规则了。
            // 「未实装」那一支的失败由 TryConsume_NotImplementedScene_ReportsSceneNotImplemented 用假表覆盖。
            real.Request(new WorldTransitionRequest("no_such_scene_in_the_table"));

            WorldTransitionResolution first = real.TryConsume();
            Assert.That(first.Success, Is.False);
            Assert.That(first.Failure, Is.EqualTo(WorldTransitionFailure.SceneUnknown));
            Assert.That(first.Request.SceneKey, Is.EqualTo("no_such_scene_in_the_table"), "失败也把被丢弃的请求带回来，便于回查");

            Assert.That(real.HasPending, Is.False, "校验没过也要清空");
            WorldTransitionResolution second = real.TryConsume();
            Assert.That(second.Failure, Is.EqualTo(WorldTransitionFailure.NoPending), "第二次是「没有待处理」，不是重复上次的失败");
        }

        [Test]
        public void Clear_DropsPendingWithoutConsuming()
        {
            ready.Request(new WorldTransitionRequest(ReadyScene));

            ready.Clear();

            Assert.That(ready.HasPending, Is.False);
            Assert.That(ready.Current, Is.Null);
            Assert.That(ready.TryConsume().Failure, Is.EqualTo(WorldTransitionFailure.NoPending));
        }

        // ---------------------------------------------------------------- 取用前校验（SceneKey 用的就是不消费的那条）

        [Test]
        public void Peek_ValidatesWithoutConsuming()
        {
            ready.Request(new WorldTransitionRequest(ReadyScene));

            // WorldSceneState.SceneKey 靠 Peek 拿地址：它必须**不**把待处理转场吃掉，
            // 否则真正的消费（OnSceneReadyAsync 里那次）会扑空。
            Assert.That(ready.Peek().Success, Is.True);
            Assert.That(ready.HasPending, Is.True, "Peek 不消费");

            Assert.That(ready.TryConsume().Success, Is.True, "Peek 之后照样能消费");
        }

        [Test]
        public void Peek_NoPending_ReportsNoPending()
        {
            WorldTransitionResolution peeked = ready.Peek();

            Assert.That(peeked.Success, Is.False);
            Assert.That(peeked.Failure, Is.EqualTo(WorldTransitionFailure.NoPending));
            Assert.That(peeked.Request, Is.Null);
            Assert.That(peeked.Error, Is.Not.Empty, "失败原因必须能读出来");
        }

        // ---------------------------------------------------------------- 四类失败原因（各报各的修法）

        [Test]
        public void TryConsume_NoPending_ReportsNoPending()
        {
            WorldTransitionResolution consumed = ready.TryConsume();

            Assert.That(consumed.Failure, Is.EqualTo(WorldTransitionFailure.NoPending));
            Assert.That(consumed.Error, Does.Contain("没有待处理转场"));
            Assert.That(consumed.Error, Does.Contain("WorldSceneState"), "要说清谁会因为这条失败而进不去");
        }

        [Test]
        public void TryConsume_SceneNotInTable_ReportsSceneUnknownAndListsKnownKeys()
        {
            real.Request(new WorldTransitionRequest("nowhere_scene"));

            WorldTransitionResolution consumed = real.TryConsume();

            Assert.That(consumed.Failure, Is.EqualTo(WorldTransitionFailure.SceneUnknown));
            Assert.That(consumed.Error, Does.Contain("nowhere_scene"), "要点名是哪个场景");
            Assert.That(consumed.Error, Does.Contain("TbScene"), "要指到哪张表");
            Assert.That(consumed.Error, Does.Contain(WorldTestSupport.HumanScene), "顺带列出表里现有的场景键（写错一个字母是最常见的情形）");
            Assert.That(consumed.Error, Does.Contain(WorldTestSupport.YaoScene));
        }

        [Test]
        public void TryConsume_NotImplementedScene_ReportsSceneNotImplemented()
        {
            // 两个状态各验一遍。原来这一条靠「真表恰好两张图都 implemented=false」，接线波把两张主图都实装了，
            // 所以未实装那一支改用假表造；下面顺带补上「实装的那一支放行且答案正确」的对照。
            WorldCatalog fakeCatalog = WorldTestSupport.CatalogWithFakeScenes(
                new WorldTestSupport.FakeScene("fake_unimplemented", false, string.Empty, "fake_spawn", "fake_spawn"),
                new WorldTestSupport.FakeScene("fake_implemented", true, "FakeImplementedAddress", "fake_spawn", "fake_spawn"));
            var fake = new WorldTransition(fakeCatalog);
            fake.Request(new WorldTransitionRequest("fake_unimplemented", "fake_spawn", WorldRules.ArrivalMirror, "portal_x"));

            WorldTransitionResolution consumed = fake.TryConsume();

            Assert.That(consumed.Failure, Is.EqualTo(WorldTransitionFailure.SceneNotImplemented));
            Assert.That(consumed.Error, Does.Contain("implemented=false"));
            Assert.That(consumed.Error, Does.Contain("fake_unimplemented"));
            Assert.That(consumed.Error, Does.Contain("Tables/Data/world/scene/"), "修法要指到数据文件");

            fake.Request(new WorldTransitionRequest("fake_implemented", "fake_spawn"));
            WorldTransitionResolution ok = fake.TryConsume();

            Assert.That(ok.Success, Is.True, ok.Error);
            Assert.That(ok.Request.SceneKey, Is.EqualTo("fake_implemented"));
            Assert.That(fakeCatalog.SceneAddressOf("fake_implemented"), Is.EqualTo("FakeImplementedAddress"),
                "消费通过之后调用方就能按这个键取地址去加载");
        }

        [Test]
        public void TryConsume_ImplementedButAddressEmpty_ReportsSceneAddressMissing()
        {
            // 这个形状是**表级非法**的（校验器会报「实装无地址」），正常路径上 WorldCatalog 在读表时就抛了。
            // 所以这里先建一张表级合法的假表（未实装 + 无地址），再旁路校验把 implemented 翻成 true ——
            // 验的是「校验被绕过时这一层还拦不拦得住」，而不是表的合法形状。
            WorldCatalog catalog = WorldTestSupport.CatalogWithFakeScenes(
                new WorldTestSupport.FakeScene("fake_no_address", false, string.Empty, string.Empty));
            typeof(global::cfg.world.Scene).GetField("Implemented")
                .SetValue(catalog.GetScene("fake_no_address"), true);
            var transition = new WorldTransition(catalog);
            transition.Request(new WorldTransitionRequest("fake_no_address"));

            WorldTransitionResolution consumed = transition.TryConsume();

            Assert.That(consumed.Failure, Is.EqualTo(WorldTransitionFailure.SceneAddressMissing));
            Assert.That(consumed.Error, Does.Contain("scene_address"));
        }

        [Test]
        public void TryConsume_LegacyIsometricAddress_IsNotAWorldScene()
        {
            // 负对照的边界：遗留原型地址（MonsterEncounterState 的常量）不在 TbScene 里，
            // 走世界转场时会被判成「不在表里」——那是**对的**：世界转场只认表里的场景键，
            // 遗留路径不经这里（两条并存，见 PRP §4 风险表的处置）。
            real.Request(new WorldTransitionRequest("IsometricEncounter"));

            WorldTransitionResolution consumed = real.TryConsume();

            Assert.That(consumed.Failure, Is.EqualTo(WorldTransitionFailure.SceneUnknown));
        }

        // ---------------------------------------------------------------- 请求本身的形状

        [Test]
        public void Request_NormalizesArrivalMethodAndEmptyParts()
        {
            var minimal = new WorldTransitionRequest("a_scene");

            Assert.That(minimal.SpawnId, Is.Empty, "没写出生点 = 用目标场景的默认出生点");
            Assert.That(minimal.ArrivalMethod, Is.EqualTo(WorldRules.ArrivalDefault), "没写到达方式就是 default");
            Assert.That(minimal.PortalId, Is.Empty);
            Assert.That(minimal.FromPortal, Is.False);

            var fromPortal = new WorldTransitionRequest("a_scene", null, WorldRules.ArrivalMirror, "portal_x");
            Assert.That(fromPortal.FromPortal, Is.True);
            Assert.That(fromPortal.ArrivalMethod, Is.EqualTo(WorldRules.ArrivalMirror));
        }

        [Test]
        public void Request_EmptySceneKey_Throws()
        {
            // 空目标 = 没接线（同 PortalAnchor.CanTrigger 的口径）：不该悄悄传送到某处。
            Assert.That(() => new WorldTransitionRequest(string.Empty), Throws.TypeOf<ArgumentException>());
            Assert.That(() => new WorldTransitionRequest(null), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Request_Describe_MentionsPortalSceneSpawnAndArrival()
        {
            string text = new WorldTransitionRequest(ReadyScene, "sp_b", WorldRules.ArrivalMirror, "portal_x").Describe();

            Assert.That(text, Does.Contain("portal_x"));
            Assert.That(text, Does.Contain(ReadyScene));
            Assert.That(text, Does.Contain("sp_b"));
            Assert.That(text, Does.Contain(WorldRules.ArrivalMirror));
        }

        [Test]
        public void WorldTransition_NullCatalog_Throws()
        {
            Assert.That(() => new WorldTransition(null), Throws.TypeOf<ArgumentNullException>());
        }
    }
}
