// 职责：驱动任务 HUD 与世界空间目标标记——启动后打开常驻 HUD 并实例化头顶标记，任务事件驱动刷新标题 / 目标文本；
//   每帧：目标在画面内只摆头顶标记，画面外才画 HUD 贴边箭头与节流后的距离；对白期间隐藏，点击打开任务面板。
// 为什么新建：QuestHudView 只显示不注入服务；QuestSceneBinder 只管场景目标解析，QuestPanelController 只管面板会话，逐帧指引与 HUD 生命周期无处可放。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Game.Quest
{
    /// <summary>
    /// 任务 HUD 入口点。HUD 在 <see cref="BootCompletedEvent"/> 后打开并常驻——入口点 Start 早于 UIService 初始化，那时开不了面板。
    /// 字符串只在事件驱动的刷新里拼；Tick 只在整数米变化时生成距离文本。
    /// 目标标记（<see cref="QuestTargetMarker"/>）在 HUD 打开后按 <see cref="QuestConfig.TargetMarkerAddress"/> 实例化；
    /// 实例化失败只影响画面内提示，画面外箭头照常。
    /// </summary>
    public sealed class QuestHudPresenter : IStartable, ITickable, IDisposable
    {
        private const string MeterSuffix = " m";

        private readonly QuestService service;
        private readonly QuestSceneBinder binder;
        private readonly QuestPanelController panel;
        private readonly QuestConfig config;
        private readonly DialogueService dialogue;
        private readonly IUIService ui;
        private readonly IAssetService assets;
        private readonly IClock clock;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ISubscriber<QuestActivatedEvent> activated;
        private readonly ISubscriber<QuestObjectiveProgressedEvent> progressed;
        private readonly ISubscriber<QuestCompletedEvent> completed;
        private readonly ISubscriber<QuestTrackingChangedEvent> tracking;
        private readonly ITelemetryScope telemetry;

        private IDisposable subscription;
        private QuestHudView hud;
        private QuestTargetMarker marker;
        private GameObject markerInstance;
        private bool disposed;
        private bool dialogueHooked;
        private int lastTrackedId = -1;
        private int lastMeters = -1;
        private float distanceElapsed;
        private bool cameraMissingReported;

        public QuestHudPresenter(QuestService service, QuestSceneBinder binder, QuestPanelController panel,
            QuestConfig config, DialogueService dialogue, IUIService ui, IAssetService assets, IClock clock,
            ISubscriber<BootCompletedEvent> bootCompleted, ISubscriber<QuestActivatedEvent> activated,
            ISubscriber<QuestObjectiveProgressedEvent> progressed, ISubscriber<QuestCompletedEvent> completed,
            ISubscriber<QuestTrackingChangedEvent> tracking, ITelemetryScope telemetry)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (binder == null) throw new ArgumentNullException(nameof(binder));
            this.binder = binder;
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.activated = activated ?? throw new ArgumentNullException(nameof(activated));
            this.progressed = progressed ?? throw new ArgumentNullException(nameof(progressed));
            this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
            this.tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => OpenHudAsync().Forget()).AddTo(bag);
            activated.Subscribe(_ => RefreshText()).AddTo(bag);
            progressed.Subscribe(_ => RefreshText()).AddTo(bag);
            completed.Subscribe(_ => RefreshText()).AddTo(bag);
            tracking.Subscribe(_ => RefreshText()).AddTo(bag);
            subscription = bag.Build();

            dialogue.OnStarted += HandleDialogueStarted;
            dialogue.OnEnded += HandleDialogueEnded;
            dialogueHooked = true;
        }

        public void Tick()
        {
            if (hud == null) return;
            if (dialogue.IsRunning)
            {
                HideMarker();
                return;
            }

            if (!service.TryGetTracked(out QuestProgress progress) || !progress.HasCurrentObjective ||
                !binder.TryResolveTarget(progress.CurrentObjective, out QuestTarget target))
            {
                HideGuidance();
                return;
            }

            Camera camera = binder.SceneCamera;
            Transform anchor = binder.PlayerAnchor;
            if (camera == null || anchor == null)
            {
                if (camera == null && !cameraMissingReported)
                {
                    cameraMissingReported = true;
                    telemetry.TrackWarn("camera_missing", TelemetryProps.Of(("id", progress.Id)));
                }

                HideGuidance();
                return;
            }

            cameraMissingReported = false;
            // 头顶标记每帧跟着锚点走（NPC 可能在动）；画面外时标记也留在世界里，被相机裁掉即可。
            if (marker != null) marker.Show(target.Anchor);

            Vector3 viewport = camera.WorldToViewportPoint(target.Anchor);
            QuestGuidance guidance = QuestGuidanceMath.Solve(viewport, hud.CanvasSize, config.EdgeMargin, config.HoverOffset);
            if (guidance.OnScreen)
            {
                // 画面内由头顶标记提示，HUD 不画标识；下次出画面时立刻重算距离。
                hud.HideGuidance();
                lastMeters = -1;
                return;
            }

            // 距离节流：按 unscaled 时间累计，到间隔才重算；只在整数米变化时才生成文本（唯一的分配点）。
            string distanceText = null;
            distanceElapsed += clock.UnscaledDeltaTime;
            if (lastMeters < 0 || distanceElapsed >= config.DistanceRefreshInterval)
            {
                distanceElapsed = 0f;
                int meters = QuestGuidanceMath.DistanceMeters(anchor.position, target.Position);
                if (meters != lastMeters)
                {
                    lastMeters = meters;
                    distanceText = meters + MeterSuffix;
                }
            }

            hud.SetGuidance(guidance, distanceText);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }

            if (dialogueHooked)
            {
                dialogue.OnStarted -= HandleDialogueStarted;
                dialogue.OnEnded -= HandleDialogueEnded;
                dialogueHooked = false;
            }

            if (hud != null)
            {
                hud.OnClicked -= HandleHudClicked;
                CloseHudAsync(hud).Forget();
            }

            hud = null;
            ReleaseMarker();
        }

        // 事件驱动：只在任务事件、HUD 打开、对白结束时调用，字符串拼接放在这里而不是 Tick。
        private void RefreshText()
        {
            if (hud == null) return;
            int trackedId = 0;
            if (service.TryGetTracked(out QuestProgress progress))
            {
                trackedId = progress.Id;
                hud.SetTracked(progress.Definition.Title, BuildObjectiveText(progress));
            }
            else
            {
                hud.SetUntracked(config.UntrackedLabel);
            }

            if (trackedId != lastTrackedId)
            {
                lastTrackedId = trackedId;
                // 换了追踪目标：下一帧立刻重算距离，不沿用上一个目标的数字。
                lastMeters = -1;
                telemetry.Track("hud_refreshed", ("id", trackedId));
            }
        }

        private static string BuildObjectiveText(QuestProgress progress)
        {
            if (!progress.HasCurrentObjective) return string.Empty;
            QuestObjectiveDefinition objective = progress.CurrentObjective;
            return objective.RequiredCount > 1
                ? objective.Text + " " + progress.Count + "/" + objective.RequiredCount
                : objective.Text;
        }

        private void HideGuidance()
        {
            hud.HideGuidance();
            HideMarker();
            // 指引重新出现时立刻刷新距离。
            lastMeters = -1;
        }

        private void HideMarker()
        {
            if (marker != null) marker.Hide();
        }

        private void HandleDialogueStarted(DialogueStartedEvent e)
        {
            if (hud != null) hud.SetVisible(false);
            HideMarker();
        }

        private void HandleDialogueEnded(DialogueEndedEvent e)
        {
            if (hud == null) return;
            hud.SetVisible(true);
            RefreshText();
        }

        private void HandleHudClicked() => OpenPanelAsync().Forget();

        private async UniTaskVoid OpenPanelAsync()
        {
            try
            {
                await panel.OpenAsync();
            }
            catch (OperationCanceledException)
            {
                // 打开途中被取消不是错误。
            }
            catch (Exception e)
            {
                telemetry.TrackError("panel_open_failed", e);
                Log.Error($"QuestHudPresenter：打开任务面板失败：{e}");
            }
        }

        private async UniTaskVoid OpenHudAsync()
        {
            try
            {
                QuestHudView opened = await ui.OpenAsync<QuestHudView>();
                // await 期间 Dispose 可能已执行：hud 字段此时还没赋值，若不在这里收尾，
                // 新开出来的面板既没记进 hud 也没被 Dispose 关掉，会成为关不掉的孤儿实例。
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }

                hud = opened;
                hud.OnClicked -= HandleHudClicked;
                hud.OnClicked += HandleHudClicked;
                hud.HideGuidance();
                hud.SetVisible(!dialogue.IsRunning);
                lastTrackedId = -1;
                RefreshText();
            }
            catch (Exception e)
            {
                // HUD 只是提示，开不出来不影响任务推进，记 Error 不崩。
                Log.Error($"QuestHudPresenter：打开任务 HUD 失败：{e}");
                return;
            }

            await SpawnMarkerAsync();
        }

        // 标记与 HUD 同寿：HUD 开好后实例化一次，常驻到 Dispose。失败埋一次 Warn 并记 Error，画面外箭头不受影响。
        private async UniTask SpawnMarkerAsync()
        {
            if (markerInstance != null) return;
            GameObject instance;
            try
            {
                instance = await assets.InstantiateAsync(config.TargetMarkerAddress);
            }
            catch (Exception e)
            {
                ReportMarkerMissing($"实例化任务目标标记（地址「{config.TargetMarkerAddress}」）失败：{e}");
                return;
            }

            // await 期间 Dispose 可能已执行：此时没人会再还这个实例，就地还回去。
            if (disposed)
            {
                if (instance != null) ReleaseInstanceQuietly(instance);
                return;
            }

            if (instance == null)
            {
                ReportMarkerMissing($"实例化任务目标标记（地址「{config.TargetMarkerAddress}」）返回空。");
                return;
            }

            // 只在实例化后取一次组件，不在每帧路径上。
            QuestTargetMarker component = instance.GetComponent<QuestTargetMarker>();
            if (component == null)
            {
                ReleaseInstanceQuietly(instance);
                ReportMarkerMissing($"预制体「{config.TargetMarkerAddress}」根上没有 QuestTargetMarker 组件。");
                return;
            }

            UnityEngine.Object.DontDestroyOnLoad(instance);
            markerInstance = instance;
            marker = component;
            marker.Hide();
        }

        private void ReportMarkerMissing(string message)
        {
            telemetry.TrackWarn("marker_missing", TelemetryProps.Of(("address", config.TargetMarkerAddress)));
            Log.Error("QuestHudPresenter：" + message + " 画面内的目标头顶标记不会显示。");
        }

        private void ReleaseMarker()
        {
            GameObject instance = markerInstance;
            markerInstance = null;
            marker = null;
            // 资源服务先于本入口点释放时会强制回收实例，此时已是伪空（== null），不再重复归还。
            if (instance != null) ReleaseInstanceQuietly(instance);
        }

        // 作用域销毁时资源服务可能已释放，与 CloseHudAsync 同理静默。
        private void ReleaseInstanceQuietly(GameObject instance)
        {
            try
            {
                assets.ReleaseInstance(instance);
            }
            catch (ObjectDisposedException)
            {
                // 见方法注释。
            }
            catch (Exception e)
            {
                Log.Warn($"QuestHudPresenter：归还任务目标标记失败：{e.Message}");
            }
        }

        // 作用域销毁时 UIService 往往已先释放（按注册顺序），关面板会抛 ObjectDisposedException——
        // 那时 UIRoot 连同 HUD 已由 UIService 一并销毁，静默即可。
        private async UniTaskVoid CloseHudAsync(QuestHudView view)
        {
            try
            {
                await ui.CloseAsync(view);
            }
            catch (ObjectDisposedException)
            {
                // 见方法注释。
            }
            catch (Exception e)
            {
                Log.Warn($"QuestHudPresenter：关闭任务 HUD 失败：{e.Message}");
            }
        }
    }
}
