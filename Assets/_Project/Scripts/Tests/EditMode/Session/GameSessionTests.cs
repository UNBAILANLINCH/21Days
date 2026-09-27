// 职责：覆盖 GameSession——新游戏建槽、继续读槽、坏文件 / 高版本槽不可用且继续失败不动内存、删除、
//   RequestSave 在闸门关时不写、开时写一次（多次请求合并）。
// 为什么新建：GameSession 是 Session 模块的核心门面，按「测试类 = <被测类>Tests」单独成文件。
// 怎么搭：存档用真实 JsonSaveService 指向临时目录（照 SettingsServiceTests）；玩法现场、状态流、通知、事件发布都用最小假实现。
//   异步用例写成 [UnityTest] + UniTask.ToCoroutine（同步等会死锁，见 JsonSaveServiceTests 类注释）。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Monster;
using Game.Session;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Session
{
    public sealed class GameSessionTests
    {
        private const string Progress = "主线：出发";

        private string saveRoot;
        private FakePlatformService platform;
        private JsonSaveService saves;
        private SessionConfig config;
        private FakeClock clock;
        private FakeLogicClock logicClock;
        private FakeGameFlow flow;
        private FakeStateSource state;
        private FakeNotificationService notifications;
        private FakePublisher<SessionStartedEvent> started;
        private FakePublisher<SaveCompletedEvent> completed;
        private GameSession session;

        [SetUp]
        public void SetUp()
        {
            saveRoot = Path.Combine(Path.GetTempPath(), "21Days-session-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            platform = new FakePlatformService(saveRoot);
            saves = new JsonSaveService(platform, null, null);
            config = ScriptableObject.CreateInstance<SessionConfig>();
            clock = new FakeClock { UtcNow = new DateTime(2026, 9, 26, 8, 30, 0, DateTimeKind.Utc) };
            logicClock = new FakeLogicClock { Tick = 42 };
            flow = new FakeGameFlow();
            state = new FakeStateSource { IsGameplayState = true, ProgressText = Progress };
            notifications = new FakeNotificationService();
            started = new FakePublisher<SessionStartedEvent>();
            completed = new FakePublisher<SaveCompletedEvent>();
            session = CreateSession(saves);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            session.Dispose();
            Object.DestroyImmediate(config);
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
        }

        [UnityTest]
        public IEnumerator NewGame_ThenSaveNow_WritesSlotWithMetadata() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(await session.NewGameAsync(2), Is.True);

            Assert.That(session.CurrentSlot, Is.EqualTo(2));
            Assert.That(flow.GoToCalls, Is.EqualTo(new[] { typeof(MonsterEncounterState) }), "新游戏切进遭遇状态");
            Assert.That(state.PrepareRestoreCalls, Is.EqualTo(new[] { false }), "新游戏清掉遭遇恢复");
            Assert.That(started.Received.Count, Is.EqualTo(1));
            Assert.That(started.Received[0].Slot, Is.EqualTo(2));
            Assert.That(started.Received[0].IsNewGame, Is.True);

            clock.UnscaledDeltaTime = 1.5f;
            session.Tick();
            session.Tick();

            Assert.That(await session.SaveNowAsync("test"), Is.True);

            Assert.That(File.Exists(Path.Combine(saveRoot, "slot2.json")), Is.True, "落盘建出槽文件");
            Assert.That(state.CapturedTicks, Is.EqualTo(new[] { 42L }), "按逻辑 tick 捕获遭遇");
            Assert.That(session.LatestSlot, Is.EqualTo(2));
            Assert.That(completed.Received.Count, Is.EqualTo(1));
            Assert.That(completed.Received[0].Success, Is.True);
            Assert.That(completed.Received[0].Reason, Is.EqualTo("test"));
            Assert.That(notifications.Titles, Is.EqualTo(new[] { config.SaveNoticeTitle }), "成功弹「已保存」");

            IReadOnlyList<SlotInfo> infos = await session.ReadSlotInfosAsync();
            Assert.That(infos.Count, Is.EqualTo(config.SlotCount));
            Assert.That(infos[0].State, Is.EqualTo(SlotState.Empty));
            Assert.That(infos[1].State, Is.EqualTo(SlotState.Available));
            Assert.That(infos[1].SavedAtUtc, Is.EqualTo(clock.UtcNow));
            Assert.That(infos[1].PlaytimeSeconds, Is.EqualTo(3f).Within(1e-4f), "两帧各 1.5 秒");
            Assert.That(infos[1].ProgressText, Is.EqualTo(Progress));
        });

        [UnityTest]
        public IEnumerator Continue_AfterRestart_ReadsBackMetadataAndPreparesRestore() => UniTask.ToCoroutine(async () =>
        {
            await session.NewGameAsync(1);
            clock.UnscaledDeltaTime = 2f;
            session.Tick();
            Assert.That(await session.SaveNowAsync("test"), Is.True);

            // 模拟重启：新的存档服务与会话，内存分区是空的。
            JsonSaveService freshSaves = new JsonSaveService(platform, null, null);
            GameSession fresh = CreateSession(freshSaves);
            state.PrepareRestoreCalls.Clear();
            flow.GoToCalls.Clear();
            started.Received.Clear();

            await fresh.InitializeAsync(CancellationToken.None);
            Assert.That(fresh.LatestSlot, Is.EqualTo(1), "启动扫描得到最近槽");
            Assert.That(fresh.CurrentSlot, Is.Zero, "启动只扫描不加载");

            Assert.That(await fresh.ContinueAsync(1), Is.True);

            Assert.That(fresh.CurrentSlot, Is.EqualTo(1));
            Assert.That(freshSaves.Get<SessionSaveData>().ProgressText, Is.EqualTo(Progress));
            Assert.That(fresh.PlaytimeSeconds, Is.EqualTo(2f).Within(1e-4f), "游玩时长从存档续上");
            Assert.That(state.PrepareRestoreCalls, Is.EqualTo(new[] { true }));
            Assert.That(started.Received.Count, Is.EqualTo(1));
            Assert.That(started.Received[0].IsNewGame, Is.False);
            Assert.That(flow.GoToCalls, Is.EqualTo(new[] { typeof(MonsterEncounterState) }));
            fresh.Dispose();
        });

        [UnityTest]
        public IEnumerator CorruptAndNewerSlots_AreUnavailable_AndContinueFailsWithoutSideEffects() =>
            UniTask.ToCoroutine(async () =>
            {
                File.WriteAllText(Path.Combine(saveRoot, "slot1.json"), "{ 这不是合法 JSON ", new UTF8Encoding(false));
                File.WriteAllText(
                    Path.Combine(saveRoot, "slot2.json"),
                    "{ \"formatVersion\": 1, \"partitions\": { \"Game.Session.SessionSaveData\": "
                    + "{ \"version\": 99, \"data\": { \"SavedAtUtcTicks\": 1, \"ProgressText\": \"未来\" } } } }",
                    new UTF8Encoding(false));

                // 坏档与高版本档按设计会记 Error；这里断言的是「判不可用、不抛、不动内存」。
                LogAssert.ignoreFailingMessages = true;

                IReadOnlyList<SlotInfo> infos = await session.ReadSlotInfosAsync();
                Assert.That(infos[0].State, Is.EqualTo(SlotState.Unavailable), "损坏文件");
                Assert.That(infos[1].State, Is.EqualTo(SlotState.Unavailable), "分区版本高于代码");
                Assert.That(infos[2].State, Is.EqualTo(SlotState.Empty));

                await session.NewGameAsync(3);
                saves.Get<SessionSaveData>().ProgressText = "内存里的当前局";
                flow.GoToCalls.Clear();
                started.Received.Clear();

                Assert.That(await session.ContinueAsync(1), Is.False);
                Assert.That(await session.ContinueAsync(2), Is.False);

                Assert.That(session.CurrentSlot, Is.EqualTo(3), "失败不改当前槽");
                Assert.That(saves.Get<SessionSaveData>().ProgressText, Is.EqualTo("内存里的当前局"), "失败不动内存分区");
                Assert.That(flow.GoToCalls, Is.Empty, "失败不切状态");
                Assert.That(started.Received, Is.Empty);
                Assert.That(notifications.Titles, Is.EqualTo(new[] { config.LoadFailedTitle, config.LoadFailedTitle }));
            });

        [UnityTest]
        public IEnumerator DeleteSlot_WhenCurrent_RemovesFileAndResetsSlots() => UniTask.ToCoroutine(async () =>
        {
            await session.NewGameAsync(1);
            await session.SaveNowAsync("test");
            Assert.That(session.LatestSlot, Is.EqualTo(1));

            await session.DeleteSlotAsync(1);

            Assert.That(File.Exists(Path.Combine(saveRoot, "slot1.json")), Is.False);
            Assert.That(session.CurrentSlot, Is.Zero, "删的是当前槽则归 0");
            Assert.That(session.LatestSlot, Is.Zero, "没有可用槽了");
        });

        [UnityTest]
        public IEnumerator RequestSave_WhenGateClosed_DoesNotWrite_ThenWritesOnceWhenOpen() => UniTask.ToCoroutine(async () =>
        {
            await session.NewGameAsync(1);
            state.DialogueRunning = true;

            session.RequestSave("quest_activated");
            session.RequestSave("crate");
            session.Tick();

            Assert.That(completed.Received, Is.Empty, "对白中不存");
            Assert.That(File.Exists(Path.Combine(saveRoot, "slot1.json")), Is.False);
            Assert.That(session.HasPendingSave, Is.True, "请求挂着等闸门");

            state.DialogueRunning = false;
            session.Tick();
            Assert.That(session.HasPendingSave, Is.False, "闸门一开就取走");

            await WaitUntilAsync(() => completed.Received.Count > 0);
            session.Tick();
            session.Tick();

            Assert.That(completed.Received.Count, Is.EqualTo(1), "两次请求合并成一次落盘");
            Assert.That(completed.Received[0].Reason, Is.EqualTo("crate"), "原因记最后一次");
            Assert.That(completed.Received[0].Success, Is.True);
            Assert.That(File.Exists(Path.Combine(saveRoot, "slot1.json")), Is.True);
        });

        [Test]
        public void RequestSave_BeforeGameStarts_IsIgnored()
        {
            session.RequestSave("crate");

            Assert.That(session.HasPendingSave, Is.False, "标题页阶段（CurrentSlot == 0）不记请求");
        }

        [UnityTest]
        public IEnumerator SaveNow_WhenNotInGameplay_SkipsWithoutWriting() => UniTask.ToCoroutine(async () =>
        {
            await session.NewGameAsync(1);
            state.IsGameplayState = false;

            Assert.That(await session.SaveNowAsync("quit"), Is.False);

            Assert.That(state.CapturedTicks, Is.Empty, "不在玩法状态不捕获（会捕获到停掉的遭遇）");
            Assert.That(File.Exists(Path.Combine(saveRoot, "slot1.json")), Is.False);
        });

        private GameSession CreateSession(ISaveService saveService)
        {
            return new GameSession(
                saveService, clock, logicClock, flow, state, config, notifications, started, completed,
                NullTelemetryScope.Instance);
        }

        private static async UniTask WaitUntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("等待超时：落盘没有完成");
                await UniTask.Yield();
            }
        }

        private sealed class FakePlatformService : IPlatformService
        {
            public FakePlatformService(string saveRoot) => SaveRoot = saveRoot;
            public PlatformKind Kind => PlatformKind.Standalone;
            public string SaveRoot { get; }
            public bool IsTouchPrimary => false;

            public void Vibrate(VibrationKind kind)
            {
            }
        }

        private sealed class FakeClock : IClock
        {
            public DateTime UtcNow { get; set; }
            public float GameTime => 0f;
            public float UnscaledTime => 0f;
            public float DeltaTime => 0f;
            public float UnscaledDeltaTime { get; set; }
        }

        private sealed class FakeLogicClock : ILogicClock
        {
            public long Tick { get; set; }
            public float FixedDeltaTime => 1f / 60f;
            public float SimTime => Tick * FixedDeltaTime;
        }

        /// <summary>只记录切到了哪个状态类型，不真的进状态。</summary>
        private sealed class FakeGameFlow : IGameFlow
        {
            public List<Type> GoToCalls { get; } = new List<Type>();
            public GameState Current => null;

            public UniTask GoToAsync<TState>(CancellationToken ct = default) where TState : GameState
            {
                GoToCalls.Add(typeof(TState));
                return UniTask.CompletedTask;
            }
        }

        private sealed class FakeStateSource : ISessionStateSource
        {
            public bool IsGameplayState { get; set; }
            public bool DialogueRunning { get; set; }
            public bool AnyPanelOpen { get; set; }
            public bool BattleResultPending { get; set; }
            public string ProgressText { get; set; }
            public List<long> CapturedTicks { get; } = new List<long>();
            public List<bool> PrepareRestoreCalls { get; } = new List<bool>();

            public void CaptureEncounter(long tick) => CapturedTicks.Add(tick);
            public void PrepareRestore(bool fromSave) => PrepareRestoreCalls.Add(fromSave);
            public string BuildProgressText() => ProgressText;
        }

        private sealed class FakeNotificationService : INotificationService
        {
            public List<string> Titles { get; } = new List<string>();
            public void Show(string title, string body = null, float seconds = 0f) => Titles.Add(title);
        }

        private sealed class FakePublisher<T> : IPublisher<T>
        {
            public List<T> Received { get; } = new List<T>();
            public void Publish(T message) => Received.Add(message);
        }
    }
}
