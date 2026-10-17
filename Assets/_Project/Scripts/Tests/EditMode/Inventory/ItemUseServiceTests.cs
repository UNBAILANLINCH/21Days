// 职责：钉住 ItemUseService 这条**完整的使用通路**——查使用表 → 找效果执行者 → 执行 → 扣件数，以及每一条失败分支
//   （没在使用表里 / 没有执行者 / 执行者拒绝 / 件数不够）都**不改背包**。
// 为什么新建：ItemUseRulesTests 只测纯判定（它不碰执行者与背包）；「点一下用了会怎样」只有串起 Loot 与执行者才测得到。
//   效果执行者用真的 HealPlayerEffect + 假的目标口（IItemEffectTarget），不依赖 UnityEngine.Object 与容器。
//   依赖照 LootServiceTests 的做法：真实生成的配置表 + 真实 QuestService，其余是最小假实现。
// 日志断言：「效果目标口为 null」那条会经 HealPlayerEffect 打一条 Error（设计如此：不许静默失败），
//   UTF 把未声明的 Error 判为失败，所以那一条用 LogAssert.Expect 显式声明（同 BattleFlowTests 的做法）。
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Config;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Inventory;
using Game.Loot;
using Game.Quest;
using Game.Session;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Inventory
{
    /// <summary><see cref="ItemUseService"/> 的 EditMode 测试。经真实 <see cref="LootService"/>（内存分区）落盘。</summary>
    public sealed class ItemUseServiceTests
    {
        private const int Potion = 1004;
        private const int IronSword = 1001;
        private const int UnknownItem = 9999;

        private readonly List<Object> created = new List<Object>();
        private MemorySaves saves;
        private LootService loot;
        private QuestService quest;
        private FakeEffectTarget target;
        private ItemEffectRegistry effects;
        private ItemUseService service;

        [SetUp]
        public void SetUp()
        {
            global::cfg.Tables tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            var configService = new FixedConfigService(tables);
            saves = new MemorySaves();
            quest = new QuestService(new QuestCatalog(configService, NullTelemetryScope.Instance), saves,
                new Recorder<QuestActivatedEvent>(), new Recorder<QuestObjectiveProgressedEvent>(),
                new Recorder<QuestCompletedEvent>(), new Recorder<QuestTrackingChangedEvent>(),
                new NoopSubscriber<SessionStartedEvent>(), NullTelemetryScope.Instance);
            quest.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

            var lootConfig = ScriptableObject.CreateInstance<LootConfig>();
            created.Add(lootConfig);
            loot = new LootService(lootConfig, saves, configService, quest, new NoNotifications(),
                new LootEventPublishers(new Recorder<CrateCollectedEvent>(), new Recorder<MonsterDroppedEvent>(),
                    new Recorder<LootResetEvent>(), new Recorder<BackpackChangedEvent>()),
                NullTelemetryScope.Instance);
            loot.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

            target = new FakeEffectTarget(health: 1, maxHealth: 3);
            effects = new ItemEffectRegistry();
            effects.Register(new HealPlayerEffect(amount: 2));
            service = new ItemUseService(
                new[]
                {
                    new ItemUseSpec(Potion, HealPlayerEffect.PlaceholderEffectId),
                    new ItemUseSpec(IronSword, "shop.enter_with_skin"),
                },
                effects, loot, NullTelemetryScope.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            quest.Dispose();
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
        }

        [Test]
        public void TryUse_WhenTheEffectHasAnExecutor_AppliesItAndConsumesOne()
        {
            loot.GrantItems(Potion, 2);

            ItemUseOutcome outcome = service.TryUse(Potion, target);

            Assert.That(outcome.IsOk, Is.True);
            Assert.That(outcome.Status, Is.EqualTo(ItemUseStatus.Ok));
            Assert.That(outcome.Effect.Status, Is.EqualTo(ItemEffectStatus.Applied));
            Assert.That(outcome.Effect.Amount, Is.EqualTo(2), "目标只差 2 点血，实际回 2");
            Assert.That(target.Health, Is.EqualTo(3));
            Assert.That(outcome.ConsumedCount, Is.EqualTo(1));
            Assert.That(loot.Items[Potion], Is.EqualTo(1), "用掉一件：背包数量减一");
        }

        [Test]
        public void TryUse_WhenTheLastOneIsUsed_RemovesTheEntry()
        {
            loot.GrantItems(Potion, 1);

            Assert.That(service.TryUse(Potion, target).IsOk, Is.True);

            Assert.That(loot.Items.ContainsKey(Potion), Is.False, "扣到 0 删掉这一项，与 LootRules.Consume 口径一致");
        }

        [Test]
        public void ReusableEffect_AppliesTwiceAndKeepsTheOwnedItem()
        {
            var reusable = new ItemUseService(new[] { new ItemUseSpec(Potion, HealPlayerEffect.PlaceholderEffectId, 0) },
                effects, loot, NullTelemetryScope.Instance);
            Assert.That(reusable.TryUse(Potion, target).IsOk, Is.False);
            loot.GrantItems(Potion, 1);
            Assert.That(reusable.TryUse(Potion, target).IsOk, Is.True);
            target.Health = 1;
            Assert.That(reusable.TryUse(Potion, target).IsOk, Is.True);
            Assert.That(loot.Items[Potion], Is.EqualTo(1));
        }

        // 关键分支：效果标识没有执行者时**不扣件数**、并回一个明确的失败码（防静默失败）。
        [Test]
        public void TryUse_WhenTheEffectHasNoExecutor_FailsWithoutConsuming()
        {
            loot.GrantItems(IronSword, 1);

            ItemUseOutcome outcome = service.TryUse(IronSword, target);

            Assert.That(outcome.IsOk, Is.False);
            Assert.That(outcome.Status, Is.EqualTo(ItemUseStatus.NoExecutor));
            Assert.That(outcome.ConsumedCount, Is.Zero);
            Assert.That(loot.Items[IronSword], Is.EqualTo(1), "没有执行者绝不扣件数");
        }

        [Test]
        public void TryUse_WhenTheItemIsNotInTheUseTable_FailsAsUnknownItem()
        {
            loot.GrantItems(UnknownItem, 1);

            ItemUseOutcome outcome = service.TryUse(UnknownItem, target);

            Assert.That(outcome.Status, Is.EqualTo(ItemUseStatus.UnknownItem));
            Assert.That(loot.Items[UnknownItem], Is.EqualTo(1));
        }

        [Test]
        public void TryUse_WhenNotOwned_FailsAsNotEnoughWithoutApplyingTheEffect()
        {
            ItemUseOutcome outcome = service.TryUse(Potion, target);

            Assert.That(outcome.Status, Is.EqualTo(ItemUseStatus.NotEnough));
            Assert.That(outcome.Effect.Status, Is.EqualTo(ItemEffectStatus.None), "判定就没过，根本没走到执行阶段");
            Assert.That(target.Health, Is.EqualTo(1), "血没回");
        }

        // 执行者拒绝（满血）：效果没生效、件数也不扣 —— 这是「先执行、成功后才扣」换来的好处。
        [Test]
        public void TryUse_WhenTheTargetIsAtFullHealth_RejectsWithoutConsuming()
        {
            loot.GrantItems(Potion, 1);
            target.Health = 3;

            ItemUseOutcome outcome = service.TryUse(Potion, target);

            Assert.That(outcome.Status, Is.EqualTo(ItemUseStatus.EffectRejected));
            Assert.That(outcome.Effect.Reason, Is.Not.Empty, "失败必须带中文原因（给玩家看）");
            Assert.That(outcome.ConsumedCount, Is.Zero);
            Assert.That(loot.Items[Potion], Is.EqualTo(1), "效果没生效就不扣道具");
        }

        [Test]
        public void TryUse_WhenTheTargetIsDown_RejectsWithItsOwnReason()
        {
            loot.GrantItems(Potion, 1);
            target.Health = 0;

            ItemUseOutcome outcome = service.TryUse(Potion, target);

            Assert.That(outcome.Status, Is.EqualTo(ItemUseStatus.EffectRejected));
            Assert.That(outcome.Effect.Reason, Is.EqualTo("你已经倒下了"));
        }

        // 玩家生命口没接（独立测试作用域会走到这一支）：明确报「治疗口还没接上」，不是静默什么都不做，也不扣道具。
        // 这一支会经 HealPlayerEffect 打一条 Error（设计如此：不许静默失败），先声明它，否则 UTF 把未声明的 Error 判失败。
        [Test]
        public void TryUse_WhenTheEffectTargetIsMissing_ReportsBadTargetAndDoesNotConsume()
        {
            LogAssert.Expect(LogType.Error, new Regex("玩家生命口.*没接进来"));
            loot.GrantItems(Potion, 1);

            ItemUseOutcome outcome = service.TryUse(Potion, null);

            Assert.That(outcome.Status, Is.EqualTo(ItemUseStatus.EffectRejected));
            Assert.That(outcome.Effect.Status, Is.EqualTo(ItemEffectStatus.BadTarget));
            Assert.That(outcome.Effect.Reason, Is.Not.Empty);
            Assert.That(loot.Items[Potion], Is.EqualTo(1));
        }

        [Test]
        public void TryUse_WithNonPositiveItemId_FailsWithoutTouchingTheBackpack()
        {
            loot.GrantItems(Potion, 1);

            Assert.That(service.TryUse(0, target).Status, Is.EqualTo(ItemUseStatus.UnknownItem));
            Assert.That(service.TryUse(-1, target).Status, Is.EqualTo(ItemUseStatus.UnknownItem));
            Assert.That(loot.Items[Potion], Is.EqualTo(1));
            Assert.That(target.Health, Is.EqualTo(1));
        }

        // 界面用的两个只读查询：可不可用、有没有执行者。
        [Test]
        public void CanUseAndHasExecutor_FollowTheUseTableAndTheRegistry()
        {
            Assert.That(service.CanUse(Potion), Is.True);
            Assert.That(service.HasExecutor(Potion), Is.True);
            Assert.That(service.CanUse(IronSword), Is.True, "在表里（所以「使用」按钮能点）");
            Assert.That(service.HasExecutor(IronSword), Is.False, "但它的效果还没接执行者（按钮文案要说清楚）");
            Assert.That(service.CanUse(UnknownItem), Is.False);
            Assert.That(service.EffectIdOf(Potion), Is.EqualTo(HealPlayerEffect.PlaceholderEffectId));
            Assert.That(service.EffectIdOf(UnknownItem), Is.Empty);
        }

        /// <summary>可写的假目标口：血可以随意设，TryHeal 按上限封顶。</summary>
        private sealed class FakeEffectTarget : IItemEffectTarget
        {
            public FakeEffectTarget(int health, int maxHealth)
            {
                Health = health;
                MaxHealth = maxHealth;
            }

            public int Health { get; set; }

            public int MaxHealth { get; }

            public int CurrentHealth => Health;

            public int TryHeal(int amount)
            {
                if (amount <= 0 || Health <= 0 || Health >= MaxHealth) return 0;
                int before = Health;
                Health = Math.Min(MaxHealth, Health + amount);
                return Health - before;
            }
        }

        private sealed class FixedConfigService : IConfigService
        {
            public FixedConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => 0;
        }

        private sealed class NoNotifications : INotificationService
        {
            public void Show(string title, string body = null, float seconds = 0f)
            {
            }

            public void ShowCornerHint(string text, float seconds = 0f)
            {
            }
        }

        private sealed class Recorder<T> : IPublisher<T>
        {
            public void Publish(T message)
            {
            }
        }

        private sealed class NoopSubscriber<T> : ISubscriber<T>
        {
            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
                => new NoopDisposable();

            private sealed class NoopDisposable : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }

        private sealed class MemorySaves : ISaveService
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

            public UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new()
                => throw new NotSupportedException();

            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default)
                where T : class, new() => throw new NotSupportedException();

            public UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class
                => throw new NotSupportedException();

            public bool Exists(int slot) => false;
            public void Delete(int slot) => throw new NotSupportedException();
        }
    }
}
