// 职责：钉住 BattleInstaller 的接线——按 Boot 的接法把真实的 MonsterInstaller + BattleInstaller 装进一个容器，
//   断言构建回调确实把 BattleFlow 作为「实时遭遇升级回合制」的端口交给了 EncounterStep（PRP/combat-iteration D5）；
//   负对照：没装 BattleInstaller 时遭遇没有端口（不是别处顺手接上的）；没装 MonsterInstaller 时战斗侧照常构建、不抛。
// 为什么新建：BattleInstaller 此前没有容器级用例；`BattleFlowEncounterTests` 是手工 `step.BindEscalation(flow)`，
//   验的是「这套类凑在一起能工作」，不是「产品启动起来能工作」（pitfalls.md:225）。删掉构建回调里那一行，本组第一条会红。
// 夹具：MonsterInstaller 的框架侧依赖照 `EncounterBattleBridgeTests.BuildContainer`（真表、真 TelemetryService、假时钟 / 输入源）；
//   BattleInstaller 的两个配置用代码建的默认资产（SerializedObject 写私有字段）。它注册的四个对外口（剧情口、背包口、门闸、表现）
//   在 Install 之后用 BattleFakes 覆盖——它们要真的 NarrativeService / LootService / 场景，与本组要钉的那一行装配无关
//   （VContainer 同一服务类型后注册的覆盖先注册的）。
// 2026-10-14（PRP/combat-iteration D6）：再钉 BOSS 发难的场景绑定——容器里那个 BossAggroSceneBinder 入口点 Start / 场景加载回调
//   把场上的 BossAggro 绑上、按名册武装，同物体的 BossEntryContext 在这一刻接上容器里的配置；负对照：构建完还没绑、
//   名册没开发难不武装、容器里没 MonsterConfig 不武装。
using System;
using System.Collections.Generic;
using System.Threading;
using Game.Battle;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using Game.TurnBased;
using MessagePipe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VContainer;
using StageEvent = Game.Narrative.BattleStageEnteredEvent;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleInstallerTests
    {
        private GameObject host;
        private IObjectResolver container;
        private InputService input;
        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private SimulationConfig simulationConfig;
        private TurnBasedConfig turnBasedConfig;
        private BossRosterConfig bossRosterConfig;
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            input = new InputService();
            input.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            turnBasedConfig = ScriptableObject.CreateInstance<TurnBasedConfig>();
            bossRosterConfig = ScriptableObject.CreateInstance<BossRosterConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            container?.Dispose();
            container = null;
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            host = null;
            foreach (GameObject boss in spawned)
            {
                if (boss != null) UnityEngine.Object.DestroyImmediate(boss);
            }

            spawned.Clear();
            input?.Dispose();
            Destroy(playerConfig);
            Destroy(monsterConfig);
            Destroy(simulationConfig);
            Destroy(turnBasedConfig);
            Destroy(bossRosterConfig);
        }

        /// <summary>两个注册器都在：容器里那只 EncounterStep 的升级端口就是容器里那个 BattleFlow。</summary>
        [Test]
        public void Build_WithMonsterAndBattleInstallers_BindsEncounterEscalationToBattleFlow()
        {
            Build(withMonster: true, withBattle: true);

            EncounterStep step = container.Resolve<EncounterStep>();
            BattleFlow flow = container.Resolve<BattleFlow>();

            Assert.That(step.EscalationPort, Is.Not.Null, "BattleInstaller 的构建回调必须 BindEscalation");
            Assert.That(step.EscalationPort, Is.SameAs(flow), "端口就是根作用域那个 BattleFlow，不是别的实例");
        }

        /// <summary>负对照：没装 BattleInstaller（独立原型场景 / 纯 Monster 作用域）→ 遭遇没有端口，升级不会发生。</summary>
        [Test]
        public void Build_WithoutBattleInstaller_LeavesEncounterWithoutEscalationPort()
        {
            Build(withMonster: true, withBattle: false);

            Assert.That(container.Resolve<EncounterStep>().EscalationPort, Is.Null);
        }

        /// <summary>负对照：没装 MonsterInstaller → 战斗侧照常构建、不抛（TryResolve 落空即跳过）。</summary>
        [Test]
        public void Build_WithoutMonsterInstaller_BuildsBattleFlowWithoutThrowing()
        {
            Assert.DoesNotThrow(() => Build(withMonster: false, withBattle: true));

            Assert.That(container.Resolve<BattleFlow>(), Is.Not.Null);
            Assert.That(container.TryResolve(out EncounterStep _), Is.False);
        }

        // ─────────── BOSS 主动发难的场景绑定（PRP/combat-iteration D6，BossAggroSceneBinder） ───────────

        /// <summary>
        /// 容器里那个绑定入口点 Start（= 玩法场景已加载）：场上的 BossAggro 拿到外部世界、按名册那一行武装，
        /// 同物体的 BossEntryContext 在这一刻就接上了容器里的 MonsterConfig 与玩家模型（不等开仗）。
        /// 前置钉住「容器构建完还没绑」——绑定不在构建回调里（那时只有 Boot 场景，pitfalls.md）。
        /// </summary>
        [Test]
        public void SceneBinderStart_WithAggroBossInLoadedScene_BindsAndArmsFromContainer()
        {
            WriteRoster(aggroEnabled: true);
            BossAggro aggro = CreateAggroBoss("BattleInstallerTests_AggroBoss");
            Build(withMonster: true, withBattle: true);
            BossAggroSceneBinder binder = container.Resolve<BossAggroSceneBinder>();
            Assert.That(aggro.IsBound, Is.False, "前置：容器构建完（构建回调已跑）还没绑");

            binder.Start();

            Assert.That(aggro.IsBound, Is.True);
            Assert.That(aggro.IsArmed, Is.True, "名册那一行开了发难 + 局势已接上");
            Assert.That(aggro.GetComponent<BossEntryContext>().IsReady, Is.True, "局势在场景加载时接了容器里的 MonsterConfig / PlayerModel");
            Assert.That(container.Resolve<BossAggroSceneBinder>(), Is.SameAs(binder), "入口点是单例");
        }

        /// <summary>
        /// Start 之后才加载的场景（真实流程：标题「开始」→ SampleScene）：binder 不每帧找，所以新出现的 BOSS 在场景加载回调前没绑；
        /// 回调（sceneLoaded → BindScene）跑过就绑上。EditMode 里发不出 SceneManager.sceneLoaded，直接调它转发的那一个方法。
        /// </summary>
        [Test]
        public void SceneBinder_SceneLoadedAfterStart_BindsNewBossOnlyOnSceneCallback()
        {
            WriteRoster(aggroEnabled: true);
            Build(withMonster: true, withBattle: true);
            BossAggroSceneBinder binder = container.Resolve<BossAggroSceneBinder>();
            binder.Start();
            BossAggro late = CreateAggroBoss("BattleInstallerTests_LateBoss");
            Assert.That(late.IsBound, Is.False, "前置：不每帧扫场景");

            int bound = binder.BindScene(late.gameObject.scene);

            Assert.That(bound, Is.GreaterThanOrEqualTo(1));
            Assert.That(late.IsBound && late.IsArmed, Is.True);
        }

        /// <summary>负对照：名册那一行没开发难 → 绑上了但不武装（= 加组件之前的行为）。</summary>
        [Test]
        public void SceneBinderStart_RosterRowWithoutAggro_BindsButStaysUnarmed()
        {
            WriteRoster(aggroEnabled: false);
            BossAggro aggro = CreateAggroBoss("BattleInstallerTests_CalmBoss");
            Build(withMonster: true, withBattle: true);

            container.Resolve<BossAggroSceneBinder>().Start();

            Assert.That(aggro.IsBound, Is.True);
            Assert.That(aggro.IsArmed, Is.False);
        }

        /// <summary>负对照：容器里没有 MonsterConfig（没装 MonsterInstaller）→ 局势接不上、不武装——配置来自容器，不是测试自己塞的。</summary>
        [Test]
        public void SceneBinderStart_WithoutMonsterConfig_LeavesAggroUnarmed()
        {
            WriteRoster(aggroEnabled: true);
            BossAggro aggro = CreateAggroBoss("BattleInstallerTests_UnconfiguredBoss");
            Build(withMonster: false, withBattle: true);

            container.Resolve<BossAggroSceneBinder>().Start();

            Assert.That(aggro.IsBound, Is.True);
            Assert.That(aggro.GetComponent<BossEntryContext>().IsReady, Is.False);
            Assert.That(aggro.IsArmed, Is.False);
        }

        // ──────────────────────── 夹具 ────────────────────────

        private const string AggroBossId = "sample_boss";

        /// <summary>
        /// 名册写一行 sample_boss（按 Inspector 的接法写私有数组）。SerializedProperty 加出来的新元素字段全是零值、
        /// 不走字段初始值，所以必填的数值逐个写上（否则名册校验不过，BattleInstaller 退回空名册并报 Error）。
        /// </summary>
        private void WriteRoster(bool aggroEnabled)
        {
            var serialized = new SerializedObject(bossRosterConfig);
            SerializedProperty bosses = serialized.FindProperty("bosses");
            bosses.arraySize = 1;
            SerializedProperty row = bosses.GetArrayElementAtIndex(0);
            row.FindPropertyRelative("id").stringValue = AggroBossId;
            row.FindPropertyRelative("displayName").stringValue = "测试 BOSS";
            row.FindPropertyRelative("maxHealth").intValue = 12;
            row.FindPropertyRelative("outOfBattleDrunk").intValue = 0;
            row.FindPropertyRelative("playerHealth").intValue = 10;
            row.FindPropertyRelative("monsterKindId").intValue = 0;
            row.FindPropertyRelative("aggroEnabled").boolValue = aggroEnabled;
            row.FindPropertyRelative("aggroChargeSpeed").floatValue = BossDefinition.PlaceholderAggroChargeSpeed;
            row.FindPropertyRelative("aggroRadius").floatValue = BossDefinition.PlaceholderAggroRadius;
            row.FindPropertyRelative("aggroReactionSeconds").floatValue = BossDefinition.PlaceholderAggroReactionSeconds;
            row.FindPropertyRelative("aggroLeashDistance").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>照 SampleScene 的 Npc_SampleBoss 摆一只：BossEntryContext + NarrativeTrigger + BossAggro（bossId = sample_boss）。</summary>
        private BossAggro CreateAggroBoss(string objectName)
        {
            var boss = new GameObject(objectName);
            spawned.Add(boss);
            boss.AddComponent<BossEntryContext>();
            boss.AddComponent<Game.Narrative.NarrativeTrigger>();
            BossAggro aggro = boss.AddComponent<BossAggro>();
            var serialized = new SerializedObject(aggro);
            serialized.FindProperty("bossId").stringValue = AggroBossId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return aggro;
        }

        private void Build(bool withMonster, bool withBattle)
        {
            host = new GameObject("GameBootstrap(BattleInstaller 接线测试)");
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

            // BattleInstaller 自己解析的框架侧服务（世界锁 / 战斗场景宿主 / 流程入口）。
            builder.RegisterInstance(new BattleFakes.Bus<StageEvent>()).As<ISubscriber<StageEvent>>();
            builder.RegisterInstance(new BattleFakes.Pause()).As<IWorldPauseService>();
            builder.RegisterInstance(input).As<IInputService>();
            builder.RegisterInstance(new BattleFakes.UI()).As<IUIService>();
            builder.RegisterInstance(new BattleFakes.Curtain()).As<ILoadingCurtain>();
            builder.RegisterInstance(new BattleFakes.Assets()).As<IAssetService>();

            if (withMonster)
            {
                MonsterInstaller monsterInstaller = host.AddComponent<MonsterInstaller>();
                Attach(monsterInstaller, "config", monsterConfig);
                monsterInstaller.Install(builder);
            }

            if (withBattle)
            {
                BattleInstaller battleInstaller = host.AddComponent<BattleInstaller>();
                Attach(battleInstaller, "turnBasedConfig", turnBasedConfig);
                Attach(battleInstaller, "bossRoster", bossRosterConfig);
                battleInstaller.Install(builder);

                // 覆盖四个对外口（理由见文件头）：后注册的覆盖 BattleInstaller 注册的那一份。
                builder.RegisterInstance(new BattleFakes.Narrative()).As<IBattleNarrative>();
                builder.RegisterInstance(new BattleFakes.Backpack()).As<IBattleBackpack>();
                builder.RegisterInstance(new BattleFakes.Gate()).As<IBattleWorldGate>();
                builder.RegisterInstance(new BattleFakes.Presenter()).As<IBattlePresenter>();
            }

            container = builder.Build();
        }

        /// <summary>按 Boot 的接法给注册器赋配置：私有序列化字段用 SerializedObject 写（不给生产代码开 setter）。</summary>
        private static void Attach(GameplayInstaller installer, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(installer);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 EncounterDropTests 的最小假实现）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>不采样任何输入的输入源：本组只构建容器，不推进 tick。</summary>
        private sealed class SilentInputSource : IInputSource
        {
            public void Sample(long tick) { }
            public InputCommand Current => default;
        }
    }
}
