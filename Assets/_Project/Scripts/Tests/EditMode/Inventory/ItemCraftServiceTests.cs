// 职责：钉住 ItemCraftService 这条**完整的合成通路**——找配方 → 判定原料 → 产物入包，以及每条失败分支都不改背包。
// 为什么新建：ItemCraftRulesTests 只测纯判定（它不写背包）；「合成一件之后背包变成什么样」只有串起 Loot 才测得到。
//   依赖照 LootServiceTests 的做法：真实生成的配置表 + 真实 QuestService，其余是最小假实现。
using System;
using System.Collections.Generic;
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
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Inventory
{
    /// <summary><see cref="ItemCraftService"/> 的 EditMode 测试。</summary>
    public sealed class ItemCraftServiceTests
    {
        // 与真实 tbitem 对齐的三个 id：水痕 / 湿皮 / 进入废弃官署的钥匙（05_皮面具与道具.md:106 R7）。
        private const int WaterMark = 1401;
        private const int WetSkin = 1102;
        private const int OfficeKey = 1701;

        private readonly List<Object> created = new List<Object>();
        private MemorySaves saves;
        private LootService loot;
        private QuestService quest;

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

        // 不扣原料那一档（consumeIngredients = false）：资产里现在勾的是 true（用户 2026-10-10 拍板 R8），
        // 但这条口径在服务契约里仍然成立，用显式 false 覆盖它。
        [Test]
        public void TryCraft_WithEnoughIngredients_AddsTheProductAndKeepsIngredients()
        {
            ItemCraftService service = NewService(consumeIngredients: false);
            loot.GrantItems(WaterMark, 1);
            loot.GrantItems(WetSkin, 1);

            ItemCraftResult result = service.TryCraft(OfficeKey);

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.Product.ItemId, Is.EqualTo(OfficeKey));
            Assert.That(result.Product.Count, Is.EqualTo(1));
            Assert.That(loot.Items[OfficeKey], Is.EqualTo(1), "产物进了背包");
            Assert.That(loot.Items[WaterMark], Is.EqualTo(1), "占位口径：原料不消失（05:107 R8 未定）");
            Assert.That(loot.Items[WetSkin], Is.EqualTo(1));
        }

        [Test]
        public void TryCraft_WhenConsumeIngredientsIsOn_DeductsBothIngredients()
        {
            ItemCraftService service = NewService(consumeIngredients: true);
            loot.GrantItems(WaterMark, 1);
            loot.GrantItems(WetSkin, 2);

            Assert.That(service.TryCraft(OfficeKey).IsOk, Is.True);

            Assert.That(loot.Items.ContainsKey(WaterMark), Is.False, "水痕只有 1 件，扣到 0 删项");
            Assert.That(loot.Items[WetSkin], Is.EqualTo(1), "湿皮扣 1 还剩 1");
            Assert.That(loot.Items[OfficeKey], Is.EqualTo(1));
        }

        // 负对照（回放里也钉这条）：原料不够时合成被拒，背包一件都不动。
        [Test]
        public void TryCraft_WhenAnIngredientIsMissing_IsRejectedAndLeavesTheBackpackAlone()
        {
            ItemCraftService service = NewService(consumeIngredients: true);
            loot.GrantItems(WaterMark, 1); // 只有水痕，没有湿皮

            ItemCraftResult result = service.TryCraft(OfficeKey);

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.MissingIngredient));
            Assert.That(result.MissingItemId, Is.EqualTo(WetSkin), "报出缺的是哪一件");
            Assert.That(loot.Items.ContainsKey(OfficeKey), Is.False, "没有产物");
            Assert.That(loot.Items[WaterMark], Is.EqualTo(1), "原料一件都没扣");
        }

        [Test]
        public void TryCraft_WhenTheProductIsAlreadyOwned_IsRejected()
        {
            ItemCraftService service = NewService(consumeIngredients: false);
            loot.GrantItems(WaterMark, 1);
            loot.GrantItems(WetSkin, 1);
            loot.GrantItems(OfficeKey, 1);

            ItemCraftResult result = service.TryCraft(OfficeKey);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.AlreadyOwned));
            Assert.That(loot.Items[OfficeKey], Is.EqualTo(1), "不凭空造出第二件");
        }

        [Test]
        public void TryCraft_WhenThereIsNoRecipeForThatProduct_FailsAsNoRecipe()
        {
            ItemCraftService service = NewService(consumeIngredients: false);

            ItemCraftResult result = service.TryCraft(1001);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.NoRecipe));
            Assert.That(loot.Items, Is.Empty);
        }

        [Test]
        public void TryCraft_WithNonPositiveProductId_FailsWithoutTouchingTheBackpack()
        {
            ItemCraftService service = NewService(consumeIngredients: false);

            Assert.That(service.TryCraft(0).Status, Is.EqualTo(ItemCraftStatus.NoRecipe));
            Assert.That(service.TryCraft(-1).Status, Is.EqualTo(ItemCraftStatus.NoRecipe));
            Assert.That(loot.Items, Is.Empty);
        }

        [Test]
        public void HasRecipeAndFindRecipe_FollowTheRecipeList()
        {
            ItemCraftService service = NewService(consumeIngredients: false);

            Assert.That(service.HasRecipe(OfficeKey), Is.True);
            Assert.That(service.HasRecipe(1001), Is.False);
            Assert.That(service.HasRecipe(0), Is.False);
            Assert.That(service.FindRecipe(OfficeKey).Id, Is.EqualTo("office_key"));
            Assert.That(service.FindRecipe(1001), Is.Null);
        }

        // 占位配置资产：没填任何行时回一条占位配方（空表会让「合成」按钮对每件东西都报没有配方）。
        [Test]
        public void InventoryConfig_WithNoRows_FallsBackToThePlaceholderRecipeAndUseSpec()
        {
            var config = ScriptableObject.CreateInstance<InventoryConfig>();
            created.Add(config);

            Assert.That(config.ConfiguredRecipeCount, Is.Zero);
            IReadOnlyList<ItemRecipe> recipes = config.CreateRecipes();
            Assert.That(recipes.Count, Is.EqualTo(1));
            Assert.That(recipes[0].ProductId, Is.EqualTo(OfficeKey));
            Assert.That(recipes[0].RequiredCount(WaterMark), Is.EqualTo(1));
            Assert.That(recipes[0].RequiredCount(WetSkin), Is.EqualTo(1));

            Assert.That(config.ConfiguredUseCount, Is.Zero);
            IReadOnlyList<ItemUseSpec> specs = config.CreateUseSpecs();
            Assert.That(specs.Count, Is.EqualTo(1));
            Assert.That(specs[0].ItemId, Is.EqualTo(1004), "占位治疗药水");
            Assert.That(specs[0].EffectId, Is.EqualTo(HealPlayerEffect.PlaceholderEffectId));

            Assert.That(config.ConsumeIngredients, Is.False, "占位默认不扣原料（05:107 R8 未定）");
        }

        private ItemCraftService NewService(bool consumeIngredients)
        {
            IReadOnlyList<ItemRecipe> recipes = new[] { ItemRecipeEntry.PlaceholderOfficeKey().ToRecipe() };
            return new ItemCraftService(recipes, loot, consumeIngredients, NullTelemetryScope.Instance);
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
