// 职责：背包面板的会话控制——开关面板、开着期间持世界暂停令牌并关 Gameplay 输入图、把背包数据（LootService.Items + 物品表）
//   经 InventoryRules 绑到面板、处理筛选与选中；开着时订阅开箱 / 重置事件刷新。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestPanelController 绑的是任务服务与任务事件。
//   2. 扩展不行：InventoryPanelView 只显示不注入服务；LootService 是数据门面，不该依赖 UI / 暂停 / 输入。
//   会话收尾（返回、Esc 外部关闭、作用域销毁）照 QuestPanelController 的写法。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Config;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Loot;
using MessagePipe;

namespace Game.Inventory
{
    /// <summary>
    /// 背包面板控制器（根作用域单例）。面板开着时世界暂停、Gameplay 输入图关闭；关闭时只恢复进来之前的状态。
    /// </summary>
    public sealed class InventoryPanelController : IDisposable
    {
        private readonly LootService loot;
        private readonly IConfigService config;
        private readonly IUIService ui;
        private readonly IWorldPauseService pause;
        private readonly IInputService input;
        private readonly ISubscriber<CrateCollectedEvent> collected;
        private readonly ISubscriber<LootResetEvent> reset;
        private readonly ITelemetryScope telemetry;
        private readonly List<InventoryEntry> buffer = new List<InventoryEntry>();

        private InventoryPanelView view;
        private IDisposable pauseToken;
        private IDisposable eventSubscription;
        private bool gameplayWasEnabled;
        private bool opening;
        private bool closing;
        private bool disposed;
        private bool tableWarned;
        private int selectedId;
        private InventoryFilter filter = InventoryFilter.All;

        public InventoryPanelController(LootService loot, IConfigService config, IUIService ui, IWorldPauseService pause,
            IInputService input, ISubscriber<CrateCollectedEvent> collected, ISubscriber<LootResetEvent> reset,
            ITelemetryScope telemetry)
        {
            this.loot = loot ?? throw new ArgumentNullException(nameof(loot));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.pause = pause ?? throw new ArgumentNullException(nameof(pause));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.collected = collected ?? throw new ArgumentNullException(nameof(collected));
            this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>面板是否开着（打开流程走完、关闭流程未开始收尾）。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>正在打开或已开着——给热键防连按用。</summary>
        public bool IsBusy => IsOpen || opening || closing;

        /// <summary>打开背包面板。已开或正在开时直接返回。每次打开回到「全部」档、选中第一条。</summary>
        public async UniTask OpenAsync(CancellationToken ct = default)
        {
            if (disposed || IsBusy) return;
            opening = true;
            try
            {
                InventoryPanelView opened = await ui.OpenAsync<InventoryPanelView>(ct: ct);
                // await 期间 Dispose 可能已执行：此时不收尾就会留下没人关的面板。
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }

                view = opened;
                pauseToken = pause.Acquire(this);
                // 只恢复进来之前的状态：Gameplay 图本来就关着时，关面板后不擅自打开。
                bool hasInput = input.Actions != null; // lint-ok: 只判动作集是否已创建，不读设备输入、不影响回放
                gameplayWasEnabled = hasInput && input.Actions.Gameplay.enabled; // lint-ok: 只读动作图启用状态用于收尾恢复，不读设备输入、不影响回放
                if (hasInput) input.DisableMap(InputService.GameplayMap);

                view.OnFilterChanged += HandleFilterChanged;
                view.OnEntrySelected += HandleEntrySelected;
                view.OnClose += HandleCloseRequested;
                view.OnClosed += HandleViewClosed;

                IsOpen = true;
                filter = InventoryFilter.All;
                selectedId = 0; // Refresh 会落到列表第一项
                Refresh();

                // 订阅句柄必须托管（EventConventions.cs 第 5 条）；面板开着时列表跟着开箱 / 重置变。
                DisposableBagBuilder bag = DisposableBag.CreateBuilder();
                collected.Subscribe(_ => Refresh()).AddTo(bag);
                reset.Subscribe(_ => Refresh()).AddTo(bag);
                eventSubscription = bag.Build();

                telemetry.Track("inventory_opened", ("count", buffer.Count));
            }
            finally
            {
                opening = false;
            }
        }

