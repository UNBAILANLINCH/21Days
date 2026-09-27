// 职责：把演出管线的事件 broker、配置、规则、服务与场景绑定注册进根作用域。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册。
//   2. 扩展不行：DialogueInstaller / LootInstaller 只服务本模块，塞进去会让它们认识演出。
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Performance
{
    /// <summary>
    /// 演出模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，排在 DialogueInstaller 之后
    /// （GameLifetimeScope 按 GetComponents 的组件顺序调用；对白按 TryResolve 取演出服务，顺序只影响 IGameService 初始化次序），
    /// 把 <c>Data/Performance/PerformanceConfig.asset</c> 拖到 Config 字段。只 Register 不 Resolve。
    /// </summary>
    public sealed class PerformanceInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "performance";

        [Tooltip("演出参数（长按跳过秒数、黑边高度、黑场时长、提示文案、默认策略）。拖 Data/Performance/PerformanceConfig.asset。")]
        [SerializeField] private PerformanceConfig config;

        public override void InstallEvents(IContainerBuilder builder, MessagePipeOptions options)
        {
            builder.RegisterMessageBroker<PerformanceStartedEvent>(options);
            builder.RegisterMessageBroker<PerformanceEndedEvent>(options);
        }

        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveConfig());
            // 工厂注册：构造要 ITelemetryScope，容器里只有 ITelemetryService。
            builder.Register<PerformanceRules>(resolver => new PerformanceRules(
                resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            // 同一条注册上挂全部接口：VContainer 对同一实现注册两条会建出两个实例 / 撞键。
            builder.Register<PerformanceService>(resolver => new PerformanceService(
                    resolver.Resolve<PerformanceConfig>(),
                    resolver.Resolve<PerformanceRules>(),
                    resolver.Resolve<IAssetService>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<ISaveService>(),
                    resolver.Resolve<IPublisher<PerformanceStartedEvent>>(),
                    resolver.Resolve<IPublisher<PerformanceEndedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton)
                .AsSelf()
                .As<IPerformanceService>()
                .As<IGameService>();
            builder.RegisterEntryPoint(resolver => new PerformanceSceneBinder(
                    resolver.Resolve<IPerformanceService>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton)
                .AsSelf();
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 LootInstaller）。
        private PerformanceConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("PerformanceInstaller 的 Config 字段没赋值，已用默认值顶上。"
                      + "把 Data/Performance/PerformanceConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 PerformanceInstaller 上。", this);
            return ScriptableObject.CreateInstance<PerformanceConfig>();
        }
    }
}
