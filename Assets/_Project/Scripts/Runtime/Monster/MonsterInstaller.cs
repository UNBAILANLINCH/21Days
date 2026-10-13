// 职责：将怪物、遭遇状态及 Player→Monster 的固定 tick 顺序接入根作用域。
// 为什么新建：Core 不能引用玩法，SampleInstaller 的入口只服务示例场景。
// 按种类数值的接线（2026-10-07，聚光灯「怪物种类数据化」）：
//   全局默认值 = MonsterConfig（本物体上的资产）；按种类的数值 = MonsterKindCatalog（monster_species 表）。
//   本物体的「种类 Id」非 0 时取那一行；为 0（默认）时用表里第一条，且**读不到表就按全局默认跑**——
//   旧场景 / 独立原型场景 / 表没就绪这三种情况的行为都与拆分前一致。
//   **种类不在本类里查**：本类的 Install 与下面的 RegisterBuildCallback 都跑在容器构建期
//   （回调在容器建好之后、IGameService 初始化之前），那时配置表还没加载，查只会永远拿到「表没就绪」。
//   所以只把目录与种类 id 交给 MonsterRules，由它在 Reset（遭遇场景就绪）时查一次。
// 构建回调里的最后一段（2026-10-08 掩体贯通 / 击杀掉落）：`UseStealth(stealth)` 现在会把潜行内核那只
//   `StealthSight` 一并交给每一只巡逻怪（`EncounterStep.ApplyMonsterOcclusion`），怪物视锥从此吃遮挡；
//   另外用 `TryResolve<IMonsterDropSink>()` 把击杀掉落口接进遭遇结算。
// 2026-10-09（roadmap C5 剩下的那一半）：新建 `EncounterBattleBridge` 并**紧跟 `EncounterStep` 之后**
//   加入同一张 tick 步骤表——它把空 payload 的 Battle 阶段（活遭遇档）标成一仗，胜负出了就在同一 tick 里
//   取走结果、在渲染帧回写剧情。顺序不能改（镜碎会在**同一帧更晚的渲染回调**里 `End()`，下一场遭遇的 `Begin()`
//   会把结果抹掉——理由与时序见那个类的文件头），
//   剧情侧仍走**消费方自己拉**：`TryResolve<NarrativeService>()` + `TryResolve<ISubscriber<BattleStageEnteredEvent>>()`，
//   两个都拿不到就不 Bind——桥每帧只走一次布尔早退，行为与接线前逐字一致。
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
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

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
            // 实时遭遇 → 剧情的结果桥（roadmap C5 剩下的那一半）。注册成入口点（ITickable）才能每渲染帧拿到回写时机；
            // 步骤表里的位置在下面的构建回调里定（必须紧跟 EncounterStep）。
            builder.RegisterEntryPoint(resolver => new EncounterBattleBridge(resolver.Resolve<EncounterStep>()), Lifetime.Singleton)
                .AsSelf();
            builder.Register<MonsterEncounterState>(Lifetime.Singleton);
            // 标题「开始」不在本模块路由：已由存档会话（Game.Session 的 SessionTitleRouter）接管，PRP/save-session D5。

            builder.RegisterBuildCallback(resolver =>
            {
                EncounterStep step = resolver.Resolve<EncounterStep>();
                SimulationRunner runner = resolver.Resolve<SimulationRunner>();
                runner.AddStep(step);

                // 桥必须**紧跟在 EncounterStep 之后**（顺序 = tick 顺序）：玩家生命归零那一刻，
                // 镜碎会在同一帧更晚的渲染回调里调 step.End()，把 PendingResult / BattleSettlement 一起抹掉——
                // 只有落在同一个 tick 里的取走步骤能抢在它前面。理由与全过程见 EncounterBattleBridge 文件头。
                EncounterBattleBridge bridge = resolver.Resolve<EncounterBattleBridge>();
                runner.AddStep(bridge);

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

                // 击杀掉落口（IMonsterDropSink，实现在 Game.Loot）：不接时什么都不掉，与接线前逐字一致——
                // 独立原型场景与纯 Monster 测试作用域没有 Loot，那里不该凭空多出一条掉落。
                // 同上是**消费方自己拉**：Monster 声明接口、Loot 反向实现，Monster 不认识 LootService。
                if (resolver.TryResolve(out IMonsterDropSink dropSink))
                {
                    step.BindDropSink(dropSink);
                }

                // —— 实时遭遇的结果回写（C5 剩下的那一半）：把桥接上剧情侧。
                // 同样是**消费方自己拉**：Monster 认识 Narrative（同上面 NarrativeConditionSource 那一行），
                // Narrative 不反过来认识 Monster。两样缺任一件（没装 NarrativeInstaller 的测试作用域 / 独立原型场景）
                // 就不 Bind：桥不登记、不结算、不回写，行为与接线前逐字一致。
                if (resolver.TryResolve(out ITelemetryService telemetryService))
                {
                    bridge.UseTelemetry(telemetryService.Scope("monster"));
                }

                if (resolver.TryResolve(out NarrativeService narrative)
                    && resolver.TryResolve(out ISubscriber<BattleStageEnteredEvent> battleStages))
                {
                    bridge.Bind(narrative, battleStages);
                }
            });
        }
    }
}
