// 职责：把物资箱模块的事件 broker、配置、服务、场景绑定与交互焦点注册进根作用域。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册。
//   2. 扩展不行：QuestInstaller / DialogueInstaller 只服务本模块，塞进去会让它们认识 Loot（依赖方向是 Loot → Quest / Dialogue）。
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Quest;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Loot
{
    /// <summary>
    /// 物资箱模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，排在 QuestInstaller 之后、
    /// ExplorationInstaller 之前（GameLifetimeScope 按 GetComponents 的组件顺序调用），
    /// 把 <c>Data/Loot/LootConfig.asset</c> 拖到 Config 字段。只 Register 不 Resolve。
    /// </summary>
    public sealed class LootInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "loot";

        [Tooltip("物资箱参数（交互半径、任务上报键、提示 / 通知文案、标记抬高）。拖 Data/Loot/LootConfig.asset。")]
        [SerializeField] private LootConfig config;

        public override void InstallEvents(IContainerBuilder builder, MessagePipeOptions options)
        {
            builder.RegisterMessageBroker<CrateCollectedEvent>(options);
            builder.RegisterMessageBroker<LootResetEvent>(options);
        }

        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveConfig());
            // 工厂注册：构造要 ITelemetryScope，容器里只有 ITelemetryService。
            // 同一条注册上 AsSelf + As<IGameService>：参与启动串行，又能被按具体类型注入（同 QuestInstaller）。
            // 注册顺序在 QuestService 之后，InitializeAsync 串行时任务服务已就绪。
            builder.Register<LootService>(resolver => new LootService(
                    resolver.Resolve<LootConfig>(),
                    resolver.Resolve<ISaveService>(),
                    resolver.Resolve<IConfigService>(),
                    resolver.Resolve<QuestService>(),
                    resolver.Resolve<INotificationService>(),
                    resolver.Resolve<IPublisher<CrateCollectedEvent>>(),
                    resolver.Resolve<IPublisher<LootResetEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton)
                .AsSelf()
                .As<IGameService>();
            // AsSelf：焦点与探索 HUD 要按具体类型注入 Binder（RegisterEntryPoint 默认只注册接口）。
            // 构造参数容器里都有，按类型自动注入。
            builder.RegisterEntryPoint<LootSceneBinder>(Lifetime.Singleton).AsSelf();
            // AsSelf：探索 HUD 要订阅 OnFocusChanged。
            builder.RegisterEntryPoint(resolver => new SupplyCrateFocus(
                    resolver.Resolve<LootService>(),
                    resolver.Resolve<LootSceneBinder>(),
                    resolver.Resolve<LootConfig>(),
                    resolver.Resolve<DialogueSceneBinder>(),
                    resolver.Resolve<DialogueInteractionFocus>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<IHudVisibility>(),
                    resolver.Resolve<IInputService>()), Lifetime.Singleton).AsSelf();
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 QuestInstaller）。
        private LootConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("LootInstaller 的 Config 字段没赋值，已用默认值顶上。"
                      + "把 Data/Loot/LootConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 LootInstaller 上。", this);
            return ScriptableObject.CreateInstance<LootConfig>();
        }
    }
}
