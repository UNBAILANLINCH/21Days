// 职责：把背包模块的面板控制器与背包键入口点注册进根作用域。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册。
//   2. 扩展不行：LootInstaller 只服务物资箱，塞进去会让 Loot 认识面板（依赖方向是 Inventory → Loot）。
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Loot;
using MessagePipe;
using VContainer;
using VContainer.Unity;

namespace Game.Inventory
{
    /// <summary>
    /// 背包模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，排在 LootInstaller 之后
    /// （GameLifetimeScope 按 GetComponents 的组件顺序调用）。无配置资产、无模块事件。只 Register 不 Resolve。
    /// </summary>
    public sealed class InventoryInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "inventory";

        public override void Install(IContainerBuilder builder)
        {
            // 工厂注册：构造要 ITelemetryScope，容器里只有 ITelemetryService（同 QuestInstaller）。实现 IDisposable，容器托管释放。
            builder.Register<InventoryPanelController>(resolver => new InventoryPanelController(
                    resolver.Resolve<LootService>(),
                    resolver.Resolve<IConfigService>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ISubscriber<CrateCollectedEvent>>(),
                    resolver.Resolve<ISubscriber<LootResetEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            builder.RegisterEntryPoint(resolver => new InventoryHotkeyPresenter(
                    resolver.Resolve<InventoryPanelController>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<IHudVisibility>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
        }
    }
}
