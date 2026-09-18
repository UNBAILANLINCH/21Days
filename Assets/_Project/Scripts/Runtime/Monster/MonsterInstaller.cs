// 职责：将怪物、遭遇状态及 Player→Monster 的固定 tick 顺序接入根作用域。
// 为什么新建：Core 不能引用玩法，SampleInstaller 的入口只服务示例场景。
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Player;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Monster
{
    public sealed class MonsterInstaller : GameplayInstaller
    {
        [SerializeField] private MonsterConfig config;

        public override void Install(IContainerBuilder builder)
        {
            MonsterConfig value = config;
            if (value == null)
            {
                Log.Error("MonsterInstaller 缺少 MonsterConfig，使用暂定默认值", this);
                value = ScriptableObject.CreateInstance<MonsterConfig>();
            }

            builder.RegisterInstance(value);
            builder.Register<MonsterModel>(Lifetime.Singleton);
            builder.Register<MonsterRules>(resolver => new MonsterRules(
                    resolver.Resolve<MonsterConfig>(),
                    resolver.Resolve<MonsterModel>(),
                    resolver.Resolve<IRandomService>(),
                    resolver.Resolve<ITelemetryService>().Scope("monster")), Lifetime.Singleton);
            builder.Register<EncounterStep>(Lifetime.Singleton);
            builder.Register<MonsterEncounterState>(Lifetime.Singleton);
            builder.RegisterEntryPoint<MonsterTitleRouter>(Lifetime.Singleton);

            builder.RegisterBuildCallback(resolver =>
            {
                EncounterStep step = resolver.Resolve<EncounterStep>();
                resolver.Resolve<SimulationRunner>().AddStep(step);

                // 接线顺序就是快照字节布局，改变时必须升级 ReplayFormat 版本。
                IReplayStateProvider replay = resolver.Resolve<IReplayStateProvider>();
                replay.Register(resolver.Resolve<PlayerModel>());
                replay.Register(resolver.Resolve<MonsterRules>());
                replay.Register(step);
            });
        }
    }
}
