// 职责：选槽面板的会话控制——读槽摘要并开面板、按模式分派行点击（继续 / 新游戏 / 覆盖确认）、删除确认与刷新、
//   离开标题状态时收掉面板、外部关闭时收尾。
// 为什么新建：SaveSlotsView 只显示不注入服务；GameSession 是存档入口，不该依赖 UI。标题「开始」（无空槽时）与
//   「选择存档」两条路由都要一行调用打开面板，写法照 QuestPanelController（外部关闭 OnClosed 收尾）。
//   与它不同：在标题页打开，不暂停世界、不动 Gameplay 输入图。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Core.UI.Views;
using MessagePipe;

namespace Game.Session
{
    /// <summary>
    /// 选槽面板控制器（根作用域单例）。<see cref="OpenAsync"/> 按模式打开；面板开着时同一时刻只处理一个操作（忙时点击忽略）。
    /// </summary>
    public sealed class SaveSlotsController : IDisposable
    {
        private const string CancelText = "取消";
        private const string OverwriteConfirmText = "覆盖";
        private const string DeleteConfirmText = "删除";
        private const string NewGameFailedTitle = "无法开始新游戏";
        private const string DeleteFailedTitle = "删除存档失败";

        private readonly GameSession session;
        private readonly IUIService ui;
        private readonly SessionConfig config;
        private readonly INotificationService notifications;
        private readonly ISubscriber<GameStateChangingEvent> stateChanging;
        private readonly ITelemetryScope telemetry;

        private SaveSlotsView view;
        private IReadOnlyList<SlotInfo> infos = Array.Empty<SlotInfo>();
        private SlotsMode mode;
        private IDisposable stateSubscription;
        private bool opening;
        private bool closing;
        private bool busy;
        private bool disposed;

        /// <remarks>
        /// 比派单多一个 <see cref="ISubscriber{GameStateChangingEvent}"/>：UIService 切状态时不会自动关 Panel，
        /// 继续 / 新游戏成功后流程离开标题，本面板若不自己收掉会盖在玩法画面上（并让保存闸门一直认为有面板开着）。
        /// </remarks>
        public SaveSlotsController(GameSession session, IUIService ui, SessionConfig config,
            INotificationService notifications, ISubscriber<GameStateChangingEvent> stateChanging, ITelemetryScope telemetry)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            // SessionConfig 是 ScriptableObject，判空只用 ==。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            this.stateChanging = stateChanging ?? throw new ArgumentNullException(nameof(stateChanging));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>面板是否开着（打开流程走完、关闭流程未开始收尾）。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>按 <paramref name="slotsMode"/> 打开选槽面板；已开着时切模式并刷新，正在开 / 关时直接返回。</summary>
        public async UniTask OpenAsync(SlotsMode slotsMode, CancellationToken ct = default)
        {
            if (disposed || opening || closing) return;
            if (IsOpen)
            {
                mode = slotsMode;
                await RefreshAsync(ct);
                return;
            }

            opening = true;
            try
            {
                IReadOnlyList<SlotInfo> read = await session.ReadSlotInfosAsync(ct);
                SaveSlotsView opened = await ui.OpenAsync<SaveSlotsView>(ct: ct);
                // await 期间 Dispose 可能已执行：此时不收尾就会留下没人关的面板。
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }

                view = opened;
                mode = slotsMode;
                infos = read;
                view.OnSlotClicked += HandleSlotClicked;
                view.OnDeleteClicked += HandleDeleteClicked;
                view.OnBack += HandleBack;
                view.OnClosed += HandleViewClosed;
                stateSubscription = stateChanging.Subscribe(HandleStateChanging);
                IsOpen = true;
                view.Bind(infos, mode);
                telemetry.Track("slots_opened", ("mode", mode.ToString()), ("slots", infos.Count));
            }
            finally
            {
                opening = false;
            }
        }

        /// <summary>关闭选槽面板：退订 → 关 UI。没开时空操作。</summary>
        public async UniTask CloseAsync()
        {
            if (!IsOpen || closing || view == null) return;
            closing = true;
            SaveSlotsView target = view;
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
            SaveSlotsView target = view;
            bool wasOpen = IsOpen;
            ReleaseSession();
            if (target != null)
            {
                Detach(target);
                if (wasOpen) CloseViewAsync(target).Forget();
            }
        }

        private void HandleSlotClicked(int slot) => RunGuardedAsync(slot, false).Forget();

        private void HandleDeleteClicked(int slot) => RunGuardedAsync(slot, true).Forget();

        private void HandleBack() => CloseGuardedAsync().Forget();

        // 流程要离开标题（继续 / 新游戏成功后）：面板随之收掉，不留在玩法画面上。
        private void HandleStateChanging(GameStateChangingEvent e)
        {
            if (e.From == typeof(TitleState)) CloseGuardedAsync().Forget();
        }

        // 面板被 UIService 从外部关掉（Esc / 手柄 B 走 CloseTopAsync）：只收尾会话资源，不再调 ui.CloseAsync。
        private void HandleViewClosed()
        {
            if (closing || view == null) return;
            Detach(view);
            ReleaseSession();
        }

