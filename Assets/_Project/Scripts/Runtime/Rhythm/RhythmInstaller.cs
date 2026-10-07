// 职责：将音游会话注册进根作用域。Core 不得认识玩法，不能扩展 GameLifetimeScope 注册玩法类型。
using Game.Core.Boot;
using UnityEngine;
using VContainer;
using VContainer.Unity;
namespace Game.Rhythm
{
    public sealed class RhythmInstaller : GameplayInstaller
    {
        [SerializeField] private RhythmConfig config;
        [SerializeField] private RhythmCatalogConfig catalog;
        public override void Install(IContainerBuilder builder)
        {
            if (config == null) throw new System.InvalidOperationException("RhythmInstaller 未配置谱面");
            builder.RegisterInstance(config);
            builder.RegisterEntryPoint<RhythmState>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<RhythmDemoEntry>(Lifetime.Singleton);
            if (catalog != null) builder.RegisterBuildCallback(container => container.Resolve<RhythmState>().ConfigureCatalog(catalog));
        }
    }
}
