// 职责：战斗场景的进出——黑幕落 → 叠加加载 BattleArena → 表现开场 → 黑幕揭；收场对称，幕下还原世界、
//   跑完调用方给的揭幕前一步（BattleFlow 的剧情回写）再揭幕（PRP D4 / D6）。
//   开场失败也是「幕下先还原世界、再揭幕」：原先先揭幕、调用方在外层 finally 才还原，中间会露出一小段被暂停的世界画面。
//   不切 ActiveScene（D4：否则别的模块 Instantiate 会落进战斗场景、跟着被卸载）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：GameFlow 的黑幕与切场景是「整状态切换」（Single 加载、Exit / Enter 状态），战斗要的是不离开世界的叠加加载。
//   2. 扩展不行：给 GameFlow / SceneGameState 加叠加加载会让框架层认识战斗时序；这里只用 ILoadingCurtain 与
//      IAssetService 两个公开契约组合出来。
// 场景缺失（W2 之前 BattleArena.unity 还不存在）时抛带地址与修法的 InvalidOperationException，黑幕照样揭开，不吞错。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.TurnBased;
using UnityEngine.SceneManagement;

namespace Game.Battle
{
    /// <summary>战斗场景宿主（根作用域单例）。一次只容一场：上一场没 <see cref="ExitAsync"/> 前再 Enter 会抛。</summary>
    public sealed class BattleArena
    {
        /// <summary>战斗场景的 Addressables 地址（W2 建 Assets/_Project/Scenes/BattleArena.unity 并登记进 AssetGroups/Scenes.asset）。</summary>
        public const string SceneKey = "BattleArena";

        private readonly ILoadingCurtain curtain;
        private readonly IAssetService assets;
        private readonly ITelemetryScope telemetry;
        private SceneHandle scene;

        public BattleArena(ILoadingCurtain curtain, IAssetService assets, ITelemetryScope telemetry)
        {
            this.curtain = curtain ?? throw new ArgumentNullException(nameof(curtain));
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>战斗场景是否在场（加载成功、还没卸载）。</summary>
        public bool IsLoaded => scene != null && !scene.IsDisposed;

        /// <summary>
        /// 开场：黑幕落 → 叠加加载 <see cref="SceneKey"/> → <see cref="IBattlePresenter.OpenAsync"/> → 黑幕揭。
        /// 任何一步失败（含取消）：已开场的表现会被 CloseAsync、场景卸载、<paramref name="restoreOnFailure"/>（恢复世界）在黑幕下执行，
        /// **然后才揭幕**（与 <see cref="ExitAsync"/> 同序），再把异常原样（场景缺失时包一层说明）抛出。成功时不调它。
        /// </summary>
        /// <param name="restoreOnFailure">开场失败时在黑幕下恢复世界（BattleFlow 传世界锁的 Dispose）；它自己再抛只留痕，不盖掉原始异常。</param>
        public async UniTask EnterAsync(IBattlePresenter presenter, BossDefinition boss, BattleSession session, Action restoreOnFailure,
            CancellationToken ct)
        {
            if (presenter == null) throw new ArgumentNullException(nameof(presenter));
            if (IsLoaded) throw new InvalidOperationException("战斗场景已经在场：上一场没有走 BattleArena.ExitAsync");
            bool opened = false;
            try
            {
                await curtain.CoverAsync(ct);
                scene = await LoadAsync(ct);
                opened = true; // 从这里起 OpenAsync 可能已做了一半，失败也要 CloseAsync（IBattlePresenter 的契约）
                await presenter.OpenAsync(new BattleOpening(boss, session, scene.Scene), ct);
            }
            catch
            {
                if (opened) await ClosePresenterQuietlyAsync(presenter);
                UnloadScene();
                RestoreQuietly(restoreOnFailure);
                throw;
            }
            finally
            {
                await RevealQuietlyAsync();
            }
        }

        /// <summary>
        /// 收场：黑幕落 → <see cref="IBattlePresenter.CloseAsync"/> → 卸载场景 → <paramref name="whileCovered"/>（恢复世界）
        /// → <paramref name="beforeReveal"/>（剧情回写：BOSS 在幕下退场）→ 黑幕揭。
        /// 每一步都容错，保证场景卸掉、黑幕揭开：关表现 / 落幕失败只记日志；<paramref name="whileCovered"/> 或
        /// <paramref name="beforeReveal"/> 抛异常时先揭幕，再把异常原样抛给调用方（前者抛了就不再跑后者）。
        /// 不在场时不碰黑幕，依次执行两个回调。
        /// </summary>
        public async UniTask ExitAsync(IBattlePresenter presenter, Action whileCovered = null, Func<UniTask> beforeReveal = null)
        {
            if (!IsLoaded)
            {
                whileCovered?.Invoke();
                if (beforeReveal != null) await beforeReveal();
                return;
            }

            try
            {
                await curtain.CoverAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                Log.Warn($"BattleArena：收场落幕失败，直接收场：{e.Message}");
            }

            try
            {
                if (presenter != null) await ClosePresenterQuietlyAsync(presenter);
            }
            finally
            {
                UnloadScene();
                try
                {
                    whileCovered?.Invoke();
                    if (beforeReveal != null) await beforeReveal();
                }
                finally
                {
                    await RevealQuietlyAsync();
                }
            }
        }

        private async UniTask<SceneHandle> LoadAsync(CancellationToken ct)
        {
            try
            {
                SceneHandle handle = await assets.LoadSceneAsync(SceneKey, LoadSceneMode.Additive, ct);
                telemetry.Track("arena_loaded", ("key", SceneKey));
                return handle;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                telemetry.TrackError("arena_load_failed", e, TelemetryProps.Of(("key", SceneKey)));
                throw new InvalidOperationException(
                    $"战斗场景「{SceneKey}」叠加加载失败：Addressables 里没有这个地址，或场景还没建。" +
                    "修法：建 Assets/_Project/Scenes/BattleArena.unity，并登记进 AssetGroups/Scenes.asset（地址 = BattleArena，照 c72e3a0）。" +
                    "原始错误：" + e.Message, e);
            }
        }

        private void UnloadScene()
        {
            if (scene == null) return;
            SceneHandle handle = scene;
            scene = null;
            handle.Dispose();
        }

        // 开场失败路径上的恢复世界：再抛也只记日志，原始的开场异常照样抛给调用方（BattleFlow 外层 finally 还会幂等地再还原一次）。
        private void RestoreQuietly(Action restore)
        {
            if (restore == null) return;
            try
            {
                restore();
            }
            catch (Exception e)
            {
                Log.Error($"BattleArena：开场失败后恢复世界也失败了：{e}");
                telemetry.TrackError("restore_failed", e);
            }
        }

        private async UniTask ClosePresenterQuietlyAsync(IBattlePresenter presenter)
        {
            try
            {
                await presenter.CloseAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                Log.Error($"BattleArena：战斗表现收场失败（场景照样卸载）：{e}");
                telemetry.TrackError("presenter_close_failed", e);
            }
        }

        private async UniTask RevealQuietlyAsync()
        {
            try
            {
                await curtain.RevealAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                // ILoadingCurtain 契约：出错也会把黑幕硬收掉再抛，这里只留痕。
                Log.Warn($"BattleArena：揭幕失败：{e.Message}");
            }
        }
    }
}
