// 职责：钉住「内核真的装进了正式流程」这一步——按 Boot 的接法把真实的 MonsterInstaller / IdentityInstaller /
//   StealthInstaller 装进一个容器，断言构建回调确实把身份与潜行内核接进了 EncounterStep：
//   ① `EncounterStep.Sight` 就是内核那只（UseStealth 生效，占位默认值那条路断了）；
//   ② 身份生效中敌人不攻击、身份失效后恢复攻击（S1 的验收点，走的是容器装出来的那条链）。
// 为什么新建：`EncounterStealthTests` / `MonsterAttackPermissionTests` 都是手工 new 出 EncounterStep 再手工 Bind，
//   而本波新增的两行装配（MonsterInstaller 的构建回调里 TryResolve + BindIdentity / UseStealth）
//   此前一条测试都没有。「手工接上了」与「容器装上了」是两件事——这正是这一波要防的失效。
// **故意把 MonsterInstaller 第一个 Install**：GameLifetimeScope 按 GetComponents 的组件顺序调注册器，
//   而两侧绑定写在各自的构建回调里，这里用最坏顺序证明它与次序无关、也没有循环依赖。
// 容器只补框架侧最小依赖：IClock / IInputSource 用假实现，配置表用真 .bytes（同 MonsterKindCatalogTests），
//   埋点用真 TelemetryService + 假时钟 + 收集型 sink（同 GameFlowTests）。
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Identity;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Stealth;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterKernelWiringTests
    {
        private const string StealthConfigPath = "Assets/_Project/Data/Stealth/StealthConfig.asset";

        /// <summary>身份 id 与 `IdentityTestContent` 里的「都知」一致（01 R15 的能力表里通行权限最全的那个）。</summary>
        private const string StewardId = "duzhi";

        private GameObject host;
        private IObjectResolver container;
        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private IdentityConfig identityConfig;
        private SimulationConfig simulationConfig;
        private StealthConfig stealthConfig;

        [TearDown]
        public void TearDown()
        {
            container?.Dispose();
            container = null;
            if (host != null)
            {
                Object.DestroyImmediate(host);
            }

            host = null;
            Destroy(identityConfig);
            Destroy(simulationConfig);
            Destroy(playerConfig);
            Destroy(monsterConfig);
            identityConfig = null;
            simulationConfig = null;
            playerConfig = null;
            monsterConfig = null;
            stealthConfig = null;
        }

        /// <summary>装配正向：容器里取得到身份侧与潜行侧的服务，且潜行内核绑的是资产那份配置。</summary>
        [Test]
        public void ContainerBuiltFromInstallers_ResolvesIdentityAndStealthServices()
        {
            Build();

            Assert.That(container.Resolve<IdentityState>(), Is.Not.Null);
            Assert.That(container.Resolve<IdentityLedger>(), Is.Not.Null);
            Assert.That(container.Resolve<IdentityRules>(), Is.Not.Null);
            StealthKernel kernel = container.Resolve<StealthKernel>();
            Assert.That(kernel, Is.Not.Null);
            Assert.That(kernel.Config, Is.SameAs(stealthConfig), "内核要用 Boot 上拖的那份配置装配");
        }

        /// <summary>`UseStealth` 生效：遭遇结算用的就是内核那只 Sight，遮挡体喂进去会真的影响判定。</summary>
        [Test]
        public void UseStealth_IsApplied_EncounterStepSeesThroughTheKernelSight()
        {
            Build();
            StealthKernel kernel = container.Resolve<StealthKernel>();
            EncounterStep step = container.Resolve<EncounterStep>();

            Assert.That(step.Sight, Is.SameAs(kernel.Sight),
                "两者不是同一个对象，说明 UseStealth 没被调到——遭遇跑的仍是占位默认值，策划改 StealthConfig 不生效");

            // 正式流程由场景就绪时喂遮挡体（MonsterEncounterState），这里直接喂内核那一只。
            kernel.Sight.SetOccluders(new[]
            {
                StealthOccluder.MakeRectangle(new Vector2(1.5f, 0f), new Vector2(0.5f, 2f), 1),
            });
            step.Begin(Vector2.zero, new[] { new Vector2(3f, 0f), Vector2.zero });

            Tick(step, 0f);

            Assert.That(step.Facts.IsTrue(StealthFactKeys.Hidden), Is.True, "柱子在中间：这一 tick 看不见玩家");
        }

        /// <summary>负对照：不喂遮挡体时同一份装配照样看得见（Sight 是同一个对象，差的是那一份清单）。</summary>
        [Test]
        public void WithoutOccluders_SameAssembly_PlayerIsSeen()
        {
            Build();
            EncounterStep step = container.Resolve<EncounterStep>();
            container.Resolve<StealthKernel>().Sight.Clear();
            step.Begin(Vector2.zero, new[] { new Vector2(3f, 0f), Vector2.zero });

            Tick(step, 0f);

            Assert.That(step.Facts.IsTrue(StealthFactKeys.Hidden), Is.False);
        }

        /// <summary>
        /// `BindIdentity` 生效（S1 验收点）：身份生效中敌人不攻击，身份失效后恢复攻击。
        /// 借身份走的是容器里那套规则与表，不是测试自己 new 的——「容器装出来的链」正是这条用例要守的东西。
        /// </summary>
        [Test]
        public void BindIdentity_IsApplied_IdentityInEffectStopsTheEnemyFromAttacking()
        {
            Build();
            IdentityState state = container.Resolve<IdentityState>();
            IdentityLedger ledger = container.Resolve<IdentityLedger>();
            IdentityRules rules = container.Resolve<IdentityRules>();
            EncounterStep step = container.Resolve<EncounterStep>();
            PlayerModel player = container.Resolve<PlayerModel>();
            var random = new RandomService(11ul);

            Assert.That(rules.Catalog.TryGet(IdentityId.From(StewardId), out _), Is.True,
                "配置里那条身份没进表，这条用例就测不到东西了");
            Assert.That(rules.TryEnter(state, ledger, IdentityId.From(StewardId)),
                Is.EqualTo(IdentityEnterResult.Entered));
            Assert.That(state.IsInEffect, Is.True, "占位时限为 0 = 无时限，借到即生效");

            // 玩家摆在怪物红区与攻击距离之内（同 MonsterAttackPermissionTests 的夹具）。
            step.Begin(Vector2.zero, new[] { new Vector2(0.5f, 0f), new Vector2(10f, 0f) });
            Tick(step, 0.1f, random);
            Assert.That(player.Health, Is.EqualTo(playerConfig.MaxHealth), "身份生效中：敌人不攻击");

            rules.TryExit(state);
            Assert.That(state.IsInEffect, Is.False);
            Tick(step, 0.1f, random);
            Assert.That(player.Health, Is.LessThan(playerConfig.MaxHealth), "身份失效后：恢复攻击");
        }

        // ──────────────────────────── 夹具 ────────────────────────────

        /// <summary>按 Boot 的接法把三个注册器挂到同一个物体上再装容器；MonsterInstaller 故意排在最前。</summary>
        private void Build()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            identityConfig = NewIdentityConfigWithOneDefinition();
            simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            stealthConfig = AssetDatabase.LoadAssetAtPath<StealthConfig>(StealthConfigPath);
            Assert.That(stealthConfig, Is.Not.Null, StealthConfigPath + " 读不到");

            host = new GameObject("GameBootstrap(测试)");
            MonsterInstaller monster = host.AddComponent<MonsterInstaller>();
            IdentityInstaller identity = host.AddComponent<IdentityInstaller>();
            StealthInstaller stealth = host.AddComponent<StealthInstaller>();
            AttachConfig(monster, monsterConfig);
            AttachConfig(identity, identityConfig);
            AttachConfig(stealth, stealthConfig);

            var builder = new ContainerBuilder();
            var configService = new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            builder.RegisterInstance(configService).As<IConfigService>();
            builder.RegisterInstance(new YaoCatalog(configService));
            builder.RegisterInstance(new RandomService(11ul)).As<IRandomService>();
            builder.RegisterInstance(TelemetryOptions.Default);
            builder.RegisterInstance(new FakeTelemetryClock()).As<ITelemetryClock>();
            builder.RegisterInstance(new ITelemetrySink[] { new RecordingTelemetrySink() });
            builder.Register<TelemetryService>(Lifetime.Singleton).As<ITelemetryService>();
            builder.RegisterInstance(playerConfig);
            builder.RegisterInstance(new PlayerModel());
            builder.Register(resolver => new PlayerRules(
                    resolver.Resolve<PlayerConfig>(), resolver.Resolve<PlayerModel>(), NullTelemetryScope.Instance),
                Lifetime.Singleton);
            builder.RegisterInstance(simulationConfig);
            builder.RegisterInstance(new LocalClock()).As<IClock>();
            builder.RegisterInstance(new SilentInputSource()).As<IInputSource>();
            builder.Register<SimulationRunner>(Lifetime.Singleton);
            builder.Register<ReplayStateRegistry>(Lifetime.Singleton).As<IReplayStateProvider>();

            monster.Install(builder);
            identity.Install(builder);
            stealth.Install(builder);
            container = builder.Build();
        }

        /// <summary>
        /// 造一份「有一个可借身份」的配置。真资产的 definitions 还是空的（身份内容待策划拍板），
        /// 而 S1 的验收点要求身份真的能生效——所以在这里塞一条进去，不为了测试去改生产资产。
        /// </summary>
        private static IdentityConfig NewIdentityConfigWithOneDefinition()
        {
            var config = ScriptableObject.CreateInstance<IdentityConfig>();
            var serialized = new SerializedObject(config);
            SerializedProperty definitions = serialized.FindProperty("definitions");
            definitions.arraySize = 1;
            SerializedProperty entry = definitions.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("id").stringValue = StewardId;
            entry.FindPropertyRelative("displayName").stringValue = "都知";
            entry.FindPropertyRelative("sourceCharacterId").stringValue = "duzhi_npc";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        /// <summary>按 Boot 的接法给注册器赋配置：私有序列化字段用 SerializedObject 写（不给生产代码开 setter）。</summary>
        private static void AttachConfig(GameplayInstaller installer, Object config)
        {
            var serialized = new SerializedObject(installer);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Tick(EncounterStep step, float deltaTime, RandomService random = null)
        {
            RandomService source = random ?? new RandomService(11ul);
            var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
            var context = new SimulationContext(0, deltaTime, in command, source);
            step.Step(in context);
        }

        private static void Destroy(Object target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 MonsterKindCatalogTests 的最小假实现）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new System.NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>不采样任何输入的输入源：本组用例直接调 EncounterStep.Step，不经过推进器。</summary>
        private sealed class SilentInputSource : IInputSource
        {
            public void Sample(long tick)
            {
            }

            public InputCommand Current => default;
        }
    }
}
