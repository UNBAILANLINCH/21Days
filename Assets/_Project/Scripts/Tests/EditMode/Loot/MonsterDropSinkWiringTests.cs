// 职责：钉住「击杀掉落这条链接线接对了没有」——**容器级**的正反两条。
// 为什么单独一条（2026-10-09 主窗口补）：
//   `EncounterDropTests` 覆盖的是**调用侧**（谁在什么时候调 `IMonsterDropSink`），
//   `LootServiceTests` 覆盖的是**实现侧**（`LootMonsterDropSink` 转发对不对），
//   而两端之间的那一层——`LootInstaller` 把实现注册成 `IMonsterDropSink`、`MonsterInstaller`
//   的构建回调 `TryResolve` 到它并 `BindDropSink`——**一条用例都没有**。
//   这一层坏掉的表现是：编译不报、单测不报（单测都是手工 `BindDropSink`）、回放里也看不出，
//   只是怪死了什么都不掉。本工程把这类叫「静默缺功能」，值得一条专门的用例。
// 为什么不改 `EncounterKernelWiringTests`：它是另一路（Stealth 回放修复波）正在改的文件，
//   同一波不许两个 agent 改同一文件；所以本文件自带最小容器夹具，只装 `MonsterInstaller`。
// 夹具口径与 `EncounterKernelWiringTests` 一致（按 Boot 的接法挂注册器、`SerializedObject` 写私有配置字段）。
using System.Collections.Generic;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace Game.Tests.EditMode.Loot
{
    /// <summary>
    /// 「容器里注册的掉落口真的被绑进遭遇结算了吗」。
    /// </summary>
    public sealed class MonsterDropSinkWiringTests
    {
        /// <summary>真表里 `monster_species` 第一条是 1001（巡夜人）→ `yao_id` 1 → 掉落 `[1101 普通皮]`。</summary>
        private const int FirstSpeciesYaoId = 1;
        private const int CommonSkin = 1101;

        private GameObject host;
        private IObjectResolver container;
        private MonsterConfig monsterConfig;
        private PlayerConfig playerConfig;
        private SimulationConfig simulationConfig;
        private RecordingSink sink;

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
            DestroyConfig(playerConfig);
            DestroyConfig(monsterConfig);
            DestroyConfig(simulationConfig);
            playerConfig = null;
            monsterConfig = null;
            simulationConfig = null;
            sink = null;
        }

        /// <summary>销毁测试里现造的 ScriptableObject；为 null 时跳过（`TearDown` 里可能被重复调用）。</summary>
        private static void DestroyConfig(Object asset)
        {
            if (asset != null)
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// 正向：容器里注册了 `IMonsterDropSink` 时，**主怪一死就结算一次**，带的是真表那一行的 `yaoId` 与掉落。
        /// 这条用例的价值全在「容器装出来的 `EncounterStep`」——手工 `BindDropSink` 的那种测法（`EncounterDropTests`）
        /// 绕过了 `MonsterInstaller` 的构建回调，正是本文件要补的那一段。
        /// </summary>
        [Test]
        public void ContainerBuiltFromMonsterInstaller_BindsTheRegisteredDropSink()
        {
            Build(registerSink: true);
            EncounterStep step = container.Resolve<EncounterStep>();
            MonsterRules monster = container.Resolve<MonsterRules>();

            step.Begin(Vector2.zero, new[] { Vector2.zero, new Vector2(10f, 0f) });
            KillPrimary(monster);
            Tick(step, 0.1f);

            Assert.That(sink.Drops, Has.Count.EqualTo(1),
                "主怪死亡必须结算一次掉落——`MonsterInstaller` 的构建回调没把 `IMonsterDropSink` 绑进来的话，"
                + "这条会静默地一条都不掉（编译不报、单测不报、回放也看不出）");
            Assert.That(sink.Drops[0].Yao, Is.EqualTo(FirstSpeciesYaoId),
                "种类 id 传 0 时 `MonsterRules.Reset` 取真表第一条（1001 巡夜人）→ yao 1");
            Assert.That(sink.Drops[0].Items, Is.EqualTo(new[] { CommonSkin }),
                "掉落内容就是 tbyao 那一行的 `drop_items`（yao 1 = [1101 普通皮]）");
        }

        /// <summary>
        /// 负对照：**容器里没有** `IMonsterDropSink` 时（独立原型场景 / 纯 Monster 测试作用域就是这一支），
        /// `TryResolve` 落空 → 不绑 → 死亡什么都不掉，**而且不抛**。
        /// 没有这一条，上面那条无法排除「其实是谁都会掉」。
        /// </summary>
        [Test]
        public void ContainerWithoutAnyDropSink_DropsNothingAndDoesNotThrow()
        {
            Build(registerSink: false);
            EncounterStep step = container.Resolve<EncounterStep>();
            MonsterRules monster = container.Resolve<MonsterRules>();

            step.Begin(Vector2.zero, new[] { Vector2.zero, new Vector2(10f, 0f) });
            KillPrimary(monster);

            Assert.DoesNotThrow(() => Tick(step, 0.1f), "不接掉落口是合法装配（缺 Loot 的独立场景），不许炸");
            Assert.That(sink.Drops, Is.Empty, "没接就一条都不掉，与接线前逐字一致");
        }

        // ──────────────────────── 夹具 ────────────────────────

        /// <summary>按 Boot 的接法把 `MonsterInstaller` 挂到物体上再装容器。</summary>
        private void Build(bool registerSink)
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            sink = new RecordingSink();

            host = new GameObject("GameBootstrap(掉落接线测试)");
            MonsterInstaller monster = host.AddComponent<MonsterInstaller>();
            AttachConfig(monster, monsterConfig);

            var builder = new ContainerBuilder();
            // 这一行就是本文件的主角：注册成**接口**，MonsterInstaller 才 TryResolve 得到。
            if (registerSink)
            {
                builder.RegisterInstance(sink).As<IMonsterDropSink>();
            }

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
            container = builder.Build();
        }

        /// <summary>
        /// 让主怪死亡：走**公开的常规击杀入口**，不直写模型状态。
        /// `MonsterModel.Health` / `.Mode` 是 `internal set`，而 `Game.Runtime` 没有对测试程序集开
        /// `InternalsVisibleTo`（工程约定），直写必然 CS0272。
        /// 伤害取 `ResolveMaxHealth`（种类优先）而不写死：改表不会让这条无声漂移；返回值也断言，
        /// `killable = false` 的种类会拒伤，那时这条前置会直接点名是表的问题。
        /// </summary>
        private void KillPrimary(MonsterRules monster)
        {
            var damage = new DamageIntent(monsterConfig.ResolveMaxHealth(monster.Kind));
            var attacker = new PlayerSnapshot(Vector2.zero, Vector2.right, false, false, 3);
            Assert.That(monster.ApplyDamage(in damage, in attacker), Is.True,
                "前置：真表第一条种类要能被常规击杀打死（种类 1001 的 killable 为 true、生命为正）");
        }

        private static void Tick(EncounterStep step, float deltaTime)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
            var context = new SimulationContext(0, deltaTime, in command, new RandomService(5ul));
            step.Step(in context);
        }

        /// <summary>按 Boot 的接法给注册器赋配置：私有序列化字段用 SerializedObject 写（不给生产代码开 setter）。</summary>
        private static void AttachConfig(GameplayInstaller installer, Object config)
        {
            var serialized = new SerializedObject(installer);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>收集型掉落口：只记调用，不做结算（实现侧由 `LootServiceTests` 覆盖）。</summary>
        private sealed class RecordingSink : IMonsterDropSink
        {
            public List<(int Yao, int[] Items)> Drops { get; } = new List<(int, int[])>();

            public void SettleKillDrop(int yaoId, IReadOnlyList<int> dropItemIds)
            {
                var copy = new int[dropItemIds == null ? 0 : dropItemIds.Count];
                for (int i = 0; i < copy.Length; i++)
                {
                    copy[i] = dropItemIds[i];
                }

                Drops.Add((yaoId, copy));
            }
        }

        /// <summary>只递一份现成的 `cfg.Tables`（同 `MonsterKindCatalogTests` 的最小假实现）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new System.NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>不采样任何输入的输入源：本组用例直接调 `EncounterStep.Step`，不经过推进器。</summary>
        private sealed class SilentInputSource : IInputSource
        {
            public void Sample(long tick)
            {
            }

            public InputCommand Current => default;
        }
    }
}
