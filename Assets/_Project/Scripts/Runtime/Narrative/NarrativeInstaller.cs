// 职责：根作用域组合叙事服务与事实源；复用 Dialogue 和 Session，不创建第二套 UI/存档服务。
using Game.Core.Boot;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Dialogue;
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
        }
    }
}
