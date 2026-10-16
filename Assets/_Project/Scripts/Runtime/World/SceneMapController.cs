// 职责：共用探索小地图 HUD；随玩家锚点与场景变化更新，不暂停世界、不接管输入。
// 为什么新建：背包控制器绑定道具数据，不能复用；扩展世界移动驱动会把移动与面板生命周期混在一起。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.UI;
using Game.Interaction;
using UnityEngine;
using VContainer.Unity;

namespace Game.World
{
    public sealed class SceneMapController : ITickable, IDisposable
    {
        private readonly IUIService ui;
        private readonly IInputService input;
        private readonly IInteractionRegistry interaction;
        private readonly IGameFlow flow;
        private readonly WorldCatalog catalog;
        private SceneMapView view;
        private GameState boundState;
        private string boundSceneKey;
        private int revision;
        private int boundRevision = -1;
        private int attemptedRevision = -1;
        private bool opening;
        private bool disposed;
        public bool IsOpen => view != null;

        public SceneMapController(IUIService ui, IInputService input, IInteractionRegistry interaction,
            IGameFlow flow, WorldCatalog catalog)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            interaction.OnActorChanged += HandleActorChanged;
        }

        // 每帧只检查已有服务引用与版本；几何扫描只在玩家锚点或场景变化时发生。
        public void Tick()
        {
            if (disposed || opening || input.Actions == null) return; // lint-ok: 只判断启动是否创建动作集，不读取设备输入
            if (!ShouldShow(interaction.Actor != null, flow.Current is SceneGameState))
            {
                if (view != null && view.IsShowing) view.ClearScene();
                return;
            }
            if (view == null)
            {
                // 同一场景加载失败只报一次，避免每帧重复加载与刷错误；换场景可重试。
                if (attemptedRevision != revision) OpenHudAsync().Forget();
                return;
            }
            string sceneKey = CurrentWorldKey();
            if (boundRevision != revision || boundState != flow.Current || boundSceneKey != sceneKey || !view.IsShowing)
                BindCurrentScene();
        }

        public static bool ShouldShow(bool hasActor, bool explorationActive) => hasActor && explorationActive;

        public async UniTask OpenAsync()
        {
            if (disposed || opening || view != null || !ShouldShow(interaction.Actor != null, flow.Current is SceneGameState)) return;
            opening = true;
            attemptedRevision = revision;
            try
            {
                SceneMapView opened = await ui.OpenAsync<SceneMapView>();
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }
                view = opened;
                BindCurrentScene();
            }
            catch
            {
                try { if (view != null) await ui.CloseAsync(view); }
                finally { view = null; }
                throw;
            }
            finally
            {
                opening = false;
            }
        }

        private string CurrentWorldKey()
        {
            return flow.Current is WorldSceneState world && world.Spawn != null ? world.Spawn.SceneKey : string.Empty;
        }

        private void BindCurrentScene()
        {
            if (!ShouldShow(interaction.Actor != null, flow.Current is SceneGameState))
            {
                view.ClearScene();
                return;
            }
            string sceneKey = CurrentWorldKey();
            string title = catalog.TryGetScene(sceneKey, out global::cfg.world.Scene scene) ? scene.DisplayName : "探索地图";
            view.SetScene(interaction.Actor.Anchor, title, catalog);
            boundSceneKey = sceneKey;
            boundState = flow.Current;
            boundRevision = revision;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            interaction.OnActorChanged -= HandleActorChanged;
            SceneMapView target = view;
            view = null;
            if (target != null) CloseDisposedAsync(target).Forget();
        }

        private void HandleActorChanged(InteractionActor actor)
        {
            revision++;
            if (view != null) view.ClearScene();
        }

        private async UniTaskVoid OpenHudAsync()
        {
            try { await OpenAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Log.Error($"小地图加载失败：{error}"); }
        }

        private async UniTaskVoid CloseDisposedAsync(SceneMapView target)
        {
            try { await ui.CloseAsync(target); }
            catch (ObjectDisposedException) { }
            catch (Exception error) { Log.Warn($"地图清理失败：{error.Message}"); }
        }
    }
}
