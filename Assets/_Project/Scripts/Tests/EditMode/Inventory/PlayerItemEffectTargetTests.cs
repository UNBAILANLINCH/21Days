// 职责：钉住「道具效果 → 玩家生命」这条适配链——PlayerItemEffectTarget 的转发、PlayerRules.Heal 的三条边界
//   （封顶 / 满血 / 已倒下）、以及从 ItemUseService 走一遍真闭环（用掉一件 → 扣件数 → 回血）。
// 为什么新建：一个被测类一个测试类；这条链跨了 Game.Inventory（效果口）与 Game.Player（生命规则）两侧，
//   是 2026-10-07 主窗口裁决「批准 PlayerRules.Heal + Inventory 侧薄适配器」的验收点。
//   这里用**真的** PlayerRules / PlayerConfig / PlayerModel（不是假目标口），所以它同时钉住了
//   「适配器没有绕过 PlayerRules」这件事：改生命仍然经过上限与状态判断。
// 造受伤现场：优先走公开口 rules.ApplyDamage（与玩法同一条路）；要造「已倒下」（Health = 0）时公开口造不出来
//   （ApplyDamage 在 Health == 0 时不再结算），经反射写 PlayerModel.Health —— Game.Runtime **没有**对测试程序集开
//   InternalsVisibleTo（多处源码注释明写这条工程约定），所以 `model.Health = x` 会 CS0272。
//   反射写法同 DialogueInteractableTests / MirrorSceneBinderTests。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Config;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Inventory;
using Game.Loot;
using Game.Player;
using Game.Quest;
using Game.Session;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Inventory
{
    /// <summary><see cref="PlayerItemEffectTarget"/> 与 <see cref="PlayerRules.Heal"/> 的 EditMode 测试。</summary>
    public sealed class PlayerItemEffectTargetTests
    {
        private const int Potion = 1004;

        private readonly List<Object> created = new List<Object>();
        private PlayerConfig playerConfig;
        private PlayerModel model;
        private PlayerRules rules;
        private PlayerItemEffectTarget target;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            created.Add(playerConfig);
            model = new PlayerModel();
            rules = new PlayerRules(playerConfig, model, NullTelemetryScope.Instance);
            // Reset 把生命顶到 MaxHealth（占位配置 3 点）；回血测试先把它压下去。
            rules.Reset(Vector2.zero);
            target = new PlayerItemEffectTarget(rules);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
        }

        // 1. 真的回血：生命增加，返回值等于实际回复量。
        [Test]
        public void TryHeal_WhenHurt_HealsAndReportsTheAmount()
        {
            SetHealth(1);

            int healed = target.TryHeal(2);

            Assert.That(healed, Is.EqualTo(2));
            Assert.That(target.CurrentHealth, Is.EqualTo(3));
            Assert.That(model.Health, Is.EqualTo(3), "改的是 PlayerRules 手里那份模型");
        }

        // 2. 封顶：接近满血时只回到 MaxHealth，返回值不是 amount。
        [Test]
        public void TryHeal_WhenAmountOvershoots_CapsAtMaxHealth()
        {
            SetHealth(2);

            int healed = target.TryHeal(99);

            Assert.That(healed, Is.EqualTo(1), "只差 1 点就满：返回值是实际回复量，不是 99");
            Assert.That(model.Health, Is.EqualTo(playerConfig.MaxHealth));
        }

        // 3. 负对照：满血时返回 0 且生命不变。
        [Test]
        public void TryHeal_WhenAlreadyFull_ReturnsZeroAndChangesNothing()
        {
            Assert.That(model.Health, Is.EqualTo(playerConfig.MaxHealth), "前置：Reset 后满血");

            Assert.That(target.TryHeal(3), Is.Zero);
            Assert.That(model.Health, Is.EqualTo(playerConfig.MaxHealth));
        }

        // 4. 负对照：已倒下时返回 0 且生命不变（回血不该把结局抹掉，重开走 Reset）。
        [Test]
        public void TryHeal_WhenDown_ReturnsZeroAndChangesNothing()
        {
            SetHealth(0);

            Assert.That(target.TryHeal(3), Is.Zero);
            Assert.That(model.Health, Is.Zero);
        }

        // 5. 负对照：amount <= 0 时返回 0 且生命不变（与 ApplyDamage 抛异常的口径不同，理由见 Heal 的注释）。
        [Test]
        public void TryHeal_WithNonPositiveAmount_ReturnsZeroAndChangesNothing()
        {
            SetHealth(1);

            Assert.That(target.TryHeal(0), Is.Zero);
            Assert.That(target.TryHeal(-5), Is.Zero);
            Assert.That(model.Health, Is.EqualTo(1));
        }

        [Test]
        public void CurrentHealth_ForwardsTheModelValue()
        {
            SetHealth(2);

            Assert.That(target.CurrentHealth, Is.EqualTo(2));
        }

        [Test]
        public void Constructor_WithNullRules_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PlayerItemEffectTarget(null));
        }

        // 6. 端到端：从 ItemUseService 走一遍真闭环——用一件 1004 → 扣件数 → 经 PlayerItemEffectTarget 回血。
        [Test]
        public void ItemUseService_WithTheRealPlayerTarget_ConsumesOneAndHeals()
        {
            var saves = new MemorySaves();
            var configService = new FixedConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            QuestService quest = NewQuest(configService, saves);
            LootConfig lootConfig = ScriptableObject.CreateInstance<LootConfig>();
            created.Add(lootConfig);
            var loot = new LootService(lootConfig, saves, configService, quest, new NoNotifications(),
                new LootEventPublishers(new Recorder<CrateCollectedEvent>(), new Recorder<MonsterDroppedEvent>(),
                    new Recorder<LootResetEvent>(), new Recorder<BackpackChangedEvent>()),
                NullTelemetryScope.Instance);
            loot.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

            var effects = new ItemEffectRegistry();
            effects.Register(new HealPlayerEffect(amount: 2));
            var useService = new ItemUseService(
                new[] { new ItemUseSpec(Potion, HealPlayerEffect.PlaceholderEffectId) },
                effects, loot, NullTelemetryScope.Instance);
            loot.GrantItems(Potion, 2);
            SetHealth(1);
            try
            {
                ItemUseOutcome outcome = useService.TryUse(Potion, target);

                Assert.That(outcome.IsOk, Is.True, "产线通了：判定 → 执行 → 扣件数三段都过");
                Assert.That(outcome.Effect.Status, Is.EqualTo(ItemEffectStatus.Applied));
                Assert.That(outcome.Effect.Amount, Is.EqualTo(2), "血从 1 回到上限 3，实际回 2");
                Assert.That(model.Health, Is.EqualTo(3), "玩家真的回血了（不是只在假目标口上通）");
                Assert.That(outcome.ConsumedCount, Is.EqualTo(1));
                Assert.That(loot.Items[Potion], Is.EqualTo(1), "背包数量减一");
            }
            finally
            {
                quest.Dispose();
            }
        }

        /// <summary>
        /// 造受伤 / 倒地现场。Game.Runtime 没有对测试程序集开 InternalsVisibleTo，`model.Health = x` 会 CS0272
        /// （PlayerModel.Health 是 internal set），所以经反射写；这是本工程测试的既有做法
        /// （DialogueInteractableTests / MirrorSceneBinderTests 都这么写）。
        /// </summary>
        private void SetHealth(int value)
        {
            PropertyInfo property = typeof(PlayerModel).GetProperty(nameof(PlayerModel.Health));
            Assert.That(property, Is.Not.Null, "PlayerModel.Health 查不到（改名了？）");
            property.SetValue(model, value);
        }

        private static QuestService NewQuest(IConfigService configService, ISaveService saves)        {
            var quest = new QuestService(new QuestCatalog(configService, NullTelemetryScope.Instance), saves,
                new Recorder<QuestActivatedEvent>(), new Recorder<QuestObjectiveProgressedEvent>(),
                new Recorder<QuestCompletedEvent>(), new Recorder<QuestTrackingChangedEvent>(),
                new NoopSubscriber<SessionStartedEvent>(), NullTelemetryScope.Instance);
            quest.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return quest;
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
