// 职责：钉住 MirrorInputPresenter 的纯判定——让位原因与优先级（PRD V11：对白中、暂停、沉浸、结果画面开着时照镜键无效）、
//   冷却、结果画面到时 / 确认键关闭，以及图片加载、打开、关闭与销毁交错时的资源所有权。
// 为什么新建：一个被测类一个测试类；可控异步服务验证竞态，不依赖真实 Addressables 或帧循环。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Config;
using Game.Core.Input;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Loot;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Quest;
using Game.Session;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorInputPresenter"/> 纯判定的 EditMode 测试。</summary>
    public sealed class MirrorInputPresenterTests
    {
        private const float Cooldown = 0.4f;

        [Test]
        public void BlockReason_NothingBlocking_ReturnsNull()
        {
            Assert.That(MirrorInputPresenter.BlockReason(false, false, false, false, 10f, Cooldown), Is.Null);
        }

        [TestCase(true, false, false, false, 10f, "view_open")]
        [TestCase(false, true, false, false, 10f, "dialogue")]
        [TestCase(false, false, true, false, 10f, "paused")]
        [TestCase(false, false, false, true, 10f, "hud_hidden")]
        [TestCase(false, false, false, false, 0.1f, "cooldown")]
        public void BlockReason_EachCondition_Reported(bool viewOpen, bool dialogue, bool paused, bool hudHidden,
            float since, string expected)
        {
            Assert.That(MirrorInputPresenter.BlockReason(viewOpen, dialogue, paused, hudHidden, since, Cooldown),
                Is.EqualTo(expected));
        }

        [Test]
        public void BlockReason_Several_FirstInOrderWins()
        {
            Assert.That(MirrorInputPresenter.BlockReason(true, true, true, true, 0f, Cooldown), Is.EqualTo("view_open"));
            Assert.That(MirrorInputPresenter.BlockReason(false, true, true, true, 0f, Cooldown), Is.EqualTo("dialogue"));
            Assert.That(MirrorInputPresenter.BlockReason(false, false, true, true, 0f, Cooldown), Is.EqualTo("paused"));
        }

        [Test]
        public void BlockReason_CooldownBoundary_ElapsedEqualsCooldownAllowed()
        {
            Assert.That(MirrorInputPresenter.BlockReason(false, false, false, false, Cooldown, Cooldown), Is.Null);
        }

        [Test]
        public void ShouldCloseResult_ConfirmPressed_ClosesImmediately()
        {
            Assert.That(MirrorInputPresenter.ShouldCloseResult(0f, 2.5f, true), Is.True);
        }

        [Test]
        public void ShouldCloseResult_ByTime()
        {
            Assert.That(MirrorInputPresenter.ShouldCloseResult(2.4f, 2.5f, false), Is.False);
            Assert.That(MirrorInputPresenter.ShouldCloseResult(2.5f, 2.5f, false), Is.True);
        }

        [Test]
        public void ShouldCloseResult_ZeroSeconds_OnlyByKey()
        {
            Assert.That(MirrorInputPresenter.ShouldCloseResult(100f, 0f, false), Is.False);
            Assert.That(MirrorInputPresenter.ShouldCloseResult(100f, 0f, true), Is.True);
        }

        [Test]
        public void ShowAsync_DisposedDuringImageLoad_ReleasesLateHandleWithoutOpening()
        {
            using var scope = new PresenterScope();
            UniTask show = scope.Show();
            scope.Presenter.Dispose();
            Assert.That(scope.Assets.Token.IsCancellationRequested, Is.True);
            AssetHandle<Sprite> image = scope.CompleteImage(); // 模拟加载器在取消后仍成功返回。
            Complete(show);
            Assert.That(image.IsDisposed, Is.True);
            Assert.That(scope.UI.OpenCount, Is.Zero);
            Assert.That(scope.Pause.IsPaused, Is.False);
        }

        [Test]
        public void ShowAsync_ImageLoadCanceled_ReleasesPauseAndAllowsRetry()
        {
            using var scope = new PresenterScope();
            UniTask show = scope.Show();
            scope.Assets.Pending.TrySetCanceled();
            Complete(show);
            Assert.That(scope.Presenter.IsShowing, Is.False);
            Assert.That(scope.Pause.IsPaused, Is.False);
            scope.Assets.Pending = new UniTaskCompletionSource<AssetHandle<Sprite>>();
            UniTask retry = scope.Show();
            scope.CompleteImage();
            scope.UI.PendingOpen.TrySetResult(scope.UI.View);
            Complete(retry);
            Assert.That(scope.Presenter.IsShowing, Is.True);
        }

        [TestCase("disposed")]
        [TestCase("canceled")]
        [TestCase("failed")]
        public void ShowAsync_ViewOpenFails_ReleasesImageAndPause(string reason)
        {
            using var scope = new PresenterScope();
            UniTask show = scope.Show();
            AssetHandle<Sprite> image = scope.CompleteImage();
            if (reason == "canceled") scope.UI.PendingOpen.TrySetCanceled();
            else if (reason == "disposed") scope.UI.PendingOpen.TrySetException(new ObjectDisposedException("UIService"));
            else
            {
                LogAssert.Expect(LogType.Error, new Regex("MirrorInputPresenter：打开照镜结果画面失败"));
                scope.UI.PendingOpen.TrySetException(new InvalidOperationException("打开失败"));
            }
            Complete(show);
            Assert.That(scope.UI.View.Current, Is.Null, "取消淡入时已绑定的图片引用也要清除");
            Assert.That(image.IsDisposed, Is.True);
            Assert.That(scope.Presenter.IsShowing, Is.False);
            Assert.That(scope.Pause.IsPaused, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShowAsync_DisposedWhileOpening_KeepsImageUntilLateViewCloses(bool closeServiceDisposed)
        {
            using var scope = new PresenterScope();
            UniTask show = scope.Show();
            AssetHandle<Sprite> image = scope.CompleteImage();
            scope.Presenter.Dispose();
            Assert.That(image.IsDisposed, Is.False, "打开中的视图可能已绑定图片");
            scope.UI.PendingOpen.TrySetResult(scope.UI.View);
            Assert.That(scope.UI.CloseCount, Is.EqualTo(1));
            Assert.That(image.IsDisposed, Is.False, "淡出未完，仍由打开流程持有");
            if (closeServiceDisposed) scope.UI.PendingClose.TrySetException(new ObjectDisposedException("UIService"));
            else scope.UI.PendingClose.TrySetResult();
            Complete(show);
            Assert.That(scope.UI.View.Current, Is.Null);
            Assert.That(image.IsDisposed, Is.True);
            Assert.That(scope.Pause.IsPaused, Is.False);
        }

        [Test]
        public void ShowAsync_RepeatedOpenAndClose_OnlyReleasesEachSessionOnce()
        {
            using var scope = new PresenterScope();
            UniTask first = scope.Show();
            Complete(scope.Show());
            AssetHandle<Sprite> image = scope.CompleteImage();
            scope.UI.PendingOpen.TrySetResult(scope.UI.View);
            Complete(first);
            Complete(scope.Show());
            Assert.That(scope.UI.OpenCount, Is.EqualTo(1));
            scope.Close();
            scope.Close();
            Assert.That(scope.UI.CloseCount, Is.EqualTo(1));
            Assert.That(image.IsDisposed, Is.False);
            scope.UI.PendingClose.TrySetResult();
            Assert.That(image.IsDisposed, Is.True);
            Assert.That(scope.Pause.IsPaused, Is.False);

            scope.Assets.Pending = new UniTaskCompletionSource<AssetHandle<Sprite>>();
            scope.UI.PendingOpen = new UniTaskCompletionSource<MirrorResultView>();
            scope.UI.PendingClose = new UniTaskCompletionSource();
            UniTask second = scope.Show();
            AssetHandle<Sprite> next = scope.CompleteImage();
            scope.UI.PendingOpen.TrySetResult(scope.UI.View);
            Complete(second);
            scope.Presenter.Dispose();
            scope.Presenter.Dispose();
            Assert.That(scope.UI.CloseCount, Is.EqualTo(2));
            Assert.That(next.IsDisposed, Is.False);
            scope.UI.PendingClose.TrySetResult();
            Assert.That(next.IsDisposed, Is.True);
            Assert.That(scope.ReleaseCount, Is.EqualTo(2));
            Assert.That(scope.Pause.IsPaused, Is.False);
        }

        [Test]
        public void ShowAsync_ViewClosedBeforeOpenReturns_DoesNotLatchShowing()
        {
            using var scope = new PresenterScope();
            UniTask show = scope.Show();
            AssetHandle<Sprite> image = scope.CompleteImage();
            scope.UI.View.OnCloseAsync(CancellationToken.None).GetAwaiter().GetResult();
            scope.UI.PendingOpen.TrySetResult(scope.UI.View);
            Complete(show);
            Assert.That(scope.Presenter.IsShowing, Is.False);
            Assert.That(scope.Pause.IsPaused, Is.False);
            Assert.That(image.IsDisposed, Is.True);
        }

        [Test]
        public void CloseResult_CloseFails_ClearsViewBeforeReleasingImage()
        {
            using var scope = new PresenterScope();
            UniTask show = scope.Show();
            AssetHandle<Sprite> image = scope.CompleteImage();
            scope.UI.PendingOpen.TrySetResult(scope.UI.View);
            Complete(show);
            scope.Close();
            LogAssert.Expect(LogType.Warning, new Regex("MirrorInputPresenter：关闭照镜结果画面失败"));
            scope.UI.PendingClose.TrySetException(new InvalidOperationException("关闭失败"));
            Assert.That(scope.UI.View.Current, Is.Null);
            Assert.That(image.IsDisposed, Is.True);
            Assert.That(scope.Presenter.IsShowing, Is.False);
            Assert.That(scope.Pause.IsPaused, Is.False);
        }

        private static void Complete(UniTask task)
        {
            Assert.That(task.Status.IsCompleted(), Is.True, "受控依赖应已完成，不等待编辑器帧");
            task.GetAwaiter().GetResult();
        }

        // 真实门面与配置表，只有资源、UI 和暂停时序可控。反射只用来进入私有呈现流程，避免模拟设备输入。
        private sealed class PresenterScope : IDisposable
        {
            private readonly List<Object> created = new List<Object>();
            private readonly DialogueService dialogue;
            private readonly MirrorService service;
            private readonly LootService loot;
            private readonly QuestService quest;
            private readonly InputService input = new InputService();
            public ControlledAssets Assets { get; } = new ControlledAssets();
            public ControlledUI UI { get; }
            public CountingPause Pause { get; } = new CountingPause();
            public MirrorInputPresenter Presenter { get; }
            public int ReleaseCount { get; private set; }

            public PresenterScope()
            {
                UI = new ControlledUI();
                var tables = new TableConfig();
                var saves = new JsonSaveService(new TestPlatform(), null, null);
                quest = new QuestService(new QuestCatalog(tables, NullTelemetryScope.Instance), saves, new Bus<QuestActivatedEvent>(),
                    new Bus<QuestObjectiveProgressedEvent>(), new Bus<QuestCompletedEvent>(), new Bus<QuestTrackingChangedEvent>(),
                    new Bus<SessionStartedEvent>(), null);
                loot = new LootService(Create<LootConfig>(), saves, tables, quest, new NoNotifications(),
                    new Bus<CrateCollectedEvent>(), new Bus<LootResetEvent>(), null);
                MirrorConfig config = Create<MirrorConfig>();
                service = new MirrorService(config, saves, tables, loot, new PlayerModel(), Create<PlayerConfig>(),
                    new MirrorSceneBinder(new MonsterModel(), null), new Bus<MirrorCastEvent>(), _ => { }, null);
                var rules = new DialogueRules(new DialogueReadData(), 500, null);
                var catalog = new DialogueCatalog(tables);
                var clock = new LocalClock();
                var controller = new DialogueController(rules, catalog, UI, Assets, clock, null);
                dialogue = new DialogueService(catalog, rules, controller, Create<DialogueConfig>(),
                    new DefaultDialogueConditionSource(), Pause, input, UI, null);
                Presenter = new MirrorInputPresenter(service, config, new EncounterStep(null, null), dialogue,
                    Pause, UI, input, UI, Assets, clock, null);
            }

            public UniTask Show() => (UniTask)typeof(MirrorInputPresenter)
                .GetMethod("ShowAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(Presenter, new object[] { new MirrorResult(MirrorResultKind.TrueForm, 0, 1, 1f), null });

            public void Close() => typeof(MirrorInputPresenter)
                .GetMethod("CloseResult", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Presenter, null);

            public AssetHandle<Sprite> CompleteImage()
            {
                var image = new AssetHandle<Sprite>(default, _ => ReleaseCount++);
                Assets.Pending.TrySetResult(image);
                return image;
            }

            private T Create<T>() where T : ScriptableObject
            {
                T value = ScriptableObject.CreateInstance<T>();
                created.Add(value);
                return value;
            }

            public void Dispose()
            {
                Presenter.Dispose();
                Assets.Pending.TrySetCanceled();
                UI.PendingOpen.TrySetCanceled();
                UI.PendingClose.TrySetResult();
                dialogue.Dispose();
                service.Dispose();
                loot.Dispose();
                quest.Dispose();
                input.Dispose();
                Object.DestroyImmediate(UI.View.gameObject);
                foreach (Object value in created) Object.DestroyImmediate(value);
            }
        }

        private sealed class ControlledAssets : IAssetService
        {
            public UniTaskCompletionSource<AssetHandle<Sprite>> Pending { get; set; } = new UniTaskCompletionSource<AssetHandle<Sprite>>();
            public CancellationToken Token { get; private set; }
            public async UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : Object
            {
                Token = ct;
                return (AssetHandle<T>)(object)await Pending.Task;
            }
            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default) where T : Object => throw new NotSupportedException();
            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default) => throw new NotSupportedException();
            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException();
            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private sealed class ControlledUI : IUIService, IHudVisibility
        {
            public MirrorResultView View { get; } = new GameObject("MirrorResultTest", typeof(RectTransform)).AddComponent<MirrorResultView>();
            public UniTaskCompletionSource<MirrorResultView> PendingOpen { get; set; } = new UniTaskCompletionSource<MirrorResultView>();
            public UniTaskCompletionSource PendingClose { get; set; } = new UniTaskCompletionSource();
            public int OpenCount { get; private set; }
            public int CloseCount { get; private set; }
            public bool IsHudHidden => false;
            public async UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                OpenCount++;
                // 模拟 OnOpen 已绑定内容、淡入仍挂起；关闭仍走真实 MirrorResultView.OnCloseAsync。
                typeof(MirrorResultView).GetProperty(nameof(MirrorResultView.Current)).SetValue(View, arg);
                return (T)(UIView)await PendingOpen.Task;
            }
            public async UniTask CloseAsync(UIView view, CancellationToken ct = default)
            {
                CloseCount++;
                await PendingClose.Task;
                await view.OnCloseAsync(ct);
            }
            public UniTask CloseTopAsync(CancellationToken ct = default) => CloseAsync(View, ct);
            public T Get<T>() where T : UIView => View as T;
            public void SetLayerVisible(UILayer layer, bool visible) { }
            public bool IsLayerVisible(UILayer layer) => true;
            public void SetHudHidden(bool hidden) { }
        }

        private sealed class CountingPause : IWorldPauseService
        {
            public bool IsPaused { get; private set; }
            public IDisposable Acquire(object owner)
            {
                Assert.That(IsPaused, Is.False, "不能重入持有暂停令牌");
                IsPaused = true;
                return new Release(this);
            }
            private sealed class Release : IDisposable
            {
                private readonly CountingPause owner;
                public Release(CountingPause owner) => this.owner = owner;
                public void Dispose() => owner.IsPaused = false;
            }
        }

        private sealed class TableConfig : IConfigService
        {
            public global::cfg.Tables Tables { get; } = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            public ulong ContentHash => 0;
        }

        private sealed class Bus<T> : IPublisher<T>, ISubscriber<T>, IDisposable
        {
            public void Publish(T message) { }
            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters) => this;
            public void Dispose() { }
        }

        private sealed class NoNotifications : INotificationService
        {
            public void Show(string title, string body = null, float seconds = 0f) { }
            public void ShowCornerHint(string text, float seconds = 0f) { }
        }

        private sealed class TestPlatform : IPlatformService
        {
            public PlatformKind Kind => PlatformKind.Standalone;
            public string SaveRoot => Application.temporaryCachePath;
            public bool IsTouchPrimary => false;
            public void Vibrate(VibrationKind kind) { }
        }
    }
}
