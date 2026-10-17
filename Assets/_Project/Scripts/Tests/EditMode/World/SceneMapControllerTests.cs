// 职责：小地图加载、失败和销毁不改变输入，标题不显示地图；成功路径由真实流程回放覆盖。
// 为什么新建：现有面板测试不能复用地图会话，纯打开门槛也不能验证异步失败的收尾。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.UI;
using Game.Interaction;
using Game.World;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.World
{
    public sealed class SceneMapControllerTests
    {
        private GameObject actorNode;
        private InputService input;
        private InteractionRegistry registry;
        private SceneMapController controller;
        private TestUi ui;
        private TestFlow flow;

        [SetUp]
        public void SetUp()
        {
            actorNode = new GameObject("地图玩家测试");
            var actor = actorNode.AddComponent<InteractionActor>();
            registry = new InteractionRegistry(() => actor);
            registry.SetActor(actor);
            input = new InputService();
            input.InitializeAsync(default).GetAwaiter().GetResult();
            ui = new TestUi();
            flow = new TestFlow { Current = new TestSceneState() };
            controller = new SceneMapController(ui, input, registry, flow,
                new WorldCatalog(new WorldTestSupport.StubConfigService(null)));
        }

        [TearDown]
        public void TearDown()
        {
            controller.Dispose();
            registry.Dispose();
            input.Dispose();
            UnityEngine.Object.DestroyImmediate(actorNode);
        }

        [Test]
        public void OpenAsync_ViewLoadFails_DoesNotChangeGameplay()
        {
            Assert.Throws<InvalidOperationException>(() => controller.OpenAsync().GetAwaiter().GetResult());
            Assert.That(input.Actions.Gameplay.enabled, Is.True);
            Assert.That(controller.IsOpen, Is.False);
            Assert.Throws<InvalidOperationException>(() => controller.OpenAsync().GetAwaiter().GetResult());
            Assert.That(ui.OpenCalls, Is.EqualTo(2), "失败不能永久卡住 busy 位");
        }

        [Test]
        public void OpenAsync_GameplayWasDisabled_DoesNotEnableIt()
        {
            input.DisableMap(InputService.GameplayMap);
            Assert.Throws<InvalidOperationException>(() => controller.OpenAsync().GetAwaiter().GetResult());
            Assert.That(ui.OpenCalls, Is.EqualTo(1));
            Assert.That(input.Actions.Gameplay.enabled, Is.False);
        }

        [Test]
        public void OpenAsync_NoActor_DoesNotOpenAMapAtTheTitle()
        {
            registry.SetActor(null);
            controller.OpenAsync().GetAwaiter().GetResult();
            Assert.That(ui.OpenCalls, Is.Zero);
        }

        [Test]
        public void Dispose_WhileLoading_ClosesLateViewWithoutChangingInput()
        {
            ui.Pending = new UniTaskCompletionSource<SceneMapView>();
            UniTask loading = controller.OpenAsync();
            Assert.That(input.Actions.Gameplay.enabled, Is.True);
            controller.Dispose();
            Assert.That(input.Actions.Gameplay.enabled, Is.True);
            var node = new GameObject("晚到的小地图", typeof(RectTransform));
            try
            {
                ui.Pending.TrySetResult(node.AddComponent<SceneMapView>());
                loading.GetAwaiter().GetResult();
                Assert.That(ui.CloseCalls, Is.EqualTo(1));
                Assert.That(controller.IsOpen, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(node); }
        }

        [Test]
        public void Tick_AfterLoadFailure_DoesNotRetryEveryFrame()
        {
            Assert.Throws<InvalidOperationException>(() => controller.OpenAsync().GetAwaiter().GetResult());
            controller.Tick();
            controller.Tick();
            Assert.That(ui.OpenCalls, Is.EqualTo(1));
        }

        [Test]
        public void OpenAsync_NoExplorationState_DoesNotShowAtTitle()
        {
            flow.Current = null;
            controller.OpenAsync().GetAwaiter().GetResult();
            Assert.That(ui.OpenCalls, Is.Zero);
        }

        private sealed class TestUi : IUIService
        {
            public int OpenCalls { get; private set; }
            public int CloseCalls { get; private set; }
            public UniTaskCompletionSource<SceneMapView> Pending { get; set; }
            public async UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                OpenCalls++;
                if (Pending == null) throw new InvalidOperationException("测试预制体加载失败");
                return await Pending.Task as T;
            }
            public UniTask CloseAsync(UIView view, CancellationToken ct = default) { CloseCalls++; return UniTask.CompletedTask; }
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => null;
            public void SetLayerVisible(UILayer layer, bool visible) { }
            public bool IsLayerVisible(UILayer layer) => true;
        }

        private sealed class TestSceneState : SceneGameState
        {
            public TestSceneState() : base(new WorldTestSupport.UnusedAssets()) { }
            protected override string SceneKey => "测试场景";
        }

        private sealed class TestFlow : IGameFlow
        {
            public GameState Current { get; set; }
            public UniTask GoToAsync<TState>(CancellationToken ct = default) where TState : GameState => UniTask.CompletedTask;
        }
    }
}
