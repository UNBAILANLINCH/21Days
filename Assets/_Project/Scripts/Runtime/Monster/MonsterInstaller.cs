// 职责：将怪物、遭遇状态及 Player→Monster 的固定 tick 顺序接入根作用域。
// 为什么新建：Core 不能引用玩法，SampleInstaller 的入口只服务示例场景。
// 按种类数值的接线（2026-10-07，聚光灯「怪物种类数据化」）：
//   全局默认值 = MonsterConfig（本物体上的资产）；按种类的数值 = MonsterKindCatalog（monster_species 表）。
//   本物体的「种类 Id」非 0 时取那一行；为 0（默认）时用表里第一条，且**读不到表就按全局默认跑**——
//   旧场景 / 独立原型场景 / 表没就绪这三种情况的行为都与拆分前一致。
//   **种类不在本类里查**：本类的 Install 与下面的 RegisterBuildCallback 都跑在容器构建期
//   （回调在容器建好之后、IGameService 初始化之前），那时配置表还没加载，查只会永远拿到「表没就绪」。
//   所以只把目录与种类 id 交给 MonsterRules，由它在 Reset（遭遇场景就绪）时查一次。
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Logging;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Mirror;
using Game.Narrative;
using Game.Player;
using Game.Stealth;
using UnityEngine;
using VContainer;

namespace Game.Monster
{
    public sealed class MonsterInstaller : GameplayInstaller
    {
        [SerializeField] private MonsterConfig config;

        [Tooltip("这只怪的种类 Id（monster_species 表的主键）。0 = 用表里第一条种类；表读不到时按 MonsterConfig 的全局默认值跑。")]
        [SerializeField, Min(0)] private int kindId;

        public override void Install(IContainerBuilder builder)
        {
            MonsterConfig value = config;
            if (value == null)
            {
                Log.Error("MonsterInstaller 缺少 MonsterConfig，使用暂定默认值", this);
                value = ScriptableObject.CreateInstance<MonsterConfig>();
            }

            builder.RegisterInstance(value);
            // 种类目录：表适配层，只读。敌对半径由全局配置定（见 MonsterConfig.hostileRadius 的注释），
            // 所以构造它要先把全局值传进去，用于校验「种类橙区半径必须大于全局红区半径」。
            builder.Register(resolver => new MonsterKindCatalog(
                    resolver.Resolve<IConfigService>(),
                    resolver.Resolve<YaoCatalog>(),
                    value.HostileRadius), Lifetime.Singleton)
                .AsSelf();
            builder.Register<MonsterModel>(Lifetime.Singleton);
            // 种类在这里**不查**（查也查不到）：本方法跑在容器构建期，配置表要到 IGameService 初始化时才加载。
            // 只把目录与种类 id 交给规则，由 MonsterRules.Reset 在遭遇场景就绪时查一次。
            builder.Register<MonsterRules>(resolver => new MonsterRules(
                    resolver.Resolve<MonsterConfig>(),
                    resolver.Resolve<MonsterModel>(),
                    resolver.Resolve<IRandomService>(),
                    resolver.Resolve<ITelemetryService>().Scope("monster"),
                    kind: null,
                    kindCatalog: resolver.Resolve<MonsterKindCatalog>(),
                    kindId: kindId,
                    describeKind: () => "MonsterInstaller(GameBootstrap)"), Lifetime.Singleton);
            builder.Register<EncounterStep>(Lifetime.Singleton);
            builder.Register<MonsterEncounterState>(Lifetime.Singleton);
            // 标题「开始」不在本模块路由：已由存档会话（Game.Session 的 SessionTitleRouter）接管，PRP/save-session D5。

            builder.RegisterBuildCallback(resolver =>
            {
                EncounterStep step = resolver.Resolve<EncounterStep>();
                resolver.Resolve<SimulationRunner>().AddStep(step);

                // 接线顺序就是快照字节布局，改变时必须升级 ReplayFormat 版本。
                IReplayStateProvider replay = resolver.Resolve<IReplayStateProvider>();
                replay.Register(resolver.Resolve<PlayerModel>());
                replay.Register(resolver.Resolve<MonsterRules>());
                replay.Register(step);

                // —— S 组内核接线（Q3 波）：身份 / 潜行内核此前是死代码，这里把它们接进遭遇结算。
                // 方向是**消费方自己拉**（本模块只 TryResolve）：Identity 与 Stealth 都不反过来认识 Monster，
                // 所以不会成环；缺 IdentityInstaller / StealthInstaller 也合法——独立原型场景与纯 Monster 的
                // 测试作用域就没有它们，那时只少这两层，行为与接线前逐字一致。
                // 时机：构建回调在容器建好之后、IGameService 初始化之前跑，两组注册都已就位；
                // 这里只装配引用，**不查任何配置表**（表要到 IGameService 初始化时才加载，见本文件头注释）。
                if (resolver.TryResolve(out IdentityState identity))
                {
                    // 身份生效中 → 敌人不攻击（两层兼容见 IdentityAttackRules）。
                    step.BindIdentity(identity);
                }

                if (resolver.TryResolve(out StealthKernel stealth))
                {
                    // 没有这一步，EncounterStep 跑的是各内核的占位默认值：策划改 StealthConfig.asset 不生效。
                    step.UseStealth(stealth);
                }

                // 瞬时事实（stealth.* / chase.*）的写入方是本模块（EncounterStep 的内存事实集），
                // 读方是剧情条件源——由写入方这一侧把事实集交出去。不接时那些条件恒为假（与接线前一致）。
                // 放在本模块而不是 NarrativeInstaller：Narrative 不依赖 Monster，反过来会新开一条依赖方向。
                if (resolver.TryResolve(out NarrativeConditionSource conditions))
                {
                    conditions.BindEncounterFacts(step.Facts);
                }
            });
        }
    }
}