        // 同一时刻只处理一个操作：确认弹窗开着、读写盘进行中时的重复点击直接忽略。
        private async UniTaskVoid RunGuardedAsync(int slot, bool delete)
        {
            if (busy || !IsOpen) return;
            busy = true;
            try
            {
                if (delete) await DeleteAsync(slot);
                else await SelectAsync(slot);
            }
            catch (OperationCanceledException)
            {
                // 作用域销毁时的取消，不是错误。
            }
            catch (Exception e)
            {
                telemetry.TrackError(delete ? "slot_delete_failed" : "slot_select_failed", e);
                Log.Error($"SaveSlotsController：处理槽 {slot} 失败：{e}");
                if (delete) notifications.Show(DeleteFailedTitle);
            }
            finally
            {
                busy = false;
            }
        }

        private async UniTask SelectAsync(int slot)
        {
            SlotInfo info = FindInfo(slot);
            if (mode == SlotsMode.Load)
            {
                // 行在读档模式下只有可读槽可点；这里再挡一次，防止摘要过期或键盘绕过 interactable。
                if (info.State != SlotState.Available) return;
                telemetry.Track("slot_continue", ("slot", slot));
                bool ok = await session.ContinueAsync(slot);
                // 成功时流程已离开标题、面板已随 GameStateChangingEvent 收掉；失败（「存档不可用」通知由 GameSession 发）刷新留在面板。
                if (!ok) await RefreshAsync(default);
                return;
            }

            if (info.State != SlotState.Empty)
            {
                bool confirmed = await ConfirmAsync("覆盖存档 " + slot + "？原有进度将丢失。", OverwriteConfirmText);
                if (!confirmed || !IsOpen) return;
                await session.DeleteSlotAsync(slot);
            }

            telemetry.Track("slot_new_game", ("slot", slot), ("overwrite", info.State != SlotState.Empty));
            bool started = await session.NewGameAsync(slot);
            if (!started)
            {
                notifications.Show(NewGameFailedTitle);
                await RefreshAsync(default);
            }
        }

        private async UniTask DeleteAsync(int slot)
        {
            if (FindInfo(slot).State != SlotState.Available) return;
            bool confirmed = await ConfirmAsync("删除存档 " + slot + "？此操作无法撤销。", DeleteConfirmText);
            if (!confirmed || !IsOpen) return;

            await session.DeleteSlotAsync(slot);
            telemetry.Track("slot_deleted", ("slot", slot));
            await RefreshAsync(default);

            // 删掉的可能是最近槽：标题「继续」跟着更新（面板关掉后标题直接可见，不等下一次回标题）。
            TitleView title = ui.Get<TitleView>();
            if (title != null) title.SetContinueVisible(SessionTitleRules.ShouldShowContinue(session.LatestSlot));
        }

        // 开通用确认弹窗等玩家选择；弹窗被 Esc 关掉按取消。关闭由调用方负责（ConfirmView 约定）。
        private async UniTask<bool> ConfirmAsync(string message, string confirmText)
        {
            ConfirmView confirm = await ui.OpenAsync<ConfirmView>(new ConfirmRequest(message, confirmText, CancelText));
            try
            {
                return await confirm.WaitAsync();
            }
            finally
            {
                // 已被外部关掉时 CloseAsync 是空操作。
                if (confirm != null) await ui.CloseAsync(confirm);
            }
        }

        private async UniTask RefreshAsync(CancellationToken ct)
        {
            IReadOnlyList<SlotInfo> read = await session.ReadSlotInfosAsync(ct);
            if (view == null) return;
            infos = read;
            view.Bind(infos, mode);
        }

        private SlotInfo FindInfo(int slot)
        {
            for (int i = 0; i < infos.Count; i++)
            {
                if (infos[i].Slot == slot) return infos[i];
            }

            // 摘要里没有这个槽（不该发生）：按不可用处理，读档不会被触发、新游戏会先确认。
            return SlotInfo.Unavailable(slot);
        }

        private async UniTaskVoid CloseGuardedAsync()
        {
            try
            {
                await CloseAsync();
            }
            catch (Exception e)
            {
                telemetry.TrackError("slots_close_failed", e);
                Log.Error($"SaveSlotsController：关闭选槽面板失败：{e}");
            }
        }

        private void Detach(SaveSlotsView target)
        {
            target.OnSlotClicked -= HandleSlotClicked;
            target.OnDeleteClicked -= HandleDeleteClicked;
            target.OnBack -= HandleBack;
            target.OnClosed -= HandleViewClosed;
        }

        // 关闭的同步部分：退订状态事件、清引用。幂等。
        private void ReleaseSession()
        {
            bool wasOpen = IsOpen;
            if (stateSubscription != null)
            {
                stateSubscription.Dispose();
                stateSubscription = null;
            }

            view = null;
            IsOpen = false;
            if (wasOpen) telemetry.Track("slots_closed", ("mode", mode.ToString()), ("slots", config.SlotCount));
        }

        // 作用域销毁时 UIService 往往已先释放，关面板会抛 ObjectDisposedException——那时面板已随 UIRoot 一并销毁，静默即可。
        private async UniTaskVoid CloseViewAsync(SaveSlotsView target)
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
                Log.Warn($"SaveSlotsController：关闭选槽面板失败：{e.Message}");
            }
        }
    }
}
