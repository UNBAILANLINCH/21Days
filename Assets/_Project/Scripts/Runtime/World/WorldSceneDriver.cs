// 职责：世界场景的驱动——把机制层的「传送点 → 待处理转场 → 世界场景状态」这一条链接起来，
//   并让玩家在灰盒世界里真的能走：逻辑 tick 里按输入推进玩家，渲染帧里把逻辑位置投影到场景根物体、
//   跑传送点的范围判定与交互键、发起转场。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：机制波交付的 PortalAnchor 只「发一个事件就结束」（它的类注释明说：交互键不由组件读、
//      不做转场、调用方的事），而工程里**没有任何调用方**——PRP §1.2 第 3 条就是这个缺口。
//   2. 扩展不行：把这一坨塞回 PortalAnchor 会推翻它的契约（读输入 + 认识 GameFlow + 逐帧测距），
//      而那个契约正是它能在 EditMode 里直接 AddComponent 测出来的原因。
//      塞进 WorldSceneState 也不行：状态是流程层的一次性进入逻辑，没有每帧 / 每 tick 的驱动位。
//   3. 所以照 SupplyCrateFocus（逐帧读交互键的 ITickable）+ MonsterInstaller 的 AddStep（固定 tick 的玩法步）
//      的形状新建一个驱动。
//
// **为什么一个类同时实现 ITickable 与 ISimulationStep**：这两条时间线在世界场景里是同一件事的两半——
//   逻辑 tick 推玩家（确定性内核，读 InputCommand，回放可复现），渲染帧投影与传送点判定（表现层，读输入动作）。
//   拆成两个类会各自持有一份 WorldSceneBinder 与玩家引用，反而更容易把「谁是权威位置」搞乱。
//   两条路径各自读自己的时间源，不互相调用。
//
// 逐 tick 推进玩家为什么不用 Monster 的 EncounterStep：那条路会连怪物与潜行结算一起激活
//   （EncounterStep 是 Monster 模块的遭遇结算），而世界场景只该推玩家。两条路各推各的玩家会变成速度翻倍，
//   所以本类用「场景里有没有 SpawnAnchor」把自己与遭遇场景区分开（判据见 WorldSceneBinder 的文件头注释）。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Game.World
{
    /// <summary>
    /// 世界场景驱动（根作用域单例）。
    /// <list type="bullet">
    /// <item><b>ISimulationStep.Step</b>：世界场景在时按 <c>InputCommand</c> 推进玩家位置（走 <see cref="PlayerRules"/>，
    /// 不重写规则）；不在时一步都不推，所以不与遭遇原型场景的 <c>EncounterStep</c> 抢玩家。</item>
    /// <item><b>ITickable.Tick</b>：把 <see cref="PlayerModel.Position"/> 投影到玩家根物体、
    /// 跑传送点范围判定与交互键，触发时写待处理转场并请流程切到 <see cref="WorldSceneState"/>。</item>
    /// </list>
    /// </summary>
    public sealed class WorldSceneDriver : IStartable, ITickable, ISimulationStep, IDisposable
    {
        /// <summary>埋点模块名：与 <see cref="WorldInstaller"/> 用的是同一个。</summary>
        private const string TelemetryModule = "world";

        private readonly WorldSceneBinder binder;
        private readonly WorldCatalog catalog;
        private readonly IWorldTransition transition;
        private readonly IGameFlow flow;
        private readonly IInputService input;
        private readonly PlayerModel player;
        private readonly PlayerRules playerRules;
        private readonly SimulationRunner runner;
        private readonly ITelemetryScope telemetry;

        // 已经订阅过 OnTriggered 的那一批传送点。传送点随场景加载 / 卸载换人，靠 binder.PortalsVersion 判断要不要重订。
        private readonly System.Collections.Generic.List<PortalAnchor> subscribed =
            new System.Collections.Generic.List<PortalAnchor>();

        private int subscribedVersion = -1;
        private bool transitioning;
        private bool disposed;

        public WorldSceneDriver(WorldSceneBinder binder, WorldCatalog catalog, IWorldTransition transition,
            IGameFlow flow, IInputService input, PlayerModel player, PlayerRules playerRules, SimulationRunner runner,
            ITelemetryService telemetry)
        {
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.transition = transition ?? throw new ArgumentNullException(nameof(transition));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.playerRules = playerRules ?? throw new ArgumentNullException(nameof(playerRules));
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.telemetry = telemetry == null ? NullTelemetryScope.Instance : telemetry.Scope(TelemetryModule);
        }

        /// <summary>
        /// 把自己挂进确定性内核的步骤表（<see cref="Step"/> 才会被调到）。
        /// <para>
        /// 用 <see cref="IStartable"/> 而不是 <c>RegisterBuildCallback</c>：容器构建回调在**建容器那一刻**解析本类，
        /// 而那会要求容器里已经有 Player / Input / Flow 全套（装配测试只装框架服务就建不起来）。
        /// 开局装配是启动期一次性动作，走 <c>Start</c> 时机不早不晚，也不改变任何初始化串行。
        /// </para>
        /// </summary>
        public void Start() => runner.AddStep(this);

        /// <summary>当前有没有世界场景在跑（测试与调试读）。</summary>
        public bool InWorldScene => binder.HasWorldScene;

        /// <summary>正在转场（已经写了待处理转场、还没等流程切完）。这期间不再收新的传送点触发。</summary>
        public bool IsTransitioning => transitioning;

        /// <summary>
        /// 逻辑 tick：世界场景在时按这一 tick 的输入推进玩家。不在时什么都不做——
        /// 「这一步属不属于我」的判据只此一处，避免与遭遇原型路径抢同一个玩家。
        /// </summary>
        public void Step(in SimulationContext context)
        {
            if (disposed || !binder.HasWorldScene)
            {
                return;
            }

            InputCommand command = context.Input;
            var intent = new PlayerIntent(
                command.Axis0,
                command.HasButton(InputCommand.ButtonSneak),
                command.HasButton(InputCommand.ButtonDisguise),
                command.HasButton(InputCommand.ButtonAttack),
                command.HasButton(InputCommand.ButtonRun));
            playerRules.Step(in intent, context.DeltaTime);
        }

        /// <summary>渲染帧：投影玩家位置 → 传送点范围与交互键 → 触发时发起转场。</summary>
        public void Tick()
        {
            if (disposed)
            {
                return;
            }

            // 订阅同步放在最前面：场景卸载时（HasWorldScene 已经为假）也要把旧传送点退订掉。
            SyncPortalSubscriptions();
            if (!binder.HasWorldScene)
            {
                return;
            }

            ProjectPlayer();
            UpdatePortals();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            UnsubscribeAll();
            subscribedVersion = -1;
        }

        // 把逻辑位置投影到场景根物体：逻辑平面是 XZ（世界 x → 逻辑 x，世界 z → 逻辑 y），高度保留场景里摆的。
        // 每帧跑，但没有分配（Vector3 是结构体），也不做任何查找。
        private void ProjectPlayer()
        {
            Transform root = binder.PlayerRoot;
            if (root == null)
            {
                return;
            }

            Vector2 position = player.Position;
            float y = root.position.y;
            root.position = new Vector3(position.x, y, position.y);
        }

        // 传送点范围判定：纯距离（不查物理，同 PortalAnchor 的契约）。进范围叫 NotifyEntered（进入即触发的那一种当场就走），
        // 出范围叫 NotifyExited。交互键的那一种在下面单独读一次动作。
        private void UpdatePortals()
        {
            System.Collections.Generic.IReadOnlyList<PortalAnchor> portals = binder.Portals;
            if (portals.Count == 0)
            {
                return;
            }

            Vector2 playerPosition = player.Position;
            for (int i = 0; i < portals.Count; i++)
            {
                PortalAnchor portal = portals[i];
                if (portal == null)
                {
                    continue;
                }

                Vector3 world = portal.transform.position;
                var portalPosition = new Vector2(world.x, world.z);
                float radius = portal.TriggerRadius;
                bool inRange = (playerPosition - portalPosition).sqrMagnitude <= radius * radius;
                if (inRange)
                {
                    portal.NotifyEntered();
                }
                else
                {
                    portal.NotifyExited();
                }
            }

            // 交互键不属于确定性模拟（不进逻辑帧、不影响回放），同 SupplyCrateFocus / DialogueInteractionFocus 读同一路动作。
            // 动作集可能晚于入口点就绪，每帧判空容错。
            // 确定性那一路走的是 Step 里的 context.Input（InputCommand），两条路各自读自己的时间源。
            GameInput actions = input.Actions; // lint-ok: 传送点触发不属于确定性模拟，同 SupplyCrateFocus 读动作表
            if (actions == null || !actions.Gameplay.Interact.WasPressedThisFrame()) // lint-ok: 传送点触发不属于确定性模拟，同 SupplyCrateFocus 读 Interact 动作
            {
                return;
            }

            for (int i = 0; i < portals.Count; i++)
            {
                PortalAnchor portal = portals[i];
                if (portal != null && portal.PlayerInRange && portal.TriggerKind == PortalTriggerKind.Interact
                    && portal.TryInteract())
                {
                    // 一次只处理一个：TryInteract 会同步走 OnTriggered，本帧不再看别的出口。
                    return;
                }
            }
        }

        // 传送点随场景换人，所以订阅要跟着 binder 的登记表走。binder.PortalsVersion 变了才重订，
        // 每帧只做一次 int 比较。
        private void SyncPortalSubscriptions()
        {
            if (binder.PortalsVersion == subscribedVersion)
            {
                return;
            }

            subscribedVersion = binder.PortalsVersion;
            UnsubscribeAll();

            System.Collections.Generic.IReadOnlyList<PortalAnchor> portals = binder.Portals;
            for (int i = 0; i < portals.Count; i++)
            {
                PortalAnchor portal = portals[i];
                if (portal == null)
                {
                    continue;
                }

                portal.OnTriggered += HandleTriggered;
                subscribed.Add(portal);
            }
        }

        private void UnsubscribeAll()
        {
            for (int i = 0; i < subscribed.Count; i++)
            {
                PortalAnchor portal = subscribed[i];
                if (portal != null)
                {
                    portal.OnTriggered -= HandleTriggered;
                }
            }

            subscribed.Clear();
        }

        /// <summary>
        /// 传送点触发：写一条待处理转场，然后请流程切到 <see cref="WorldSceneState"/>。
        /// 黑幕由 <c>GameFlow</c> 统一落 / 揭（E4 已做），这里**不再叠一层**。
        /// </summary>
        private void HandleTriggered(PortalAnchor portal)
        {
            if (disposed || transitioning || portal == null)
            {
                return;
            }

            WorldTransitionRequest request = BuildRequest(portal);
            transitioning = true;
            transition.Request(request);
            telemetry.Track("portal_triggered",
                ("portal", request.PortalId),
                ("kind", portal.TriggerKind.ToString()),
                ("to", request.SceneKey));

            GoToWorldAsync().Forget();
        }

        /// <summary>
        /// 拼一次转场请求。目标场景与目标出生点取**场景侧**那两个字段（PortalAnchor 的接线）；
        /// <c>portal_id</c> 与 <c>arrival_method</c> 只有表里有（组件上不带这两项），按 <c>anchor_id</c> 回查 TbPortal。
        /// 回查不到时**点名报出来**（那是「场景摆了出口但表里没有对应行」的接线错），再按不带来源的方式继续。
        /// </summary>
        private WorldTransitionRequest BuildRequest(PortalAnchor portal)
        {
            string portalId = string.Empty;
            string arrival = null;

            System.Collections.Generic.IReadOnlyList<global::cfg.world.Scene> scenes = catalog.AllScenes;
            for (int i = 0; i < scenes.Count && string.IsNullOrEmpty(portalId); i++)
            {
                System.Collections.Generic.IReadOnlyList<global::cfg.world.Portal> rows = catalog.PortalsOf(scenes[i].SceneKey);
                for (int r = 0; r < rows.Count; r++)
                {
                    global::cfg.world.Portal row = rows[r];
                    if (string.Equals(row.AnchorId, portal.AnchorId, StringComparison.Ordinal)
                        && string.Equals(row.TargetScene, portal.TargetSceneKey, StringComparison.Ordinal))
                    {
                        portalId = row.PortalId;
                        arrival = row.ArrivalMethod;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(portalId))
            {
                Log.Warn($"WorldSceneDriver：场景里的传送点「{portal.name}」（anchor_id「{portal.AnchorId}」→「{portal.TargetSceneKey}」）"
                         + "在 TbPortal 里没有对应行，本次转场不带 portal_id、到达方式按默认。"
                         + "表见 Tables/Defines/world.xml 的 TbPortal，数据在 Tables/Data/world/portal/。");
            }

            return new WorldTransitionRequest(portal.TargetSceneKey, portal.TargetSpawnId, arrival, portalId);
        }

        private async UniTaskVoid GoToWorldAsync()
        {
            try
            {
                await flow.GoToAsync<WorldSceneState>();
            }
            catch (Exception e)
            {
                // 只能 Warn：这条路上报 Error 会被回放框架当未预期异常计数；失败原因已经由 GameFlow 打了 Error。
                Log.Warn($"WorldSceneDriver：切到世界场景失败：{e.GetType().Name}：{e.Message}");
            }
            finally
            {
                transitioning = false;
            }
        }
    }
}
