// 职责：把任务模块的事件 broker、配置、内容目录、服务、面板控制器、场景绑定、目标驱动与 HUD 入口点注册进根作用域。
// 为什么新建：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册；DialogueInstaller 只服务对白，塞进去会让模块互相耦合。
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Quest
{
    /// <summary>
    /// 任务模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上（排在 DialogueInstaller 之后），
    /// 把 <c>Data/Quest/QuestConfig.asset</c> 拖到 Config 字段。只 Register 不 Resolve。
    /// </summary>
    public sealed class QuestInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "quest";

        [Tooltip("任务表现参数（指引边缘留白、悬浮偏移、距离刷新间隔、HUD / 面板固定文案）。拖 Data/Quest/QuestConfig.asset。")]
        [SerializeField] private QuestConfig config;

        public override void InstallEvents(IContainerBuilder builder, MessagePipeOptions options)
        {
            builder.RegisterMessageBroker<QuestActivatedEvent>(options);
            builder.RegisterMessageBroker<QuestObjectiveProgressedEvent>(options);
            builder.RegisterMessageBroker<QuestCompletedEvent>(options);
            builder.RegisterMessageBroker<QuestTrackingChangedEvent>(options);
        }

        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveConfig());
            // 工厂注册：构造要 ITelemetryScope，容器里只有 ITelemetryService，按类型自动注入解析不到。
            builder.Register<QuestCatalog>(resolver => new QuestCatalog(
                    resolver.Resolve<IConfigService>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            // 同一条注册上 AsSelf + As<IGameService>：参与启动串行，又能被按具体类型注入；
            // 另起一条同实现类型的注册会在 VContainer 注册表里撞键（见 GameLifetimeScope.RegisterSimulationDriver）。
            builder.Register<QuestService>(resolver => new QuestService(
                    resolver.Resolve<QuestCatalog>(),
                    resolver.Resolve<ISaveService>(),
                    resolver.Resolve<IPublisher<QuestActivatedEvent>>(),
                    resolver.Resolve<IPublisher<QuestObjectiveProgressedEvent>>(),
                    resolver.Resolve<IPublisher<QuestCompletedEvent>>(),
                    resolver.Resolve<IPublisher<QuestTrackingChangedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton)
                .AsSelf()
                .As<IGameService>();
            // 工厂注册：构造要 ITelemetryScope（同 QuestCatalog）。实现 IDisposable 的话容器会托管释放。
            builder.Register<QuestPanelController>(resolver => new QuestPanelController(
                    resolver.Resolve<QuestService>(),
                    resolver.Resolve<QuestConfig>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ISubscriber<QuestActivatedEvent>>(),
                    resolver.Resolve<ISubscriber<QuestObjectiveProgressedEvent>>(),
                    resolver.Resolve<ISubscriber<QuestCompletedEvent>>(),
                    resolver.Resolve<ISubscriber<QuestTrackingChangedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            // AsSelf：目标驱动与 HUD 要按具体类型注入 Binder（RegisterEntryPoint 默认只注册接口）。
            // 构造要 DialogueSceneBinder + QuestConfig，两者容器里都有，按类型自动注入即可。
            builder.RegisterEntryPoint<QuestSceneBinder>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint(resolver => new QuestObjectiveDriver(
                    resolver.Resolve<QuestService>(),
                    resolver.Resolve<QuestSceneBinder>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            builder.RegisterEntryPoint(resolver => new QuestHudPresenter(
                    resolver.Resolve<QuestService>(),
                    resolver.Resolve<QuestSceneBinder>(),
                    resolver.Resolve<QuestPanelController>(),
                    resolver.Resolve<QuestConfig>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IAssetService>(),
                    resolver.Resolve<IClock>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ISubscriber<QuestActivatedEvent>>(),
                    resolver.Resolve<ISubscriber<QuestObjectiveProgressedEvent>>(),
                    resolver.Resolve<ISubscriber<QuestCompletedEvent>>(),
                    resolver.Resolve<ISubscriber<QuestTrackingChangedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 DialogueInstaller）。
        private QuestConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("QuestInstaller 的 Config 字段没赋值，已用默认值顶上。"
                      + "把 Data/Quest/QuestConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 QuestInstaller 上。", this);
            return ScriptableObject.CreateInstance<QuestConfig>();
        }
    }
}
