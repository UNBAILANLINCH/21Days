// 职责：钉住 QuestService.ResetProgress / ReloadFromSave——重置回新开局或换入分区后，进度与分区一致、
//   存档分区同步写回，并补发 HUD / 面板刷新所需的事件（含读档后规则层静默时的追踪事件）。
// 为什么新建：QuestService 此前没有测试文件（QuestRulesTests 测纯规则，不经过门面的存档写回与事件发布），
//   按「测试类 = <被测类>Tests」单独成文件。数据用真实生成的任务表（与 QuestCatalogTests 同一来源）。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Config;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Quest;
using Game.Session;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;

namespace Game.Tests.EditMode.Quest
{
    /// <summary><see cref="QuestService"/> 的 EditMode 测试。不经容器，依赖全部用最小假实现。</summary>
    public sealed class QuestServiceTests
    {
        private const int MainQuest = 1001;
        private const int SecondMainQuest = 1002;
        private const int LookoutQuest = 2001;
        private const int CrateQuest = 2002;

        private FakeSaveService saves;
        private FakePublisher<QuestActivatedEvent> activated;
        private FakePublisher<QuestObjectiveProgressedEvent> progressed;
        private FakePublisher<QuestTrackingChangedEvent> tracking;
        private FakeSubscriber<SessionStartedEvent> sessionStarted;
        private QuestService service;

        [SetUp]
        public void SetUp()
        {
            global::cfg.Tables tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            var catalog = new QuestCatalog(new FakeConfigService(tables), NullTelemetryScope.Instance);
            saves = new FakeSaveService();
            activated = new FakePublisher<QuestActivatedEvent>();
            progressed = new FakePublisher<QuestObjectiveProgressedEvent>();
            tracking = new FakePublisher<QuestTrackingChangedEvent>();
            sessionStarted = new FakeSubscriber<SessionStartedEvent>();
            service = new QuestService(
                catalog,
                saves,
                activated,
                progressed,
                new FakePublisher<QuestCompletedEvent>(),
                tracking,
                sessionStarted,
                NullTelemetryScope.Instance);
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(service.IsReady, Is.True, "真实任务表应能通过校验");
        }

        [TearDown]
        public void TearDown() => service.Dispose();

        [Test]
        public void ResetProgress_AfterAdvancing_ReturnsToInitialState()
        {
            service.Report(QuestObjectiveKind.TalkTo, MainQuest.ToString());
            service.Report(QuestObjectiveKind.Counter, "crate", 2);
            service.Track(CrateQuest);
            Assert.That(service.TryGet(MainQuest, out QuestProgress main), Is.True);
            Assert.That(main.ObjectiveIndex, Is.EqualTo(1), "前置：主线应已推进到第二个目标");
            Assert.That(service.TrackedId, Is.EqualTo(CrateQuest), "前置：追踪已切到支线");

            progressed.Received.Clear();
            tracking.Received.Clear();
            service.ResetProgress();

            Assert.That(service.TrackedId, Is.EqualTo(MainQuest), "重置后追踪回到默认主线");
            Assert.That(service.TryGet(MainQuest, out main), Is.True);
            Assert.That(main.State, Is.EqualTo(QuestState.InProgress));
            Assert.That(main.ObjectiveIndex, Is.Zero, "主线目标回到第一个");
            Assert.That(main.Count, Is.Zero);
            Assert.That(service.TryGet(CrateQuest, out QuestProgress crates), Is.True);
            Assert.That(crates.State, Is.EqualTo(QuestState.InProgress));
            Assert.That(crates.Count, Is.Zero, "物资箱计数清零");

            QuestSaveData saved = saves.Get<QuestSaveData>();
            Assert.That(saved.TrackedId, Is.EqualTo(MainQuest), "存档分区同步写回");
            Assert.That(saved.Quests.Exists(q => q.Id == CrateQuest && q.Count == 0), Is.True);
        }

