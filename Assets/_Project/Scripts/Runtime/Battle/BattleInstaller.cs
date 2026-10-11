// 职责：把回合制战斗流程接进根作用域——BOSS 名册、剧情口、背包口、开战门闸、世界锁、战斗场景宿主、开仗装配、战斗表现、流程入口点。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime（asmdef 依赖方向），玩法类型只能经 GameplayInstaller 这道缝自己注册。
//   2. 扩展不行：塞进 NarrativeInstaller / LootInstaller 会让它们认识战斗；TurnBased 是纯规则、没有 Installer 也不该有。
// 注册顺序：本类注册的类型**没有一个是 IGameService**（流程是入口点，其余是普通单例），挂在 GameBootstrap 组件列表哪个位置
//   都不改变启动串行；PRP §4 定挂在列表末尾（W2 接 Boot.unity）。Install 只 Register 不 Resolve（同 GameplayInstaller 的类注释）。
// 表现层（IBattlePresenter）W2a 起注册为 BattleScenePresenter（BattleArena 舞台 + BattleView）；流程仍用 TryResolve 取，
//   取不到时开战报错并放弃（EditMode 流程测试换假表现）。
using System;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Loot;
using Game.TurnBased;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;
// 两个同名 BattleOutcome（Game.TurnBased / Game.Narrative）：不同时 using 两个命名空间，剧情侧类型用别名（PRP §7 坑 4）。
using NarrativeService = Game.Narrative.NarrativeService;
using StageEvent = Game.Narrative.BattleStageEnteredEvent;

namespace Game.Battle
{
    /// <summary>
    /// 战斗模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体的组件列表末尾，
    /// <c>Turn Based Config</c> 拖 <c>Data/TurnBased/TurnBasedConfig.asset</c>，<c>Boss Roster</c> 拖 <c>Data/Battle/BossRosterConfig.asset</c>。
    /// 依赖 NarrativeInstaller / LootInstaller 已注册的服务（玩家血量不再读 Player 模块，见 BattleSetup 文件头）。
    /// </summary>
    public sealed class BattleInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "battle";

        [Tooltip("回合制数值。拖 Data/TurnBased/TurnBasedConfig.asset。")]
        [SerializeField] private TurnBasedConfig turnBasedConfig;

        [Tooltip("BOSS 名册（剧情 Battle 阶段的 payload → BOSS 定义）。拖 Data/Battle/BossRosterConfig.asset。")]
        [SerializeField] private BossRosterConfig bossRoster;

        public override void Install(IContainerBuilder builder)
        {
            TurnBasedConfig rules = ResolveTurnBasedConfig();
            BossRosterConfig roster = ResolveBossRoster();
            BattleSettings settings = rules.Settings;
            string issue = rules.Validate();
            if (issue != null)
            {
                // 不拦启动：开战时 BattleSetup 会再校验一次并拒绝开仗，这里先在启动日志里点名。
                Log.Error($"BattleInstaller：TurnBasedConfig 不自洽，开战会被拒：{issue}", this);
            }

            builder.Register(_ => CreateRoster(roster), Lifetime.Singleton);
            builder.Register<IBattleNarrative>(resolver => new NarrativeBattlePort(resolver.Resolve<NarrativeService>()), Lifetime.Singleton);
            builder.Register<IBattleBackpack>(resolver => new LootBattleBackpack(
                resolver.Resolve<LootService>(), resolver.Resolve<IConfigService>()), Lifetime.Singleton);
            builder.Register(resolver => new BattleItemInventory(resolver.Resolve<IBattleBackpack>()), Lifetime.Singleton);
            builder.Register<IBattleWorldGate>(resolver => new SceneWorldGate(
                resolver.Resolve<IGameFlow>(), resolver.Resolve<ILoadingCurtain>()), Lifetime.Singleton);
            builder.Register(resolver => new BattleWorldLock(
                resolver.Resolve<IWorldPauseService>(), resolver.Resolve<IInputService>(), resolver.Resolve<IUIService>()), Lifetime.Singleton);
            builder.Register(resolver => new BattleArena(
                resolver.Resolve<ILoadingCurtain>(), resolver.Resolve<IAssetService>(), Scope(resolver)), Lifetime.Singleton);
            builder.Register(resolver => new BattleSetup(
                settings, SeedSource(resolver), resolver.Resolve<BattleItemInventory>()), Lifetime.Singleton);
            builder.Register<IBattlePresenter>(resolver => new BattleScenePresenter(
                resolver.Resolve<IUIService>(), resolver.Resolve<IInputService>(), settings, Scope(resolver)), Lifetime.Singleton);
            // AsSelf：W2 的暂停菜单 / Showcase 要按具体类型读 IsBattleRunning（RegisterEntryPoint 默认只注册接口）。
            builder.RegisterEntryPoint(resolver => new BattleFlow(
                resolver.Resolve<ISubscriber<StageEvent>>(),
                resolver.Resolve<BossRoster>(),
                resolver.Resolve<IBattleNarrative>(),
                resolver.Resolve<IBattleWorldGate>(),
                resolver.Resolve<BattleWorldLock>(),
                resolver.Resolve<BattleArena>(),
                resolver.Resolve<BattleSetup>(),
                resolver.Resolve<BattleItemInventory>(),
                resolver.TryResolve(out IBattlePresenter presenter) ? presenter : null,
                Scope(resolver)), Lifetime.Singleton).AsSelf();
        }

        private static ITelemetryScope Scope(IObjectResolver resolver) => resolver.Resolve<ITelemetryService>().Scope(TelemetryModule);

        // 会话主种子（PRP D8 的本地战斗流从它派生，见 BattleSetup 文件头）。RandomService 没按具体类型注册时退回一次性种子。
        private static Func<ulong> SeedSource(IObjectResolver resolver)
        {
            if (resolver.TryResolve(out RandomService random)) return () => random.MasterSeed;
            ulong fallback = RandomService.CreateSeedFromGuid();
            return () => fallback;
        }

        // 名册写坏不拦启动：记 Error 并退回空名册，开战时按「找不到 BOSS」报错（同 IdentityInstaller 的退路口径）。
        private BossRoster CreateRoster(BossRosterConfig roster)
        {
            try
            {
                return roster.CreateRoster();
            }
            catch (ArgumentException e)
            {
                Log.Error($"BattleInstaller：BossRosterConfig 写坏了，本次用空名册（所有战斗阶段都会因找不到 BOSS 而不开仗）：{e.Message}", this);
                return BossRoster.Empty;
            }
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 LootInstaller / IdentityInstaller）。
        private TurnBasedConfig ResolveTurnBasedConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (turnBasedConfig != null) return turnBasedConfig;
            Log.Error("BattleInstaller 的 Turn Based Config 字段没赋值，已用默认占位值顶上。"
                      + "把 Data/TurnBased/TurnBasedConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 BattleInstaller 上。", this);
            return ScriptableObject.CreateInstance<TurnBasedConfig>();
        }

        private BossRosterConfig ResolveBossRoster()
        {
            if (bossRoster != null) return bossRoster;
            Log.Error("BattleInstaller 的 Boss Roster 字段没赋值，已用空名册顶上（所有战斗阶段都会因找不到 BOSS 而不开仗）。"
                      + "把 Data/Battle/BossRosterConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 BattleInstaller 上。", this);
            return ScriptableObject.CreateInstance<BossRosterConfig>();
        }
    }
}
