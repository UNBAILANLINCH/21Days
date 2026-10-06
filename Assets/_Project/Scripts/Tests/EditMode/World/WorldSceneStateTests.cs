// 职责：钉住世界场景状态的判定层——按待处理转场取地址、没有转场就报错、出生点四种组合从状态侧的调用、
//   放置失败与作用域绑定/解绑（含「不清存档进度」这条负对照）。
// 为什么新建：PRP/world-scenes §2.1 把「一个状态 + 一个待处理转场」定为核心决策，
//   而这些判定在 EditMode 里加载不了场景、跑不到 OnSceneReadyAsync，所以状态把它们开成了 EnterScene()。
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Game.Core.Save;
using Game.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="WorldSceneState"/> 的 EditMode 测试。</summary>
    public sealed class WorldSceneStateTests
    {
        private const string SceneA = "fake_scene_a";
        private const string SceneB = "fake_scene_b";
        private const string AddressA = "FakeAddressA";
        private const string AddressB = "FakeAddressB";
        private const string DefaultA = "a_default";
        private const string NamedA = "a_named";

        private WorldTestSupport.InMemorySaveService saves;
        private WorldTestSupport.UnusedAssets assets;
        private RecordingPlacement placement;
        private WorldCatalog catalog;
        private WorldTransition transition;
        private WorldSceneState state;

        [SetUp]
        public void SetUp()
        {
            saves = new WorldTestSupport.InMemorySaveService();
            assets = new WorldTestSupport.UnusedAssets();
            placement = new RecordingPlacement();

            // 两张「已实装且地址非空」的假场景：真表现状是两张图都没实装（implemented=false），
            // 成功路径只能自己造；两张图才能验「同一实体 id 各记一份」。
            catalog = WorldTestSupport.CatalogWithFakeScenes(
                new WorldTestSupport.FakeScene(SceneA, true, AddressA, DefaultA, DefaultA, NamedA),
                new WorldTestSupport.FakeScene(SceneB, true, AddressB, "b_default", "b_default", "b_named"));
            transition = new WorldTransition(catalog);
            state = new WorldSceneState(assets, transition, catalog, placement, saves);
        }

        // ---------------------------------------------------------------- 地址从待处理转场读

        [Test]
        public void SceneAddress_WithPendingTransition_ReadsTheTableAddress()
        {
            Request(SceneA);
            Assert.That(state.SceneAddress, Is.EqualTo(AddressA));

            // 换一个目标：地址跟着换（证明它不是常量，这就是「一个状态服务所有世界场景」的判据）。
            transition.Clear();
            Request(SceneB);
            Assert.That(state.SceneAddress, Is.EqualTo(AddressB));
        }

        [Test]
        public void SceneAddress_WithoutPendingTransition_Throws()
        {
            // PRP §2.1 二选一里选「报错」：悄悄进一张默认图，会把「本来要去妖界却站在人间」表现成一张正常画面。
            WorldResolveException error = Assert.Throws<WorldResolveException>(() => ReadAddress(state));

            Assert.That(error.Message, Does.Contain("没有待处理转场"));
            Assert.That(error.Message, Does.Contain("WorldSceneState"));
            Assert.That(error.Message, Does.Contain("不以常量地址兜底"), "报错里写清这是刻意选择，不是漏了默认值");
        }

        [Test]
        public void SceneAddress_NotImplementedScene_ThrowsPointingAtImplementedColumn()
        {
            // 两个状态各验一遍：未实装的场景拿不到地址（这一支原来靠「真表恰好都没实装」，接线波把两张图
            // 都实装了，所以改用假表造未实装的那一行）；已实装的场景地址来自表里的 scene_address。
            WorldCatalog fakeCatalog = WorldTestSupport.CatalogWithFakeScenes(
                new WorldTestSupport.FakeScene("fake_unimplemented", false, string.Empty, "fake_spawn", "fake_spawn"),
                new WorldTestSupport.FakeScene("fake_implemented", true, "FakeImplementedAddress", "fake_spawn", "fake_spawn"));
            var fakeTransition = new WorldTransition(fakeCatalog);
            var fakeState = new WorldSceneState(assets, fakeTransition, fakeCatalog, placement, saves);

            fakeTransition.Request(new WorldTransitionRequest("fake_unimplemented"));
            WorldResolveException error = Assert.Throws<WorldResolveException>(() => ReadAddress(fakeState));

            Assert.That(error.Message, Does.Contain("fake_unimplemented"));
            Assert.That(error.Message, Does.Contain("implemented=false"));

            fakeTransition.Clear();
            fakeTransition.Request(new WorldTransitionRequest("fake_implemented"));
            Assert.That(ReadAddress(fakeState), Is.EqualTo("FakeImplementedAddress"), "实装的那一支照常给出地址");
        }

        [Test]
        public void SceneAddress_ReadTwice_DoesNotConsumeTheTransition()
        {
            // 基类在 EnterAsync 里会读两次 SceneKey（加载 + 重复进入的 Warn），而真正的消费在 OnSceneReadyAsync。
            Request(SceneA);

            Assert.That(state.SceneAddress, Is.EqualTo(AddressA));
            Assert.That(state.SceneAddress, Is.EqualTo(AddressA));
            Assert.That(transition.HasPending, Is.True, "读地址不能把待处理转场吃掉");

            Assert.That(state.EnterScene().Success, Is.True, "读地址之后照样能进场景");
        }

        // ---------------------------------------------------------------- 进入：消费 / 失败档位

        [Test]
        public void EnterScene_ConsumesTransition_AndBindsScope()
        {
            Request(SceneA, NamedA, WorldRules.ArrivalMirror, "portal_x");

            WorldSceneEntry entry = state.EnterScene();

            Assert.That(entry.Success, Is.True);
            Assert.That(entry.Spawn.SceneKey, Is.EqualTo(SceneA));
            Assert.That(entry.Spawn.SpawnId, Is.EqualTo(NamedA));
            Assert.That(entry.Spawn.UsedFallback, Is.False);
            Assert.That(entry.Request.ArrivalMethod, Is.EqualTo(WorldRules.ArrivalMirror));
            Assert.That(transition.HasPending, Is.False, "消费即清空");
            Assert.That(state.Scope, Is.Not.Null);
            Assert.That(state.Scope.SceneKey, Is.EqualTo(SceneA));
            Assert.That(state.Spawn, Is.SameAs(entry.Spawn));
        }

        [Test]
        public void EnterScene_NoPendingTransition_ReportsNoPendingTransition()
        {
            WorldSceneEntry entry = state.EnterScene();

            Assert.That(entry.Success, Is.False);
            Assert.That(entry.Failure, Is.EqualTo(WorldSceneEntryFailure.NoPendingTransition));
            Assert.That(entry.Spawn, Is.Null);
            Assert.That(entry.Request, Is.Null, "压根没有目标，没东西可带回来");
            Assert.That(state.Scope, Is.Null);
            Assert.That(placement.Calls, Is.EqualTo(0), "目标都没定，不该调放置策略");
        }

        [Test]
        public void EnterScene_NotImplementedScene_ReportsSceneNotLoadable()
        {
            // 转场层的「未实装 / 不在表里 / 表没就绪」在状态侧统一映射成 SceneNotLoadable（调用方的处置相同：这张图先不进）。
            // 同上一处：未实装那一支改用假表造，并在同一个用例里补上「实装的那一支进得去」的对照。
            WorldCatalog fakeCatalog = WorldTestSupport.CatalogWithFakeScenes(
                new WorldTestSupport.FakeScene("fake_unimplemented", false, string.Empty, "fake_spawn", "fake_spawn"),
                new WorldTestSupport.FakeScene("fake_implemented", true, "FakeImplementedAddress", "fake_spawn", "fake_spawn"));
            var fakeTransition = new WorldTransition(fakeCatalog);
            var fakeState = new WorldSceneState(assets, fakeTransition, fakeCatalog, placement, saves);
            fakeTransition.Request(new WorldTransitionRequest("fake_unimplemented"));

            WorldSceneEntry entry = fakeState.EnterScene();

            Assert.That(entry.Success, Is.False);
            Assert.That(entry.Failure, Is.EqualTo(WorldSceneEntryFailure.SceneNotLoadable));
            Assert.That(entry.Error, Does.Contain("implemented=false"));
            Assert.That(entry.Request.SceneKey, Is.EqualTo("fake_unimplemented"), "失败也带回来「本来要去哪」");

            fakeTransition.Request(new WorldTransitionRequest("fake_implemented", "fake_spawn"));
            WorldSceneEntry ok = fakeState.EnterScene();

            Assert.That(ok.Success, Is.True, "同一段判定在实装的场景上必须放行：" + ok.Error);
            Assert.That(ok.Spawn.SceneKey, Is.EqualTo("fake_implemented"));
        }

        // ---------------------------------------------------------------- 出生点四种组合（从状态侧调用的那一层）

        [Test]
        public void EnterScene_NamedSpawnExists_UsesIt()
        {
            Request(SceneA, NamedA);

            WorldSceneEntry entry = state.EnterScene();

            Assert.That(entry.Success, Is.True);
            Assert.That(entry.Spawn.SpawnId, Is.EqualTo(NamedA));
            Assert.That(entry.Spawn.UsedFallback, Is.False);
            Assert.That(placement.LastSpawn.SpawnId, Is.EqualTo(NamedA), "交给放置策略的就是选中的那个落点");
        }

        [Test]
        public void EnterScene_SpawnLeftEmpty_UsesDefaultAndFlagsFallback()
        {
            Request(SceneA, string.Empty);

            WorldSceneEntry entry = state.EnterScene();

            Assert.That(entry.Spawn.SpawnId, Is.EqualTo(DefaultA));
            Assert.That(entry.Spawn.UsedFallback, Is.True, "没指名就是回退到默认出生点");
        }

        [Test]
        public void EnterScene_NamedSpawnMissing_FallsBackToDefault()
        {
            // 规则层（WorldRules 组已有用例）的核心口径：指名不存在时**先回退**，不直接失败。
            // 这里验的是「状态侧确实走了这条回退并把结果交出去」，不重复规则层的穷举。
            Request(SceneA, "nowhere");

            WorldSceneEntry entry = state.EnterScene();

            Assert.That(entry.Success, Is.True);
            Assert.That(entry.Spawn.SpawnId, Is.EqualTo(DefaultA));
            Assert.That(entry.Spawn.UsedFallback, Is.True);
            Assert.That(placement.LastSpawn.UsedFallback, Is.True, "回退标记要传给放置策略（它决定要不要播过渡表现）");
        }

        [Test]
        public void EnterScene_SpawnUnresolvable_ReportsSpawnUnresolved()
        {
            // 「有出生点但没有默认出生点」在**实装**场景上是表级非法的（校验器要求实装场景必须有默认出生点），
            // 所以这里先建合法假表再旁路校验清掉 default_spawn_id——验的是解析失败这一层怎么报（同 WorldRulesTests 的改列法）。
            WorldCatalog withoutDefault = WorldTestSupport.CatalogWithFakeScenes(
                new WorldTestSupport.FakeScene("fake_no_default", true, "AddrNoDefault", "sp_only", "sp_only"));
            typeof(global::cfg.world.Scene).GetField("DefaultSpawnId")
                .SetValue(withoutDefault.GetScene("fake_no_default"), string.Empty);
            var localTransition = new WorldTransition(withoutDefault);
            var localState = new WorldSceneState(assets, localTransition, withoutDefault, placement, saves);
            localTransition.Request(new WorldTransitionRequest("fake_no_default", "nowhere"));

            WorldSceneEntry entry = localState.EnterScene();

            Assert.That(entry.Success, Is.False);
            Assert.That(entry.Failure, Is.EqualTo(WorldSceneEntryFailure.SpawnUnresolved));
            Assert.That(entry.Error, Does.Contain("没有默认出生点"), "失败原因要说清是「没人可回退」");
            Assert.That(entry.Request, Is.Not.Null);
            Assert.That(localState.Scope, Is.Null, "没落点就不绑作用域");
            Assert.That(placement.Calls, Is.EqualTo(0), "出生点都没选出来，不该调放置策略");
        }

        // ---------------------------------------------------------------- 放置策略

        [Test]
        public void EnterScene_PlacementRefused_ReportsPlacementFailed()
        {
            placement.Result = false;
            placement.Reason = "场景里没有锚点物体 a_default";
            Request(SceneA);

            WorldSceneEntry entry = state.EnterScene();

            Assert.That(entry.Success, Is.False);
            Assert.That(entry.Failure, Is.EqualTo(WorldSceneEntryFailure.PlacementFailed));
            Assert.That(entry.Error, Is.EqualTo(placement.Reason), "放置策略给的原因原样带出来（谁修谁知道缺什么）");
            Assert.That(entry.Spawn, Is.Not.Null, "落点已经选出来了，失败的是「摆」这一步");
            Assert.That(entry.Spawn.SpawnId, Is.EqualTo(DefaultA), "带回来的就是解析出的那个落点，不是随便一个");
            Assert.That(state.Scope, Is.Null, "没摆成就不绑作用域");
            Assert.That(transition.HasPending, Is.False, "失败也已经消费掉待处理转场，不会卡在待处理位上");
        }

        [Test]
        public void UnwiredSpawnPlacement_RefusesWithActionableReason()
        {
            var unwired = new UnwiredSpawnPlacement();
            var spawn = new WorldSpawnTarget(SceneA, DefaultA, true);

            LogAssert.Expect(LogType.Warning, new Regex("出生点放置未接线"));
            bool placed = unwired.TryPlace(spawn, new WorldTransitionRequest(SceneA), out string reason);

            Assert.That(placed, Is.False, "默认实现绝不当成摆成了（静默成功比崩更难查）");
            Assert.That(reason, Does.Contain(SceneA));
            Assert.That(reason, Does.Contain(DefaultA), "要说清该摆到哪个出生点");
            Assert.That(reason, Does.Contain("ISpawnPlacement"), "要说清下一步实现什么");
            Assert.That(reason, Does.Contain("WorldInstaller"), "要说清在哪一行换实现");
        }

        // ---------------------------------------------------------------- 跨场景状态（从状态侧）

        [Test]
        public void EnterScene_SameEntityIdInTwoScenes_IsRecordedSeparately()
        {
            Request(SceneA);
            Assert.That(state.EnterScene().Success, Is.True);
            Assert.That(state.Scope.SceneKey, Is.EqualTo(SceneA));
            Assert.That(state.Scope.MarkCrateOpened("crate_1"), Is.True);

            Request(SceneB);
            Assert.That(state.EnterScene().Success, Is.True);

            Assert.That(state.Scope.SceneKey, Is.EqualTo(SceneB), "换图之后作用域跟着换");
            Assert.That(state.Scope.IsCrateOpened("crate_1"), Is.False, "同一实体 id 在另一张图是另一份状态");
            Assert.That(state.Scope.MarkCrateOpened("crate_1"), Is.True);

            List<string> stored = saves.Get<WorldSaveData>().CollectedCrates;
            Assert.That(stored, Is.EquivalentTo(new[]
            {
                SceneStateKey.Scoped(SceneA, "crate_1"),
                SceneStateKey.Scoped(SceneB, "crate_1"),
            }), "两张图各记一份，谁的记录都不覆盖谁");
        }

        [Test]
        public void ExitAsync_UnbindsScopeButKeepsSavedProgress()
        {
            Request(SceneA);
            state.EnterScene();
            state.Scope.MarkCrateOpened("crate_1");

            state.ExitAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(state.Scope, Is.Null, "卸载即解绑（谁再拿 Scope 用会拿到 null，那是「这张图不在了」的显式表达）");
            Assert.That(state.Spawn, Is.Null);
            Assert.That(saves.Get<WorldSaveData>().CollectedCrates,
                Does.Contain(SceneStateKey.Scoped(SceneA, "crate_1")),
                "**进度不清**：WorldSaveData 是存档，不是场景的运行期状态");
        }

        [Test]
        public void EnterScene_FailedReentry_DoesNotKeepThePreviousScope()
        {
            Request(SceneA);
            state.EnterScene();
            Assert.That(state.Scope, Is.Not.Null);

            // 第二次没有待处理转场（模拟接线漏了写目标）：不能留着上一张图的作用域，否则箱子状态会串图。
            WorldSceneEntry second = state.EnterScene();

            Assert.That(second.Success, Is.False);
            Assert.That(state.Scope, Is.Null);
            Assert.That(state.Spawn, Is.Null);
        }

        // ---------------------------------------------------------------- 构造参数

        [Test]
        public void Constructor_NullDependencies_Throw()
        {
            Assert.That(() => new WorldSceneState(null, transition, catalog, placement, saves), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new WorldSceneState(assets, null, catalog, placement, saves), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new WorldSceneState(assets, transition, null, placement, saves), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new WorldSceneState(assets, transition, catalog, null, saves), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new WorldSceneState(assets, transition, catalog, placement, null), Throws.TypeOf<ArgumentNullException>());
        }

        // ---------------------------------------------------------------- 辅助

        private void Request(string sceneKey, string spawnId = null, string arrival = null, string portalId = null) =>
            transition.Request(new WorldTransitionRequest(sceneKey, spawnId, arrival, portalId));

        // 属性 getter 会抛，必须要有个方法体才能当 TestDelegate 用。
        private static string ReadAddress(WorldSceneState target) => target.SceneAddress;

        /// <summary>记录型放置策略：验「状态有没有把落点交出去」以及「策略说摆不成时状态怎么报」。</summary>
        private sealed class RecordingPlacement : ISpawnPlacement
        {
            /// <summary>策略的结论；默认 true（摆成了）。</summary>
            public bool Result { get; set; } = true;

            /// <summary>说摆不成时给的原因。</summary>
            public string Reason { get; set; } = "假策略：摆不成";

            /// <summary>被调了几次。</summary>
            public int Calls { get; private set; }

            /// <summary>最后一次拿到的落点。</summary>
            public WorldSpawnTarget LastSpawn { get; private set; }

            /// <summary>最后一次拿到的转场来源。</summary>
            public WorldTransitionRequest LastRequest { get; private set; }

            public bool TryPlace(WorldSpawnTarget spawn, WorldTransitionRequest request, out string reason)
            {
                Calls++;
                LastSpawn = spawn;
                LastRequest = request;
                reason = Result ? string.Empty : Reason;
                return Result;
            }
        }
    }
}
