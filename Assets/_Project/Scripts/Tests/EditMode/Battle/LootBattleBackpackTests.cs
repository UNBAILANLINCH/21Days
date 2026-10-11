// 职责：钉住真实背包口 LootBattleBackpack——数量读 LootService、类别与名字查真实 tbitem（1004 治疗药水是消耗品、1002 精钢长剑不是）、
//   扣 1 写进 Loot 存档分区；再经 BattleItemInventory 走一遍「只列消耗品 + string↔int」。
// 为什么新建：BattleItemInventoryTests 用的是假背包，「真实物品表里谁是消耗品」只有接上 Loot 与生成表才测得到；
//   依赖照 LootServiceTests 的做法：真实生成表 + 真实 QuestService，其余最小假实现。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.Core.Config;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Loot;
using Game.Quest;
using Game.Session;
using Game.TurnBased;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Battle
{
    public sealed class LootBattleBackpackTests
    {
        private const int Potion = 1004;
        private const int Sword = 1002;

        private MemorySaves saves;
        private LootConfig config;
        private LootService loot;
        private LootBattleBackpack backpack;

        [SetUp]
        public void SetUp()
        {
            var configService = new TableConfig(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            saves = new MemorySaves();
            var quest = new QuestService(new QuestCatalog(configService, NullTelemetryScope.Instance), saves,
                new Publisher<QuestActivatedEvent>(), new Publisher<QuestObjectiveProgressedEvent>(),
                new Publisher<QuestCompletedEvent>(), new Publisher<QuestTrackingChangedEvent>(),
                new BattleFakes.Bus<SessionStartedEvent>(), NullTelemetryScope.Instance);
            quest.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            config = ScriptableObject.CreateInstance<LootConfig>();
            loot = new LootService(config, saves, configService, quest, new Notifications(), new Publisher<CrateCollectedEvent>(),
                new Publisher<MonsterDroppedEvent>(), new Publisher<LootResetEvent>(), NullTelemetryScope.Instance);
            loot.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            backpack = new LootBattleBackpack(loot, configService);
        }

        [TearDown]
        public void TearDown()
        {
            if (config != null) UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void IsConsumable_FollowsRealItemTable()
        {
            Assert.That(backpack.IsConsumable(Potion), Is.True, "1004 治疗药水 = Consumable");
            Assert.That(backpack.IsConsumable(Sword), Is.False, "负对照：1002 精钢长剑不是消耗品");
            Assert.That(backpack.IsConsumable(987654), Is.False, "负对照：表里没有的 id");
            Assert.That(backpack.NameOf(987654), Is.EqualTo("#987654"));
            Assert.That(backpack.NameOf(Potion), Is.Not.EqualTo("#1004"), "名字来自物品表");
        }

        [Test]
        public void TryConsumeOne_WritesLootPartition()
        {
            saves.Get<LootSaveData>().Items[Potion] = 2;

            Assert.That(backpack.TryConsumeOne(Potion), Is.True);
            Assert.That(saves.Get<LootSaveData>().Items[Potion], Is.EqualTo(1));
            Assert.That(backpack.TryConsumeOne(Sword), Is.False, "负对照：没有就扣不动");
        }

        [Test]
        public void Inventory_OverRealBackpack_ListsOnlyConsumables()
        {
            LootSaveData data = saves.Get<LootSaveData>();
            data.Items[Potion] = 2;
            data.Items[Sword] = 1;
            var inventory = new BattleItemInventory(backpack);
            var slots = new List<BattleItemSlot>();

            inventory.ListSlots(new BattleItemLedger(true), slots);

            Assert.That(slots.Count, Is.EqualTo(1));
            Assert.That(slots[0].ItemId, Is.EqualTo("1004"));
            Assert.That(inventory.Owns("1004"), Is.True);
            Assert.That(inventory.Owns("1002"), Is.False, "剑在背包里，但不是战斗道具");
        }

        private sealed class TableConfig : IConfigService
        {
            public TableConfig(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => 0;
        }

        private sealed class Publisher<T> : IPublisher<T>
        {
            public void Publish(T message) { }
        }

        private sealed class Notifications : INotificationService
        {
            public void Show(string title, string body = null, float seconds = 0f) { }
            public void ShowCornerHint(string text, float seconds = 0f) { }
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
            public UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new() => throw new NotSupportedException();
            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default) where T : class, new() => throw new NotSupportedException();
            public UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class => throw new NotSupportedException();
            public bool Exists(int slot) => false;
            public void Delete(int slot) => throw new NotSupportedException();
        }
    }
}
