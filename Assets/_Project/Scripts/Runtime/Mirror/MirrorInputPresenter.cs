// 职责：照镜输入与结果画面——读 Gameplay/Mirror、Gameplay/MirrorSelf 按下沿；对白中 / 世界暂停 / 沉浸 / 结果画面开着 / 冷却内让位并埋 mirror_blocked；
//   否则调 MirrorService 判定，组结果内容（真形图经 IAssetService 按地址加载）后打开结果画面，显示期间持世界暂停令牌；
//   确认键或停留时间（真实时间）到了关闭，关闭时释放令牌与图片句柄。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：SupplyCrateFocus 只认物资箱焦点与 Interact 键；DialogueInteractionFocus 只驱动对白。
//   2. 扩展不行：塞进 MirrorService 会让门面认识输入、UI 与资源加载，服务就没法在 EditMode 里脱离这些测试。
//   照镜不属于确定性模拟（同 Loot 的开箱，PRP/mirror-core 2.3「不改内核」），所以直接读动作按下沿。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Monster;
using UnityEngine;
using VContainer.Unity;

namespace Game.Mirror
{
    /// <summary>
    /// 照镜入口点。只在遭遇活动（<see cref="EncounterStep.IsActive"/>）时响应；标题页、镜碎后、场景切换途中按键静默忽略。
    /// 结果画面同一时刻只有一份；冷却从上一次照镜 / 自照起算（真实时间，世界暂停时也走）。
    /// </summary>
    public sealed class MirrorInputPresenter : ITickable, IDisposable
    {
        private const string ReasonDialogue = "dialogue";
        private const string ReasonPaused = "paused";
        private const string ReasonHudHidden = "hud_hidden";
        private const string ReasonCooldown = "cooldown";
        private const string ReasonViewOpen = "view_open";

        private readonly MirrorService service;
        private readonly MirrorConfig config;
        private readonly EncounterStep step;
        private readonly DialogueService dialogue;
        private readonly IWorldPauseService worldPause;
        private readonly IHudVisibility hudVisibility;
        private readonly IInputService input;
        private readonly IUIService ui;
        private readonly IAssetService assets;
        private readonly IClock clock;
        private readonly ITelemetryScope telemetry;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private MirrorResultView view;
        private IDisposable pauseToken;
        private AssetHandle<Sprite> imageHandle;
        private bool showing;
        private bool closeRequested;
        private float shownAt;
        private float lastCastAt = float.NegativeInfinity;
        private bool disposed;

        public MirrorInputPresenter(MirrorService service, MirrorConfig config, EncounterStep step, DialogueService dialogue,
            IWorldPauseService worldPause, IHudVisibility hudVisibility, IInputService input, IUIService ui,
            IAssetService assets, IClock clock, ITelemetryScope telemetry)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            // MirrorConfig 是 ScriptableObject，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.step = step ?? throw new ArgumentNullException(nameof(step));
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.worldPause = worldPause ?? throw new ArgumentNullException(nameof(worldPause));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>结果画面是否在显示（含打开途中）。</summary>
        public bool IsShowing => showing;

        /// <summary>
        /// 让位原因：结果画面开着 → view_open；对白进行中 → dialogue；世界暂停 → paused；沉浸 → hud_hidden；冷却内 → cooldown；
        /// 都不是返回 null。按此顺序取第一条。纯函数，供 Tick 与测试共用。
        /// </summary>
        public static string BlockReason(bool viewOpen, bool dialogueRunning, bool worldPaused, bool hudHidden,
            float sinceLastCast, float cooldown)
        {
            if (viewOpen) return ReasonViewOpen;
            if (dialogueRunning) return ReasonDialogue;
            if (worldPaused) return ReasonPaused;
            if (hudHidden) return ReasonHudHidden;
            if (sinceLastCast < cooldown) return ReasonCooldown;
            return null;
        }

        /// <summary>结果画面该不该关：确认键按下，或停留满 <paramref name="seconds"/>（真实时间；≤ 0 表示只能按键关）。纯函数。</summary>
        public static bool ShouldCloseResult(float elapsed, float seconds, bool confirmPressed) =>
            confirmPressed || (seconds > 0f && elapsed >= seconds);

        public void Tick()
        {
            if (disposed) return;
            // 动作集可能晚于入口点就绪，每帧判空容错（同 SupplyCrateFocus）。
            GameInput actions = input.Actions; // lint-ok: 照镜不属于确定性模拟，同 SupplyCrateFocus 读 Interact 动作
            if (actions == null) return;

            bool castPressed = actions.Gameplay.Mirror.WasPressedThisFrame();
            bool selfPressed = actions.Gameplay.MirrorSelf.WasPressedThisFrame();

            if (showing)
            {
                // 已发起关闭（淡出途中）就不再重复关。
                if (view != null && !closeRequested)
                {
                    bool confirm = actions.UI.Submit.WasPressedThisFrame() || actions.Gameplay.Confirm.WasPressedThisFrame();
                    if (ShouldCloseResult(clock.UnscaledTime - shownAt, config.ResultSeconds, confirm)) CloseResult();
                }

                if (castPressed || selfPressed) TrackBlocked(ReasonViewOpen, selfPressed);
                return;
            }

            if (!castPressed && !selfPressed) return;
            // 不在遭遇里（标题、镜碎后、切场景途中）静默忽略，不算「被挡」。
            if (!step.IsActive) return;

            float now = clock.UnscaledTime;
            string reason = BlockReason(false, dialogue.IsRunning, worldPause.IsPaused, hudVisibility.IsHudHidden,
                now - lastCastAt, config.CastCooldown);
            if (reason != null)
            {
                TrackBlocked(reason, selfPressed);
                return;
            }

            lastCastAt = now;
            // 同一帧两个键都按：自照优先（结果恒定、不写辨认记录以外的东西）。
            MirrorResult result = selfPressed ? service.LookSelf() : service.Cast();
            MirrorSubject subject = selfPressed ? null : service.LastSubject;
            ShowAsync(result, subject).Forget();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            if (view != null)
            {
                view.OnClosed -= HandleViewClosed;
                view = null;
            }

            Finish();
        }

