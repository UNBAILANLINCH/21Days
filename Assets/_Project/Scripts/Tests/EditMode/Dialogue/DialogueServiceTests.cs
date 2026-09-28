// 职责：钉住 DialogueService 的会话级保护——重入拒绝、未知 id 不碰任何资源、展示异常时暂停令牌 / 规则状态 / 结束事件都收干净、
//   对白期间藏 Hud 层且结束按进来前的值恢复。
// 为什么新建：DialogueRulesTests 测状态机、DialogueCatalogTests 测内容翻译，二者都不涉及暂停令牌与输入图这类会话资源，
//   塞进去职责说不通；DialogueService 是新类，按「一个被测类一个测试类」新建。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Config;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Dialogue
{
    /// <summary>
    /// <see cref="DialogueService"/> 的 EditMode 测试。
    /// <para>
    /// 所有假服务都同步完成（或同步取消 / 抛出），整条 <c>PlayAsync</c> 链路不需要等帧；
    /// 每条用例都把自己产生的 UniTask 异常用 <see cref="Capture"/> 观察掉，不留未观察异常砸别的用例（见 pitfalls）。
    /// 默认输入服务的 <c>Actions</c> 为 null，同时覆盖「InputService 未初始化时不抛 NRE」的守卫；
    /// 动作图开关的用例经 <c>UseRealInput</c> 换成真实 <see cref="InputService"/>（外包一层记录调用顺序）。
    /// </para>
    /// </summary>
    public sealed class DialogueServiceTests
    {
        private const int KnownId = 1001;
        private const int OtherId = 1002;
        private const int UnknownId = 9999;

        private FakeWorldPause pause;
        private FakeInput input;
        private FakeUIService ui;
        private DialogueRules rules;
        private DialogueConfig config;
        private DialogueService service;
        private DialogueCatalog catalog;
        private DialogueController controller;
        private InputService realInput;

        [SetUp]
        public void SetUp()
        {
            pause = new FakeWorldPause();
            input = new FakeInput();
            ui = new FakeUIService();
            rules = new DialogueRules(new DialogueReadData(), 500, null);
            config = ScriptableObject.CreateInstance<DialogueConfig>();
            catalog = new DialogueCatalog(new FakeConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes())));
            controller = new DialogueController(rules, catalog, ui, new FakeAssetService(), new FakeClock(), null);
            service = new DialogueService(catalog, rules, controller, config, new DefaultDialogueConditionSource(),
                pause, input, ui, null);
        }

        [TearDown]
        public void TearDown()
        {
            service.Dispose();
            realInput?.Dispose();
            realInput = null;
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void PlayAsync_WhileRunning_SwapsGameplayMapForDialogueMap()
        {
            RecordingInput recording = UseRealInput();
            Assert.That(realInput.Actions.Dialogue.enabled, Is.False, "启动时 Dialogue 图不应启用");
            Assert.That(realInput.Actions.Gameplay.enabled, Is.True);
            ui.Mode = FakeUIService.OpenMode.Pending;
            using var cts = new CancellationTokenSource();

            UniTask<DialogueResult> play = service.PlayAsync(KnownId, cts.Token);

            Assert.That(realInput.Actions.Dialogue.enabled, Is.True, "对白期间 Dialogue 图应启用");
            Assert.That(realInput.Actions.Gameplay.enabled, Is.False, "对白期间 Gameplay 图应关闭");

            cts.Cancel();
            Assert.That(Capture(play), Is.InstanceOf<OperationCanceledException>());
            Assert.That(realInput.Actions.Dialogue.enabled, Is.False, "取消后 Dialogue 图应关闭");
            Assert.That(realInput.Actions.Gameplay.enabled, Is.True, "取消后 Gameplay 图应恢复");
            Assert.That(recording.Calls, Is.EqualTo(new[]
            {
                "Disable:" + InputService.GameplayMap, "Enable:" + DialogueService.InputMap,
                "Disable:" + DialogueService.InputMap, "Enable:" + InputService.GameplayMap,
            }));
        }

        [Test]
        public void PlayAsync_WhenPresentThrows_ClosesDialogueMap()
        {
            RecordingInput recording = UseRealInput();
            ui.Mode = FakeUIService.OpenMode.Throw;

            Exception error = Capture(service.PlayAsync(KnownId));

            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(realInput.Actions.Dialogue.enabled, Is.False, "异常收尾后 Dialogue 图应关闭");
            Assert.That(realInput.Actions.Gameplay.enabled, Is.True, "异常收尾后 Gameplay 图应恢复");
            Assert.That(recording.Calls, Has.Member("Disable:" + DialogueService.InputMap));
        }

        [Test]
        public void PlayAsync_WhenGameplayWasDisabled_ClosesDialogueMapWithoutReopeningGameplay()
        {
            RecordingInput recording = UseRealInput();
            realInput.DisableMap(InputService.GameplayMap); // 调用方本来关着 Gameplay（如过场中）
            ui.Mode = FakeUIService.OpenMode.Throw;

            Capture(service.PlayAsync(KnownId));

            Assert.That(realInput.Actions.Dialogue.enabled, Is.False);
            Assert.That(realInput.Actions.Gameplay.enabled, Is.False, "进来前关着的 Gameplay 图不应被打开");
            Assert.That(recording.Calls, Has.No.Member("Enable:" + InputService.GameplayMap));
        }

        [Test]
        public void PlayAsync_WhileRunning_HidesHudLayerOnly_AndRestoresVisibleAfterEnd()
        {
            DialogueEndedEvent ended = default;
            int endedCount = 0;
            service.OnEnded += e => { ended = e; endedCount++; };
            ui.Mode = FakeUIService.OpenMode.Pending;
            using var cts = new CancellationTokenSource();

            UniTask<DialogueResult> play = service.PlayAsync(KnownId, cts.Token);

            Assert.That(ui.IsLayerVisible(UILayer.Hud), Is.False, "对白期间 Hud 层应隐藏");
            Assert.That(ui.IsLayerVisible(UILayer.Popup), Is.True, "对白框在 Popup 层，不能藏");

            cts.Cancel();
            Assert.That(Capture(play), Is.InstanceOf<OperationCanceledException>());
            Assert.That(ui.IsLayerVisible(UILayer.Hud), Is.True, "进来前可见 → 结束后恢复可见");
            Assert.That(endedCount, Is.EqualTo(1));
            Assert.That(ended.Completed, Is.False, "取消仍发清理通知，但不能推进任务");
        }

        [Test]
        public void PlayAsync_WhenHudWasHidden_KeepsHudHiddenAfterEnd()
        {
            ui.SetLayerVisible(UILayer.Hud, false); // 调用方本来藏着 Hud（如过场中）
            ui.Mode = FakeUIService.OpenMode.Throw;

            Capture(service.PlayAsync(KnownId));

            Assert.That(ui.IsLayerVisible(UILayer.Hud), Is.False, "进来前隐藏的 Hud 层不应被亮出来");
        }

        [Test]
        public void PlayAsync_WhenIdIsUnknown_DoesNotTouchHudLayer()
        {
            Capture(service.PlayAsync(UnknownId));

            Assert.That(ui.LayerCalls, Is.EqualTo(0), "未知 id 不该动 Hud 层");
        }

        // 换成带真实动作集的输入服务（InputService 同步初始化、启用 Gameplay 图），并重建 Service 使用它。
        private RecordingInput UseRealInput()
        {
            realInput = new InputService();
            realInput.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            var recording = new RecordingInput(realInput);
            service.Dispose();
            service = new DialogueService(catalog, rules, controller, config, new DefaultDialogueConditionSource(),
                pause, recording, ui, null);
            return recording;
        }

        [Test]
        public void PlayAsync_WhenAlreadyRunning_ThrowsInvalidOperation()
        {
            ui.Mode = FakeUIService.OpenMode.Pending;
            using var cts = new CancellationTokenSource();
            UniTask<DialogueResult> first = service.PlayAsync(KnownId, cts.Token);
            Assert.That(service.IsRunning, Is.True, "第一段对白应停在打开面板处");

            Exception busy = Capture(service.PlayAsync(OtherId));
            Assert.That(busy, Is.TypeOf<InvalidOperationException>());
            Assert.That(service.IsRunning, Is.True, "被拒的第二次调用不能打断第一段");

            // 收尾：取消第一段并观察它的取消异常，不留未观察异常。
            cts.Cancel();
            Assert.That(Capture(first), Is.InstanceOf<OperationCanceledException>());
            Assert.That(service.IsRunning, Is.False);
            Assert.That(pause.Holders, Is.EqualTo(0));
        }

        [Test]
        public void PlayAsync_WhenIdIsUnknown_ThrowsArgumentAndTouchesNothing()
        {
            Exception error = Capture(service.PlayAsync(UnknownId));

            Assert.That(error, Is.InstanceOf<ArgumentException>());
            Assert.That(service.IsRunning, Is.False);
            Assert.That(pause.AcquireCount, Is.EqualTo(0), "未知 id 不该取暂停令牌");
            Assert.That(input.MapCalls, Is.EqualTo(0), "未知 id 不该动 Gameplay 输入图");
            Assert.That(ui.OpenCount, Is.EqualTo(0));
        }

        [Test]
        public void PlayAsync_WhenPresentThrows_RethrowsAndCleansUp()
        {
            ui.Mode = FakeUIService.OpenMode.Throw;
            int endedCount = 0;
            DialogueEndedEvent ended = default;
            service.OnEnded += e => { endedCount++; ended = e; };

            Exception error = Capture(service.PlayAsync(KnownId));

            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(error.Message, Is.EqualTo(FakeUIService.OpenFailure), "异常须原样传出");
            Assert.That(rules.Phase, Is.EqualTo(DialogueSaveData.Phase.Closed));
            Assert.That(pause.AcquireCount, Is.EqualTo(1));
            Assert.That(pause.Holders, Is.EqualTo(0), "暂停令牌须已释放");
            Assert.That(service.IsRunning, Is.False);
            Assert.That(endedCount, Is.EqualTo(1));
            Assert.That(ended.DialogueId, Is.EqualTo(KnownId));
            Assert.That(ended.Outcome, Is.Empty);
            Assert.That(ended.Completed, Is.False);
        }

        // 观察一个已完成的 UniTask：返回它抛出的异常（成功返回 null）。未完成视为用例失败——假服务都应同步结束。
        private static Exception Capture(UniTask<DialogueResult> task)
        {
            Assert.That(task.Status.IsCompleted(), Is.True, "假服务同步完成，PlayAsync 此时应已结束");
            try
            {
                task.GetAwaiter().GetResult();
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        /// <summary>计数的世界暂停：记录取令牌次数与当前持有数。</summary>
        private sealed class FakeWorldPause : IWorldPauseService
        {
            public int AcquireCount { get; private set; }
            public int Holders { get; private set; }
            public bool IsPaused => Holders > 0;

            public IDisposable Acquire(object owner)
            {
                AcquireCount++;
                Holders++;
                return new Token(this);
            }

            private sealed class Token : IDisposable
            {
                private FakeWorldPause owner;

                public Token(FakeWorldPause owner)
                {
                    this.owner = owner;
                }

                public void Dispose()
                {
                    if (owner == null) return;
                    owner.Holders--;
                    owner = null;
                }
            }
        }

        /// <summary>未初始化的输入服务：Actions 为 null，只记开关图的调用次数。</summary>
        private sealed class FakeInput : IInputService
        {
            public int MapCalls { get; private set; }
            public GameInput Actions => null;
            public void EnableMap(string map) => MapCalls++;
            public void DisableMap(string map) => MapCalls++;
        }

        /// <summary>包一层真实 InputService：照常开关动作图，并按顺序记录「Enable:图名 / Disable:图名」。</summary>
        private sealed class RecordingInput : IInputService
        {
            private readonly InputService inner;

            public RecordingInput(InputService inner)
            {
                this.inner = inner;
            }

            public List<string> Calls { get; } = new List<string>();
            public GameInput Actions => inner.Actions;

            public void EnableMap(string map)
            {
                Calls.Add("Enable:" + map);
                inner.EnableMap(map);
            }

            public void DisableMap(string map)
            {
                Calls.Add("Disable:" + map);
                inner.DisableMap(map);
            }
        }

        /// <summary>打开面板要么挂起直到取消，要么直接抛；关闭一律空操作；按层记显隐（初始全可见）。</summary>
        private sealed class FakeUIService : IUIService
        {
            public const string OpenFailure = "假 UI 打不开对白面板";

            public enum OpenMode { Pending, Throw }

            public OpenMode Mode { get; set; } = OpenMode.Throw;
            public int OpenCount { get; private set; }

            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                OpenCount++;
                if (Mode == OpenMode.Throw) return UniTask.FromException<T>(new InvalidOperationException(OpenFailure));
                var source = new UniTaskCompletionSource<T>();
                ct.Register(() => source.TrySetCanceled(ct));
                return source.Task;
            }

            public UniTask CloseAsync(UIView view, CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => null;
            public int LayerCalls { get; private set; }
            private readonly HashSet<UILayer> hiddenLayers = new HashSet<UILayer>();

            public void SetLayerVisible(UILayer layer, bool visible)
            {
                LayerCalls++;
                if (visible) hiddenLayers.Remove(layer);
                else hiddenLayers.Add(layer);
            }

            public bool IsLayerVisible(UILayer layer) => !hiddenLayers.Contains(layer);
        }

        /// <summary>本文件的用例走不到立绘加载；被调到就说明流程不对。</summary>
        private sealed class FakeAssetService : IAssetService
        {
            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException("假资源服务不加载");

            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default)
                where T : UnityEngine.Object => throw new NotSupportedException("假资源服务不加载");

            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default) =>
                throw new NotSupportedException("假资源服务不实例化");

            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException("假资源服务不实例化");

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default) =>
                throw new NotSupportedException("假资源服务不加载场景");
        }

        /// <summary>静止的时钟。</summary>
        private sealed class FakeClock : IClock
        {
            public DateTime UtcNow => DateTime.UnixEpoch;
            public float GameTime => 0f;
            public float UnscaledTime => 0f;
            public float DeltaTime => 0f;
            public float UnscaledDeltaTime => 0f;
        }

        /// <summary>只递一份现成 <c>cfg.Tables</c> 的配置服务；不提供内容指纹。</summary>
        private sealed class FakeConfigService : IConfigService
        {
            public FakeConfigService(global::cfg.Tables tables)
            {
                Tables = tables;
            }

            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
