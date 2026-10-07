// 职责：钉住 C5 的**拒绝路径**与唯一写入方纪律——身份四项对不上 / 结果未声明一律拒绝、不推进、且留痕。
// 为什么新建：`NarrativeBattleTests` 测的是 `NarrativeRules` 的纯规则返回值；PRP/battle-to-narrative §2.6
//   要求「拒绝路径必须有点（它是内容写错的唯一现场）」，而 `battle_result_rejected` 埋点落在服务层
//   （NarrativeService.CompleteBattleAsync），只有接上真实的 Narrative/Dialogue/Save 才测得到。
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
using Game.Tests.EditMode.Telemetry;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class NarrativeBattleRejectionTests
    {
        /// <summary>样例战斗关（`Tables/Data/narrative/sample_battle_variants.json`：入口就是战斗阶段）。</summary>
        private const string BattleStory = "sample_battle_variants";

        private const string BattleStage = "fight";

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private JsonSaveService saves;
        private DialogueService dialogue;
        private NarrativeService narrative;
        private NarrativeConditionSource conditions;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            var config = new TableConfig();
            saves = new JsonSaveService(new TestPlatform(), null, null);
            var player = new PlayerModel();
            player.Restore(new PlayerSaveData { Health = 10 });
            conditions = new NarrativeConditionSource(player, saves);
            var dialogues = new DialogueCatalog(config);
            var dialogueRules = new DialogueRules(new DialogueReadData(), 500, null);
            var ui = new PendingUI();
            var controller = new DialogueController(dialogueRules, dialogues, ui, new UnusedAssets(), new TestClock(), null);
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
            var dialogueConfig = Track(ScriptableObject.CreateInstance<DialogueConfig>());
            dialogue = new DialogueService(dialogues, dialogueRules, controller, dialogueConfig, conditions,
                new Pause(), new NoInput(), ui, null);
            var binder = new DialogueSceneBinder(dialogue, ui, new Bus<HudVisibilityChangedEvent>());
            narrative = new NarrativeService(new NarrativeCatalog(config, dialogues), dialogue, binder, conditions, saves,
                new Bus<SessionStartedEvent>(), new Bus<QuestCompletedEvent>(), new Bus<NarrativeChangedEvent>(),
                telemetry.Scope("narrative"));
            narrative.InitializeAsync(default).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            narrative?.Dispose();
            dialogue?.Dispose();
            telemetry?.Dispose();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        /// <summary>正向对照：身份四项全对 + 结果在本阶段声明里 → 推进，并留 `battle_result_applied`。</summary>
        [Test]
        public void CompleteBattle_WithMatchingIdentityAndDeclaredResult_Advances()
        {
            StartBattle();

            bool accepted = Complete(narrative.Generation, Activation(), "boss", "Victory");

            Assert.That(accepted, Is.True);
            // Victory 出口指向的 End 阶段没有父阶段，自动推进把它收成整段结局：Current 清空、Outcome 记 End 阶段自己的词。
            Assert.That(narrative.Capture().Current, Is.Null);
            Assert.That(narrative.Capture().Outcome, Is.EqualTo("Success"));
            Assert.That(HasEvent("battle_result_applied"), Is.True);
        }

        /// <summary>身份对不上（旧回调 / 读档前的迟到结果）→ 拒绝、不推进，并留 `battle_result_rejected`。</summary>
        [Test]
        public void CompleteBattle_WithStaleGeneration_RejectsWithoutAdvancing()
        {
            StartBattle();
            long generation = narrative.Generation;
            long activation = Activation();
            sink.Clear();

            bool accepted = Complete(generation + 1, activation, "boss", "Victory");

            Assert.That(accepted, Is.False);
            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo(BattleStage), "被拒的结果不许推进阶段");
            Assert.That(HasEvent("battle_result_rejected"), Is.True, "拒绝路径必须留痕");
        }

        /// <summary>结果码未声明（拼错 / 内容写漏）→ 拒绝、不推进，并留痕。</summary>
        [Test]
        public void CompleteBattle_WithUndeclaredResult_RejectsWithoutAdvancing()
        {
            StartBattle();
            long generation = narrative.Generation;
            long activation = Activation();
            sink.Clear();

            bool accepted = Complete(generation, activation, "boss", "NotAResult");

            Assert.That(accepted, Is.False);
            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo(BattleStage));
            Assert.That(HasEvent("battle_result_rejected"), Is.True);
        }

        /// <summary>目标对不上 → 同样拒绝（身份四项里的 TargetId）。</summary>
        [Test]
        public void CompleteBattle_WithWrongTarget_RejectsWithoutAdvancing()
        {
            StartBattle();
            long generation = narrative.Generation;
            long activation = Activation();

            Assert.That(Complete(generation, activation, "someone_else", "Victory"), Is.False);
            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo(BattleStage));
        }

        /// <summary>`BossPhaseChanged:&lt;形态&gt;` 不换阶段：自环回到同一个战斗阶段（PRP §2.3）。</summary>
        [Test]
        public void CompleteBattle_BossPhaseChanged_KeepsSameBattleStage()
        {
            StartBattle();
            long generation = narrative.Generation;
            long activation = Activation();

            Assert.That(Complete(generation, activation, "boss", "BossPhaseChanged:form2"), Is.True);

            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo(BattleStage));
            Assert.That(narrative.Capture().Current.ActivationId, Is.GreaterThan(activation), "自环也是一次阶段进入");
        }

        private void StartBattle()
        {
            Assert.That(narrative.StartAsync(BattleStory, "boss").GetAwaiter().GetResult(), Is.True);
            Assert.That(narrative.Capture().Current.StageId, Is.EqualTo(BattleStage), "入口就是战斗阶段");
        }

        private long Activation() => narrative.Capture().Current.ActivationId;

        private bool Complete(long generation, long activation, string targetId, string resultKey) =>
            narrative.CompleteBattleAsync(generation, activation, targetId, resultKey).GetAwaiter().GetResult();

        private bool HasEvent(string name)
        {
            foreach (string line in sink.Lines)
                if (line.IndexOf(name, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private T Track<T>(T value) where T : UnityEngine.Object { created.Add(value); return value; }

        private sealed class TableConfig : IConfigService
        {
            public global::cfg.Tables Tables { get; } = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            public ulong ContentHash => 0;
        }

        private sealed class TestPlatform : IPlatformService
        {
            public PlatformKind Kind => PlatformKind.Standalone;
            public string SaveRoot { get; } = Path.Combine(Path.GetTempPath(), "narrative-battle-" + Guid.NewGuid().ToString("N"));
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
            public bool IsHudHidden => false;
            public void SetHudHidden(bool hidden) { }
            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView =>
                UniTask.FromException<T>(new NotSupportedException("战斗阶段不播对白"));
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
