// 职责：验证真实帧循环中的标题路由，冷启动只读候选、新游戏恢复阳版。
// 为什么新建：NextFrame 依赖 PlayMode 帧推进，纯判定测试无法覆盖异步路由；复用真实 JsonSaveService，不改正式存档。
using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Narrative;
using Game.Session;
using MessagePipe;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode.Session
{
    public sealed class SessionTitleRouterTests
    {
        [UnityTest]
        public IEnumerator SelectedSlot_TitleDestroyedBeforeSceneReady_DoesNotCancelLoad() =>
            UniTask.ToCoroutine(async () =>
            {
                string saveRoot = Path.Combine(Path.GetTempPath(), "21Days-title-tests", Guid.NewGuid().ToString("N"));
                var saves = new JsonSaveService(new TestPlatform(saveRoot), null, null);
                saves.Get<SessionSaveData>().SavedAtUtcTicks = DateTime.UtcNow.Ticks;
                Assert.That(await saves.SaveAsync(1), Is.True);
                saves.ResetAll();
                SessionConfig config = ScriptableObject.CreateInstance<SessionConfig>();
                UIConfig uiConfig = ScriptableObject.CreateInstance<UIConfig>();
                var titleRoot = new GameObject("Title", typeof(RectTransform), typeof(TitleView));
                SaveSlotsView selection = CreateSelection();
                var ui = new TestUI(titleRoot.GetComponent<TitleView>(), selection);
                bool entered = false;
                var flow = new TestFlow
                {
                    Transition = async ct =>
                    {
                        // 标题先正常退出，模拟尚未完成的冷场景加载。
                        Object.Destroy(titleRoot);
                        await UniTask.NextFrame();
                        await UniTask.NextFrame();
                        ct.ThrowIfCancellationRequested();
                        entered = true;
                    },
                };
                var notifications = new TestNotifications();
                var session = new GameSession(saves, new TestClock(), new TestLogicClock(), flow,
                    new TestState(), config, notifications, new TestPublisher<SessionStartedEvent>(),
                    new TestPublisher<SaveCompletedEvent>(), NullTelemetryScope.Instance);
                var tutorial = new NewGameTutorial(ui, uiConfig, NullTelemetryScope.Instance, new UnityTelemetryClock());
                using (var slots = new SaveSlotsController(session, tutorial, ui, config, notifications,
                           new TestSubscriber<GameStateChangingEvent>(), NullTelemetryScope.Instance))
                {
                    try
                    {
                        await slots.OpenAsync(SlotsMode.Load);
                        Button row = Array.Find(selection.GetComponentsInChildren<Button>(), button =>
                        {
                            Transform label = button.transform.Find("Slot");
                            return label != null && label.GetComponent<TMP_Text>().text == SaveSlotsView.FormatSlotLabel(1);
                        });
                        Assert.That(row, Is.Not.Null);
                        row.onClick.Invoke();
                        float deadline = Time.realtimeSinceStartup + 5f;
                        while (!entered && Time.realtimeSinceStartup < deadline) await UniTask.NextFrame();
                        Assert.That(titleRoot == null, Is.True, "测试必须经过标题销毁");
                        Assert.That(entered, Is.True, "标题关闭不能取消尚未完成的场景加载");
                        Assert.That(session.CurrentSlot, Is.EqualTo(1));
                    }
                    finally
                    {
                        session.Dispose();
                        if (selection != null) Object.Destroy(selection.gameObject);
                        if (titleRoot != null) Object.Destroy(titleRoot);
                        Object.Destroy(uiConfig);
                        Object.Destroy(config);
                        if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
                    }
                }
            });

        private static SaveSlotsView CreateSelection()
        {
            var root = new GameObject("Selection", typeof(RectTransform), typeof(CanvasGroup), typeof(SaveSlotsView));
            var list = new GameObject("List", typeof(RectTransform));
            list.transform.SetParent(root.transform, false);
            var row = new GameObject("Row", typeof(RectTransform), typeof(Button));
            row.transform.SetParent(list.transform, false);
            foreach (string name in new[] { "Slot", "Primary", "Secondary" })
                new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).transform.SetParent(row.transform, false);
            new GameObject("Delete", typeof(RectTransform), typeof(Button)).transform.SetParent(row.transform, false);
            row.SetActive(false);
            SaveSlotsView view = root.GetComponent<SaveSlotsView>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(SaveSlotsView).GetField("listRoot", flags).SetValue(view, list.GetComponent<RectTransform>());
            typeof(SaveSlotsView).GetField("rowTemplate", flags).SetValue(view, row);
            return view;
        }

        [UnityTest]
        public IEnumerator Appearance_AfterRestart_DoesNotCommitCandidateAndNewGameRestoresNormal() =>
            UniTask.ToCoroutine(async () =>
            {
                const string key = "world.title_yin";
                string saveRoot = Path.Combine(Path.GetTempPath(), "21Days-title-tests", Guid.NewGuid().ToString("N"));
                var saves = new JsonSaveService(new TestPlatform(saveRoot), null, null);
                saves.Get<SessionSaveData>().SavedAtUtcTicks = DateTime.UtcNow.Ticks;
                saves.Get<NarrativeSaveData>().StoryFlags.Add(key);
                Assert.That(await saves.SaveAsync(1), Is.True);
                saves.ResetAll();

                SessionConfig config = ScriptableObject.CreateInstance<SessionConfig>();
                typeof(SessionConfig).GetField("titleYinStoryFlag", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic).SetValue(config, key);
                UIConfig uiConfig = ScriptableObject.CreateInstance<UIConfig>();
                var titleRoot = new GameObject("Title", typeof(RectTransform), typeof(Image), typeof(TitleView));
                var ui = new TestUI(titleRoot.GetComponent<TitleView>());
                Image image = titleRoot.GetComponent<Image>();
                var changed = new TestSubscriber<GameStateChangedEvent>();
                var notifications = new TestNotifications();
                var session = new GameSession(saves, new TestClock(), new TestLogicClock(), new TestFlow(),
                    new TestState(), config, notifications, new TestPublisher<SessionStartedEvent>(),
                    new TestPublisher<SaveCompletedEvent>(), NullTelemetryScope.Instance);
                var tutorial = new NewGameTutorial(ui, uiConfig, NullTelemetryScope.Instance, new UnityTelemetryClock());
                using (var slots = new SaveSlotsController(session, tutorial, ui, config, notifications,
                           new TestSubscriber<GameStateChangingEvent>(), NullTelemetryScope.Instance))
                using (var router = new SessionTitleRouter(session, slots, tutorial, ui,
                           new TestSubscriber<TitleStartClickedEvent>(), new TestSubscriber<TitleContinueClickedEvent>(),
                           new TestSubscriber<TitleLoadClickedEvent>(), changed, NullTelemetryScope.Instance,
                           saves, config, null))
                {
                    try
                    {
                        await session.InitializeAsync(CancellationToken.None);
                        SessionSaveData sentinel = saves.Get<SessionSaveData>();
                        sentinel.ProgressText = "保留内存，不加载标题候选";
                        router.Start();
                        changed.Publish(new GameStateChangedEvent(null, typeof(TitleState)));
                        await WaitForColor(image, config.TitleYinTint);
                        Assert.That(session.CurrentSlot, Is.Zero, "标题候选不能开启会话");
                        Assert.That(saves.Get<SessionSaveData>(), Is.SameAs(sentinel), "标题不能 Commit 候选");
                        Assert.That(sentinel.ProgressText, Is.EqualTo("保留内存，不加载标题候选"));
                        Assert.That(saves.Get<NarrativeSaveData>().StoryFlags, Does.Not.Contain(key));

                        await session.NewGameAsync(2);
                        changed.Publish(new GameStateChangedEvent(null, typeof(TitleState)));
                        await WaitForColor(image, Color.white);
                        Assert.That(session.CurrentSlot, Is.EqualTo(2));
                    }
                    finally
                    {
                        session.Dispose();
                        Object.Destroy(titleRoot);
                        Object.Destroy(uiConfig);
                        Object.Destroy(config);
                        if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
                    }
                }
            });

        private static async UniTask WaitForColor(Image image, Color color)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (image.color != color)
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("标题色调刷新超时");
                await UniTask.NextFrame();
            }
        }

        private sealed class TestSubscriber<T> : ISubscriber<T>, IDisposable
        {
            private IMessageHandler<T> handler;
            public IDisposable Subscribe(IMessageHandler<T> next, params MessageHandlerFilter<T>[] filters)
            {
                handler = next;
                return this;
            }
            public void Publish(T message) => handler?.Handle(message);
            public void Dispose() => handler = null;
        }
        private sealed class TestPublisher<T> : IPublisher<T> { public void Publish(T message) { } }
        private sealed class TestUI : IUIService
        {
            private readonly TitleView title;
            private readonly SaveSlotsView selection;
            public TestUI(TitleView title, SaveSlotsView selection = null)
            {
                this.title = title;
                this.selection = selection;
            }
            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                if (selection != null && selection is T typed) return UniTask.FromResult(typed);
                throw new NotSupportedException();
            }
            public UniTask CloseAsync(UIView view, CancellationToken ct = default)
            {
                if (view != null) Object.Destroy(view.gameObject);
                return UniTask.CompletedTask;
            }
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => title as T;
            public void SetLayerVisible(UILayer layer, bool visible) { }
            public bool IsLayerVisible(UILayer layer) => true;
        }
        private sealed class TestPlatform : IPlatformService
        {
            public TestPlatform(string saveRoot) => SaveRoot = saveRoot;
            public PlatformKind Kind => PlatformKind.Standalone;
            public string SaveRoot { get; }
            public bool IsTouchPrimary => false;
            public void Vibrate(VibrationKind kind) { }
        }
        private sealed class TestClock : IClock
        {
            public DateTime UtcNow => DateTime.UtcNow;
            public float GameTime => 0f;
            public float UnscaledTime => 0f;
            public float DeltaTime => 0f;
            public float UnscaledDeltaTime => 0f;
        }
        private sealed class TestLogicClock : ILogicClock
        {
            public long Tick => 0;
            public float FixedDeltaTime => 0.02f;
            public float SimTime => 0f;
        }
        private sealed class TestFlow : IGameFlow
        {
            public GameState Current => null;
            public Func<CancellationToken, UniTask> Transition { get; set; }
            public UniTask GoToAsync<TState>(CancellationToken ct = default) where TState : GameState =>
                Transition == null ? UniTask.CompletedTask : Transition(ct);
        }
        private sealed class TestState : ISessionStateSource
        {
            public bool IsGameplayState => true;
            public bool DialogueRunning => false;
            public bool AnyPanelOpen => false;
            public bool BattleResultPending => false;
            public bool NarrativeStable => true;
            public void CaptureEncounter(long tick) { }
            public void PrepareRestore(bool fromSave) { }
            public void ValidateCandidate(SaveSnapshot candidate) { }
            public string BuildProgressText() => "测试";
        }
        private sealed class TestNotifications : INotificationService
        {
            public void Show(string title, string body = null, float seconds = 0f) { }
            public void ShowCornerHint(string text, float seconds = 0f) { }
        }
    }
}
