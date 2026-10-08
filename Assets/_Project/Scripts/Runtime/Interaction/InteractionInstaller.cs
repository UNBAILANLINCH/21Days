// 职责：把统一交互的登记表（InteractionRegistry）、焦点入口点（InteractionFocus）与交互转向表现（InteractionPuppetPresenter）
//   注册进根作用域。挂在 Boot 场景的 GameBootstrap 物体上。
// 为什么新建：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册；
//   塞进 DialogueInstaller / LootInstaller 会让交互的生命周期挂在某一个实现方模块上，另一个实现方就得反向依赖它（PRP/interaction D1）。
using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using MessagePipe;
using VContainer;
using VContainer.Unity;

namespace Game.Interaction
{
    /// <summary>
    /// 统一交互注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上（排在 DialogueInstaller 之前）。只 Register 不 Resolve。
    /// <para>
    /// 与注册器顺序无关：Dialogue / Loot / Narrative 只在解析期（容器建好之后）才取 <see cref="IInteractionRegistry"/>，
    /// 容器构建前所有注册都已收齐；玩家标记晚于登记器找到时，登记表发 <see cref="IInteractionRegistry.OnActorChanged"/> 补通知。
    /// 排在 DialogueInstaller 之前只是让登记表的 Start（扫玩家标记、订阅 sceneLoaded）先跑，少一次补通知。
    /// </para>
    /// </summary>
    public sealed class InteractionInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "interaction";

        public override void Install(IContainerBuilder builder)
        {
            // RegisterEntryPoint 默认按全部接口注册（IInteractionRegistry / IStartable / IDisposable）；AsSelf 供需要具体类型的验证代码解析。
            // 工厂注册：InteractionRegistry 另有一个给测试注入扫描函数的构造，按类型注册时 VContainer 会挑参数最多的那个而解析失败。
            builder.RegisterEntryPoint(_ => new InteractionRegistry(), Lifetime.Singleton).AsSelf();
            // 工厂注册：构造要 ITelemetryScope，容器里只有 ITelemetryService。
            builder.RegisterEntryPoint(resolver => new InteractionFocus(
                    resolver.Resolve<IInteractionRegistry>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IHudVisibility>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton).AsSelf();
            // 交互时小人转向（PRP/interaction D11）：只订阅焦点的 OnInteracted，不读输入、不写 Animator 参数。
            builder.RegisterEntryPoint(resolver => new InteractionPuppetPresenter(
                    resolver.Resolve<IInteractionFocus>(),
                    resolver.Resolve<IInteractionRegistry>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton).AsSelf();
        }
    }
}
