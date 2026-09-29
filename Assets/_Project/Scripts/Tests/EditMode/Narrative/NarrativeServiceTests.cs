// 职责：真实 Narrative/Dialogue/Quest/Save 服务间的取消、重试与槽位隔离；规则单测不经过这些边界。
// UI 只挂起或抛错，成功对白及可见选项由 Narrative Showcase 经过真实面板验证。
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Config;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Narrative;
using Game.Player;
using Game.Quest;
using Game.Session;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class NarrativeServiceTests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private JsonSaveService saves;
        private PlayerModel player;
        private NarrativeService narrative;
        private NarrativeConditionSource conditions;
        private DialogueService dialogue;
        private DialogueCatalog dialogues;
        private DialogueRules dialogueRules;
        private QuestService quest;
        private QuestObjectiveDriver objectives;
        private PendingUI ui;
        private Bus<SessionStartedEvent> sessions;
        private NarrativeTrigger target;

        [SetUp]
        public void SetUp()
        {
            var config = new TableConfig();
            saves = new JsonSaveService(new TestPlatform(), null, null);
            player = new PlayerModel();
            player.Restore(new PlayerSaveData { Health = 10 });
            conditions = new NarrativeConditionSource(player, saves);
            dialogues = new DialogueCatalog(config);
            dialogueRules = new DialogueRules(new DialogueReadData(), 500, null);
            ui = new PendingUI();
            var controller = new DialogueController(dialogueRules, dialogues, ui, new UnusedAssets(), new TestClock(), null);
            dialogue = new DialogueService(dialogues, dialogueRules, controller, Track(ScriptableObject.CreateInstance<DialogueConfig>()),
                conditions, new Pause(), new NoInput(), ui, null);
            var binder = new DialogueSceneBinder(dialogue, ui, new Bus<HudVisibilityChangedEvent>());
            sessions = new Bus<SessionStartedEvent>();
            var completed = new Bus<QuestCompletedEvent>();
            quest = new QuestService(new QuestCatalog(config, NullTelemetryScope.Instance), saves,
                new Bus<QuestActivatedEvent>(), new Bus<QuestObjectiveProgressedEvent>(), completed,
                new Bus<QuestTrackingChangedEvent>(), sessions, NullTelemetryScope.Instance);
            quest.InitializeAsync(default).GetAwaiter().GetResult();
            narrative = new NarrativeService(new NarrativeCatalog(config, dialogues), dialogue, binder, conditions, saves,
                sessions, completed, new Bus<NarrativeChangedEvent>(), null);
            narrative.InitializeAsync(default).GetAwaiter().GetResult();
            objectives = new QuestObjectiveDriver(quest,
                new QuestSceneBinder(binder, Track(ScriptableObject.CreateInstance<QuestConfig>())), dialogue, null);
            objectives.Start();
            var actor = Track(new GameObject("NarrativeTestPlayer")).AddComponent<DialogueInteractionActor>();
            target = Track(new GameObject("NarrativeTestTarget")).AddComponent<NarrativeTrigger>();
            target.Configure("sample_target", "SampleTraveler", narrative, actor);
        }

        [TearDown]
        public void TearDown()
        {
            narrative?.Dispose();
            dialogue?.Dispose();
            objectives?.Dispose();
            quest?.Dispose();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void Interact_CancelThenRetry_RetriesSameConsumedEncounter()
        {
            ReadySecondQuest();
            // EditMode 不发送普通 MonoBehaviour 生命周期消息；禁用/启用由 NarrativeShowcase 验证。
            using var firstCancellation = new CancellationTokenSource();
            UniTask<bool> first = target.InteractAsync(firstCancellation.Token);
            NarrativeSaveData active = narrative.Capture();
            Assert.That(dialogue.IsRunning, Is.True);
            Assert.That(target.InteractAsync().GetAwaiter().GetResult(), Is.False, "进行中重入被拒绝");
            firstCancellation.Cancel();
            Assert.That(Error(first), Is.InstanceOf<OperationCanceledException>());
            Assert.That(narrative.CanSave, Is.False);
            Assert.That(narrative.Capture().Current.RequestIssued, Is.False);
            AssertSecondQuestIncomplete();

            using var cancellation = new CancellationTokenSource();
            UniTask<bool> retry = target.InteractAsync(cancellation.Token);
            Assert.That(ui.Opens, Is.EqualTo(2), "点击同目标必须重新打开对白");
            Assert.That(narrative.Capture().Current.ActivationId, Is.EqualTo(active.Current.ActivationId));
            Assert.That(narrative.Capture().ConsumedTriggers, Is.EquivalentTo(active.ConsumedTriggers));
            cancellation.Cancel();
            Assert.That(Error(retry), Is.InstanceOf<OperationCanceledException>());
            AssertSecondQuestIncomplete();
        }

        [Test]
        public void Interact_PresentationFails_LeavesRetryableStageWithoutCompletingQuest()
        {
            ReadySecondQuest();
            ui.Throws = true;
            Assert.That(Error(target.InteractAsync()), Is.TypeOf<InvalidOperationException>());
            long activation = narrative.Capture().Current.ActivationId;
            Assert.That(Error(target.InteractAsync()), Is.TypeOf<InvalidOperationException>());
            Assert.That(ui.Opens, Is.EqualTo(2));
            Assert.That(narrative.Capture().Current.ActivationId, Is.EqualTo(activation));
            Assert.That(narrative.CanSave, Is.False);
            AssertSecondQuestIncomplete();
        }

        [Test]
        public void Retry_AutomaticTriggerOrDeadPlayer_DoesNotRestartDialogue()
        {
            ui.Throws = true;
            Error(target.InteractAsync());
            var candidate = new EncounterRules.Candidate { TriggerId = target.TargetId, TriggerKind = "Seen", Context = conditions.Snapshot(target.TargetId) };
            Assert.That(narrative.TryEncounterAsync(new[] { candidate }).GetAwaiter().GetResult(), Is.False);
            player.Restore(new PlayerSaveData { Health = 0 });
            Assert.That(target.InteractAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(ui.Opens, Is.EqualTo(1));
        }

        [Test]
        public void SessionSwapsPartition_LateOldPlaybackCleanup_DoesNotChangeRestoredFrame()
        {
            narrative.StartAsync("sample_wait", target.TargetId).GetAwaiter().GetResult();
            SaveSnapshot saved = saves.Capture();
            ui.IgnoreCancellation = true;
            UniTask<bool> old = target.InteractAsync();
            long oldGeneration = narrative.Generation;
            saves.Commit(saved);
            sessions.Publish(new SessionStartedEvent(2, false));
            Assert.That(narrative.Generation, Is.GreaterThan(oldGeneration));
            ui.FailPending();
            Assert.That(Error(old), Is.TypeOf<InvalidOperationException>());
            Assert.That(narrative.Capture().Current.StoryId, Is.EqualTo("sample_wait"));
            Assert.That(narrative.Capture().Current.RequestIssued, Is.False);
            Assert.That(narrative.Capture().ConsumedTriggers, Is.Empty);
            Assert.That(narrative.CanSave, Is.True);
        }

        [Test]
        public void QuestCompletes_ConditionalChoiceRechecksCurrentSlotAndRejectsStaleAvailability()
        {
            ReadySecondQuest();
            Assert.That(dialogues.TryGet(9001, out DialogueContent content), Is.True);
            dialogueRules.Start(content);
            var choice = new DialogueIntent(DialogueIntent.Action.Choose, dialogueRules.Generation, dialogueRules.Visit, "verify");
            dialogueRules.Apply(in choice, conditions.Snapshot(target.TargetId));
            Assert.That(dialogueRules.Phase, Is.EqualTo(DialogueSaveData.Phase.AwaitChoice));
            quest.Report(QuestObjectiveKind.TalkTo, "1002");
            Assert.That(conditions.Snapshot(target.TargetId).Read(EncounterContext.Fact.StoryFlag, "quest_completed_1002"), Is.True);
            SaveSnapshot finished = saves.Capture();
            dialogueRules.Apply(in choice, conditions.Snapshot(target.TargetId));
            Assert.That(dialogueRules.Outcome, Is.EqualTo("Verified"));

            saves.ResetAll();
            sessions.Publish(new SessionStartedEvent(1, true));
            Assert.That(conditions.Snapshot(target.TargetId).Read(EncounterContext.Fact.StoryFlag, "quest_completed_1002"), Is.False);
            dialogueRules.Start(content);
            var staleChoice = new DialogueIntent(DialogueIntent.Action.Choose, dialogueRules.Generation, dialogueRules.Visit, "verify");
            dialogueRules.Apply(in staleChoice, conditions.Snapshot(target.TargetId));
            Assert.That(dialogueRules.Phase, Is.EqualTo(DialogueSaveData.Phase.AwaitChoice));
            saves.Commit(finished);
            sessions.Publish(new SessionStartedEvent(2, false));
            Assert.That(conditions.Snapshot(target.TargetId).Read(EncounterContext.Fact.StoryFlag, "quest_completed_1002"), Is.True);
        }

        [Test]
        public void ValidateCandidate_UnknownStoryOrIssuedParent_RejectsWithoutChangingCurrent()
        {
            narrative.StartAsync("sample_wait", target.TargetId).GetAwaiter().GetResult();
            NarrativeSaveData part = saves.Get<NarrativeSaveData>();
            part.Parent = narrative.Capture().Current;
            part.Parent.RequestIssued = true;
            Assert.Throws<ArgumentException>(() => narrative.ValidateCandidate(saves.Capture()));
            part.Parent = null;
            part.Current.StoryId = "missing";
            Assert.Throws<ArgumentException>(() => narrative.ValidateCandidate(saves.Capture()));
            Assert.That(narrative.Capture().Current.StoryId, Is.EqualTo("sample_wait"));
        }

        private void ReadySecondQuest()
        {
            quest.Report(QuestObjectiveKind.TalkTo, "1001");
            quest.Report(QuestObjectiveKind.ReachLocation, "camp");
            AssertSecondQuestIncomplete();
        }

        private void AssertSecondQuestIncomplete()
        {
            Assert.That(quest.TryGet(1002, out QuestProgress progress), Is.True);
            Assert.That(progress.State, Is.EqualTo(QuestState.InProgress));
            Assert.That(conditions.Snapshot(target.TargetId).Read(EncounterContext.Fact.StoryFlag, "quest_completed_1002"), Is.False);
        }

        private T Track<T>(T value) where T : UnityEngine.Object { created.Add(value); return value; }
        private static Exception Error(UniTask<bool> task)
        {
            Assert.That(task.Status.IsCompleted(), Is.True);
            try { task.GetAwaiter().GetResult(); return null; }
            catch (Exception e) { return e; }
        }

        private sealed class TableConfig : IConfigService
        {
            public global::cfg.Tables Tables { get; } = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            public ulong ContentHash => 0;
        }
        private sealed class TestPlatform : IPlatformService
        {
            public PlatformKind Kind => PlatformKind.Standalone;
            public string SaveRoot { get; } = Path.Combine(Path.GetTempPath(), "narrative-tests-" + Guid.NewGuid().ToString("N"));
            public bool IsTouchPrimary => false;
            public void Vibrate(VibrationKind kind) { }
        }
        private sealed class Bus<T> : ISubscriber<T>, IPublisher<T>
        {
            private readonly List<IMessageHandler<T>> handlers = new List<IMessageHandler<T>>();
            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            { handlers.Add(handler); return new Subscription(() => handlers.Remove(handler)); }
            public void Publish(T message) { foreach (IMessageHandler<T> handler in handlers.ToArray()) handler.Handle(message); }
        }
        private sealed class Subscription : IDisposable
        {
            private Action release;
            public Subscription(Action release) => this.release = release;
            public void Dispose() { release?.Invoke(); release = null; }
        }
        private sealed class PendingUI : IUIService, IHudVisibility
        {
            private Action fail;
            public bool Throws { get; set; }
            public bool IgnoreCancellation { get; set; }
            public int Opens { get; private set; }
            public bool IsHudHidden => false;
            public void SetHudHidden(bool hidden) { }
            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                Opens++;
                if (Throws) return UniTask.FromException<T>(new InvalidOperationException("测试展示失败"));
                var source = new UniTaskCompletionSource<T>();
                fail = () => source.TrySetException(new InvalidOperationException("迟到展示失败"));
                if (!IgnoreCancellation) ct.Register(() => source.TrySetCanceled(ct));
                return source.Task;
            }
            public void FailPending() => fail();
            public UniTask CloseAsync(UIView view, CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => null;
            public void SetLayerVisible(UILayer layer, bool visible) { }
            public bool IsLayerVisible(UILayer layer) => true;
        }
        private sealed class Pause : IWorldPauseService
        {
            public bool IsPaused => false;
            public IDisposable Acquire(object owner) => new Subscription(() => { });
        }
        private sealed class NoInput : IInputService
        {
            public GameInput Actions => null;
            public void EnableMap(string map) { }
            public void DisableMap(string map) { }
        }
        private sealed class TestClock : IClock
        {
            public DateTime UtcNow => DateTime.UnixEpoch;
            public float GameTime => 0;
            public float UnscaledTime => 0;
            public float DeltaTime => 0;
            public float UnscaledDeltaTime => 0;
        }
        private sealed class UnusedAssets : IAssetService
        {
            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object => throw new NotSupportedException();
            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default) where T : UnityEngine.Object => throw new NotSupportedException();
            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default) => throw new NotSupportedException();
            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException();
            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default) => throw new NotSupportedException();
        }
    }
}
