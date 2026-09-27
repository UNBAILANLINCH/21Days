// 职责：钉住 LootService 的开箱门面——写分区、箱子切开、推进支线 2002 计数、弹奖励通知（物品名查 tbitem）、发布事件、幂等；重置清空并发布。
// 为什么新建：LootRulesTests 只测纯规则，不经过门面的存档 / 任务 / 通知 / 事件接线；一个被测类一个测试类。
//   依赖用真实生成的配置表 + 真实 QuestService（同 QuestServiceTests），其余是最小假实现；SupplyCrate 用 new GameObject 建，TearDown 销毁。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Config;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Loot;
using Game.Quest;
using Game.Session;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Loot
{
    /// <summary><see cref="LootService"/> 的 EditMode 测试。不经容器。</summary>
    public sealed class LootServiceTests
    {
        private const int CrateQuest = 2002;

        private global::cfg.Tables tables;
        private FakeSaveService saves;
        private QuestService quest;
        private FakeNotificationService notifications;
        private FakePublisher<CrateCollectedEvent> collected;
        private FakePublisher<LootResetEvent> reset;
        private LootConfig config;
        private LootService service;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            var configService = new FakeConfigService(tables);
            saves = new FakeSaveService();
            quest = new QuestService(
                new QuestCatalog(configService, NullTelemetryScope.Instance),
                saves,
                new FakePublisher<QuestActivatedEvent>(),
                new FakePublisher<QuestObjectiveProgressedEvent>(),
                new FakePublisher<QuestCompletedEvent>(),
                new FakePublisher<QuestTrackingChangedEvent>(),
                new NoopSubscriber<SessionStartedEvent>(),
                NullTelemetryScope.Instance);
            quest.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(quest.IsReady, Is.True, "真实任务表应能通过校验");

            config = ScriptableObject.CreateInstance<LootConfig>();
            created.Add(config);
            notifications = new FakeNotificationService();
            collected = new FakePublisher<CrateCollectedEvent>();
            reset = new FakePublisher<LootResetEvent>();
            service = new LootService(config, saves, configService, quest, notifications, collected, reset,
                NullTelemetryScope.Instance);
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            service.Dispose();
            quest.Dispose();
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void TryCollect_FirstTime_OpensCrateAdvancesQuestNotifiesAndPublishes()
        {
            global::cfg.Item item = tables.TbItem.DataList[0];
            SupplyCrate crate = CreateCrate("crate_a", item.Id, 2);

            Assert.That(service.TryCollect(crate), Is.True);

            Assert.That(crate.IsOpened, Is.True);
            Assert.That(service.IsCollected("crate_a"), Is.True);
            Assert.That(service.Items[item.Id], Is.EqualTo(2));
            Assert.That(saves.Get<LootSaveData>().CollectedCrates, Is.EqualTo(new[] { "crate_a" }), "写进存档分区");
            Assert.That(quest.TryGet(CrateQuest, out QuestProgress progress), Is.True);
            Assert.That(progress.Count, Is.EqualTo(1), "支线 2002 计数 +1");
            Assert.That(notifications.Received.Count, Is.EqualTo(1));
            Assert.That(notifications.Received[0].Title, Is.EqualTo(config.RewardTitle));
            Assert.That(notifications.Received[0].Body, Is.EqualTo(item.Name + " ×2"), "正文用 tbitem 名字");
            Assert.That(collected.Received.Count, Is.EqualTo(1));
            Assert.That(collected.Received[0].Key, Is.EqualTo("crate_a"));
            Assert.That(collected.Received[0].ItemId, Is.EqualTo(item.Id));
            Assert.That(collected.Received[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void TryCollect_SameKeyTwice_SecondIsNoOp()
        {
            SupplyCrate crate = CreateCrate("crate_a", tables.TbItem.DataList[0].Id, 1);
            service.TryCollect(crate);

            Assert.That(service.TryCollect(crate), Is.False);
            Assert.That(quest.TryGet(CrateQuest, out QuestProgress progress), Is.True);
            Assert.That(progress.Count, Is.EqualTo(1), "再次确认不推进任务");
            Assert.That(notifications.Received.Count, Is.EqualTo(1));
            Assert.That(collected.Received.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryCollect_UnknownItem_UsesIdAsName()
        {
            SupplyCrate crate = CreateCrate("crate_x", 987654, 3);

            service.TryCollect(crate);

            Assert.That(notifications.Received[0].Body, Is.EqualTo("#987654 ×3"));
        }

        [Test]
        public void Reset_AfterCollecting_ClearsAndPublishes()
        {
            service.TryCollect(CreateCrate("crate_a", tables.TbItem.DataList[0].Id, 1));

            service.Reset();

            Assert.That(service.IsCollected("crate_a"), Is.False);
            Assert.That(service.Items, Is.Empty);
            Assert.That(reset.Received.Count, Is.EqualTo(1));
        }

        [Test]
        public void ComposeBody_WhenFormatBroken_FallsBackWithoutThrowing()
        {
            Assert.That(LootService.ComposeBody("{0} ×{1}", "绷带", 2), Is.EqualTo("绷带 ×2"));
            Assert.That(LootService.ComposeBody("{2}", "绷带", 2), Is.EqualTo("{2}绷带 ×2"));
            Assert.That(LootService.ComposeBody(null, "绷带", 2), Is.EqualTo("绷带 ×2"));
        }

        private SupplyCrate CreateCrate(string key, int itemId, int count)
        {
            var go = new GameObject("TestCrate_" + key);
            created.Add(go);
            SupplyCrate crate = go.AddComponent<SupplyCrate>();
            var serialized = new SerializedObject(crate);
            serialized.FindProperty("crateKey").stringValue = key;
            serialized.FindProperty("itemId").intValue = itemId;
            serialized.FindProperty("count").intValue = count;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return crate;
        }

        /// <summary>只记录收到的消息。</summary>
        private sealed class FakePublisher<T> : IPublisher<T>
        {
            public List<T> Received { get; } = new List<T>();
            public void Publish(T message) => Received.Add(message);
        }

        /// <summary>不接任何消息的假订阅者：本文件不测 <c>QuestService.ReloadFromSave</c>，只是构造要这个参数。</summary>
        private sealed class NoopSubscriber<T> : ISubscriber<T>
        {
            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters) =>
                EmptyDisposable.Instance;

            private sealed class EmptyDisposable : IDisposable
            {
                public static readonly EmptyDisposable Instance = new EmptyDisposable();
                public void Dispose()
                {
                }
            }
        }

        /// <summary>只记录收到的通知。</summary>
        private sealed class FakeNotificationService : INotificationService
        {
            public List<(string Title, string Body)> Received { get; } = new List<(string Title, string Body)>();
            public void Show(string title, string body = null, float seconds = 0f) => Received.Add((title, body));
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>。</summary>
        private sealed class FakeConfigService : IConfigService
        {
            public FakeConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>内存分区：Get 按类型懒建；落盘相关一律不支持（本文件用不到）。</summary>
        private sealed class FakeSaveService : ISaveService
        {
            private readonly Dictionary<Type, ISaveData> parts = new Dictionary<Type, ISaveData>();

            public T Get<T>() where T : class, ISaveData, new()
            {
                if (!parts.TryGetValue(typeof(T), out ISaveData part))
                {
                    part = new T();
                    parts[typeof(T)] = part;
                }

                return (T)part;
            }

            public UniTask<bool> SaveAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public UniTask<bool> LoadAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public UniTask<SaveSnapshot> ReadCandidateAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public SaveSnapshot Capture() => throw new NotSupportedException();
            public void Commit(SaveSnapshot snapshot) => throw new NotSupportedException();
            public void ResetAll() => parts.Clear();

            public UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new() =>
                throw new NotSupportedException();

            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default)
                where T : class, new() => throw new NotSupportedException();

            public UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class =>
                throw new NotSupportedException();

            public bool Exists(int slot) => false;
            public void Delete(int slot) => throw new NotSupportedException();
        }
    }
}
