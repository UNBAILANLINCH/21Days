// 职责：把镜模块的事件 broker、配置、场景绑定、门面服务与三个呈现器注册进根作用域。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime，玩法类型只能经 GameplayInstaller 缝注册。
//   2. 扩展不行：LootInstaller / MonsterInstaller 只服务本模块，塞进去会让它们认识 Mirror（依赖方向是 Mirror → 它们）。
// 依赖说明：保存请求委托要解析 Game.Session.GameSession——Mirror → Session 与 Mirror → Loot / Monster / Player / Dialogue 同为
//   Game.Runtime 程序集内的单向引用；Session 引用 Monster / Loot / Quest / Dialogue，但没有任何模块引用 Mirror，不成环。
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Loot;
using Game.Monster;
using Game.Player;
using Game.Session;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Mirror
{
    /// <summary>
    /// 镜模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，排在 <c>InventoryInstaller</c> 之后、<c>ExplorationInstaller</c> 之前
    /// （GameLifetimeScope 按 GetComponents 的组件顺序调用；MirrorService.InitializeAsync 只取分区，不依赖别的服务先初始化），
    /// 把 <c>Data/Mirror/MirrorConfig.asset</c> 拖到 Config 字段。只 Register 不 Resolve。
    /// 存档分区 <see cref="MirrorSaveData"/> 不用注册：ISaveService.Get 首次访问即建默认分区，读档按类型全名自动认领。
    /// </summary>
    public sealed class MirrorInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "mirror";

        [Tooltip("镜参数（距离、裂痕缩减、扇形、冷却、结果停留、通灵视、可见范围、文案、影子提示图）。拖 Data/Mirror/MirrorConfig.asset。")]
        [SerializeField] private MirrorConfig config;

        public override void InstallEvents(IContainerBuilder builder, MessagePipeOptions options)
        {
            builder.RegisterMessageBroker<MirrorCastEvent>(options);
            builder.RegisterMessageBroker<MirrorCrackedEvent>(options);
            builder.RegisterMessageBroker<MirrorShatteredEvent>(options);
        }

        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveConfig());

            // 场景绑定：构造要 ITelemetryScope（工厂注册）；AsSelf 让服务与通灵视按具体类型注入。
            builder.RegisterEntryPoint(resolver => new MirrorSceneBinder(
                    resolver.Resolve<MonsterModel>(),
                    Scope(resolver)), Lifetime.Singleton)
                .AsSelf();

            // 门面：同一条注册上 AsSelf + As<IGameService>（同 LootInstaller）。保存请求委托解析 GameSession（SessionInstaller 已 AsSelf）。
            builder.Register<MirrorService>(resolver =>
                {
                    GameSession session = resolver.Resolve<GameSession>();
                    return new MirrorService(
                        resolver.Resolve<MirrorConfig>(),
                        resolver.Resolve<ISaveService>(),
                        resolver.Resolve<IConfigService>(),
                        resolver.Resolve<LootService>(),
                        resolver.Resolve<PlayerModel>(),
                        resolver.Resolve<PlayerConfig>(),
                        resolver.Resolve<MirrorSceneBinder>(),
                        resolver.Resolve<IPublisher<MirrorCastEvent>>(),
                        reason => session.RequestSave(reason),
                        Scope(resolver));
                }, Lifetime.Singleton)
                .AsSelf()
                .As<IGameService>();

            // 照镜输入与结果画面。AsSelf：回放 / 测试按具体类型拿 IsShowing。
            builder.RegisterEntryPoint(resolver => new MirrorInputPresenter(
                    resolver.Resolve<MirrorService>(),
                    resolver.Resolve<MirrorConfig>(),
                    resolver.Resolve<EncounterStep>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<IHudVisibility>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IAssetService>(),
                    resolver.Resolve<IClock>(),
                    Scope(resolver)), Lifetime.Singleton)
                .AsSelf();

            // 镜裂、HUD、视野遮罩与镜碎页。AsSelf：回放按具体类型拿 IsShatterShowing。
            builder.RegisterEntryPoint(resolver => new MirrorCrackPresenter(
                    resolver.Resolve<MirrorService>(),
                    resolver.Resolve<MirrorConfig>(),
                    resolver.Resolve<EncounterStep>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<IGameFlow>(),
                    resolver.Resolve<IClock>(),
                    resolver.Resolve<IPublisher<MirrorCrackedEvent>>(),
                    resolver.Resolve<IPublisher<MirrorShatteredEvent>>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    Scope(resolver)), Lifetime.Singleton)
                .AsSelf();

            // 通灵视影子提示。AsSelf：回放按具体类型拿 IsActive / ShownCount。
            builder.RegisterEntryPoint(resolver => new SpiritSightPresenter(
                    resolver.Resolve<MirrorSceneBinder>(),
                    resolver.Resolve<MirrorConfig>(),
                    resolver.Resolve<EncounterStep>(),
                    resolver.Resolve<PlayerModel>(),
                    Scope(resolver)), Lifetime.Singleton)
                .AsSelf();
        }

        private static ITelemetryScope Scope(IObjectResolver resolver) =>
            resolver.Resolve<ITelemetryService>().Scope(TelemetryModule);

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 LootInstaller）。
        private MirrorConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("MirrorInstaller 的 Config 字段没赋值，已用默认值顶上。"
                      + "把 Data/Mirror/MirrorConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 MirrorInstaller 上。", this);
            return ScriptableObject.CreateInstance<MirrorConfig>();
        }
    }
}
