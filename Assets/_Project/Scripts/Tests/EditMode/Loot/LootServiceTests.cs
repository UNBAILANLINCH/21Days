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
        private FakePublisher<MonsterDroppedEvent> monsterDropped;
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
            monsterDropped = new FakePublisher<MonsterDroppedEvent>();
            reset = new FakePublisher<LootResetEvent>();
            service = new LootService(config, saves, configService, quest, notifications, collected, monsterDropped,
                reset, NullTelemetryScope.Instance);
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

        // 怪物掉落：item id 列表进背包、弹通知（名字查 tbitem）、发布事件；不动箱子键、不推任务计数。
        [Test]
        public void SettleMonsterDrop_WithDropList_AddsItemsNotifiesAndPublishes()
        {
            int[] drops = { tables.TbItem.Get(1001).Id, tables.TbItem.Get(1004).Id };

            Assert.That(service.SettleMonsterDrop(2, drops), Is.True);

            Assert.That(service.Items[drops[0]], Is.EqualTo(1));
            Assert.That(service.Items[drops[1]], Is.EqualTo(1));
            Assert.That(saves.Get<LootSaveData>().CollectedCrates, Is.Empty, "怪物掉落不写箱子键");
            Assert.That(service.IsCollected("crate_a"), Is.False);
            Assert.That(notifications.Received.Count, Is.EqualTo(1));
            Assert.That(notifications.Received[0].Title, Is.EqualTo(config.RewardTitle));
            Assert.That(notifications.Received[0].Body,
                Is.EqualTo(tables.TbItem.Get(1001).Name + " ×1、" + tables.TbItem.Get(1004).Name + " ×1"),
                "正文逐件列出，名字查 tbitem");
            Assert.That(monsterDropped.Received.Count, Is.EqualTo(1));
            Assert.That(monsterDropped.Received[0].YaoId, Is.EqualTo(2));
            Assert.That(monsterDropped.Received[0].ItemIds, Is.EqualTo(drops));
            // 不断言「这条支线不激活」：quest.TryGet 对表里存在的任务一律返回 true（它的语义是「认得这个 id」，
            // 不是「已激活」），拿它当断言会被自己的错误理解骗过。真正的证据是另一条用例里的计数不变。
        }

        // 更硬的一条：先开一只箱子把支线计数顶到 1，再结算一次怪物掉落，计数必须原样不动。
        [Test]
        public void SettleMonsterDrop_DoesNotAdvanceCrateQuestCounter()
        {
            service.TryCollect(CreateCrate("crate_a", tables.TbItem.Get(1001).Id, 1));
            Assert.That(quest.TryGet(CrateQuest, out QuestProgress before) && before != null, Is.True);
            Assert.That(before.Count, Is.EqualTo(1));

            service.SettleMonsterDrop(2, new[] { tables.TbItem.Get(1004).Id });

            Assert.That(quest.TryGet(CrateQuest, out QuestProgress after) && after != null, Is.True);
            Assert.That(after.Count, Is.EqualTo(1), "怪物掉落不推物资箱计数");
        }

        [Test]
        public void SettleMonsterDrop_EmptyListOrBadYaoId_IsNoOp()
        {
            Assert.That(service.SettleMonsterDrop(2, new int[0]), Is.False);
            Assert.That(service.SettleMonsterDrop(0, new[] { tables.TbItem.DataList[0].Id }), Is.False);
            Assert.That(service.SettleMonsterDrop(2, null), Is.False);

            Assert.That(service.Items, Is.Empty);
            Assert.That(notifications.Received, Is.Empty);
            Assert.That(monsterDropped.Received, Is.Empty);
        }

        // 掉落列表里混了 item 表里没有的 id：不拦（掉落照样进背包），只留一条 Warn——
        // 拦下来玩家会看到「怪死了什么都没掉」，比日志里一条 Warn 更难查。
        [Test]
        public void SettleMonsterDrop_UnknownItemId_StillGrantsAndKeepsIdAsName()
        {
            int realId = tables.TbItem.Get(1001).Id;

            Assert.That(service.SettleMonsterDrop(2, new[] { realId, 987654 }), Is.True);

            Assert.That(service.Items[realId], Is.EqualTo(1));
            Assert.That(service.Items[987654], Is.EqualTo(1));
            Assert.That(notifications.Received[0].Body, Does.Contain("#987654"));
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

            // 拾取奖励走的是排队卡片那一档，角落小字本文件用不到。
            public void ShowCornerHint(string text, float seconds = 0f) => throw new NotSupportedException();
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
