// 职责：镜裂呈现——启动后打开镜图标 HUD 与视野遮罩；遭遇活动时每帧读击中 / 剧情裂痕，变化时刷新图标与遮罩，
//   击中裂痕增加发 MirrorCrackedEvent 并埋 mirror_cracked(source=hit)；首次镜碎结束本场遭遇、发 MirrorShatteredEvent、埋 mirror_shattered、
//   打开镜碎页，任意确认 / 取消 / 点击后埋 mirror_restart 并重进遭遇状态（回出生点、击中裂痕清零、内存进度保留）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：EncounterSceneView 的调试块只画生命数字（PRD 明确不许出现生命数字与血条）；Session / Player 没有「战败」的消费方。
//   2. 扩展不行：塞进 MirrorInputPresenter 会把「按键照镜」和「被击中的后果」两条互不相关的流程绑进一个入口点。
// 依赖说明：重进遭遇用 Game.Monster.MonsterEncounterState（同 ExplorationControlsPresenter 的重置）；Mirror → Monster 是 PRP 2.1 允许的单向读取。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Monster;
using MessagePipe;
using VContainer.Unity;

namespace Game.Mirror
{
    /// <summary>
    /// 镜裂入口点。HUD 与遮罩在 <see cref="BootCompletedEvent"/> 后打开并常驻（入口点 Start 早于 UIService 初始化，同 ExplorationHudPresenter）。
    /// <para>
    /// 只在 <see cref="EncounterStep.IsActive"/> 时判定：遭遇开始前 Health 为 0，会被算成三裂（波 1 定稿 ⑧）。
    /// </para>
    /// <para>
    /// 镜碎时先 <see cref="EncounterStep.End"/>：① 双方规则停止推进，巡逻怪不再继续打一个已经碎了镜的玩家；
    /// ② <c>SessionStateAdapter.IsGameplayState</c> 随之为 false，重进状态时「离开遭遇 → SaveNowAsync」与期间的合并式自动保存
    /// 都会跳过，存档里不会留下 Health = 0 的现场（Demo 里从没调过 StartBattle，Defeat 结果不会产生，Session 的战败闸门挡不住）。
    /// 重进后 MonsterEncounterState 调 Begin 重新激活。
    /// </para>
    /// </summary>
    public sealed class MirrorCrackPresenter : IStartable, ITickable, IDisposable
    {
        private readonly MirrorService service;
        private readonly MirrorConfig config;
        private readonly EncounterStep step;
        private readonly IUIService ui;
        private readonly IInputService input;
        private readonly IGameFlow flow;
        private readonly IClock clock;
        private readonly IPublisher<MirrorCrackedEvent> crackedPublisher;
        private readonly IPublisher<MirrorShatteredEvent> shatteredPublisher;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ITelemetryScope telemetry;
        private readonly MirrorCrackTracker tracker = new MirrorCrackTracker();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private IDisposable subscription;
        private MirrorHudView hud;
        private MirrorVisionView vision;
        private MirrorShatterView shatterView;
        private float shatterOpenedAt;
        private int shownHitCracks;
        private float shownVision = 1f;
        private bool restarting;
        private bool reentering;
        private bool disposed;

        public MirrorCrackPresenter(MirrorService service, MirrorConfig config, EncounterStep step, IUIService ui,
            IInputService input, IGameFlow flow, IClock clock, IPublisher<MirrorCrackedEvent> cracked,
            IPublisher<MirrorShatteredEvent> shattered, ISubscriber<BootCompletedEvent> bootCompleted,
            ITelemetryScope telemetry)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            // MirrorConfig 是 ScriptableObject，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.step = step ?? throw new ArgumentNullException(nameof(step));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            crackedPublisher = cracked ?? throw new ArgumentNullException(nameof(cracked));
            shatteredPublisher = shattered ?? throw new ArgumentNullException(nameof(shattered));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>镜碎页是否在显示（含重开途中）。</summary>
        public bool IsShatterShowing => restarting;

        /// <summary>镜碎页已被交回、正在重进遭遇（卸载并重载场景）；重进完成、失败或取消后回到 false。</summary>
        public bool IsRestarting => reentering;

