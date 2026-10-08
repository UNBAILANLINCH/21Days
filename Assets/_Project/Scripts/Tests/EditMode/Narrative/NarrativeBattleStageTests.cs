// 职责：钉住剧情侧的战斗接线（PRP/turnbased-battle D2 / D9 / D11）——
//   停到 Battle 阶段发布 BattleStageEnteredEvent、读档恢复到 Battle 阶段再发布；「战斗在途」登记压住存档、放弃后恢复；
//   Battle 阶段上同目标交互重开这一仗；样例内容 sample_boss 走完 taunt → fight → Downed 可再按 E 重打、Victory 后写标记不再触发。
//   Reenter 下剧情停在对白阶段时连按 E 只重试对白、不再次进入遭遇；服务已释放时 NarrativeTrigger 交互直接返回 false。
//   每条配负对照（非战斗阶段不发、在途时不重发、旧身份登记被拒、已击败不再进入、服务可用时照常进入）。
// 为什么新建：NarrativeBattleTests / NarrativeBattleRejectionTests 测的是回写的接受与拒绝，不覆盖「通知战斗侧」与存档边界；
//   这几条要接真实的 Narrative / Dialogue / Save 与生成表才测得到，单独一个类。
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
using Game.Interaction;
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
    public sealed class NarrativeBattleStageTests
    {
        private const string BossTarget = "boss_npc";
        private const string BossKind = "SampleBoss";

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
            published.Clear(); // NUnit 一个夹具实例跑全部用例，字段初始化只发生一次
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
            // 玩家锚点来源改为统一交互的登记表（PRP/interaction D7）；不 Start，Actor 为空，同原来未启动的 DialogueSceneBinder。
            var interaction = new InteractionRegistry();
            sessions = new Bus<SessionStartedEvent>();
            var battles = new Bus<BattleStageEnteredEvent>();
            battles.Subscribe(new Handler<BattleStageEnteredEvent>(published.Add));
            narrative = new NarrativeService(new NarrativeCatalog(config, dialogues), dialogue, interaction, conditions, saves,
                sessions, new Bus<QuestCompletedEvent>(), new Bus<NarrativeChangedEvent>(), null, battles);
            narrative.InitializeAsync(default).GetAwaiter().GetResult();
            var actor = Track(new GameObject("BattleStageTestPlayer")).AddComponent<InteractionActor>();
            target = Track(new GameObject("BattleStageTestBoss")).AddComponent<NarrativeTrigger>();
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

        // ── D2：停到 Battle 阶段发布 ──────────────────────────────────────────────

        [Test]
        public void Drive_StopsAtBattleStage_PublishesCurrentIdentity()
        {
            narrative.StartAsync("sample_battle_variants", BossTarget).GetAwaiter().GetResult();

            Assert.That(published.Count, Is.EqualTo(1));
            NarrativeSaveData.Frame frame = narrative.Capture().Current;
            Assert.That(published[0].Generation, Is.EqualTo(narrative.Generation));
            Assert.That(published[0].ActivationId, Is.EqualTo(frame.ActivationId));
            Assert.That(published[0].TargetId, Is.EqualTo(BossTarget));
            Assert.That(published[0].StageId, Is.EqualTo("fight"));
        }

        [Test]
        public void Drive_StopsAtWaitStage_PublishesNothing()
        {
            // 负对照：非战斗阶段停住不发战斗通知。
            narrative.StartAsync("sample_wait", BossTarget).GetAwaiter().GetResult();

            Assert.That(published, Is.Empty);
        }

        // ── D9：读档恢复到 Battle 阶段再发布；存档边界 ─────────────────────────────

        [Test]
        public void Reload_RestoredAtBattleStage_PublishesAgainWithNewGeneration()
        {
            narrative.StartAsync("sample_battle_variants", BossTarget).GetAwaiter().GetResult();
            Assert.That(narrative.CanSave, Is.True, "战斗没在打时 Battle 阶段是可恢复边界");
            SaveSnapshot saved = saves.Capture();
            Assert.DoesNotThrow(() => narrative.ValidateCandidate(saved), "Battle 阶段的存档可被继续读取");
            long before = narrative.Generation;

            saves.Commit(saved);
            sessions.Publish(new SessionStartedEvent(2, false));

            Assert.That(published.Count, Is.EqualTo(2));
            Assert.That(published[1].Generation, Is.GreaterThan(before), "读档换代，旧战斗的回写会被拒");
            Assert.That(published[1].ActivationId, Is.EqualTo(published[0].ActivationId));
            Assert.That(published[1].StageId, Is.EqualTo("fight"));
        }

        [Test]
        public void Reload_RestoredAtWaitStage_PublishesNothing()
        {
            // 负对照：恢复到非战斗阶段不发。
            narrative.StartAsync("sample_wait", BossTarget).GetAwaiter().GetResult();
            SaveSnapshot saved = saves.Capture();

            saves.Commit(saved);
            sessions.Publish(new SessionStartedEvent(2, false));

            Assert.That(published, Is.Empty);
        }

        [Test]
        public void BeginBattle_HoldsSaving_ReleaseRestoresAndStageStays()
        {
            narrative.StartAsync("sample_battle_variants", BossTarget).GetAwaiter().GetResult();
            BattleStageEnteredEvent stage = published[0];

            Assert.That(narrative.TryBeginBattle(stage.Generation, stage.ActivationId, stage.TargetId), Is.True);
            Assert.That(narrative.CanSave, Is.False, "战斗在途：自动保存被 Session 的 NarrativeStable 闸住");
            Assert.Throws<ArgumentException>(() => narrative.ValidateCandidate(saves.Capture()), "在途快照不可恢复");

            narrative.ReleaseBattle(stage.Generation, stage.ActivationId);

            Assert.That(narrative.CanSave, Is.True, "放弃后回到进来前");
            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo("fight"), "阶段原地不动，可重试");
        }

        [Test]
        public void BeginBattle_StaleIdentity_IsRefusedAndKeepsSaving()
        {
            // 负对照：旧代数 / 旧激活号的登记被拒，存档不被误压。
            narrative.StartAsync("sample_battle_variants", BossTarget).GetAwaiter().GetResult();
            BattleStageEnteredEvent stage = published[0];

            Assert.That(narrative.TryBeginBattle(stage.Generation + 1, stage.ActivationId, stage.TargetId), Is.False);
            Assert.That(narrative.TryBeginBattle(stage.Generation, stage.ActivationId + 1, stage.TargetId), Is.False);
            Assert.That(narrative.CanSave, Is.True);
        }

        [Test]
        public void CompleteBattle_AfterBegin_AdvancesAndSavingResumes()
        {
            narrative.StartAsync("sample_battle_variants", BossTarget).GetAwaiter().GetResult();
            BattleStageEnteredEvent stage = published[0];
            narrative.TryBeginBattle(stage.Generation, stage.ActivationId, stage.TargetId);

            bool accepted = narrative.CompleteBattleAsync(stage.Generation, stage.ActivationId, stage.TargetId, "Downed")
                .GetAwaiter().GetResult();

            Assert.That(accepted, Is.True);
            Assert.That(narrative.Capture().Current, Is.Null);
            Assert.That(narrative.Capture().Outcome, Is.EqualTo("Downed"));
            Assert.That(narrative.CanSave, Is.True);
        }

        // ── Battle 阶段上同目标交互重开这一仗 ─────────────────────────────────────

        [Test]
        public void Interact_AtIdleBattleStage_RepublishesStage()
        {
            ReachBossFight();
            int before = published.Count;

            Assert.That(target.InteractAsync().GetAwaiter().GetResult(), Is.True);

            Assert.That(published.Count, Is.EqualTo(before + 1));
            Assert.That(published[published.Count - 1].ActivationId, Is.EqualTo(published[before - 1].ActivationId), "同一阶段、同一身份");
        }

        [Test]
        public void Interact_WhileBattleRunning_DoesNotRepublish()
        {
            // 负对照：战斗在途时再交互不发第二次通知（两场不叠）。
            BattleStageEnteredEvent stage = ReachBossFight();
            narrative.TryBeginBattle(stage.Generation, stage.ActivationId, stage.TargetId);
            int before = published.Count;

            Assert.That(target.InteractAsync().GetAwaiter().GetResult(), Is.False);

            Assert.That(published.Count, Is.EqualTo(before));
        }

        // ── D11：样例内容 sample_boss ─────────────────────────────────────────────

        [Test]
        public void SampleBoss_TauntThenFight_PublishesBossPayload()
        {
            BattleStageEnteredEvent stage = ReachBossFight();

            Assert.That(stage.Payload, Is.EqualTo("sample_boss"), "fight 阶段的 payload = BOSS 定义 id（D3）");
            Assert.That(stage.StageId, Is.EqualTo("fight"));
            Assert.That(stage.TargetId, Is.EqualTo(BossTarget));
        }

        [Test]
        public void SampleBoss_Downed_RetreatsAndCanBeFoughtAgain()
        {
            BattleStageEnteredEvent stage = ReachBossFight();
            Complete(stage, "Downed");
            Assert.That(narrative.Capture().Current, Is.Null, "retreat 是 End：故事结束");
            Assert.That(narrative.Capture().StoryFlags, Has.No.Member("world.sampleboss.defeated"));
            int opens = ui.Opens;

            // 再按 E：Reenter 规则要能再次进入（主动交互的纪元随剧情推进变化）。
            Assert.That(Error(target.InteractAsync()), Is.TypeOf<InvalidOperationException>(), "再次进入 taunt，对白（测试里会失败）被拉起");

            Assert.That(ui.Opens, Is.EqualTo(opens + 1));
            Assert.That(narrative.Capture().Current.StoryId, Is.EqualTo("sample_boss_battle"));
            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo("taunt"));
        }

        [Test]
        public void SampleBoss_Victory_WritesDefeatedFlagAndNoLongerTriggers()
        {
            // 负对照（相对上一条）：打赢后写「已击败」，遭遇条件不再满足，再按 E 不进入。
            BattleStageEnteredEvent stage = ReachBossFight();
            Complete(stage, "Victory");
            Assert.That(narrative.Capture().StoryFlags, Has.Member("world.sampleboss.defeated"));
            int opens = ui.Opens;

            Assert.That(target.InteractAsync().GetAwaiter().GetResult(), Is.False);

            Assert.That(ui.Opens, Is.EqualTo(opens), "不再拉起 taunt");
            Assert.That(narrative.Capture().Current, Is.Null);
        }

        // ── W2b：BOSS NPC 的交互键入口（NarrativeTrigger 把交互转交过来）──────────────

        [Test]
        public void Trigger_WithDialogueInteractable_InteractKeyEntersBossEncounter()
        {
            // 场景里 BOSS NPC 的接法：同物体挂 DialogueInteractable（焦点 / 交互提示 / 头顶标记）+ NarrativeTrigger（剧情入口）。
            var npc = Track(new GameObject("BattleStageTestFocusBoss"));
            DialogueInteractable entry = npc.AddComponent<DialogueInteractable>();
            NarrativeTrigger trigger = npc.AddComponent<NarrativeTrigger>();
            var actor = Track(new GameObject("BattleStageTestFocusPlayer")).AddComponent<InteractionActor>();
            Assert.That(entry.CanInteract, Is.False, "前置：无树无台词、没转交时焦点选不中它");

            trigger.Configure("boss_npc_focus", BossKind, narrative, actor);
            int opens = ui.Opens;
            entry.Interact(); // = 焦点系统收到交互键 E / 点了交互提示

            Assert.That(trigger.HasFocusEntry && entry.HasHandover && entry.CanInteract, Is.True, "绑定即转交，焦点选得中");
            Assert.That(ui.Opens, Is.EqualTo(opens + 1), "按 E 拉起 taunt 对白");
            Assert.That(narrative.Capture().Current.StoryId, Is.EqualTo("sample_boss_battle"));
            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo("taunt"));
            Assert.That(narrative.Capture().Current.TargetId, Is.EqualTo("boss_npc_focus"));
        }

        [Test]
        public void Trigger_WithoutDialogueInteractable_HasNoFocusEntry()
        {
            // 负对照：没挂 DialogueInteractable 的叙事目标与原来一样只有点击入口，不会凭空出现焦点。
            Assert.That(target.HasFocusEntry, Is.False);
            var bare = Track(new GameObject("BattleStageTestBareNpc")).AddComponent<DialogueInteractable>();
            Assert.That(bare.HasHandover || bare.CanInteract, Is.False, "没有接管方的无树无台词 NPC 仍不可交互");
        }

        // ── Reenter 规则：同一段剧情停在对白阶段时连按 E 不再次进入遭遇 ─────────────────

        [Test]
        public void Interact_RepeatedWhileStoryAtDialogueStage_RetriesDialogueWithoutReentering()
        {
            // sample_boss 是 Reenter：主动交互的纪元取剧情的 NextActivationId，剧情一推进就换纪元。剧情停在 taunt（对白展示失败）时
            // 连按 E，由「对白重试」分支先拦住——只重拉同一段对白，不再走遭遇仲裁、不叠一层局部遭遇、不记新的消费键。
            // 负对照见 SampleBoss_Downed_RetreatsAndCanBeFoughtAgain：剧情结束后再按 E 会重新进入（新的激活号）。
            ui.Throws = true;
            Assert.That(Error(target.InteractAsync()), Is.TypeOf<InvalidOperationException>(), "前置：遭遇进入 taunt，对白展示失败");
            NarrativeSaveData before = narrative.Capture();
            Assert.That(before.Current.StageId, Is.EqualTo("taunt"));
            int opens = ui.Opens;

            for (int i = 0; i < 3; i++)
                Assert.That(Error(target.InteractAsync()), Is.TypeOf<InvalidOperationException>(), $"第 {i + 1} 次按 E：重拉 taunt 对白");

            NarrativeSaveData after = narrative.Capture();
            Assert.That(ui.Opens, Is.EqualTo(opens + 3), "按 E 没被吞：每次都重试同一段对白");
            Assert.That(after.Current.StoryId, Is.EqualTo("sample_boss_battle"));
            Assert.That(after.Current.StageId, Is.EqualTo("taunt"));
            Assert.That(after.Current.ActivationId, Is.EqualTo(before.Current.ActivationId), "还是同一次激活");
            Assert.That(after.NextActivationId, Is.EqualTo(before.NextActivationId), "没有进入任何新阶段 / 新遭遇");
            Assert.That(after.Parent, Is.Null, "没有叠一层局部遭遇");
            Assert.That(after.ConsumedTriggers, Is.EquivalentTo(before.ConsumedTriggers), "没有记新的消费键");
        }

        // ── 服务不可用时交互不往服务上调 ──────────────────────────────────────────

        [Test]
        public void Interact_AfterServiceDisposed_ReturnsFalseWithoutCallingService()
        {
            // 根作用域销毁（退出 Play / 回标题重建）后，场景里的 NPC 还挂着旧服务：交互直接返回 false，不抛「尚未初始化或已释放」。
            narrative.Dispose();
            int opens = ui.Opens;

            UniTask<bool> interaction = target.InteractAsync();

            Assert.That(interaction.Status, Is.EqualTo(UniTaskStatus.Succeeded), "同步返回、没有抛异常");
            Assert.That(interaction.GetAwaiter().GetResult(), Is.False);
            Assert.That(ui.Opens, Is.EqualTo(opens), "没有拉起任何对白");
            Assert.That(published, Is.Empty);
        }

        [Test]
        public void Interact_WhileServiceReady_ReachesService()
        {
            // 负对照：同一个目标、服务可用时，交互照常进入遭遇（taunt 对白被拉起）。
            ui.Throws = true;
            int opens = ui.Opens;

            Assert.That(Error(target.InteractAsync()), Is.TypeOf<InvalidOperationException>(), "对白展示失败（测试 UI）——说明调到了服务");

            Assert.That(ui.Opens, Is.EqualTo(opens + 1));
            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo("taunt"));
        }

        // ── 帮手 ────────────────────────────────────────────────────────────────

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
            public string SaveRoot { get; } = Path.Combine(Path.GetTempPath(), "narrative-battle-stage-" + Guid.NewGuid().ToString("N"));
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
            public int Opens { get; private set; }
            public bool IsHudHidden => false;
            public void SetHudHidden(bool hidden) { }
            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                Opens++;
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
