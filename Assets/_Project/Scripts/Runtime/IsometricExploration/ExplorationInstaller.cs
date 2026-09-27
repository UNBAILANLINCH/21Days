// 职责：把探索模块的入口点（探索 HUD / 沉浸模式呈现器）注册进根作用域。
// 为什么新建：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册；IsometricExploration 此前没有注册器，
//   塞进 DialogueInstaller / QuestInstaller 会让探索 HUD 的生命周期挂在别的模块上。
using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Dialogue;
using MessagePipe;
using VContainer;
using VContainer.Unity;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 探索模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，排在 DialogueInstaller / QuestInstaller 之后
    /// （呈现器要注入 <see cref="DialogueService"/>）。只 Register 不 Resolve。
    /// </summary>
    public sealed class ExplorationInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "exploration";

        public override void Install(IContainerBuilder builder)
        {
            // 工厂注册：构造要 ITelemetryScope，容器里只有 ITelemetryService，按类型自动注入解析不到。
            builder.RegisterEntryPoint(resolver => new ExplorationHudPresenter(
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IHudVisibility>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ISubscriber<HudVisibilityChangedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);

            // —— PRP/exploration-whitebox 波 3 追加：配置、控件区与万向标入口点。
            // 依赖的 SupplyCrateFocus / LootService / LootConfig（LootInstaller）、QuestSceneBinder / QuestService（QuestInstaller）、
            // PlayerModel（PlayerInstaller）都已 AsSelf 注册在根作用域，本注册器排在它们之后。
            builder.RegisterInstance(ResolveConfig());
            builder.RegisterEntryPoint(resolver => new ExplorationControlsPresenter(
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<Game.Core.Platform.IPlatformService>(),
                    resolver.Resolve<IsometricExplorationConfig>(),
                    resolver.Resolve<Game.Player.PlayerModel>(),
                    resolver.Resolve<Game.Loot.SupplyCrateFocus>(),
                    resolver.Resolve<Game.Loot.LootConfig>(),
                    resolver.Resolve<Game.Loot.LootService>(),
                    resolver.Resolve<Game.Quest.QuestService>(),
                    resolver.Resolve<Game.Core.Flow.IGameFlow>(),
                    resolver.Resolve<IHudVisibility>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ISubscriber<HudVisibilityChangedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            builder.RegisterEntryPoint(resolver => new ExplorationCompassPresenter(
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<Game.Quest.QuestSceneBinder>(),
                    resolver.Resolve<IsometricExplorationConfig>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ISubscriber<HudVisibilityChangedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton).AsSelf();

            // —— PRP/exploration-whitebox 波 9 追加：遮挡半透明（相机 → 玩家视线上的 SceneOccluder 变半透明）。
            builder.RegisterEntryPoint(resolver => new OccluderFadePresenter(
                    resolver.Resolve<Game.Quest.QuestSceneBinder>(),
                    resolver.Resolve<IsometricExplorationConfig>()), Lifetime.Singleton);
        }

        [UnityEngine.Tooltip("探索 HUD 的控件 / 万向标 / 重置文案配置。拖 Data/IsometricExploration/IsometricExplorationConfig.asset。")]
        [UnityEngine.SerializeField] private IsometricExplorationConfig config;

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 LootInstaller）。
        private IsometricExplorationConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Game.Core.Logging.Log.Error("ExplorationInstaller 的 Config 字段没赋值，已用默认值顶上。"
                      + "把 Data/IsometricExploration/IsometricExplorationConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 ExplorationInstaller 上。", this);
            return UnityEngine.ScriptableObject.CreateInstance<IsometricExplorationConfig>();
        }
    }
}