        [Test]
        public void ResetProgress_PublishesTrackingAndProgressEventsForRefresh()
        {
            service.Report(QuestObjectiveKind.Counter, "crate");
            progressed.Received.Clear();
            tracking.Received.Clear();

            service.ResetProgress();

            Assert.That(tracking.Received.Count, Is.GreaterThanOrEqualTo(1), "至少一次追踪变更，HUD 据此刷新");
            Assert.That(tracking.Received[tracking.Received.Count - 1].QuestId, Is.EqualTo(MainQuest));
            Assert.That(progressed.Received.Exists(e => e.QuestId == CrateQuest && e.Count == 0), Is.True,
                "进行中任务各发一条计数归零的进度事件");
        }

        [Test]
        public void ReloadFromSave_AfterPartitionSwapped_MatchesNewPartitionState()
        {
            service.Report(QuestObjectiveKind.TalkTo, MainQuest.ToString());
            service.Report(QuestObjectiveKind.Counter, "crate", 2);
            service.Track(CrateQuest);
            Assert.That(service.TrackedId, Is.EqualTo(CrateQuest), "前置：追踪已切到支线");
            Assert.That(service.TryGet(MainQuest, out QuestProgress mainBefore), Is.True);
            Assert.That(mainBefore.ObjectiveIndex, Is.EqualTo(1), "前置：主线已推进到第二个目标");

            // 模拟读档：ISaveService.LoadAsync 会整体替换分区字典，这里用 ResetAll 后写入一份不同的进度代替，
            // 换入的分区里主线还在第一个目标、追踪的是主线而不是支线，跟重载前明显不同才能证明确实读了新分区。
            saves.ResetAll();
            QuestSaveData swapped = saves.Get<QuestSaveData>();
            swapped.Initialized = true;
            swapped.TrackedId = MainQuest;
            swapped.NextAcceptOrder = 2;
            swapped.Quests.Add(new QuestProgressData
                { Id = MainQuest, State = (int)QuestState.InProgress, ObjectiveIndex = 0, Count = 0, AcceptOrder = 1 });

            service.ReloadFromSave();

            Assert.That(service.TrackedId, Is.EqualTo(MainQuest), "重载后追踪应与换入的分区一致");
            Assert.That(service.TryGet(MainQuest, out QuestProgress main), Is.True);
            Assert.That(main.ObjectiveIndex, Is.Zero, "重载后应读到换入分区里的目标下标，而不是重载前的进度");
            Assert.That(service.TryGet(CrateQuest, out QuestProgress crate), Is.True);
            Assert.That(crate.State, Is.EqualTo(QuestState.InProgress), "换入分区没有这条任务，ActivateAvailable 重新激活为默认值");
            Assert.That(crate.Count, Is.Zero);

            QuestSaveData savedBack = saves.Get<QuestSaveData>();
            Assert.That(savedBack.TrackedId, Is.EqualTo(MainQuest), "重载后 Flush 把当前状态同步写回分区");
        }

