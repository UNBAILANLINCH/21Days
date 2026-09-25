// 职责：任务面板的会话控制——开关面板、开着期间持世界暂停令牌并关 Gameplay 输入图、把任务事件转成面板刷新、处理选中与追踪切换。
// 为什么新建：QuestPanelView 只显示不注入服务；QuestService 是纯数据入口，不该依赖 UI / 暂停 / 输入。HUD 点击与将来的快捷键都要一行调用打开面板。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using MessagePipe;

namespace Game.Quest
{
    /// <summary>
    /// 任务面板控制器（根作用域单例）。面板开着时世界暂停、Gameplay 输入图关闭；关闭时只恢复进来之前的状态。
    /// </summary>
    public sealed class QuestPanelController : IDisposable
    {
        private const string TrackText = "追踪";
        private const string UntrackText = "取消追踪";

        private readonly QuestService service;
        private readonly QuestConfig config;
        private readonly IUIService ui;
        private readonly IWorldPauseService pause;
        private readonly IInputService input;
        private readonly ISubscriber<QuestActivatedEvent> activated;
        private readonly ISubscriber<QuestObjectiveProgressedEvent> progressed;
        private readonly ISubscriber<QuestCompletedEvent> completed;
        private readonly ISubscriber<QuestTrackingChangedEvent> tracking;
        private readonly ITelemetryScope telemetry;
        private readonly List<QuestProgress> buffer = new List<QuestProgress>();

        private QuestPanelView view;
        private IDisposable pauseToken;
        private IDisposable eventSubscription;
        private bool gameplayWasEnabled;
        private bool opening;
        private bool closing;
        private bool disposed;
        private int selectedId;

        public QuestPanelController(QuestService service, QuestConfig config, IUIService ui, IWorldPauseService pause,
            IInputService input, ISubscriber<QuestActivatedEvent> activated,
            ISubscriber<QuestObjectiveProgressedEvent> progressed, ISubscriber<QuestCompletedEvent> completed,
            ISubscriber<QuestTrackingChangedEvent> tracking, ITelemetryScope telemetry)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.pause = pause ?? throw new ArgumentNullException(nameof(pause));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.activated = activated ?? throw new ArgumentNullException(nameof(activated));
            this.progressed = progressed ?? throw new ArgumentNullException(nameof(progressed));
            this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
            this.tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>面板是否开着（打开流程走完、关闭流程未开始收尾）。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>打开任务面板。已开或正在开时直接返回。</summary>
        public async UniTask OpenAsync(CancellationToken ct = default)
        {
            if (disposed || IsOpen || opening || closing) return;
            opening = true;
            try
            {
                QuestPanelView opened = await ui.OpenAsync<QuestPanelView>(ct: ct);
                // await 期间 Dispose 可能已执行：此时不收尾就会留下没人关的面板。
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }

                view = opened;
                pauseToken = pause.Acquire(this);
                // 只恢复进来之前的状态：Gameplay 图本来就关着（如过场中）时，关面板后不擅自打开。
                // Actions 为 null（InputService 尚未初始化、或 EditMode 测试）时没有输入图可管，一并跳过。
                bool hasInput = input.Actions != null; // lint-ok: 只判动作集是否已创建，不读设备输入、不影响回放
                gameplayWasEnabled = hasInput && input.Actions.Gameplay.enabled; // lint-ok: 只读动作图启用状态用于收尾恢复，不读设备输入、不影响回放
                if (hasInput) input.DisableMap(InputService.GameplayMap);

                view.OnQuestSelected += HandleQuestSelected;
                view.OnTrackToggled += HandleTrackToggled;
                view.OnClose += HandleCloseRequested;
                view.OnClosed += HandleViewClosed;

                IsOpen = true;
                selectedId = service.TrackedId; // 为 0 时 Refresh 会落到列表第一项
                Refresh();

                // 订阅句柄必须托管（EventConventions.cs 第 5 条）；面板开着时列表 / 详情跟着任务事件变。
                DisposableBagBuilder bag = DisposableBag.CreateBuilder();
                activated.Subscribe(_ => Refresh()).AddTo(bag);
                progressed.Subscribe(_ => Refresh()).AddTo(bag);
                completed.Subscribe(_ => Refresh()).AddTo(bag);
                tracking.Subscribe(_ => Refresh()).AddTo(bag);
                eventSubscription = bag.Build();

                telemetry.Track("panel_opened", ("count", buffer.Count), ("selected", selectedId));
            }
            finally
            {
                opening = false;
            }
        }