        /// <summary>镜碎页出现后是否已过了按键保护时间（真实时间）。纯函数，供 Tick 与测试共用。</summary>
        public static bool CanDismissShatter(float elapsedSeconds, float inputDelay) => elapsedSeconds >= inputDelay;

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => OpenViewsAsync().Forget()).AddTo(bag);
            subscription = bag.Build();
        }

        public void Tick()
        {
            if (disposed) return;

            // 跟踪器每帧都喂：镜碎后 step 已 End，下一帧即记为「不在遭遇」，重进时重新取基线、镜碎标志清零。
            bool changed = tracker.Update(step.IsActive, service.HitCracks, service.StoryCracks,
                out bool cracked, out bool shattered);

            if (shatterView != null)
            {
                PollShatterKeys();
                return;
            }
            if (changed) Push(tracker.HitCracks, service.VisionRadius);

            if (cracked)
            {
                crackedPublisher.Publish(new MirrorCrackedEvent(tracker.HitCracks, tracker.StoryCracks));
                telemetry.Track("mirror_cracked", ("hit_cracks", tracker.HitCracks), ("story_cracks", tracker.StoryCracks),
                    ("range", service.EffectiveRange), ("source", "hit"));
            }

            if (shattered) Shatter();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }

            hud = null;
            vision = null;
            shatterView = null;
        }

        private void Push(int hitCracks, float visionRadius)
        {
            shownHitCracks = hitCracks;
            shownVision = visionRadius;
            if (hud != null) hud.SetCracks(hitCracks);
            if (vision != null) vision.SetRadius(visionRadius);
        }

        private void Shatter()
        {
            step.End();
            shatteredPublisher.Publish(new MirrorShatteredEvent());
            telemetry.Track("mirror_shattered", ("story_cracks", tracker.StoryCracks));
            ShatterFlowAsync().Forget();
        }

        // 镜碎页开着时：过了按键保护时间后，UI/Submit、UI/Cancel 任一按下即交回（点击由页面自己的 tapArea 处理）。
        // 镜碎属于表现与流程，不进确定性模拟、不影响回放，所以直接读 UI 动作（同 SupplyCrateFocus 读 Interact）。
        private void PollShatterKeys()
        {
            if (!CanDismissShatter(clock.UnscaledTime - shatterOpenedAt, config.ShatterInputDelay)) return;
            GameInput actions = input.Actions; // lint-ok: 镜碎页交回属于表现流程，不进确定性模拟与回放
            if (actions == null) return;
            if (actions.UI.Submit.WasPressedThisFrame() || actions.UI.Cancel.WasPressedThisFrame())
            {
                shatterView.Dismiss();
            }
        }

        // 时序：关 Gameplay 图（P 键开不了暂停菜单、Esc 不退沉浸）→ 开镜碎页 → 等交回 → 埋 mirror_restart
        //   → GoToAsync<MonsterEncounterState>（卸载并重载场景，Begin 让 Health 回满）→ 关镜碎页（黑底盖住重载过程）
        //   → 进来前 Gameplay 图开着才重新打开。防重入：restarting 期间再次镜碎（不应发生）直接忽略。
        private async UniTaskVoid ShatterFlowAsync()
        {
            if (restarting || disposed) return;
            restarting = true;
            CancellationToken ct = lifetime.Token;
            GameInput actions = input.Actions; // lint-ok: 只读动作图启用状态用于收尾恢复，不读设备输入、不影响回放
            bool hasInput = actions != null;
            bool gameplayWasEnabled = hasInput && actions.Gameplay.enabled;
            MirrorShatterView view = null;
            try
            {
                if (hasInput) input.DisableMap(InputService.GameplayMap);
                view = await ui.OpenAsync<MirrorShatterView>(config.ShatterText, ct);
                shatterOpenedAt = clock.UnscaledTime;
                shatterView = view;
                await view.WaitAsync(ct);
                shatterView = null;
                if (disposed) return;

                reentering = true;
                telemetry.Track("mirror_restart");
                await flow.GoToAsync<MonsterEncounterState>(ct);
            }
            catch (OperationCanceledException)
            {
                // 作用域销毁时取消，静默。
            }
            catch (ObjectDisposedException)
            {
                // 作用域销毁途中 UIService 已释放，静默。
            }
            catch (Exception e)
            {
                telemetry.TrackError("mirror_restart_failed", e);
                Log.Error($"MirrorCrackPresenter：镜碎后重开本场失败：{e}");
            }
            finally
            {
                shatterView = null;
                if (!disposed)
                {
                    if (hasInput && gameplayWasEnabled) input.EnableMap(InputService.GameplayMap);
                    if (view != null) CloseQuietlyAsync(view).Forget();
                }

                reentering = false;
                restarting = false;
            }
        }

        private async UniTaskVoid CloseQuietlyAsync(UIView view)
        {
            try
            {
                await ui.CloseAsync(view);
            }
            catch (Exception e)
            {
                Log.Warn($"MirrorCrackPresenter：关闭镜碎页失败：{e.Message}");
            }
        }

        private async UniTaskVoid OpenViewsAsync()
        {
            CancellationToken ct = lifetime.Token;
            try
            {
                // 遮罩先开：它会把自己压到 Hud 层最底。
                MirrorVisionView openedVision = await ui.OpenAsync<MirrorVisionView>(null, ct);
                MirrorHudView openedHud = await ui.OpenAsync<MirrorHudView>(null, ct);
                if (disposed) return;
                vision = openedVision;
                hud = openedHud;
                // 还没进遭遇时按「无裂痕、不遮」显示；进了遭遇由 Tick 推真实值。
                Push(tracker.Tracking ? shownHitCracks : 0, tracker.Tracking ? shownVision : 1f);
                telemetry.Track("mirror_hud_opened");
            }
            catch (OperationCanceledException)
            {
                // 作用域销毁时取消，静默。
            }
            catch (ObjectDisposedException)
            {
                // 作用域销毁途中 UIService 已释放，静默。
            }
            catch (Exception e)
            {
                // 镜图标 / 遮罩开不出来时照镜与镜碎仍可用，记 Error 不崩。
                telemetry.TrackError("mirror_hud_open_failed", e);
                Log.Error($"MirrorCrackPresenter：打开镜图标 / 视野遮罩失败：{e}");
            }
        }
    }
}
