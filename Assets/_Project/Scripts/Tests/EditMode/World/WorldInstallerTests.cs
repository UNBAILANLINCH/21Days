// 职责：钉住世界模块的装配层（WorldInstaller）——容器能解析出它注册的五项、两次解析拿到同一个单例、
//   且取出来的 WorldSceneState 真的连着容器里那份转场与目录（「注册了但取到另一份」这类失效最容易发生）。
// 为什么新建：本波**刻意没**把 WorldInstaller 挂到 Boot（Boot.unity 属共享点），所以「挂上去能不能起来」
//   没有任何真机路径；这里用真容器（VContainer 的 ContainerBuilder，同 IdentityInstallerTests）装一遍，
//   把装配问题挡在接线波之前。本类**不**覆盖 GameLifetimeScope 的收集逻辑（那要真 Boot 场景）。
using Game.Core.Assets;
using Game.Core.Config;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.IsometricExploration;
using Game.Player;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using Game.World;
using NUnit.Framework;
using UnityEngine;
using VContainer;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="WorldInstaller"/> 的装配测试。</summary>
    public sealed class WorldInstallerTests
    {
        private GameObject host;
        private IObjectResolver container;

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
        }

        [Test]
        public void Install_ResolvesEveryRegisteredService()
        {
            container = Build();

            Assert.That(container.Resolve<WorldCatalog>(), Is.Not.Null, "世界表只读查询");
            Assert.That(container.Resolve<IWorldTransition>(), Is.TypeOf<WorldTransition>(), "待处理转场（按接口取）");
            Assert.That(container.Resolve<WorldTransition>(), Is.Not.Null, "具体类型也要取得到（AsSelf：调试与接线要看 OverwrittenCount）");
            Assert.That(container.Resolve<ISpawnPlacement>(), Is.TypeOf<WorldSpawnPlacement>(),
                "机制波把这一条断言留成「接线波来换」的交接信号：本波已把 UnwiredSpawnPlacement 换成真实现，"
                + "所以这里断言的就是 WorldSpawnPlacement（换的是 WorldInstaller 里那一行注册，状态与其余测试一行没改）");
            Assert.That(container.Resolve<CameraConstraintPolicy>(), Is.Not.Null, "相机约束策略（表现层与将来的构图切换共用）");
            Assert.That(container.Resolve<WorldSceneState>(), Is.Not.Null,
                "GameFlow 按**具体类型**解析状态，所以它必须按具体类型可取");
        }

        [Test]
        public void Install_ResolvingTwice_ReturnsTheSameSingleton()
        {
            container = Build();

            Assert.That(container.Resolve<IWorldTransition>(), Is.SameAs(container.Resolve<IWorldTransition>()));
            Assert.That(container.Resolve<WorldSceneState>(), Is.SameAs(container.Resolve<WorldSceneState>()));
            Assert.That(container.Resolve<ISpawnPlacement>(), Is.SameAs(container.Resolve<ISpawnPlacement>()));
            Assert.That(container.Resolve<CameraConstraintPolicy>(), Is.SameAs(container.Resolve<CameraConstraintPolicy>()));
        }

        [Test]
        public void Install_SceneState_IsWiredToTheContainerTransitionAndCatalog()
        {
            container = Build();
            var transition = container.Resolve<IWorldTransition>();
            WorldSceneState state = container.Resolve<WorldSceneState>();

            // 真表现状（接线波之后）：human_jingyang 已实装，所以地址解析得出**真表里那一列**。
            // 地址是这个值就说明状态用的就是容器里那份**真表**目录（常量兜底早在机制波就被否掉了）。
            transition.Request(new WorldTransitionRequest(WorldTestSupport.HumanScene));
            Assert.That(ReadAddress(state), Is.EqualTo(WorldTestSupport.HumanScene));

            // 消费之后从状态侧再走一遍：EditMode 里没有加载任何世界场景，所以摆放必然失败——
            // 失败档位是「摆不成」而不是「目标不可加载」，且待处理位**被清空**。
            // 清空要落在**容器里那份**转场上（两边不是同一个对象的话，这一条会先挂）。
            WorldSceneEntry entry = state.EnterScene();
            Assert.That(entry.Success, Is.False);
            Assert.That(entry.Failure, Is.EqualTo(WorldSceneEntryFailure.PlacementFailed),
                "EditMode 没有世界场景，落点算得出来但摆不下去：" + entry.Error);
            Assert.That(entry.Spawn, Is.Not.Null, "失败也把「本该摆到哪」带回来");
            Assert.That(entry.Spawn.SceneKey, Is.EqualTo(WorldTestSupport.HumanScene));
            Assert.That(transition.HasPending, Is.False, "状态与容器里那份转场是同一个对象");
        }

        [Test]
        public void Install_WithoutAnyPendingTransition_ReportsNoPendingTransition()
        {
            // 负对照：什么请求都没写时，状态是「明确报错」而不是悄悄给一个默认地址。
            container = Build();
            WorldSceneState state = container.Resolve<WorldSceneState>();

            Assert.That(ReadAddressOrNull(state), Is.Null, "没有待处理转场就不该有地址");
            Assert.That(state.EnterScene().Failure, Is.EqualTo(WorldSceneEntryFailure.NoPendingTransition));
        }

        // 属性 getter 会抛，必须要有个方法体才能当 TestDelegate 用。
        private static string ReadAddress(WorldSceneState target) => target.SceneAddress;

        private static string ReadAddressOrNull(WorldSceneState target)
        {
            try
            {
                return target.SceneAddress;
            }
            catch (WorldResolveException)
            {
                return null;
            }
        }

        /// <summary>
        /// 按 Boot 的接法把注册器挂到一个物体上再调 <c>Install</c>，然后建容器。
        /// <para>
        /// 本类依赖的服务里，框架侧用到 <c>IAssetService</c> / <c>IConfigService</c> / <c>ISaveService</c> /
        /// <c>ITelemetryService</c> / <c>IGameFlow</c> / <c>IInputService</c>，玩法侧用到
        /// <c>PlayerModel</c> / <c>PlayerRules</c> / <c>SimulationRunner</c>，各自补一个桩或真服务就能建起来
        /// （同 IdentityInstallerTests 的做法）。
        /// </para>
        /// <para>
        /// <b>为什么玩法侧那几个也要在这儿补</b>：<c>RegisterEntryPoint&lt;WorldSceneDriver&gt;</c> 会在
        /// **容器构建期**就解析该类型（VContainer 要把它挂进 player loop），所以「装了 World 就必须有
        /// GameFlow / Input / Player / Simulation」是一条真实耦合，不是测试凑数。它换来的是
        /// 「驱动场景流转本来就依赖流程层」这件事在装配期就暴露，而不是等进场景才炸。
        /// </para>
        /// </summary>
        private IObjectResolver Build()
        {
            host = new GameObject("WorldInstaller(测试)");
            var installer = host.AddComponent<WorldInstaller>();

            var builder = new ContainerBuilder();

            // 表用真数据：要守的就是「Boot 起来后读的是真表」这条路。
            builder.RegisterInstance<IConfigService>(
                new WorldTestSupport.StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes())));
            builder.RegisterInstance<IAssetService>(new WorldTestSupport.UnusedAssets());
            builder.RegisterInstance<ISaveService>(new WorldTestSupport.InMemorySaveService());

            builder.RegisterInstance(TelemetryOptions.Default);
            builder.RegisterInstance(new FakeTelemetryClock()).As<ITelemetryClock>();
            builder.RegisterInstance(new ITelemetrySink[] { new RecordingTelemetrySink() });
            builder.Register<TelemetryService>(Lifetime.Singleton).As<ITelemetryService>();

            // —— 驱动 WorldSceneDriver 要的框架侧依赖（真依赖，不是为过测试而加）——
            builder.RegisterInstance<IGameFlow>(new UnusedGameFlow());
            builder.RegisterInstance<IInputService>(new UnusedInput());

            // —— 驱动要的玩法侧依赖：用真类型 + 代码建的配置资产（字段默认值都是正数，构造校验过得去）——
            builder.RegisterInstance(UnityEngine.ScriptableObject.CreateInstance<PlayerConfig>());
            builder.Register<PlayerModel>(Lifetime.Singleton);
            builder.Register(resolver => new PlayerRules(
                resolver.Resolve<PlayerConfig>(), resolver.Resolve<PlayerModel>(), null), Lifetime.Singleton);

            // 确定性内核：WorldSceneDriver 把自己挂进它的步骤表（Start 时），装配期要有它。
            builder.RegisterInstance(UnityEngine.ScriptableObject.CreateInstance<SimulationConfig>());
            builder.RegisterInstance<IClock>(new LocalClock());
            builder.RegisterInstance<IInputSource>(new UnusedInputSource());
            builder.RegisterInstance<IRandomService>(new RandomService(1UL));
            builder.Register(resolver => new SimulationRunner(
                    resolver.Resolve<SimulationConfig>(),
                    resolver.Resolve<IClock>(),
                    resolver.Resolve<IInputSource>(),
                    resolver.Resolve<IRandomService>(),
                    null),
                Lifetime.Singleton);

            // 不传 config：WorldInstaller 没有任何序列化字段（这本身就是它「挂上即可用」的判据）。
            installer.Install(builder);
            return builder.Build();
        }

        /// <summary>装配测试不切状态：调用即说明用例走错了路。</summary>
        private sealed class UnusedGameFlow : IGameFlow
        {
            public GameState Current => null;

            public Cysharp.Threading.Tasks.UniTask GoToAsync<TState>(
                System.Threading.CancellationToken ct = default) where TState : GameState =>
                throw new System.NotSupportedException("装配测试不切状态");
        }

        /// <summary>动作集为空：本类测试不读输入（<c>Actions</c> 在真实服务初始化前也确实为 null）。</summary>
        private sealed class UnusedInput : IInputService
        {
            public GameInput Actions => null;

            public void EnableMap(string map)
            {
            }

            public void DisableMap(string map)
            {
            }
        }

        /// <summary>推进器不取输入：本类测试不跑逻辑 tick。</summary>
        private sealed class UnusedInputSource : IInputSource
        {
            public void Sample(long tick)
            {
            }

            public InputCommand Current => InputCommand.Empty;
        }
    }
}
