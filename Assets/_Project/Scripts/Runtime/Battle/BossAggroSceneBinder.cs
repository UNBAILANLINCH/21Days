// 职责：BOSS 主动发难的场景绑定，兼它看得见的外部世界（IBossAggroWorld 的真实实现）（PRP/combat-iteration D6）——
//   入口点 Start 时扫一遍已加载的场景，之后每次 SceneManager.sceneLoaded 扫新加载的那个场景：
//   ① 每个 BossEntryContext：接数值来源 / 玩家模型 / 同场景的逻辑平面投影（EncounterSceneView）——发难组件每帧要问它局势，
//      不能等到开仗那一刻（BattleInstaller.FindBossContext）才接；
//   ② 每个 BossAggro：交名册里那一行（按它的 bossId 查）与本对象（守卫、玩家位置、交互入口）。
//   时机与 NarrativeService 绑 NarrativeTrigger 同一做法（初始化时扫一遍 + sceneLoaded，NarrativeService.cs:92-96、:403-412）；
//   不在容器构建回调里找（那一刻只有 Boot 场景，pitfalls.md「容器构建回调里 FindObjectOfType 看不见玩法场景」），也不每帧找。
//
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：NarrativeService / DialogueSceneBinder 的场景扫描是它们各自的私有回调，不认识 Battle 的组件；
//      BattleInstaller.FindBossContext 是「开仗那一刻按 payload 现找一次」，而发难要在开仗之前就每帧有局势可问。
//   2. 扩展不行：BattleInstaller 是注册器，只 Register 不 Resolve、不活到运行时；BattleFlow 是流程编排，
//      加场景扫描要给它的构造添五六个与开仗无关的依赖（对白 / 演出 / 剧情 / 交互 / 暂停），动到全部流程测试，职责也说不通。
//      工程里场景绑定一律是独立的 *SceneBinder 入口点（MirrorSceneBinder / QuestSceneBinder / PerformanceSceneBinder），照此新建。
//
// SceneBinder 审计两问（battle-module-guide「SceneBinder 审计结论」）：叠加加载的战斗场景也会触发 sceneLoaded，
//   但里面没有这两种组件，扫一遍无副作用；本类**不缓存任何场景对象、不改「当前场景」**，所以卸载时无须清理
//   （组件随场景销毁，只是它们持有本对象的引用；本对象释放后 IsAvailable 为假，组件即停）。
// 每帧被读的成员（CheckGuards / IsFighting / IsWorldHiddenByBattle / TryGetPlayerPosition）只读字段与属性，零分配、零查找。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Interaction;
using Game.Monster;
using Game.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;
// 两个同名 BattleOutcome（Game.TurnBased / Game.Narrative）：剧情侧类型用别名（PRP/turnbased-battle §7 坑 4）。
using NarrativeService = Game.Narrative.NarrativeService;
using NarrativeTrigger = Game.Narrative.NarrativeTrigger;

namespace Game.Battle
{
    /// <summary>
    /// BOSS 发难的场景绑定入口点 + 外部世界。由 <c>BattleInstaller</c> 注册（入口点，AsSelf）。
    /// 守卫来源除战斗流程 / 暂停外都可空：没有对白 / 演出服务时那一道视为放行；没有剧情服务时守卫恒为「剧情不可用」= 不发难。
    /// </summary>
    public sealed class BossAggroSceneBinder : IStartable, IDisposable, IBossAggroWorld
    {
        private readonly BossRoster roster;
        private readonly BattleFlow flow;
        private readonly BattleArena arena;
        private readonly IWorldPauseService pause;
        private readonly ITelemetryScope telemetry;
        private readonly Game.Dialogue.DialogueService dialogue;
        private readonly Game.Performance.IPerformanceService performance;
        private readonly NarrativeService narrative;
        private readonly IInteractionRegistry interaction;
        private readonly MonsterConfig monsterConfig;
        private readonly PlayerModel player;
        private bool started;
        private bool disposed;

        /// <param name="dialogue">对白服务（守卫「对白中」）；可空 = 这一道放行。</param>
        /// <param name="performance">演出服务（守卫「演出中」）；可空 = 这一道放行。</param>
        /// <param name="narrative">剧情服务（守卫「剧情忙」）；可空 = 恒为「剧情不可用」，不发难。</param>
        /// <param name="interaction">交互登记表（取玩家锚点）；可空 = 找不到玩家，不冲。</param>
        /// <param name="monsterConfig">BOSS 局势的数值来源；与 <paramref name="player"/> 缺一则不给局势接配置（发难组件判「局势没接上」不武装）。</param>
        /// <param name="player">玩家模型（局势按它的逻辑位置判档位）。</param>
        public BossAggroSceneBinder(BossRoster roster, BattleFlow flow, BattleArena arena, IWorldPauseService pause,
            ITelemetryScope telemetry, Game.Dialogue.DialogueService dialogue = null,
            Game.Performance.IPerformanceService performance = null, NarrativeService narrative = null,
            IInteractionRegistry interaction = null, MonsterConfig monsterConfig = null, PlayerModel player = null)
        {
            this.roster = roster ?? throw new ArgumentNullException(nameof(roster));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.arena = arena ?? throw new ArgumentNullException(nameof(arena));
            this.pause = pause ?? throw new ArgumentNullException(nameof(pause));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            this.dialogue = dialogue;
            this.performance = performance;
            this.narrative = narrative;
            this.interaction = interaction;
            this.monsterConfig = monsterConfig;
            this.player = player;
        }

