// 职责：驱动探索 HUD 的控件区——触屏控件（摇杆 / 三键 / 走跑按钮）按平台显隐、走跑标签跟随 PlayerModel.IsRunning、
//   物资箱焦点提示、沉浸时整体隐藏控件区，以及「重置进度」确认 → 任务与物资箱复位 → 重进探索状态；埋点 progress_reset。
// PC 优先：PC 走跑走 Gameplay/Run（左 Ctrl / 手柄左摇杆按下），触屏控件仅移植阶段启用（ShowStickOnDesktop 为开发开关）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：ExplorationHudPresenter 只管沉浸开关，QuestHudPresenter 只管任务栏；二者都不认识 Loot / Player。
//   2. 扩展不行：把控件与重置塞进 ExplorationHudPresenter 会让沉浸开关依赖任务、物资箱、玩家模型与状态流，职责说不通。
// 依赖说明：重进探索用 Game.Monster.MonsterEncounterState——当前探索场景（IsometricEncounter）由遭遇状态加载，
//   状态类型归 Monster 模块是历史原因；IsometricExploration → Monster / Player / Loot / Quest 都是同一 Runtime 程序集内的单向读取。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Platform;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Loot;
using Game.Monster;
using Game.Player;
using Game.Quest;
using MessagePipe;
using VContainer.Unity;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 探索控件入口点。HUD 在 <see cref="BootCompletedEvent"/> 后拿到（与 <see cref="ExplorationHudPresenter"/> 并发打开返回同一实例），
    /// 本类不关闭 HUD。走跑标签只在 <see cref="PlayerModel.IsRunning"/> 变化时写（潜行不改标签，只显示模式）。
    /// </summary>
    public sealed class ExplorationControlsPresenter : IStartable, ITickable, IDisposable
    {
        private readonly IUIService ui;
        private readonly IPlatformService platform;
        private readonly IsometricExplorationConfig config;
        private readonly PlayerModel player;
        private readonly SupplyCrateFocus focus;
        private readonly LootConfig lootConfig;
        private readonly LootService loot;
        private readonly QuestService quest;
        private readonly IGameFlow flow;
        private readonly IHudVisibility hudVisibility;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ISubscriber<HudVisibilityChangedEvent> hudChanged;
        private readonly ITelemetryScope telemetry;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private IDisposable subscription;
        private ExplorationHudView hud;
        private bool focusHooked;
        private bool runLabelApplied;
        private bool lastRunning;
        private bool resetting;
        private bool disposed;

        public ExplorationControlsPresenter(IUIService ui, IPlatformService platform, IsometricExplorationConfig config,
            PlayerModel player, SupplyCrateFocus focus, LootConfig lootConfig, LootService loot, QuestService quest,
            IGameFlow flow, IHudVisibility hudVisibility, ISubscriber<BootCompletedEvent> bootCompleted,
            ISubscriber<HudVisibilityChangedEvent> hudChanged, ITelemetryScope telemetry)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            // 两个 Config 都是 ScriptableObject，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.focus = focus ?? throw new ArgumentNullException(nameof(focus));
            if (lootConfig == null) throw new ArgumentNullException(nameof(lootConfig));
            this.lootConfig = lootConfig;
            this.loot = loot ?? throw new ArgumentNullException(nameof(loot));
            this.quest = quest ?? throw new ArgumentNullException(nameof(quest));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.hudChanged = hudChanged ?? throw new ArgumentNullException(nameof(hudChanged));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>触屏控件（摇杆 / 三键 / 走跑按钮）是否应显示：触屏平台恒显示，桌面按开发开关。纯逻辑，供绑定与测试共用。</summary>
        public static bool ShouldShowStick(bool touchPrimary, bool showOnDesktop) => touchPrimary || showOnDesktop;

        /// <summary>走跑标签：按模式取文案（潜行不影响）。</summary>
        public static string SelectRunLabel(bool running, string runText, string walkText) => running ? runText : walkText;

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => OpenHudAsync().Forget()).AddTo(bag);
            hudChanged.Subscribe(e => HandleHudChanged(e.Hidden)).AddTo(bag);
            subscription = bag.Build();

            focus.OnFocusChanged += HandleFocusChanged;
            focusHooked = true;
        }

        public void Tick()
        {
            if (disposed || hud == null) return;
            bool running = player.IsRunning;
            if (runLabelApplied && running == lastRunning) return;
            runLabelApplied = true;
            lastRunning = running;
            hud.SetRunLabel(SelectRunLabel(running, config.RunLabel, config.WalkLabel));
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

            if (focusHooked)
            {
                focus.OnFocusChanged -= HandleFocusChanged;
                focusHooked = false;
            }

            if (hud != null) hud.OnResetClicked -= HandleResetClicked;
            hud = null;
        }

        private async UniTaskVoid OpenHudAsync()
        {
            try
            {
                // 与 ExplorationHudPresenter 并发打开：UIService 保证返回同一实例。
                ExplorationHudView opened = await ui.OpenAsync<ExplorationHudView>();
                if (disposed) return;
                hud = opened;
                hud.OnResetClicked -= HandleResetClicked;
                hud.OnResetClicked += HandleResetClicked;
                bool touch = platform.IsTouchPrimary;
                bool touchControls = ShouldShowStick(touch, config.ShowStickOnDesktop);
                hud.SetStickVisible(touchControls);
                hud.SetTouchButtonsVisible(touchControls);
                hud.SetRunToggleVisible(touchControls);
                hud.SetControlsVisible(!hudVisibility.IsHudHidden);
                hud.SetPrompt(focus.Current != null ? lootConfig.PromptText : string.Empty);
                runLabelApplied = false;
                telemetry.Track("controls_bound", ("touch", touch));
            }
            catch (ObjectDisposedException)
            {
                // 作用域销毁途中 UIService 已释放，静默。
            }
            catch (Exception e)
            {
                // 控件区开不出来时键鼠 / 手柄仍可玩，记 Error 不崩。
                telemetry.TrackError("controls_open_failed", e);
                Log.Error($"ExplorationControlsPresenter：拿探索 HUD 失败：{e}");
            }
        }

        private void HandleHudChanged(bool hidden)
        {
            if (hud != null) hud.SetControlsVisible(!hidden);
        }

        // 焦点变化是事件驱动，不在每帧路径上。
        private void HandleFocusChanged(SupplyCrate crate)
        {
            if (hud != null) hud.SetPrompt(crate != null ? lootConfig.PromptText : string.Empty);
        }

        private void HandleResetClicked() => ResetFlowAsync().Forget();

        // 时序：开确认弹窗 → 等选择 → 关弹窗 →（确认）QuestService.ResetProgress → LootService.Reset（发 LootResetEvent，箱子合上）
        //   → 埋点 → GoToAsync<MonsterEncounterState>（退出当前遭遇状态卸载场景，再进入重新加载，玩家回出生点）。
        private async UniTaskVoid ResetFlowAsync()
        {
            if (resetting || disposed) return;
            resetting = true;
            ExplorationConfirmView view = null;
            CancellationToken ct = lifetime.Token;
            try
            {
                view = await ui.OpenAsync<ExplorationConfirmView>(config.ResetMessage, ct);
                view.SetButtonLabels(config.ResetConfirmText, config.ResetCancelText);
                bool confirmed = await view.WaitAsync(ct);
                ExplorationConfirmView closing = view;
                view = null;
                await ui.CloseAsync(closing, ct);
                if (!confirmed || disposed)
                {
                    telemetry.Track("progress_reset_cancelled");
                    return;
                }

                quest.ResetProgress();
                loot.Reset();
                telemetry.Track("progress_reset");
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
                telemetry.TrackError("progress_reset_failed", e);
                Log.Error($"ExplorationControlsPresenter：重置进度失败：{e}");
            }
            finally
            {
                resetting = false;
                if (view != null && !disposed) CloseQuietlyAsync(view).Forget();
            }
        }

        private async UniTaskVoid CloseQuietlyAsync(ExplorationConfirmView view)
        {
            try
            {
                await ui.CloseAsync(view);
            }
            catch (Exception e)
            {
                Log.Warn($"ExplorationControlsPresenter：关闭确认弹窗失败：{e.Message}");
            }
        }
    }
}
