// 职责：钉住 MirrorService 的门面接线——照人不记录、照妖线索缺记「见过轮廓」、线索齐记「已照见」且首次才请求保存、
//   自照恒空白并计数、剧情裂痕只缩范围、分区不缓存（替换分区后读到新值）、存档往返后仍为已照见（PRD V1–V4 V9 服务侧）。
// 为什么新建：MirrorRulesTests 只测纯规则，不经过门面的存档 / 背包 / 配置表 / 事件接线；一个被测类一个测试类。
//   依赖用真实生成的配置表（含 tbyao）+ 真实 JsonSaveService（Capture / Commit 走 JSON 克隆，即存档往返，不落盘）
//   + 真实 LootService / QuestService（同 LootServiceTests），其余是最小假实现。候选经 CastAt 直接给，不依赖场景。
using System;
using System.Collections.Generic;
using System.Threading;
using Game.Core.Config;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Loot;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Quest;
using Game.Session;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorService"/> 的 EditMode 测试。不经容器。玩家在原点、朝 +X、生命满（无击中裂痕）。</summary>
    public sealed class MirrorServiceTests
    {
        private const int NightWatchman = 1; // 妖 1：无线索要求
        private const int WellWoman = 2;     // 妖 2：需要 1005
        private const int OldLetter = 1005;

        private JsonSaveService saves;
        private QuestService quest;
        private LootService loot;
        private PlayerModel player;
        private MirrorConfig config;
        private MirrorService service;
        private FakePublisher<MirrorCastEvent> casts;
        private readonly List<string> saveRequests = new List<string>();
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            global::cfg.Tables tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            var configService = new FakeConfigService(tables);
            saves = NewSaveService();
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

            LootConfig lootConfig = ScriptableObject.CreateInstance<LootConfig>();
            created.Add(lootConfig);
            loot = new LootService(lootConfig, saves, configService, quest, new NoopNotificationService(),
                new FakePublisher<CrateCollectedEvent>(), new FakePublisher<LootResetEvent>(), NullTelemetryScope.Instance);

            PlayerConfig playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            created.Add(playerConfig);
            player = new PlayerModel();
            new PlayerRules(playerConfig, player, NullTelemetryScope.Instance).Reset(Vector2.zero);

            config = ScriptableObject.CreateInstance<MirrorConfig>();
            created.Add(config);
            casts = new FakePublisher<MirrorCastEvent>();
            saveRequests.Clear();
            var binder = new MirrorSceneBinder(new MonsterModel(), NullTelemetryScope.Instance);
            service = new MirrorService(config, saves, configService, loot, player, playerConfig, binder, casts,
                reason => saveRequests.Add(reason), NullTelemetryScope.Instance);
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            service.Dispose();
            loot.Dispose();
            quest.Dispose();
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void CastAt_Human_ReturnsHumanRecordsNothingAndPublishes()
        {
            MirrorResult result = service.CastAt(Ahead(MirrorSubjectKind.Human, 0));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Human));
            Assert.That(saves.Get<MirrorSaveData>().Identified, Is.Empty);
            Assert.That(saves.Get<MirrorSaveData>().GlimpsedBlurry, Is.Empty);
            Assert.That(saveRequests, Is.Empty);
            Assert.That(casts.Received.Count, Is.EqualTo(1));
            Assert.That(casts.Received[0].Result.Kind, Is.EqualTo(MirrorResultKind.Human));
            Assert.That(casts.Received[0].Subject == null, Is.True, "CastAt 不经场景登记，事件里没有标记");
            Assert.That(casts.Received[0].Range, Is.EqualTo(config.BaseRange).Within(1e-4f), "生命满时无裂痕，作用距离为基础值");
            Assert.That(service.LastResult.Kind, Is.EqualTo(MirrorResultKind.Human));
        }

        [Test]
        public void LastResult_StartsNothing_FollowsEachCastAndLookSelf_ResetOnDispose()
        {
            Assert.That(service.LastResult.Kind, Is.EqualTo(MirrorResultKind.Nothing), "还没照过");
            Assert.That(service.LastResult.HasTarget, Is.False);

            MirrorResult blurry = service.CastAt(Ahead(MirrorSubjectKind.Yao, WellWoman));
            Assert.That(service.LastResult.Kind, Is.EqualTo(MirrorResultKind.Blurry));
            Assert.That(service.LastResult.YaoId, Is.EqualTo(WellWoman));
            Assert.That(service.LastResult.CandidateIndex, Is.EqualTo(blurry.CandidateIndex));
            Assert.That(service.LastResult.Distance, Is.EqualTo(blurry.Distance).Within(1e-5f));

            service.LookSelf();
            Assert.That(service.LastResult.Kind, Is.EqualTo(MirrorResultKind.Self), "自照也记下");
            Assert.That(service.LastResult.HasTarget, Is.False);

            var far = new[] { new MirrorCandidate(new Vector2(config.BaseRange + 1f, 0f), MirrorSubjectKind.Human, 0) };
            service.CastAt(far);
            Assert.That(service.LastResult.Kind, Is.EqualTo(MirrorResultKind.Nothing), "照不到覆盖上一次的结果");

            service.CastAt(Ahead(MirrorSubjectKind.Yao, NightWatchman));
            service.Dispose();
            Assert.That(service.LastResult.Kind, Is.EqualTo(MirrorResultKind.Nothing), "释放后清回初始值");
        }

        [Test]
        public void CastAt_YaoWithClueMissing_RecordsGlimpseOnlyAndNoSave()
        {
            MirrorResult result = service.CastAt(Ahead(MirrorSubjectKind.Yao, WellWoman));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Blurry));
            Assert.That(service.HasGlimpsed(WellWoman), Is.True);
            Assert.That(service.IsIdentified(WellWoman), Is.False);
            Assert.That(saveRequests, Is.Empty);
        }

        [Test]
        public void CastAt_YaoAfterClueCollected_IdentifiesAndRequestsSaveOnlyFirstTime()
        {
            service.CastAt(Ahead(MirrorSubjectKind.Yao, WellWoman));
            saves.Get<LootSaveData>().Items[OldLetter] = 1;

            MirrorResult first = service.CastAt(Ahead(MirrorSubjectKind.Yao, WellWoman));
            MirrorResult second = service.CastAt(Ahead(MirrorSubjectKind.Yao, WellWoman));

            Assert.That(first.Kind, Is.EqualTo(MirrorResultKind.TrueForm));
            Assert.That(second.Kind, Is.EqualTo(MirrorResultKind.TrueForm), "已照见的妖照常给真形（PRD Q7）");
            Assert.That(service.IsIdentified(WellWoman), Is.True);
            Assert.That(saveRequests, Is.EqualTo(new[] { "mirror_identified" }), "只有首次照见请求保存");
            Assert.That(casts.Received.Count, Is.EqualTo(3));
        }

        [Test]
        public void CastAt_YaoWithoutClueRequirement_IdentifiedImmediately()
        {
            MirrorResult result = service.CastAt(Ahead(MirrorSubjectKind.Yao, NightWatchman));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.TrueForm));
            Assert.That(service.IsIdentified(NightWatchman), Is.True);
        }

        [Test]
        public void CastAt_UnknownYaoId_BlurryNotThrow()
        {
            MirrorResult result = service.CastAt(Ahead(MirrorSubjectKind.Yao, 987654));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Blurry));
            Assert.That(service.TryGetYao(987654, out _), Is.False);
        }

        [Test]
        public void CastAt_BeyondRange_Nothing()
        {
            var far = new[] { new MirrorCandidate(new Vector2(config.BaseRange + 1f, 0f), MirrorSubjectKind.Human, 0) };

            Assert.That(service.CastAt(far).Kind, Is.EqualTo(MirrorResultKind.Nothing));
        }

        [Test]
        public void LookSelf_AlwaysBlank_CountsAndPublishes()
        {
            MirrorResult result = service.LookSelf();

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Self));
            Assert.That(saves.Get<MirrorSaveData>().SelfLooks, Is.EqualTo(1));
            Assert.That(casts.Received.Count, Is.EqualTo(1));
            Assert.That(casts.Received[0].Result.Kind, Is.EqualTo(MirrorResultKind.Self));
        }

        [Test]
        public void AddStoryCrack_ShrinksRangeAndVisionButNotHitCracks()
        {
            float rangeBefore = service.EffectiveRange;
            float visionBefore = service.VisionRadius;

            service.AddStoryCrack();

            Assert.That(service.StoryCracks, Is.EqualTo(1));
            Assert.That(service.HitCracks, Is.EqualTo(0), "剧情裂痕不计入击中裂痕");
            Assert.That(service.EffectiveRange, Is.LessThan(rangeBefore));
            Assert.That(service.VisionRadius, Is.LessThan(visionBefore));
            Assert.That(MirrorCrackRules.IsShattered(service.HitCracks), Is.False);
        }

        [Test]
        public void TryGetYao_ReturnsTableRow()
        {
            Assert.That(service.TryGetYao(WellWoman, out global::cfg.yao.Yao yao), Is.True);
            Assert.That(yao.ClueItems, Is.EqualTo(new[] { OldLetter }));
        }

        [Test]
        public void Partition_NotCached_ReadsReplacedInstance()
        {
            service.CastAt(Ahead(MirrorSubjectKind.Yao, NightWatchman));
            SaveSnapshot snapshot = saves.Capture();

            saves.ResetAll();
            Assert.That(service.IsIdentified(NightWatchman), Is.False, "新游戏整体替换分区后读到默认值");

            saves.Commit(snapshot);
            Assert.That(service.IsIdentified(NightWatchman), Is.True, "读档 Commit 后读到新分区");
        }

        [Test]
        public void SaveRoundTrip_IdentifiedAndStoryCracksSurvive()
        {
            service.CastAt(Ahead(MirrorSubjectKind.Yao, NightWatchman));
            service.CastAt(Ahead(MirrorSubjectKind.Yao, WellWoman));
            service.AddStoryCrack();

            // Capture 把每个分区经 JSON 克隆一份，Commit 进一个全新的存档服务 = 一次序列化往返，不落盘。
            JsonSaveService reloaded = NewSaveService();
            reloaded.Commit(saves.Capture());
            MirrorSaveData restored = reloaded.Get<MirrorSaveData>();

            Assert.That(restored.Identified, Is.EqualTo(new[] { NightWatchman }));
            Assert.That(restored.GlimpsedBlurry, Is.EqualTo(new[] { WellWoman }));
            Assert.That(restored.StoryCracks, Is.EqualTo(1));
        }

        private static MirrorCandidate[] Ahead(MirrorSubjectKind kind, int yaoId) =>
            new[] { new MirrorCandidate(new Vector2(2f, 0f), kind, yaoId) };

        // SaveRoot 只在 SaveAsync / LoadAsync 用到，本文件不落盘，给个不存在的临时路径即可。
        private static JsonSaveService NewSaveService() =>
            new JsonSaveService(new FakePlatformService(System.IO.Path.Combine(Application.temporaryCachePath,
                "mirror-service-tests-unused")), null, null);

        /// <summary>只记录收到的消息。</summary>
        private sealed class FakePublisher<T> : IPublisher<T>
        {
            public List<T> Received { get; } = new List<T>();
            public void Publish(T message) => Received.Add(message);
        }

        /// <summary>不接任何消息的假订阅者：构造 QuestService 要这个参数。</summary>
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

        /// <summary>吞掉通知：本文件不开箱。</summary>
        private sealed class NoopNotificationService : INotificationService
        {
            public void Show(string title, string body = null, float seconds = 0f)
            {
            }

            public void ShowCornerHint(string text, float seconds = 0f)
            {
            }
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>。</summary>
        private sealed class FakeConfigService : IConfigService
        {
            public FakeConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>只提供 SaveRoot 的假平台服务。</summary>
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
    }
}
