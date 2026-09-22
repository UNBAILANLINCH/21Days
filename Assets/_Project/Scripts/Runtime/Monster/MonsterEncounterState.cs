// 职责：Additive 加载遭遇场景、启动逻辑并在离场时清理；个体状态留在 MonsterRules。
// 为什么新建：SceneGameState 是通用基类，不知道本模块的场景和接线组件。
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Platform;
using UnityEngine;

namespace Game.Monster
{
    public sealed class MonsterEncounterState : SceneGameState
    {
        private readonly EncounterStep step;
        private readonly Game.Player.PlayerModel player;
        private readonly MonsterModel monster;
        private readonly IGameFlow flow;
        private readonly IPlatformService platform;
        private EncounterSceneView view;
        private GameObject touchControls;
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
            MonsterModel monster, IGameFlow flow, IPlatformService platform) : base(assets)
        {
            this.step = step;
            this.player = player;
            this.monster = monster;
            this.flow = flow;
            this.platform = platform;
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
                view.Bind(player, monster);
                view.OnBackClicked += HandleBackClicked;
                if (platform.IsTouchPrimary)
                {
                    touchControls = EncounterTouchControls.Create();
                }
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
                view.Unbind();
                view = null;
            }

            if (touchControls != null)
            {
                Object.Destroy(touchControls);
                touchControls = null;
            }

            return UniTask.CompletedTask;
        }

        private void HandleBackClicked()
        {
            if (!NavigationBlocked) flow.GoToAsync<TitleState>().Forget();
        }
    }
}
