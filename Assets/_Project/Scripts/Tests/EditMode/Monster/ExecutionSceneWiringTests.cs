// 职责：钉住「背后处决真的接进了正式流程」这一步。按 Boot 的接法把真实的 MonsterInstaller / StealthInstaller
//   装进容器，取容器装出来的 MonsterEncounterState，把「场景里那只」ExecutionInteractor 交给它的
//   BindExecution（OnSceneReadyAsync 走的就是这一个方法），然后断言四件事：
//     ① 组件真的被 Configure 上了——**这一波之前它一个生产调用方都没有**（全工程只有测试与注释引用它）；
//     ② 装进去的依赖就是容器里那几只：绕背处决成功 → 目标死亡 + `stealth.assassinated` 写进
//        **EncounterStep.Facts** + 遭遇承载 BattleResult(Victory) + 埋点出 `stealth_executed`；
//     ③ 负对照一：不调 BindExecution 时组件仍未接线，按 F 什么都不发生、也不埋点；
//     ④ 负对照二：容器里**没有 StealthInstaller** 时仍能接线（判定内核是可选依赖，独立原型场景与
//        纯 Monster 的测试作用域都没有它）；
//     ⑤ 负对照三：物种不是「暗杀」的怪（真表第一条）照样被拒——物种门槛没有因为接线而失效。
// 为什么新建：`ExecutionInteractorTests` 是「手工 new 出规则再手工 Configure」，测的是组件自己好用；
//   `EncounterKernelWiringTests` 守的是身份与潜行内核的装配。**「场景里那只组件被正式流程 Configure 上了」**
//   这一条此前没有覆盖，而它正是这一波的全部内容（判定 / 结算 / 交互三层当时已经全绿）。
// 前提（数据侧）：`Tables/Data/yao/3.json` 是市令（killable=false + defeat_method=暗杀），
//   `monster_species/1003.json` 指向它；本文件第一条用例会把「真表里这只怪确实可处决」当成前提断言，
//   表没重新生成时它会直接点名，而不是让后面的用例失败得莫名其妙。
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Mirror;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Stealth;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Game.Tests.EditMode.Monster
{
    public sealed class ExecutionSceneWiringTests
    {
        private const string StealthConfigPath = "Assets/_Project/Data/Stealth/StealthConfig.asset";

        /// <summary>真表里只能靠暗杀解决的那只（`yao/3.json`：killable=false + defeat_method=暗杀）。</summary>
        private const int ExecutableKindId = 1003;

        /// <summary>0 = 用 monster_species 表里第一条种类（第一条是「可击杀（方式没写）」）。</summary>
        private const int FirstTableKindId = 0;

        private GameObject host;
        private GameObject interactorHost;
        private IObjectResolver container;
        private GameInput gameInput;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;
        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private SimulationConfig simulationConfig;
        private StealthConfig stealthConfig;

        [TearDown]
        public void TearDown()
        {
            container?.Dispose();
            container = null;
            if (interactorHost != null)
            {
                Object.DestroyImmediate(interactorHost);
                interactorHost = null;
            }

            if (host != null)
            {
                Object.DestroyImmediate(host);
                host = null;
            }

            if (gameInput != null)
            {
                // **不能调 gameInput.Dispose()**：生成物的 Dispose 走 UnityEngine.Object.Destroy，
                // 在 EditMode 里会打一条 Error（测试框架据此判失败），同 ExecuteInputBindingTests。
                Object.DestroyImmediate(gameInput.asset);
                gameInput = null;
            }

            telemetry?.Dispose();
            telemetry = null;
            sink = null;
            Destroy(simulationConfig);
            Destroy(playerConfig);
            Destroy(monsterConfig);
            simulationConfig = null;
            playerConfig = null;
            monsterConfig = null;
            // stealthConfig 是 AssetDatabase 上的真资产，不能销毁（只清引用）。
            stealthConfig = null;
        }

        // ──────────────────────── 正向：接线 + 真能处决 ────────────────────────

        /// <summary>
        /// 正向：容器装出来的 MonsterEncounterState 把场景里的 ExecutionInteractor 接上玩家 / 怪物规则 /
        /// 事实写入点 / 判定内核 / 遭遇结算 / 埋点 / 动作资产；随后一次真实的绕背处决把三件事都做完。
        /// </summary>
        [Test]
        public void BindExecution_WiresTheSceneInteractor_AndARealBackstabKillsTheExecutableSpecies()
        {
            Build(ExecutableKindId, withStealth: true);
            MonsterEncounterState state = container.Resolve<MonsterEncounterState>();
            EncounterStep step = container.Resolve<EncounterStep>();
            MonsterRules monster = container.Resolve<MonsterRules>();
            ExecutionInteractor interactor = NewSceneInteractor();

            Assert.That(interactor.IsConfigured, Is.False, "接线前：组件从没被 Configure 过（这一波之前它没有生产调用方）");

            state.BindExecution(interactor);

            Assert.That(interactor.IsConfigured, Is.True, "BindExecution 之后组件要处于已接线状态");

            // 潜行贴到背后 0.5 米（deltaTime 0：只定位置与按钮，不推进移动）。
            // **Begin 必须在读种类之前**：MonsterRules 的种类是 Reset（EncounterStep.Begin 里调）时才查的，
            // 容器构建期配置表还没加载（见 MonsterInstaller 文件头）——顺序反了会读到 Kind == null。
            step.Begin(new Vector2(-0.5f, 0f), new[] { Vector2.zero, new Vector2(5f, 0f) });

            Assert.That(monster.Kind, Is.Not.Null, "容器里那只怪没拿到种类：MonsterKindCatalog 没接上");
            Assert.That(monster.Kind.DefeatMethod, Is.EqualTo(YaoCatalog.AssassinationMethod),
                $"前提：种类 {ExecutableKindId} 的 defeat_method 是「暗杀」。"
                + "真表里没有这样一条时，这条用例测不到处决的成功路径——先确认 Tables/Data 下的新行已生成进 .bytes");
            Assert.That(monster.IsKillable, Is.False, "前提：它是 killable = false，只能走处决");

            Tick(step, 0f, sneak: true);
            Assert.That(interactor.Inspect().Reject, Is.EqualTo(ExecutionReject.None),
                "四条件都成立：背后 + 距离 0.5 ≤ 1.2 + 目标未察觉 + 物种是暗杀");

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Allowed, Is.True);
            Assert.That(monster.Model.Health, Is.Zero, "处决让目标死亡（killable = false 也照杀，走 ApplyExecution）");
            Assert.That(monster.Model.Mode, Is.EqualTo(MonsterMode.Dead));
            Assert.That(step.Facts.IsTrue(StealthFactKeys.Assassinated), Is.True,
                "事实写进了**容器里那只** EncounterStep 的事实集——写入点接错时这里会是假");
            Assert.That(step.BattleSettlement.HasValue, Is.True, "遭遇结算那一项也接对了（承载 BattleResult）");
            Assert.That(step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(interactor.LastVerdict.HasValue, Is.True);
            Assert.That(interactor.LastVerdict.Value.Allowed, Is.True);
            Assert.That(LineContaining("stealth/stealth_executed"), Is.Not.Null,
                "埋点走的是容器里那套 ITelemetryService（接线时给的是 stealth 作用域）");
        }

        // ──────────────────────── 负对照 ────────────────────────

        /// <summary>
        /// 负对照（本波的核心）：**不调 BindExecution** 时，同一个场景组件仍然未接线——按 F 什么都不发生、
        /// 连一条埋点都不写。它证明正向那条用例的「有效果」确实来自那次接线，而不是组件自己会动。
        /// </summary>
        [Test]
        public void WithoutBindExecution_TheSceneInteractorStaysUnwired_AndNothingHappens()
        {
            Build(ExecutableKindId, withStealth: true);
            EncounterStep step = container.Resolve<EncounterStep>();
            MonsterRules monster = container.Resolve<MonsterRules>();
            ExecutionInteractor interactor = NewSceneInteractor();

            step.Begin(new Vector2(-0.5f, 0f), new[] { Vector2.zero, new Vector2(5f, 0f) });
            Tick(step, 0f, sneak: true);
            int telemetryBefore = sink.Count;

            ExecutionHint hint = interactor.Inspect();
            Assert.That(hint.Configured, Is.False, "没接线：面板要如实说未接线");
            Assert.That(hint.ToLabel(), Is.EqualTo(ExecutionHint.UnboundLabel));

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NoTarget), "「组件还没接好」不是玩法拒绝");
            Assert.That(interactor.LastVerdict.HasValue, Is.False, "没接线不算一次尝试，所以没有最近结果");
            Assert.That(monster.Model.Health, Is.GreaterThan(0), "怪没死");
            Assert.That(step.Facts.IsTrue(StealthFactKeys.Assassinated), Is.False, "事实没被写");
            Assert.That(sink.Count, Is.EqualTo(telemetryBefore), "没接线时连埋点都不写");
        }

        /// <summary>
        /// 负对照：容器里**没有 StealthInstaller**（没有 StealthKernel）时，MonsterEncounterState 照样能解析、
        /// 组件照样能接线（判定退回组件的占位阈值并打一条 Warn）。
        /// 这条守的是「不许把 StealthKernel 变成硬依赖」——独立原型场景与纯 Monster 的作用域都没有它。
        /// </summary>
        [Test]
        public void WithoutStealthInstaller_TheInteractorIsStillWired_WithPlaceholderThresholds()
        {
            LogAssert.Expect(LogType.Warning, new Regex("没有接 AssassinationRules"));
            Build(ExecutableKindId, withStealth: false);

            Assert.That(container.TryResolve(out StealthKernel _), Is.False, "前提：这个容器里确实没有潜行内核");

            MonsterEncounterState state = container.Resolve<MonsterEncounterState>();
            EncounterStep step = container.Resolve<EncounterStep>();
            MonsterRules monster = container.Resolve<MonsterRules>();
            ExecutionInteractor interactor = NewSceneInteractor();

            state.BindExecution(interactor);

            Assert.That(interactor.IsConfigured, Is.True, "缺内核不是接线失败：占位阈值照样能判定");
            step.Begin(new Vector2(-0.5f, 0f), new[] { Vector2.zero, new Vector2(5f, 0f) });
            Tick(step, 0f, sneak: true);

            Assert.That(interactor.TryExecute().Allowed, Is.True, "占位阈值（背后 120° / 1.2 米 / 不要求潜行）下这一刀成立");
            Assert.That(monster.Model.Health, Is.Zero);
            Assert.That(step.Facts.IsTrue(StealthFactKeys.Assassinated), Is.True);
        }

        /// <summary>
        /// 负对照：物种不是「暗杀」的怪（表里第一条，`可击杀（方式没写）`）在**同一套装配**下照样被拒——
        /// 位置条件成立、卡在物种门槛，且拒绝不写事实、不杀怪，但必须留一条拒绝埋点。
        /// </summary>
        [Test]
        public void WithANonExecutableSpecies_TheBackstabIsRejected_AndNothingIsWritten()
        {
            Build(FirstTableKindId, withStealth: true);
            MonsterEncounterState state = container.Resolve<MonsterEncounterState>();
            EncounterStep step = container.Resolve<EncounterStep>();
            MonsterRules monster = container.Resolve<MonsterRules>();
            ExecutionInteractor interactor = NewSceneInteractor();

            state.BindExecution(interactor);
            step.Begin(new Vector2(-0.5f, 0f), new[] { Vector2.zero, new Vector2(5f, 0f) });

            // 种类同样只有 Begin（Reset）之后才查得到，读早了是 null（会 NRE）。
            Assert.That(monster.Kind, Is.Not.Null, "容器里那只怪没拿到种类");
            Assert.That(monster.Kind.DefeatMethod, Is.Not.EqualTo(YaoCatalog.AssassinationMethod),
                "前提：表里第一条不是「暗杀」那条");

            Tick(step, 0f, sneak: true);

            ExecutionHint hint = interactor.Inspect();
            Assert.That(hint.Configured, Is.True);
            Assert.That(hint.HasTarget, Is.True, "附近有目标：这时拒绝原因说的是「离他最近这只为什么不行」");
            Assert.That(hint.Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));

            ExecutionVerdict verdict = interactor.TryExecute();

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));
            Assert.That(monster.Model.Health, Is.GreaterThan(0), "没死");
            Assert.That(step.Facts.IsTrue(StealthFactKeys.Assassinated), Is.False, "拒绝不写事实");
            Assert.That(step.BattleSettlement.HasValue, Is.False, "拒绝不承载战斗结果");
            Assert.That(LineContaining("stealth/stealth_execute_rejected"), Is.Not.Null,
                "拒绝路径必须有点（PRP §2.6：它是「按了没反应」的唯一现场）");
        }

        // ──────────────────────── 夹具 ────────────────────────

        /// <summary>场景里那只组件的替身：挂在独立物体上（与 SampleScene 挂在 Encounter 物体上同一形状）。</summary>
        private ExecutionInteractor NewSceneInteractor()
        {
            interactorHost = new GameObject("ExecutionInteractor(场景替身)");
            return interactorHost.AddComponent<ExecutionInteractor>();
        }

        /// <summary>
        /// 按 Boot 的接法把真实的注册器挂到同一个物体上再装容器（MonsterInstaller 第一个装，同
        /// EncounterKernelWiringTests：两侧绑定写在各自的构建回调里，证明与次序无关）。
        /// 框架侧的 IAssetService / IGameFlow 用桩：本组测试不进场景。
        /// </summary>
        private void Build(int kindId, bool withStealth)
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            stealthConfig = AssetDatabase.LoadAssetAtPath<StealthConfig>(StealthConfigPath);
            Assert.That(stealthConfig, Is.Not.Null, StealthConfigPath + " 读不到");

            host = new GameObject("GameBootstrap(测试)");
            MonsterInstaller monsterInstaller = host.AddComponent<MonsterInstaller>();
            SetSerializedInt(monsterInstaller, "kindId", kindId);
            AttachConfig(monsterInstaller, monsterConfig);

            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
            gameInput = new GameInput();

            var builder = new ContainerBuilder();
            var configService = new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            builder.RegisterInstance(configService).As<IConfigService>();
            builder.RegisterInstance(new YaoCatalog(configService));
            builder.RegisterInstance<IAssetService>(new UnusedAssets());
            builder.RegisterInstance<IGameFlow>(new UnusedGameFlow());
            builder.RegisterInstance<IInputService>(new StubInput(gameInput));
            builder.RegisterInstance(new RandomService(11ul)).As<IRandomService>();
            builder.RegisterInstance(telemetry).As<ITelemetryService>();
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
            // MonsterEncounterState 的构造参数里有 LiveInputSource（驯服切控制那条排队通路）。
            // 生产里由 GameLifetimeScope.RegisterSimulationDriver 注册；本组测试自建容器，
            // 不补这一条会在 Resolve<MonsterEncounterState>() 时直接解析失败。
            // 只建实例、不调 Initialize()：本组用例不推进输入采样，Initialize 在 EditMode 会去读动作集。
            builder.RegisterInstance(new LiveInputSource(new StubInput(gameInput))).AsSelf();

            monsterInstaller.Install(builder);
            if (withStealth)
            {
                StealthInstaller stealthInstaller = host.AddComponent<StealthInstaller>();
                AttachConfig(stealthInstaller, stealthConfig);
                stealthInstaller.Install(builder);
            }

            container = builder.Build();
        }

        private static void Tick(EncounterStep step, float deltaTime, bool sneak = false)
        {
            uint buttons = sneak ? InputCommand.ButtonSneak : 0u;
            var command = new InputCommand(Vector2.zero, Vector2.zero, buttons, Vector2.zero, 0);
            var context = new SimulationContext(0, deltaTime, in command, new RandomService(11ul));
            step.Step(in context);
        }

        /// <summary>埋点行里找含某段文字的那一条（找不到返回 null，交给断言报错）。</summary>
        private string LineContaining(string fragment)
        {
            for (int i = 0; i < sink.Lines.Count; i++)
            {
                if (sink.Lines[i].Contains(fragment))
                {
                    return sink.Lines[i];
                }
            }

            return null;
        }

        /// <summary>给注册器的私有序列化字段赋值（不给生产代码开 setter）。</summary>
        private static void AttachConfig(GameplayInstaller installer, Object config)
        {
            var serialized = new SerializedObject(installer);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary><c>MonsterInstaller.kindId</c> 是私有序列化字段：测试用它按 Boot 的接法指定这只怪的种类。</summary>
        private static void SetSerializedInt(Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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

        /// <summary>本组测试不进场景：任何加载都说明用例走错了路。</summary>
        private sealed class UnusedAssets : IAssetService
        {
            public Cysharp.Threading.Tasks.UniTask<AssetHandle<T>> LoadAsync<T>(string key,
                System.Threading.CancellationToken ct = default) where T : Object =>
                throw new System.NotSupportedException("本组测试不加载资产");

            public Cysharp.Threading.Tasks.UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label,
                System.Threading.CancellationToken ct = default) where T : Object =>
                throw new System.NotSupportedException("本组测试不加载资产");

            public Cysharp.Threading.Tasks.UniTask<GameObject> InstantiateAsync(string key, Transform parent = null,
                System.Threading.CancellationToken ct = default) =>
                throw new System.NotSupportedException("本组测试不实例化资产");

            public void ReleaseInstance(GameObject instance) => throw new System.NotSupportedException("本组测试不实例化资产");

            public Cysharp.Threading.Tasks.UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode,
                System.Threading.CancellationToken ct = default) =>
                throw new System.NotSupportedException("本组测试不进场景");
        }

        /// <summary>装配测试不切状态：调用即说明用例走错了路。</summary>
        private sealed class UnusedGameFlow : IGameFlow
        {
            public GameState Current => null;

            public Cysharp.Threading.Tasks.UniTask GoToAsync<TState>(
                System.Threading.CancellationToken ct = default) where TState : GameState =>
                throw new System.NotSupportedException("装配测试不切状态");
        }

        /// <summary>
        /// 真实动作集（`new GameInput()` 就是生成物那份），因为接线要把 <c>Gameplay/Execute</c> 绑给组件：
        /// 动作不在资产里时组件会记 Error 而拒绝接线，用空壳桩会把这条真实约束测没。
        /// </summary>
        private sealed class StubInput : IInputService
        {
            public StubInput(GameInput actions) => Actions = actions;

            public GameInput Actions { get; }

            public void EnableMap(string map)
            {
            }

            public void DisableMap(string map)
            {
            }
        }

        /// <summary>不采样任何输入：本组用例直接调 EncounterStep.Step，不经过推进器。</summary>
        private sealed class SilentInputSource : IInputSource
        {
            public void Sample(long tick)
            {
            }

            public InputCommand Current => default;
        }
    }
}
