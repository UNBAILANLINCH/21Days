// 职责：独立四店组合根；模块不在玩法场景创建子容器，也不修改正式 Boot 入口。
using Game.Clothing;
using Game.Core.Boot;
using Game.Core.Telemetry;
using Game.Core.Simulation;
using Game.Forging;
using Game.Identity;
using Game.Inventory;
using Game.Tavern;
using Game.Theater;
using UnityEngine;
using VContainer;
using VContainer.Unity;
namespace Game.ShopPlaytest
{
    public sealed class ShopPlaytestInstaller : GameplayInstaller
    {
        [SerializeField] private ClothingConfig clothing;
        [SerializeField] private ForgingConfig forging;
        [SerializeField] private TavernConfig tavern;
        [SerializeField] private TheaterConfig theater;
        public override void Install(IContainerBuilder builder)
        {
            if (clothing == null || forging == null || tavern == null || theater == null)
                throw new System.InvalidOperationException("独立试玩配置缺引用");
            builder.RegisterInstance(theater);
            builder.Register(r => new ClothingController(clothing, r.Resolve<ITelemetryService>().Scope("clothing")), Lifetime.Singleton);
            builder.Register(r => new ForgingController(forging, r.Resolve<ITelemetryService>().Scope("forging")), Lifetime.Singleton);
            builder.Register(r => new TavernController(tavern, r.Resolve<ITelemetryService>().Scope("tavern"),
                () => r.Resolve<RandomService>().MasterSeed), Lifetime.Singleton);
            builder.Register(r => new TheaterController(r.Resolve<ITelemetryService>().Scope("theater")), Lifetime.Singleton);
            builder.RegisterEntryPoint<ShopPlaytestState>(Lifetime.Singleton).AsSelf();
            builder.RegisterBuildCallback(r => r.Resolve<ItemEffectRegistry>().Register(new ShopSkinEffect(r.Resolve<ShopPlaytestState>())));
        }
    }
}
