// 职责：Additive 加载遭遇场景、启动逻辑并在离场时清理；个体状态留在 MonsterRules。
// 为什么新建：SceneGameState 是通用基类，不知道本模块的场景和接线组件。
// 背后处决接线（2026-10-07）：场景就绪时在场景根里找 ExecutionInteractor（与找 EncounterSceneView 同一模式），
//   由本类的 BindExecution 喂进玩家 / 怪物规则 / 事实写入点 / 判定内核 / 遭遇结算 / 埋点 / 动作资产——
//   在此之前那个组件**没有任何生产调用方**（判定与结算三层齐全，但按 F 没人响应）。
//   判定内核走**可选**的 StealthKernel（容器里没有它时按占位阈值跑，由组件自己打 Warn）：
//   独立原型场景与纯 Monster 的测试作用域都没有 StealthInstaller，不许把它变成硬依赖。
//   统一交互焦点（PRP/interaction D6，F 键归属）同样按可选取：容器里有 IInteractionFocus 就传给组件，按 F 时焦点在场让位给交互。
// 触屏控件（原 EncounterTouchControls，代码现搭的虚拟摇杆 + 潜行 / 伪装 / 攻击）已从本状态移除：
//   PRP/exploration-whitebox 波 2 起由 Exploration HUD 预制体（OnScreenStick / OnScreenButton）提供，按 IsTouchPrimary 显隐。
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Interaction;
using Game.Stealth;
using UnityEngine;
using VContainer;

namespace Game.Monster
{
    public sealed class MonsterEncounterState : SceneGameState
    {
        private readonly EncounterStep step;
        private readonly Game.Player.PlayerModel player;
        private readonly MonsterModel monster;
        private readonly IGameFlow flow;
        private readonly SimulationRunner runner;
        // 输入分两路（合并两侧）：LiveInputSource 是确定性内核那条「排队选择」通路（远端 taming 波：切控制对象）；
        // IInputService 只在处决接线时借一次动作资产引用（清排队走 LiveInputSource）。
        private readonly LiveInputSource input;
        private readonly IInputService inputService;
        // 处决接线（本地 2026-10-07 波）要用到的现场依赖：怪物规则（种类与掉落）、全局配置（感知范围可视化）、埋点、容器（可选判定内核）。
        private readonly MonsterRules monsterRules;
        private readonly MonsterConfig monsterConfig;
        private readonly ITelemetryService telemetry;
        private readonly IObjectResolver container;
        private EncounterSceneView view;
        private EncounterSaveData restore;
        public bool NavigationBlocked { get; set; }

        public void PrepareRestore(EncounterSaveData saved)
        {
            if (saved == null) throw new System.ArgumentNullException(nameof(saved));
            saved.Validate();
            restore = saved;
        }
        public void ClearPreparedRestore() => restore = null;

        /// <param name="container">
        /// 只为一件事而注入：**可选**地把 <c>StealthKernel</c> 取出来（处决的判定内核）。
        /// 换成构造参数会把它变成硬依赖——独立原型场景与纯 Monster 的测试作用域都没有 StealthInstaller，
        /// 那时容器解析会直接失败（VContainer 不支持「没注册就用默认值」，见 EncounterStep 的文件头）。
        /// </param>
        public MonsterEncounterState(IAssetService assets, EncounterStep step, Game.Player.PlayerModel player,
            MonsterModel monster, IGameFlow flow, SimulationRunner runner, MonsterRules monsterRules,
            MonsterConfig monsterConfig, ITelemetryService telemetry, IObjectResolver container,
            LiveInputSource input = null, IInputService inputService = null) : base(assets)
        {
            this.runner = runner;
            this.input = input;
            this.inputService = inputService;
            this.step = step;
            this.player = player;
            this.monster = monster;
            this.flow = flow;
            this.monsterRules = monsterRules;
            this.monsterConfig = monsterConfig;
            this.telemetry = telemetry;
            this.container = container;
        }

        // 该地址目前指向 Assets/Scenes/SampleScene.unity（功能 demo 示例场景），是临时指向；
        // 正式内容落地后改 Addressables 条目指向 Assets/_Project/Scenes/ 下的正式场景，代码不用动。
        protected override string SceneKey => "IsometricEncounter";

        protected override UniTask OnSceneReadyAsync(CancellationToken ct)
        {
            GameObject[] roots = Scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && view == null; i++)
            {
                view = roots[i].GetComponentInChildren<EncounterSceneView>(true);
            }

            if (view == null)
            {
                Log.Error("IsometricEncounter 场景缺少 EncounterSceneView 显式接线");
                throw new System.InvalidOperationException("IsometricEncounter 缺少 EncounterSceneView");
            }

            // 处决交互入口：与找 EncounterSceneView 同一模式（场景根里找，含 inactive）。
            // 找不到**不算致命**（遭遇本身照跑，只是按 F 不响应），但必须留下记录：本类不抛，
            // 由 BindExecution 打 Warn——「按 F 没反应」和「功能坏了」在现场得分得开。
            ExecutionInteractor execution = null;
            for (int i = 0; i < roots.Length && execution == null; i++)
            {
                execution = roots[i].GetComponentInChildren<ExecutionInteractor>(true);
            }