        private void TrackBlocked(string reason, bool self)
        {
            telemetry.Track("mirror_blocked", ("reason", reason), ("self", self));
        }

        // 时序：持暂停令牌 → 组内容（真形 / 模糊按地址加载真形图）→ 开结果画面 → 记开画时刻；
        //   关闭走 CloseResult（到时 / 确认键）或返回键被路由关掉，二者都经 OnClosed 回到 Finish。
        private async UniTaskVoid ShowAsync(MirrorResult result, MirrorSubject subject)
        {
            showing = true;
            closeRequested = false;
            pauseToken = worldPause.Acquire(this);
            CancellationToken ct = lifetime.Token;
            try
            {
                MirrorResultInfo info = await ComposeAsync(result, subject, ct);
                if (disposed) return;
                MirrorResultView opened = await ui.OpenAsync<MirrorResultView>(info, ct);
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }

                view = opened;
                view.OnClosed -= HandleViewClosed;
                view.OnClosed += HandleViewClosed;
                shownAt = clock.UnscaledTime;
            }
            catch (OperationCanceledException)
            {
                Finish();
            }
            catch (ObjectDisposedException)
            {
                // 作用域销毁途中 UIService 已释放，静默。
                Finish();
            }
            catch (Exception e)
            {
                // 结果画面开不出来：判定与辨认记录已经写好，只是看不到；释放令牌，不让世界卡在暂停里。
                telemetry.TrackError("mirror_view_failed", e);
                Log.Error($"MirrorInputPresenter：打开照镜结果画面失败：{e}");
                Finish();
            }
        }

        private async UniTask<MirrorResultInfo> ComposeAsync(MirrorResult result, MirrorSubject subject, CancellationToken ct)
        {
            string subjectName = null;
            Sprite portrait = null;
            // MirrorSubject 是 UnityEngine.Object，判空只用 != null。
            if (subject != null)
            {
                subjectName = subject.DisplayName;
                portrait = subject.Portrait;
            }

            string trueName = null;
            string trueDesc = null;
            string imageKey = null;
            if (result.Kind == MirrorResultKind.TrueForm || result.Kind == MirrorResultKind.Blurry)
            {
                if (service.TryGetYao(result.YaoId, out global::cfg.yao.Yao yao))
                {
                    trueName = yao.TrueName;
                    trueDesc = yao.TrueDesc;
                    imageKey = yao.TrueImage;
                }
            }

            MirrorResultInfo info = MirrorResultInfo.Compose(result.Kind, config, subjectName, trueName, trueDesc);
            if (result.Kind == MirrorResultKind.Human || result.Kind == MirrorResultKind.Object) return info.WithImage(portrait);
            if (string.IsNullOrEmpty(imageKey)) return info;

            try
            {
                imageHandle = await assets.LoadAsync<Sprite>(imageKey, ct);
                return info.WithImage(imageHandle == null ? null : imageHandle.Asset);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // 加载失败：记遥测、显示无图，文案照常。
                telemetry.TrackWarn("mirror_image_failed",
                    TelemetryProps.Of(("key", imageKey), ("yao_id", result.YaoId), ("error", e.Message)));
                return info;
            }
        }

        private void CloseResult()
        {
            MirrorResultView closing = view;
            if (closing == null || closeRequested) return;
            closeRequested = true;
            CloseQuietlyAsync(closing).Forget();
        }

        private async UniTaskVoid CloseQuietlyAsync(MirrorResultView closing)
        {
            try
            {
                await ui.CloseAsync(closing);
            }
            catch (Exception e)
            {
                Log.Warn($"MirrorInputPresenter：关闭照镜结果画面失败：{e.Message}");
                // 关闭失败也要放开世界，不然卡在暂停里。
                if (ReferenceEquals(view, closing))
                {
                    closing.OnClosed -= HandleViewClosed;
                    view = null;
                    Finish();
                }
            }
        }

        // 面板经任何一条路关掉（到时、确认键、返回键被 UICancelRouter 关）都会回到这里。
        private void HandleViewClosed()
        {
            view = null;
            Finish();
        }

        // 收尾：释放暂停令牌与图片句柄，允许下一次照镜。幂等。
        private void Finish()
        {
            showing = false;
            closeRequested = false;
            if (pauseToken != null)
            {
                pauseToken.Dispose();
                pauseToken = null;
            }

            if (imageHandle != null)
            {
                imageHandle.Dispose();
                imageHandle = null;
            }
        }
    }
}
