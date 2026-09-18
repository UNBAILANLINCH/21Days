// 职责：把玩家配置、规则和状态注册进根作用域。Core 与 SampleInstaller 不能注册 Player 类型。
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Telemetry;
using UnityEngine;
using VContainer;

namespace Game.Player
{
    public sealed class PlayerInstaller : GameplayInstaller
    {
        [SerializeField] private PlayerConfig config;

        public override void Install(IContainerBuilder builder)
        {
            PlayerConfig value = config;
            if (value == null)
            {
                Log.Error("PlayerInstaller 缺少 PlayerConfig，使用暂定默认值", this);
                value = ScriptableObject.CreateInstance<PlayerConfig>();
            }

            builder.RegisterInstance(value);
            builder.Register<PlayerModel>(Lifetime.Singleton);
            builder.Register<PlayerRules>(resolver => new PlayerRules(
                resolver.Resolve<PlayerConfig>(),
                resolver.Resolve<PlayerModel>(),
                resolver.Resolve<ITelemetryService>().Scope("player")), Lifetime.Singleton);
        }
    }
}
