// 职责：根作用域组合叙事服务与事实源；复用 Dialogue 和 Session，不创建第二套 UI/存档服务。
using Game.Core.Boot;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Dialogue;
using Game.Identity;
using Game.Quest;
using Game.Session;
using MessagePipe;
using VContainer;

namespace Game.Narrative
{
    public sealed class NarrativeInstaller : GameplayInstaller
    {
        public override void InstallEvents(IContainerBuilder builder, MessagePipeOptions options)
        {
            builder.RegisterMessageBroker<NarrativeChangedEvent>(options);
        }

        public override void Install(IContainerBuilder builder)
        {
            builder.Register<NarrativeConditionSource>(Lifetime.Singleton);
            builder.Register<NarrativeCatalog>(Lifetime.Singleton);
            builder.Register(resolver => new NarrativeService(
                resolver.Resolve<NarrativeCatalog>(), resolver.Resolve<DialogueService>(), resolver.Resolve<DialogueSceneBinder>(),
                resolver.Resolve<NarrativeConditionSource>(), resolver.Resolve<ISaveService>(),
                resolver.Resolve<ISubscriber<SessionStartedEvent>>(), resolver.Resolve<ISubscriber<QuestCompletedEvent>>(),
                resolver.Resolve<IPublisher<NarrativeChangedEvent>>(), resolver.Resolve<ITelemetryService>().Scope("narrative")),
                Lifetime.Singleton).AsSelf().As<IGameService>();

            // —— S 组内核接线（Q3 波）：身份 → 剧情事实投影（S1/S2）。
            // 方向是**消费方自己拉**：本模块只 TryResolve 身份侧的服务，Identity 不反过来认识 Narrative，
            // 所以不成环；没有 IdentityInstaller 时这一层不生效，条件行为与接线前逐字一致
            //（NarrativeConditionSource.BindIdentity 的契约：不接线时身份键恒不写）。
            // 时机：构建回调在容器建好之后、IGameService 初始化之前跑，身份那六项注册都已就位——
            // 这里只装配引用与纯数据（数值块 / 定义表都是内存对象），**不读任何配置表**。
            // 谁先谁后不影响结果：MonsterInstaller 的构建回调也 TryResolve 同一批单例，两处都只是赋值。
            builder.RegisterBuildCallback(resolver =>
            {
                if (!resolver.TryResolve(out IdentityState state))
                {
                    return;
                }

                resolver.Resolve<NarrativeConditionSource>().BindIdentity(
                    state,
                    resolver.Resolve<IdentityLedger>(),
                    resolver.Resolve<SuspicionState>(),
                    resolver.Resolve<IdentitySettings>());
            });
        }
    }
}