        /// <summary>关闭背包面板：关 UI → 恢复输入图 → 释放暂停令牌 → 退订。没开时空操作。</summary>
        public async UniTask CloseAsync()
        {
            if (!IsOpen || closing || view == null) return;
            closing = true;
            InventoryPanelView target = view;
            Detach(target);
            try
            {
                await ui.CloseAsync(target);
            }
            finally
            {
                ReleaseSession("button");
                closing = false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            InventoryPanelView target = view;
            bool wasOpen = IsOpen;
            ReleaseSession("dispose");
            if (target != null)
            {
                Detach(target);
                if (wasOpen) CloseViewAsync(target).Forget();
            }
        }

        // 刷新筛选标记、列表与详情；只在打开、筛选 / 选中变化、开箱 / 重置事件时调用，不每帧。
        private void Refresh()
        {
            if (view == null) return;
            InventoryRules.Build(loot.Items, TryGetTable(), filter, buffer);

            int index = -1;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].Id == selectedId)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0 && buffer.Count > 0) index = 0;
            selectedId = index < 0 ? 0 : buffer[index].Id;

            view.SetFilter(filter);
            view.SetList(buffer, selectedId);
            view.SetDetail(index >= 0, index >= 0 ? buffer[index] : default);
        }

        // 表未就绪（直接 Play 玩法场景、或启动失败）时不崩：全部按「#id」显示，只记一次 Warn。
        private global::cfg.TbItem TryGetTable()
        {
            try
            {
                return config.Tables.TbItem;
            }
            catch (InvalidOperationException e)
            {
                if (!tableWarned)
                {
                    tableWarned = true;
                    Log.Warn($"InventoryPanelController：配置表未就绪，背包条目用 id 代替名字：{e.Message}");
                    telemetry.TrackWarn("table_not_ready", TelemetryProps.Of(("reason", e.GetType().Name)));
                }

                return null;
            }
        }

        private void HandleFilterChanged(InventoryFilter value)
        {
            if (value == filter) return;
            filter = value;
            telemetry.Track("filter_changed", ("filter", value.ToString()));
            Refresh();
        }

        private void HandleEntrySelected(int id)
        {
            if (id == selectedId) return;
            selectedId = id;
            Refresh();
        }

        private void HandleCloseRequested() => CloseRequestedAsync().Forget();

        // 面板被 UIService 从外部关掉（没经过 CloseAsync，如 Esc）：只收尾会话资源，不再调 ui.CloseAsync。
        private void HandleViewClosed()
        {
            if (closing || view == null) return;
            Detach(view);
            ReleaseSession("external");
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
                Log.Error($"InventoryPanelController：关闭背包面板失败：{e}");
            }
        }

        private void Detach(InventoryPanelView target)
        {
            target.OnFilterChanged -= HandleFilterChanged;
            target.OnEntrySelected -= HandleEntrySelected;
            target.OnClose -= HandleCloseRequested;
            target.OnClosed -= HandleViewClosed;
        }

        // 关闭的同步部分：恢复输入图（仅当进来前是开的）、释放暂停令牌、退订事件。幂等。
        private void ReleaseSession(string reason)
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
            if (wasOpen) telemetry.Track("inventory_closed", ("reason", reason), ("filter", filter.ToString()));
        }

        // 作用域销毁时 UIService 往往已先释放，关面板会抛 ObjectDisposedException——那时面板已随 UIRoot 一并销毁，静默即可。
        private async UniTaskVoid CloseViewAsync(InventoryPanelView target)
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
                Log.Warn($"InventoryPanelController：关闭背包面板失败：{e.Message}");
            }
        }
    }
}