        /// <summary>关闭任务面板：关 UI → 恢复输入图 → 释放暂停令牌 → 退订。没开时空操作。</summary>
        public async UniTask CloseAsync()
        {
            if (!IsOpen || closing || view == null) return;
            closing = true;
            QuestPanelView target = view;
            Detach(target);
            try
            {
                await ui.CloseAsync(target);
            }
            finally
            {
                ReleaseSession();
                closing = false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            QuestPanelView target = view;
            bool wasOpen = IsOpen;
            ReleaseSession();
            if (target != null)
            {
                Detach(target);
                if (wasOpen) CloseViewAsync(target).Forget();
            }
        }

        // 刷新列表与详情；只在打开、任务事件、选中 / 追踪变化时调用，不每帧。
        private void Refresh()
        {
            if (view == null) return;
            service.GetOrdered(buffer);

            QuestProgress selected = null;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].Id == selectedId)
                {
                    selected = buffer[i];
                    break;
                }
            }

            if (selected == null)
            {
                selected = buffer.Count > 0 ? buffer[0] : null;
                selectedId = selected == null ? 0 : selected.Id;
            }

            view.SetList(buffer, selectedId, config.MainKindLabel, config.SideKindLabel);
            string kindLabel = selected == null
                ? string.Empty
                : selected.Definition.Kind == QuestKind.Main ? config.MainKindLabel : config.SideKindLabel;
            bool tracked = selectedId != 0 && selectedId == service.TrackedId;
            view.SetDetail(selected, tracked, kindLabel, TrackText, UntrackText);
        }

        private void HandleQuestSelected(int id)
        {
            if (id == selectedId) return;
            selectedId = id;
            telemetry.Track("quest_selected", ("id", id));
            Refresh();
        }

        // Service 发 QuestTrackingChangedEvent，由事件订阅触发 Refresh。
        private void HandleTrackToggled()
        {
            if (selectedId == 0) return;
            if (selectedId == service.TrackedId)
            {
                service.Untrack();
                telemetry.Track("track_toggled", ("id", selectedId), ("tracked", false));
            }
            else
            {
                bool ok = service.Track(selectedId);
                telemetry.Track("track_toggled", ("id", selectedId), ("tracked", ok));
            }
        }

        private void HandleCloseRequested() => CloseRequestedAsync().Forget();

        // 面板被 UIService 从外部关掉（没经过 CloseAsync）：只收尾会话资源，不再调 ui.CloseAsync。
        private void HandleViewClosed()
        {
            if (closing || view == null) return;
            Detach(view);
            ReleaseSession();
        }

        private async UniTaskVoid CloseRequestedAsync()
        {
            try
            {
                await CloseAsync();
            }
            catch (Exception e)
            {
                telemetry.TrackError("panel_close_failed", e);
                Log.Error($"QuestPanelController：关闭任务面板失败：{e}");
            }
        }

        private void Detach(QuestPanelView target)
        {
            target.OnQuestSelected -= HandleQuestSelected;
            target.OnTrackToggled -= HandleTrackToggled;
            target.OnClose -= HandleCloseRequested;
            target.OnClosed -= HandleViewClosed;
        }

        // 关闭的同步部分：恢复输入图（仅当进来前是开的）、释放暂停令牌、退订任务事件。幂等。
        private void ReleaseSession()
        {
            bool wasOpen = IsOpen;
            if (wasOpen && gameplayWasEnabled) input.EnableMap(InputService.GameplayMap);
            gameplayWasEnabled = false;
            if (pauseToken != null)
            {
                pauseToken.Dispose();
                pauseToken = null;
            }

            if (eventSubscription != null)
            {
                eventSubscription.Dispose();
                eventSubscription = null;
            }

            view = null;
            IsOpen = false;
            if (wasOpen) telemetry.Track("panel_closed", ("selected", selectedId));
        }

        // 作用域销毁时 UIService 往往已先释放，关面板会抛 ObjectDisposedException——那时面板已随 UIRoot 一并销毁，静默即可。
        private async UniTaskVoid CloseViewAsync(QuestPanelView target)
        {
            try
            {
                await ui.CloseAsync(target);
            }
            catch (ObjectDisposedException)
            {
                // 见方法注释。
            }
            catch (Exception e)
            {
                Log.Warn($"QuestPanelController：关闭任务面板失败：{e.Message}");
            }
        }
    }
}
