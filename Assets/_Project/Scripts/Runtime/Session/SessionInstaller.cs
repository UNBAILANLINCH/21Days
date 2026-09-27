// 职责：把存档会话模块的事件 broker、配置、现场适配器、GameSession、保存触发桥、选槽面板控制器与标题路由注册进根作用域。
// 为什么新建：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册；
//   Session 依赖 Quest / Loot / Dialogue / Monster，塞进其中任何一个模块的注册器都会让依赖方向反过来。

using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Session
{
    /// <summary>
    /// 存档会话注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，排在最后（<c>ExplorationInstaller</c> 之后），
    /// 把 <c>Data/Session/SessionConfig.asset</c> 拖到 Config 字段。只 Register 不 Resolve。
    /// </summary>
    public sealed class SessionInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "session";

        [Tooltip("存档会话参数（槽数、保存提示、进度占位文案、退出保存超时）。拖 Data/Session/SessionConfig.asset。")]
        [SerializeField] private SessionConfig config;

        public override void InstallEvents(IContainerBuilder builder, MessagePipeOptions options)
        {
            builder.RegisterMessageBroker<SessionStartedEvent>(options);
            builder.RegisterMessageBroker<SaveCompletedEvent>(options);
        }

        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveConfig());

            // 构造参数（IGameFlow / DialogueService / UIService / EncounterStep / MonsterEncounterState / QuestService /
            // ISaveService / SessionConfig）容器里都有且都按具体类型开了键，按类型自动注入即可。
            builder.Register<SessionStateAdapter>(Lifetime.Singleton).As<ISessionStateSource>();

            // 一条注册同时拿到三种身份：RegisterEntryPoint 按已实现接口注册（IGameService 进启动串行、ITickable 每帧驱动、
            // IDisposable 随容器释放），再 AsSelf 让保存触发桥与标题路由按具体类型注入（先例 GameLifetimeScope 的 ReplayRecorder）。
            // 不另起一条 Register<GameSession>：同实现类型第二条注册会在 VContainer 注册表撞键（见 GameLifetimeScope.RegisterSimulationDriver）。
            // 工厂注册：构造要 ITelemetryScope，容器里只有 ITelemetryService。
            builder.RegisterEntryPoint(resolver => new GameSession(
                    resolver.Resolve<ISaveService>(),
                    resolver.Resolve<IClock>(),
                    resolver.Resolve<ILogicClock>(),
                    resolver.Resolve<IGameFlow>(),
                    resolver.Resolve<ISessionStateSource>(),
                    resolver.Resolve<SessionConfig>(),
                    resolver.Resolve<INotificationService>(),
                    resolver.Resolve<IPublisher<SessionStartedEvent>>(),
                    resolver.Resolve<IPublisher<SaveCompletedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton)
                .AsSelf();

            builder.RegisterEntryPoint<SaveTriggerBridge>(Lifetime.Singleton);

            // 选槽面板控制器：只被标题路由按具体类型注入，不是入口点（IDisposable 随容器释放）。
            // 工厂注册：构造要 ITelemetryScope，容器里只有 ITelemetryService。
            builder.Register(resolver => new SaveSlotsController(
                    resolver.Resolve<GameSession>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<SessionConfig>(),
                    resolver.Resolve<INotificationService>(),
                    resolver.Resolve<ISubscriber<GameStateChangingEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);

            // 标题路由：接管 TitleStart / TitleContinue / TitleLoad 三个事件（原 MonsterTitleRouter 已退役）。
            builder.RegisterEntryPoint(resolver => new SessionTitleRouter(
                    resolver.Resolve<GameSession>(),
                    resolver.Resolve<SaveSlotsController>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<ISubscriber<TitleStartClickedEvent>>(),
                    resolver.Resolve<ISubscriber<TitleContinueClickedEvent>>(),
                    resolver.Resolve<ISubscriber<TitleLoadClickedEvent>>(),
                    resolver.Resolve<ISubscriber<GameStateChangedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 QuestInstaller）。
        private SessionConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("SessionInstaller 的 Config 字段没赋值，已用默认值顶上。"
                      + "把 Data/Session/SessionConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 SessionInstaller 上。", this);
            return ScriptableObject.CreateInstance<SessionConfig>();
        }
    }
}