        [Test]
        public void ReloadFromSave_InitializedPartitionWithNothingToActivate_PublishesTrackingOfSavedQuest()
        {
            // 开机即新开局：追踪默认主线 1001。换入的档是「1001 已完成、1002 追踪中」，两者追踪 id 不同。
            Assert.That(service.TrackedId, Is.EqualTo(MainQuest), "前置：新开局追踪默认主线");

            // 模拟读档：表里全部四条任务都已在分区里（1001 完成，1002 / 2001 / 2002 进行中），
            // Initialized = true，ActivateAvailable 既没有可新激活的任务，也不会自动改追踪——规则层一个事件都不发。
            saves.ResetAll();
            QuestSaveData swapped = saves.Get<QuestSaveData>();
            swapped.Initialized = true;
            swapped.TrackedId = SecondMainQuest;
            swapped.NextAcceptOrder = 4;
            swapped.Quests.Add(new QuestProgressData
                { Id = MainQuest, State = (int)QuestState.Completed, ObjectiveIndex = 1, Count = 1, AcceptOrder = 1 });
            swapped.Quests.Add(new QuestProgressData
                { Id = LookoutQuest, State = (int)QuestState.InProgress, ObjectiveIndex = 0, Count = 0, AcceptOrder = 2 });
            swapped.Quests.Add(new QuestProgressData
                { Id = CrateQuest, State = (int)QuestState.InProgress, ObjectiveIndex = 0, Count = 1, AcceptOrder = 3 });
            swapped.Quests.Add(new QuestProgressData
                { Id = SecondMainQuest, State = (int)QuestState.InProgress, ObjectiveIndex = 0, Count = 0, AcceptOrder = 4 });

            int activatedBefore = activated.Received.Count;
            tracking.Received.Clear();
            progressed.Received.Clear();

            service.ReloadFromSave();

            Assert.That(service.TrackedId, Is.EqualTo(SecondMainQuest), "前置核对：重载后追踪应与换入的分区一致");
            Assert.That(activated.Received.Count, Is.EqualTo(activatedBefore),
                "换入分区里没有需要新激活的任务，刷新事件不能是靠新激活顺带发出的");
            Assert.That(tracking.Received.Count, Is.GreaterThanOrEqualTo(1),
                "Restore 静默、ActivateAvailable 在已初始化的档上不改追踪，门面必须补发追踪事件让 HUD 刷新");
            Assert.That(tracking.Received[tracking.Received.Count - 1].QuestId, Is.EqualTo(SecondMainQuest),
                "最后一条追踪事件应指向换入分区的追踪任务，HUD 才不会停在读档前的文本");
            Assert.That(progressed.Received.Exists(e => e.QuestId == CrateQuest && e.Count == 1), Is.True,
                "进行中任务各补一条反映分区计数的进度事件");
        }

        [Test]
        public void SessionStartedEvent_Received_TriggersReload()
        {
            service.Report(QuestObjectiveKind.TalkTo, MainQuest.ToString());
            service.Track(CrateQuest);
            Assert.That(service.TrackedId, Is.EqualTo(CrateQuest), "前置：追踪已切到支线");

            saves.ResetAll();
            QuestSaveData swapped = saves.Get<QuestSaveData>();
            swapped.Initialized = true;
            swapped.TrackedId = MainQuest;
            swapped.NextAcceptOrder = 1;
            swapped.Quests.Add(new QuestProgressData
                { Id = MainQuest, State = (int)QuestState.InProgress, ObjectiveIndex = 0, Count = 0, AcceptOrder = 1 });

            tracking.Received.Clear();
            sessionStarted.Publish(new SessionStartedEvent(1, false));

            Assert.That(service.TrackedId, Is.EqualTo(MainQuest),
                "QuestService 在 InitializeAsync 里订阅了 SessionStartedEvent，收到后应自动 ReloadFromSave");
            Assert.That(tracking.Received.Count, Is.GreaterThanOrEqualTo(1), "事件触发的重载同样要补发追踪事件");
            Assert.That(tracking.Received[tracking.Received.Count - 1].QuestId, Is.EqualTo(MainQuest));
        }

        /// <summary>只记录收到的消息。</summary>
        private sealed class FakePublisher<T> : IPublisher<T>
        {
            public List<T> Received { get; } = new List<T>();
            public void Publish(T message) => Received.Add(message);
        }

        /// <summary>
        /// 最小假订阅者：<see cref="Subscribe"/> 记住处理器，<see cref="Publish"/> 模拟消息到达时逐个调用；
        /// 退订（Dispose 返回值）会把处理器摘掉，跟真实 MessagePipe 的订阅句柄语义一致。
        /// </summary>
        private sealed class FakeSubscriber<T> : ISubscriber<T>
        {
            private readonly List<IMessageHandler<T>> handlers = new List<IMessageHandler<T>>();

            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            {
                handlers.Add(handler);
                return new Subscription(this, handler);
            }

            public void Publish(T message)
            {
                for (int i = 0; i < handlers.Count; i++) handlers[i].Handle(message);
            }

            private sealed class Subscription : IDisposable
            {
                private FakeSubscriber<T> owner;
                private readonly IMessageHandler<T> handler;

                public Subscription(FakeSubscriber<T> owner, IMessageHandler<T> handler)
                {
                    this.owner = owner;
                    this.handler = handler;
                }

                public void Dispose()
                {
                    owner?.handlers.Remove(handler);
                    owner = null;
                }
            }
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