        /// <inheritdoc />
        public bool IsAvailable => !disposed;

        /// <inheritdoc />
        public bool IsWorldHiddenByBattle => arena.IsLoaded;

        /// <summary>入口点：先扫已加载的场景（幂等，只订阅一次），再订阅之后的场景加载。</summary>
        public void Start()
        {
            if (started || disposed)
            {
                return;
            }

            started = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) BindScene(scene);
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (started) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// 绑一个场景里的全部 BOSS 局势与发难组件（含未激活的：被击败而隐藏的 BOSS 读档后可能重新显示）。
        /// 场景加载回调与 <see cref="Start"/> 都走这里；返回绑到的发难组件个数。已释放或场景没加载时什么都不做。
        /// </summary>
        public int BindScene(Scene scene)
        {
            if (disposed || !scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            EncounterSceneView projection = FindProjection(roots);
            for (int i = 0; i < roots.Length; i++)
            {
                foreach (BossEntryContext context in roots[i].GetComponentsInChildren<BossEntryContext>(true))
                {
                    ConfigureContext(context, projection);
                }
            }

            // 局势先接好再绑发难：发难组件绑定时要看局势是否已就绪，决定武不武装。
            int bound = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                foreach (BossAggro aggro in roots[i].GetComponentsInChildren<BossAggro>(true))
                {
                    BossDefinition definition = roster.TryGet(aggro.BossId, out BossDefinition found) ? found : null;
                    aggro.Bind(this, definition, telemetry);
                    bound++;
                }
            }

            return bound;
        }

        /// <inheritdoc />
        public BossAggroGuard CheckGuards()
        {
            if (dialogue != null && dialogue.IsRunning) return BossAggroGuard.Dialogue;
            if (performance != null && performance.IsRunning) return BossAggroGuard.Performance;
            if (flow.IsBattleRunning) return BossAggroGuard.Battle;
            if (pause.IsPaused) return BossAggroGuard.WorldPaused;
            if (narrative == null || !narrative.IsReady) return BossAggroGuard.NarrativeUnavailable;
            if (narrative.IsBusy) return BossAggroGuard.NarrativeBusy;
            return BossAggroGuard.None;
        }

        /// <inheritdoc />
        public bool IsFighting(string bossId) => flow.IsFightingBoss(bossId);

        /// <inheritdoc />
        public bool TryGetPlayerPosition(out Vector3 position)
        {
            // InteractionActor 是 UnityEngine.Object，判空只用 ==（玩家所在场景卸载后成伪空）。
            // 玩家倒地（生命归零）也当「没有可追的玩家」：BOSS 不对倒地的人警觉 / 冲刺（剧情条件「玩家存活」也会拒掉那次交互）。
            InteractionActor actor = interaction != null ? interaction.Actor : null;
            if (actor == null || (player != null && !player.Snapshot.IsAlive))
            {
                position = default;
                return false;
            }

            position = actor.Anchor.position;
            return true;
        }

        /// <inheritdoc />
        public UniTask<bool> InteractAsync(NarrativeTrigger trigger, CancellationToken ct)
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));
            return trigger.InteractAsync(ct);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene(scene);

        private void ConfigureContext(BossEntryContext context, EncounterSceneView projection)
        {
            context.UseProjection(projection);
            context.UseTelemetry(telemetry);
            // MonsterConfig 是 UnityEngine.Object，判空只用 !=。缺任意一个就不接（局势不生效，与开仗时的退路同口径）。
            if (monsterConfig != null && player != null)
            {
                context.Configure(monsterConfig, player);
            }
        }

        // 同场景的投影约定（XZ / XY 由 EncounterSceneView 决定，全工程只有它一处，同 MirrorSceneBinder）；没有返回 null。
        private static EncounterSceneView FindProjection(GameObject[] roots)
        {
            for (int i = 0; i < roots.Length; i++)
            {
                EncounterSceneView view = roots[i].GetComponentInChildren<EncounterSceneView>(true);
                if (view != null) return view;
            }

            return null;
        }
    }
}
