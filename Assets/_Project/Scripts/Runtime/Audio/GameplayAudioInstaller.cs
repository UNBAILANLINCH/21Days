// 职责：跨模块音效表现的组合根；复用 Core 注册会反向依赖玩法，放入 MonsterInstaller 又会与表现消费者成环。
using Game.Core.Boot;
using VContainer;
using VContainer.Unity;
namespace Game.Audio
{
    public sealed class GameplayAudioInstaller : GameplayInstaller
    {
        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint(resolver => new GameplayAudio(resolver), Lifetime.Singleton)
                .AsSelf().As<IGameService>();
        }
    }
}