            try
            {
                view.ConfigureTaming(step);
                if (restore != null) step.Restore(restore);
                else step.Begin(view.PlayerStart, view.PatrolPositions());
                restore = null;
                // 视图绑定分两步（远端 taming 波）：Bind 只给模型与插值相位，BindControl 另给驯服控制回调；
                // execution 由 Bind 的可选参数带进去，白盒面板因此多画一行「此刻能不能按 F」。
                view.Bind(player, monster, ReadInterpolationAlpha, execution);
                view.BindControl(step, RequestControl);
                // S3 视线遮挡（Q3 波接线）：把场景里显式登记的遮挡体一次性转成纯数据几何喂给潜行内核，
                // tick 路径因此只做几何求交、不做物理查询（StealthSight / EncounterSceneView 的分工）。
                // 没登记遮挡体时喂进去的是空数组 = 视线不被遮挡，判定与接线前一致。
                step.Sight.SetOccluders(view.CollectSightOccluders());
                // 感知范围可视化（白盒）：种类值在 Begin（Reset 查表）之后才拿得到，所以放在这一步之后。
                // 没拖 awarenessConfig 时它什么都不画，判定不受影响。
                view.BindAwarenessRanges(monsterRules.Kind, monsterConfig);
                view.OnBackClicked += HandleBackClicked;
                view.OnPlayerBlocked += step.CorrectPlayerPosition;
                BindExecution(execution);
            }
            catch (System.Exception e)
            {
                Log.Error($"MonsterEncounter 接线失败：{e}");
                step.End();
                throw;
            }

            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 把场景里的处决交互接上现场：玩家 / 怪物规则 / 事实写入点（<c>step.FactSink</c>）/ 判定内核 /
        /// 遭遇结算 / 埋点 / 动作资产。
        /// <para>
        /// 单独抽成公开方法（而不是写在 <see cref="OnSceneReadyAsync"/> 里）是为了让装配测试能走**同一条路**：
        /// 容器装出来的依赖 + 场景里的那个组件，调它一次即可断言接线结果——否则测试只能自己复述一遍接线参数，
        /// 那正好把「接线写错了」这件事测不出来。
        /// </para>
        /// <para>
        /// 三处刻意的取值：① 判定内核取可选的 <c>StealthKernel</c>（容器里没有就用组件的占位阈值，它会打 Warn）；
        /// ② 埋点作用域固定 <c>stealth</c>（与 <c>ExecutionResolver</c> 的事件名同一模块）；
        /// ③ 动作资产从 <see cref="IInputService"/> 拿——那是「谁持有动作图」的唯一出处，
        /// 组件 Inspector 上的字段只在「没有接线方」时兜底；
        /// ④ 统一交互焦点取可选的 <see cref="IInteractionFocus"/>（PRP/interaction D6：按 F 时屏幕上有交互提示就归交互）——
        /// 纯 Monster 的测试作用域没有 InteractionInstaller，同 StealthKernel 不许变成硬依赖。
        /// </para>
        /// </summary>
        public void BindExecution(ExecutionInteractor interactor)
        {
            if (interactor == null)
            {
                // 不带 context 对象：本类不是 UnityEngine.Object，传 this 编不过（CS1503）。
                Log.Warn("IsometricEncounter 场景里没有 ExecutionInteractor：背后按 F 处决不会生效。"
                         + "照 SampleScene 的接法在场景根物体（Encounter）上挂一个，再让本状态接线。");
                return;
            }

            // StealthKernel 是可选的（见构造参数 container 的说明）：没有它时传 null，
            // 组件会退回占位阈值并打一条 Warn，不会静默改变手感。
            StealthKernel kernel = null;
            container.TryResolve(out kernel);
            IInteractionFocus interactionFocus = null;
            container.TryResolve(out interactionFocus);

            // lint-ok 的理由：这一行取的是**动作资产引用**（交给输入层组件做按键绑定），不是设备读数——
            // 按键由 ExecutionInteractor 自己读，而处决按 PRP §2.4 明确不进确定性内核、不进回放（已知取舍）。
            GameInput actions = inputService == null ? null : inputService.Actions; // lint-ok: 只取动作资产引用，不读输入设备；处决不进确定性内核
            interactor.Configure(player, monsterRules, step.FactSink,
                kernel == null ? null : kernel.Assassination, step, telemetry.Scope("stealth"),
                actions == null ? null : actions.asset, interactionFocus);
        }

        protected override UniTask OnSceneUnloadingAsync(CancellationToken ct)
        {
            // 清排队的入口在 LiveInputSource 上（远端 taming 波的通路）；IInputService 只借动作资产，不持有队列。
            if (input != null) input.ClearQueuedSelection();
            step.End();
            if (view != null)
            {
                view.OnBackClicked -= HandleBackClicked;
                view.OnPlayerBlocked -= step.CorrectPlayerPosition;
                view.Unbind();
                view = null;
            }

            return UniTask.CompletedTask;
        }

        private void RequestControl(int slot, bool tame)
        {
            if (input != null && step.IsActive && !runner.IsPaused)
                input.QueueSelection(slot, tame ? InputCommand.ButtonTame : InputCommand.ButtonSelectControl);
        }

        // 渲染插值比例：实时模式取推进器余量 / 步长；重放（Driven）由播放器逐 tick 推进、余量恒为 0，
        // 此时直接显示当前 tick 位置（alpha = 1），与接入插值前一致；拿不到推进器同样按 1。
        private float ReadInterpolationAlpha()
        {
            if (runner == null || runner.CurrentMode != SimulationRunner.Mode.Live)
            {
                return 1f;
            }

            return EncounterProjection.InterpolationAlpha(runner.Accumulator, runner.Clock.FixedDeltaTime);
        }

        private void HandleBackClicked()
        {
            if (!NavigationBlocked) flow.GoToAsync<TitleState>().Forget();
        }
    }
}
