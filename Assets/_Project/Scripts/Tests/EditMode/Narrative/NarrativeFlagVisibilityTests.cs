// 职责：钉住 NarrativeFlagVisibility（PRP/turnbased-battle W2b「打赢后 BOSS 退场」）——
//   标记成立时隐藏所挂物体、读档回到没写标记的进度时重新显示、标记变化随剧情分区写回即时刷新；
//   负对照：被击倒不写标记 → 物体还在；别的标记键不受影响；没绑定 / 解绑后不动物体。
// 为什么接真实 NarrativeService：显隐的触发点是剧情分区写回（Flush）与读档重载，要走真实的 sample_boss 遭遇与生成表才测得到；
//   夹具照 NarrativeBattleStageTests 搭（EditMode 不发 MonoBehaviour 生命周期消息，绑定 / 解绑都显式调用）。
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
    public sealed class NarrativeFlagVisibilityTests
    {
        private const string BossTarget = "boss_npc";
        private const string BossKind = "SampleBoss";
        private const string DefeatedFlag = "world.sampleboss.defeated";

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private readonly List<BattleStageEnteredEvent> published = new List<BattleStageEnteredEvent>();
        private JsonSaveService saves;
        private DialogueService dialogue;
        private NarrativeService narrative;
        private Bus<SessionStartedEvent> sessions;
        private PendingUI ui;
        private NarrativeTrigger target;

        [SetUp]
        public void SetUp()
        {
            published.Clear();
            var config = new TableConfig();
            saves = new JsonSaveService(new TestPlatform(), null, null);
            var player = new PlayerModel();
            player.Restore(new PlayerSaveData { Health = 10 });
            var conditions = new NarrativeConditionSource(player, saves);
            var dialogues = new DialogueCatalog(config);
            var dialogueRules = new DialogueRules(new DialogueReadData(), 500, null);
            ui = new PendingUI();
            var controller = new DialogueController(dialogueRules, dialogues, ui, new UnusedAssets(), new TestClock(), null);
            dialogue = new DialogueService(dialogues, dialogueRules, controller, Track(ScriptableObject.CreateInstance<DialogueConfig>()),
                conditions, new Pause(), new NoInput(), ui, null);
            var binder = new DialogueSceneBinder(dialogue, ui, new Bus<HudVisibilityChangedEvent>());
            sessions = new Bus<SessionStartedEvent>();
            var battles = new Bus<BattleStageEnteredEvent>();
            battles.Subscribe(new Handler<BattleStageEnteredEvent>(published.Add));
            narrative = new NarrativeService(new NarrativeCatalog(config, dialogues), dialogue, binder, conditions, saves,
                sessions, new Bus<QuestCompletedEvent>(), new Bus<NarrativeChangedEvent>(), null, battles);
            narrative.InitializeAsync(default).GetAwaiter().GetResult();
            var actor = Track(new GameObject("FlagVisibilityTestPlayer")).AddComponent<DialogueInteractionActor>();
            target = Track(new GameObject("FlagVisibilityTestBoss")).AddComponent<NarrativeTrigger>();
            target.Configure(BossTarget, BossKind, narrative, actor);
        }

        [TearDown]
        public void TearDown()
        {
            narrative?.Dispose();
            dialogue?.Dispose();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        // ── 纯判定 ──────────────────────────────────────────────────────────────

        [Test]
        public void ShouldBeVisible_FlagSet_Hides_FlagAbsent_Shows()
        {
            Assert.That(NarrativeFlagVisibility.ShouldBeVisible(true), Is.False);
            Assert.That(NarrativeFlagVisibility.ShouldBeVisible(false), Is.True);
        }

        // ── 绑定即刷新 ──────────────────────────────────────────────────────────

        [Test]
        public void Bind_FlagAbsent_StaysVisible()
        {
            // 负对照：新开局没有任何标记，绑上不该把物体藏起来。
            NarrativeFlagVisibility visibility = CreateVisibility(DefeatedFlag);

            Assert.That(visibility.IsBound, Is.True);
            Assert.That(visibility.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void Bind_FlagAlreadySet_HidesImmediately()
        {
            Complete(ReachBossFight(), "Victory");

            NarrativeFlagVisibility visibility = CreateVisibility(DefeatedFlag);

            Assert.That(visibility.gameObject.activeSelf, Is.False, "场景晚于标记加载：绑定那一刻就按当前标记藏起来");
        }

        // ── 标记变化即时刷新 ────────────────────────────────────────────────────

        [Test]
        public void Victory_WritesFlag_HidesBoundObject()
        {
            NarrativeFlagVisibility visibility = CreateVisibility(DefeatedFlag);
            BattleStageEnteredEvent stage = ReachBossFight();
            Assert.That(visibility.gameObject.activeSelf, Is.True, "前置：开打前 BOSS 在场");

            Complete(stage, "Victory");

            Assert.That(narrative.HasStoryFlag(DefeatedFlag), Is.True);
            Assert.That(visibility.gameObject.activeSelf, Is.False, "胜利写标记 → BOSS 退场");
        }

        [Test]
        public void Downed_NoFlag_KeepsObjectVisible()
        {
            // 负对照：被击倒走 retreat，不写标记，BOSS 留在原地等重打。
            NarrativeFlagVisibility visibility = CreateVisibility(DefeatedFlag);

            Complete(ReachBossFight(), "Downed");

            Assert.That(narrative.HasStoryFlag(DefeatedFlag), Is.False);
            Assert.That(visibility.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void Victory_OtherFlagKey_StaysVisible()
        {
            // 负对照：只认自己配的键，别的标记写进来不受影响。
            NarrativeFlagVisibility visibility = CreateVisibility("world.sampleboss.spared");

            Complete(ReachBossFight(), "Victory");

            Assert.That(visibility.gameObject.activeSelf, Is.True);
        }

        // ── 读档刷新 ────────────────────────────────────────────────────────────

        [Test]
        public void Reload_SaveWithoutFlag_ShowsAgain_SaveWithFlag_HidesAgain()
        {
            NarrativeFlagVisibility visibility = CreateVisibility(DefeatedFlag);
            BattleStageEnteredEvent stage = ReachBossFight();
            SaveSnapshot beforeVictory = saves.Capture();
            Complete(stage, "Victory");
            SaveSnapshot afterVictory = saves.Capture();
            Assert.That(visibility.gameObject.activeSelf, Is.False, "前置：胜利后已退场");

            saves.Commit(beforeVictory);
            sessions.Publish(new SessionStartedEvent(2, false));
            Assert.That(visibility.gameObject.activeSelf, Is.True, "读回打之前的档：BOSS 回来");

            saves.Commit(afterVictory);
            sessions.Publish(new SessionStartedEvent(2, false));
            Assert.That(visibility.gameObject.activeSelf, Is.False, "读回打赢之后的档：BOSS 不在");
        }

        // ── 没绑定 / 解绑 ───────────────────────────────────────────────────────

        [Test]
        public void Unbound_Refresh_DoesNotTouchObject()
        {
            // 负对照：没绑定剧情服务时什么都不做（不能因为「查不到标记」就当成没写标记去改显隐）。
            var visibility = Track(new GameObject("UnboundVisibility")).AddComponent<NarrativeFlagVisibility>();
            visibility.Configure(DefeatedFlag);
            visibility.gameObject.SetActive(false);

            visibility.Refresh();

            Assert.That(visibility.IsBound, Is.False);
            Assert.That(visibility.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void Unbind_ThenVictory_IgnoresChange()
        {
            // 负对照：解绑后剧情再变也不再收到通知（物体随场景卸载时走的就是这条）。
            NarrativeFlagVisibility visibility = CreateVisibility(DefeatedFlag);
            visibility.Unbind();

            Complete(ReachBossFight(), "Victory");

            Assert.That(visibility.gameObject.activeSelf, Is.True);
        }

        // ── 帮手 ────────────────────────────────────────────────────────────────

        private NarrativeFlagVisibility CreateVisibility(string key)
        {
            var visibility = Track(new GameObject("FlagVisibilityTarget")).AddComponent<NarrativeFlagVisibility>();
            visibility.Configure(key);
            visibility.Bind(narrative);
            return visibility;
        }

        // 按 E → 遭遇 sample_boss → taunt（测试 UI 让对白展示失败，阶段停在 taunt）→ 以对白出口 Done 提交 → 停到 fight。
        private BattleStageEnteredEvent ReachBossFight()
        {
            ui.Throws = true;
            Assert.That(Error(target.InteractAsync()), Is.TypeOf<InvalidOperationException>());
            NarrativeSaveData.Frame taunt = narrative.Capture().Current;
            Assert.That(taunt.StageId, Is.EqualTo("taunt"), "前置：遭遇进入 taunt");
            var done = new NarrativeIntent(narrative.Generation, taunt.ActivationId, taunt.TargetId, taunt.ActionRequestId, "Done");
            int before = published.Count;
            Assert.That(narrative.SubmitAsync(done).GetAwaiter().GetResult(), Is.True);
            Assert.That(published.Count, Is.EqualTo(before + 1), "停到 fight 时发布一次");
            return published[published.Count - 1];
        }

        private void Complete(BattleStageEnteredEvent stage, string exitKey)
        {
            Assert.That(narrative.TryBeginBattle(stage.Generation, stage.ActivationId, stage.TargetId), Is.True);
            Assert.That(narrative.CompleteBattleAsync(stage.Generation, stage.ActivationId, stage.TargetId, exitKey)
                .GetAwaiter().GetResult(), Is.True);
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
            public string SaveRoot { get; } = Path.Combine(Path.GetTempPath(), "narrative-flag-visibility-" + Guid.NewGuid().ToString("N"));
            public bool IsTouchPrimary => false;
            public void Vibrate(VibrationKind kind) { }
        }

        private sealed class Handler<T> : IMessageHandler<T>
        {
            private readonly Action<T> action;
            public Handler(Action<T> action) => this.action = action;
            public void Handle(T message) => action(message);
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
            public bool Throws { get; set; }
            public bool IsHudHidden => false;
            public void SetHudHidden(bool hidden) { }
            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                if (Throws) return UniTask.FromException<T>(new InvalidOperationException("测试展示失败"));
                var source = new UniTaskCompletionSource<T>();
                ct.Register(() => source.TrySetCanceled(ct));
                return source.Task;
            }
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
