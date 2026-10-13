// 职责：把背包模块的面板控制器与背包键入口点注册进根作用域，并把道具使用 / 合成的服务与效果执行者装配起来。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册。
//   2. 扩展不行：LootInstaller 只服务物资箱，塞进去会让 Loot 认识面板（依赖方向是 Inventory → Loot）。
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Loot;
using Game.Player;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Inventory
{
    /// <summary>
    /// 背包模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，排在 LootInstaller 之后
    /// （GameLifetimeScope 按 GetComponents 的组件顺序调用）。只 Register 不 Resolve。
    /// <para>
    /// Config 字段拖 <c>Data/Inventory/InventoryConfig.asset</c>（使用表 + 配方表，占位配置）。
    /// **没拖也不崩**：用代码里的占位默认值顶上并记一条 Error，与 LootInstaller / QuestInstaller 同一口径。
    /// </para>
    /// </summary>
    public sealed class InventoryInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "inventory";

        [Tooltip("道具使用表 + 配方表（占位配置）。拖 Data/Inventory/InventoryConfig.asset。")]
        [SerializeField] private InventoryConfig config;

        // 事件 broker（含面板订阅的 BackpackChangedEvent）全部由 LootInstaller 注册，本模块不重复注册。

        public override void Install(IContainerBuilder builder)
        {
            InventoryConfig value = ResolveConfig();

            // 效果执行者注册表：装配期建一次，注册成实例供使用服务解析。
            // 当前只挂占位治疗（heal.player_hp）；皮 / 面具类效果的执行者归 S1「换皮与附身」，
            // 查不到执行者时执行口会明确回「没有执行者」而不是静默失败。
            var effects = new ItemEffectRegistry();
            effects.Register(new HealPlayerEffect());
            builder.RegisterInstance(effects);

            // 使用表 / 配方表从配置资产读一次（配置只读，运行时不改 SO 字段）。
            builder.Register<ItemUseService>(resolver => new ItemUseService(
                    value.CreateUseSpecs(),
                    resolver.Resolve<ItemEffectRegistry>(),
                    resolver.Resolve<LootService>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            builder.Register<ItemCraftService>(resolver => new ItemCraftService(
                    value.CreateRecipes(),
                    resolver.Resolve<LootService>(),
                    value.ConsumeIngredients,
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);

            builder.Register<InventoryPanelController>(resolver => new InventoryPanelController(
                    resolver.Resolve<LootService>(),
                    resolver.Resolve<IConfigService>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ItemUseService>(),
                    resolver.Resolve<ItemCraftService>(),
                    resolver.Resolve<INotificationService>(),
                    resolver.Resolve<ISubscriber<CrateCollectedEvent>>(),
                    resolver.Resolve<ISubscriber<LootResetEvent>>(),
                    resolver.Resolve<ISubscriber<BackpackChangedEvent>>(),
                    // 玩家生命口：本模块的薄适配器持 PlayerRules、经 PlayerRules.Heal 回血。
                    // 依赖方向是 Inventory → Player（适配器放在消费方这一侧，Player 不认识 Inventory）。
                    // 在 lambda 里构造而不是外面建局部变量：Install 跑在容器构建期、那会儿解析不了任何服务
                    // （同 MonsterInstaller 文件头的纪律），而这里 resolver 才在作用域内；单例工厂只跑一次。
                    new PlayerItemEffectTarget(resolver.Resolve<PlayerRules>()),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            builder.RegisterEntryPoint(resolver => new InventoryHotkeyPresenter(
                    resolver.Resolve<InventoryPanelController>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<IHudVisibility>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 QuestInstaller）。
        private InventoryConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("InventoryInstaller 的 Config 字段没赋值，已用占位默认值顶上。"
                      + "把 Data/Inventory/InventoryConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 InventoryInstaller 上。", this);
            return ScriptableObject.CreateInstance<InventoryConfig>();
        }
    }
}
