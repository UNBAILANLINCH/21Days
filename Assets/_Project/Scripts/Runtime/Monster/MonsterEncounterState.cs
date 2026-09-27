// 职责：Additive 加载遭遇场景、启动逻辑并在离场时清理；个体状态留在 MonsterRules。
// 为什么新建：SceneGameState 是通用基类，不知道本模块的场景和接线组件。
// 触屏控件（原 EncounterTouchControls，代码现搭的虚拟摇杆 + 潜行 / 伪装 / 攻击）已从本状态移除：
//   PRP/exploration-whitebox 波 2 起由 Exploration HUD 预制体（OnScreenStick / OnScreenButton）提供，按 IsTouchPrimary 显隐。
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Monster
{
    public sealed class MonsterEncounterState : SceneGameState
    {
        private readonly EncounterStep step;
        private readonly Game.Player.PlayerModel player;
        private readonly MonsterModel monster;
        private readonly IGameFlow flow;
        private readonly SimulationRunner runner;
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

        public MonsterEncounterState(IAssetService assets, EncounterStep step, Game.Player.PlayerModel player,
            MonsterModel monster, IGameFlow flow, SimulationRunner runner) : base(assets)
        {
            this.runner = runner;
            this.step = step;
            this.player = player;
            this.monster = monster;
            this.flow = flow;
        }

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

            try
            {
                if (restore != null) step.Restore(restore);
                else step.Begin(view.PlayerStart, view.PatrolPositions());
                restore = null;
                view.Bind(player, monster, ReadInterpolationAlpha);
                view.OnBackClicked += HandleBackClicked;
                view.OnPlayerBlocked += step.CorrectPlayerPosition;
            }
            catch (System.Exception e)
            {
                Log.Error($"MonsterEncounter 接线失败：{e}");
                step.End();
                throw;
            }

            return UniTask.CompletedTask;
        }

        protected override UniTask OnSceneUnloadingAsync(CancellationToken ct)
        {
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
