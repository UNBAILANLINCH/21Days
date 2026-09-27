// 职责：存档会话——当前用哪个槽、新游戏 / 继续 / 删除 / 读槽信息、自动保存请求的合并与闸门评估、
//   游玩时长累计、落盘后发事件与弹「已保存」。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：没有任何现成类型承担「谁在什么时候把哪些分区写到哪个槽」；
//   2. 扩展不行：JsonSaveService 是框架层，不能认识任务标题、遭遇、对白这些玩法名词（PRP D1）；
//   所以在 Session 模块新建；玩法现场经 ISessionStateSource 取，本类可在 EditMode 里直接 new 出来测。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Monster;
using MessagePipe;
using VContainer.Unity;

namespace Game.Session
{
    /// <summary>
    /// 存档会话（根作用域单例 + 启动服务 + 每帧入口点）。槽号从 1 起，<see cref="CurrentSlot"/> 为 0 表示还没开局（标题页）。
    /// <para>
    /// 自动保存：各触发点调 <see cref="RequestSave"/> 只记一个待处理标志；<see cref="Tick"/> 在闸门
    /// （<see cref="SaveGateRules.CanSave"/>）打开时取走并落盘一次，多次请求合并成一次。
    /// 离开玩法状态与退出游戏走 <see cref="SaveNowAsync"/> 直接保存，不等闸门。
    /// </para>
    /// <para>不持有任何分区实例：读档会整体替换分区，每次都 <c>saves.Get&lt;T&gt;()</c> 重取。</para>
    /// </summary>
    public sealed class GameSession : IGameService, ITickable, IDisposable
    {
        private readonly ISaveService saves;
        private readonly IClock clock;
        private readonly ILogicClock logicClock;
        private readonly IGameFlow flow;
        private readonly ISessionStateSource state;
        private readonly SessionConfig config;
        private readonly INotificationService notifications;
        private readonly IPublisher<SessionStartedEvent> started;
        private readonly IPublisher<SaveCompletedEvent> completed;
        private readonly ITelemetryScope telemetry;

        private SaveGateRules.PendingSave pending;
        private int savesInFlight;
        private float playtimeSeconds;

        /// <remarks>
        /// 比契约多一个 <see cref="ILogicClock"/>：遭遇快照要记逻辑 tick（<c>EncounterStep.Capture(tick)</c>），
        /// <see cref="IClock"/> 只有渲染帧时间，两种时间不能混用（<c>ILogicClock</c> 文件头）。
        /// </remarks>
        public GameSession(
            ISaveService saves,
            IClock clock,
            ILogicClock logicClock,
            IGameFlow flow,
            ISessionStateSource state,
            SessionConfig config,
            INotificationService notifications,
            IPublisher<SessionStartedEvent> started,
            IPublisher<SaveCompletedEvent> completed,
            ITelemetryScope telemetry)
        {
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.logicClock = logicClock ?? throw new ArgumentNullException(nameof(logicClock));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            // SessionConfig 是 ScriptableObject，判空只用 ==。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            this.started = started ?? throw new ArgumentNullException(nameof(started));
            this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>本局使用的槽；0 = 还没开局（标题页），这时一切保存请求都被忽略。</summary>
        public int CurrentSlot { get; private set; }

        /// <summary>保存时间最新的可用槽；0 = 没有可用存档（标题页「继续」据此禁用）。</summary>
        public int LatestSlot { get; private set; }

        /// <summary>是否有尚未落盘的自动保存请求。</summary>
        public bool HasPendingSave => pending.HasRequest;

        /// <summary>本局累计游玩秒数（落盘时写进 <see cref="SessionSaveData.PlaytimeSeconds"/>）。</summary>
        public float PlaytimeSeconds => playtimeSeconds;

        /// <summary>启动时扫一遍各槽得到 <see cref="LatestSlot"/>；只读候选，不加载进内存。</summary>
        public async UniTask InitializeAsync(CancellationToken ct)
        {
            await RefreshLatestSlotAsync(ct);
            telemetry.Track("initialized", ("latest", LatestSlot), ("slots", config.SlotCount));
        }

        /// <summary>
        /// 在 <paramref name="slot"/> 开新游戏：丢弃内存分区 → 元数据置初值 → 清遭遇恢复 → 发 <see cref="SessionStartedEvent"/>
        /// → 切进遭遇状态。第一次落盘由「场景切换完成」触发（SaveTriggerBridge）。返回切换是否成功。
        /// </summary>
        public async UniTask<bool> NewGameAsync(int slot, CancellationToken ct = default)
        {
            if (!IsValidSlot(slot))
            {
                Log.Warn($"新游戏槽号 {slot} 越界（1..{config.SlotCount}），忽略");
                telemetry.TrackWarn("new_game_rejected", TelemetryProps.Of(("slot", slot)));
                return false;
            }

            saves.ResetAll();
            CurrentSlot = slot;
            pending = default;
            playtimeSeconds = 0f;

            SessionSaveData meta = saves.Get<SessionSaveData>();
            meta.SavedAtUtcTicks = 0;
            meta.PlaytimeSeconds = 0f;
            meta.ProgressText = config.ProgressPlaceholder;
            meta.SceneKey = saves.Get<EncounterSaveData>().SceneKey;

            state.PrepareRestore(false);
            started.Publish(new SessionStartedEvent(slot, true));
            telemetry.Track("new_game", ("slot", slot));
            return await EnterGameplayAsync(ct);
        }

        /// <summary>
        /// 读 <paramref name="slot"/> 继续：先只读候选校验（损坏 / 高版本 / 缺元数据都算失败），失败则通知「存档不可用」、
        /// 内存与 <see cref="CurrentSlot"/> 都不动并返回 false；成功则整体载入、准备遭遇恢复、发事件、切进遭遇状态。
        /// </summary>
        public async UniTask<bool> ContinueAsync(int slot, CancellationToken ct = default)
        {
            if (!IsValidSlot(slot))
            {
                Log.Warn($"继续的槽号 {slot} 越界（1..{config.SlotCount}），忽略");
                telemetry.TrackWarn("continue_rejected", TelemetryProps.Of(("slot", slot), ("reason", "invalid_slot")));
                NotifyLoadFailed();
                return false;
            }

            bool loaded = false;
            bool loadThrew = false;
            try
            {
                // 先读候选：LoadAsync 会整体替换内存分区，缺元数据的文件读进来就回不去了。
                SaveSnapshot candidate = saves.Exists(slot) ? await saves.ReadCandidateAsync(slot, ct) : null;
                if (candidate != null && candidate.Contains<SessionSaveData>())
                {
                    loaded = await saves.LoadAsync(slot, ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error($"读存档槽 {slot} 时出错，保持当前状态：{e}");
                telemetry.TrackError("continue_failed", e, TelemetryProps.Of(("slot", slot)));
                loaded = false;
                loadThrew = true;
            }

            if (!loaded)
            {
                // 异常路径已经在上面的 catch 里记过 continue_failed，这里只补「候选缺元数据」这一种拒绝原因，避免同一次失败埋两条。
                if (!loadThrew)
                {
                    telemetry.TrackWarn("continue_rejected", TelemetryProps.Of(("slot", slot), ("reason", "missing_metadata")));
                }

                NotifyLoadFailed();
                return false;
            }

            CurrentSlot = slot;
            pending = default;
            playtimeSeconds = saves.Get<SessionSaveData>().PlaytimeSeconds;

            state.PrepareRestore(true);
            started.Publish(new SessionStartedEvent(slot, false));
            telemetry.Track("continue", ("slot", slot));
            return await EnterGameplayAsync(ct);
        }

        /// <summary>读全部槽的摘要（槽 1..SlotCount），只读候选，不碰内存分区。</summary>
        public async UniTask<IReadOnlyList<SlotInfo>> ReadSlotInfosAsync(CancellationToken ct = default)
        {
            var infos = new List<SlotInfo>(config.SlotCount);
            for (int slot = 1; slot <= config.SlotCount; slot++)
            {
                infos.Add(await ReadSlotInfoAsync(slot, ct));
            }

            return infos;
        }

        /// <summary>删除槽文件；删的是当前槽则 <see cref="CurrentSlot"/> 归 0。完成后刷新 <see cref="LatestSlot"/>。</summary>
        public async UniTask DeleteSlotAsync(int slot, CancellationToken ct = default)
        {
            saves.Delete(slot);
            if (CurrentSlot == slot)
            {
                CurrentSlot = 0;
                pending = default;
            }

            telemetry.Track("slot_deleted", ("slot", slot));
            await RefreshLatestSlotAsync(ct);
        }

        /// <summary>请求一次自动保存（合并式）。还没开局时忽略。真正落盘在 <see cref="Tick"/> 闸门打开时。</summary>
        public void RequestSave(string reason)
        {
            if (CurrentSlot == 0) return;
            pending = SaveGateRules.Request(pending, reason);
        }

        /// <summary>
        /// 每帧：玩法状态中累加游玩时长（不受时间缩放）；有待处理请求、没有落盘在途、闸门打开时取走并落盘一次。
        /// 零分配：只有几次布尔比较，落盘本身按请求频率发生，不是每帧。
        /// </summary>
        public void Tick()
        {
            if (CurrentSlot == 0) return;

            bool gameplay = state.IsGameplayState;
            if (gameplay) playtimeSeconds += clock.UnscaledDeltaTime;

            // 防重入：落盘在途时新请求继续挂着，等这次写完、下一帧再评估。
            if (!pending.HasRequest || savesInFlight > 0) return;
            if (!SaveGateRules.CanSave(gameplay, state.DialogueRunning, state.AnyPanelOpen, state.BattleResultPending)) return;

            pending = SaveGateRules.Take(pending, out string reason);
            SaveNowAsync(reason).Forget();
        }

        /// <summary>
        /// 立刻保存：**同步**捕获遭遇现场、更新元数据（这一段在第一个 await 之前完成，
        /// 所以在 <c>GameStateChangingEvent</c> 回调里调用时捕获发生在前一状态 Exit 之前），再异步落盘。
        /// 还没开局、或不在玩法状态（捕获会得到停掉的遭遇）时不写并返回 false。
        /// 结果发 <see cref="SaveCompletedEvent"/>，成功弹「已保存」。
        /// </summary>
        public async UniTask<bool> SaveNowAsync(string reason, CancellationToken ct = default)
        {
            if (CurrentSlot == 0) return false;
            if (string.IsNullOrEmpty(reason)) reason = "unknown";

            if (!state.IsGameplayState)
            {
                telemetry.Track("save_skipped", ("reason", reason));
                return false;
            }

            int slot = CurrentSlot;
            long startTicks = Stopwatch.GetTimestamp();

            // 本次捕获的是最新现场，此前挂着的请求一并算作满足。
            pending = default;
            savesInFlight++;
            bool success;
            try
            {
                state.CaptureEncounter(logicClock.Tick);

                SessionSaveData meta = saves.Get<SessionSaveData>();
                meta.SavedAtUtcTicks = clock.UtcNow.Ticks;
                meta.PlaytimeSeconds = playtimeSeconds;
                meta.ProgressText = state.BuildProgressText() ?? string.Empty;
                meta.SceneKey = saves.Get<EncounterSaveData>().SceneKey;

                success = await saves.SaveAsync(slot, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error($"保存到槽 {slot} 失败（原因 {reason}）：{e}");
                success = false;
            }
            finally
            {
                savesInFlight--;
            }

            long ms = (Stopwatch.GetTimestamp() - startTicks) * 1000L / Stopwatch.Frequency;
            telemetry.Track("save_completed", ("reason", reason), ("success", success), ("ms", ms));

            if (success) LatestSlot = slot;
            completed.Publish(new SaveCompletedEvent(slot, reason, success));
            if (success) notifications.Show(config.SaveNoticeTitle, null, config.SaveNoticeSeconds);
            return success;
        }

        public void Dispose()
        {
            if (!pending.HasRequest) return;

            // 容器销毁（退出 Play / 关游戏）时还有没落盘的请求：退出路径已由退出钩子兜过一次，这里只留痕。
            Log.Warn($"会话结束时仍有未落盘的保存请求（原因 {pending.Reason}），已丢弃");
            pending = default;
        }

        private async UniTask<bool> EnterGameplayAsync(CancellationToken ct)
        {
            try
            {
                await flow.GoToAsync<MonsterEncounterState>(ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error($"进入遭遇状态失败（槽 {CurrentSlot}）：{e}");
                telemetry.TrackError("enter_gameplay_failed", e, TelemetryProps.Of(("slot", CurrentSlot)));
                return false;
            }
        }

        private async UniTask<SlotInfo> ReadSlotInfoAsync(int slot, CancellationToken ct)
        {
            if (!saves.Exists(slot)) return SlotInfo.Empty(slot);

            SaveSnapshot candidate;
            try
            {
                candidate = await saves.ReadCandidateAsync(slot, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // 迁移代码抛错会往上抛（JsonSaveService 约定）；对选槽来说这个槽就是读不了。
                Log.Error($"读存档槽 {slot} 的摘要时出错：{e}");
                return SlotInfo.Unavailable(slot);
            }

            if (candidate == null || !candidate.Contains<SessionSaveData>()) return SlotInfo.Unavailable(slot);

            SessionSaveData meta = candidate.Require<SessionSaveData>();
            if (meta.SavedAtUtcTicks < DateTime.MinValue.Ticks || meta.SavedAtUtcTicks > DateTime.MaxValue.Ticks)
            {
                return SlotInfo.Unavailable(slot);
            }

            return new SlotInfo(
                slot,
                SlotState.Available,
                new DateTime(meta.SavedAtUtcTicks, DateTimeKind.Utc),
                meta.PlaytimeSeconds,
                meta.ProgressText);
        }

        private async UniTask RefreshLatestSlotAsync(CancellationToken ct)
        {
            IReadOnlyList<SlotInfo> infos = await ReadSlotInfosAsync(ct);
            int latest = 0;
            DateTime latestAt = DateTime.MinValue;
            for (int i = 0; i < infos.Count; i++)
            {
                SlotInfo info = infos[i];
                if (info.State != SlotState.Available) continue;
                if (latest == 0 || info.SavedAtUtc > latestAt)
                {
                    latest = info.Slot;
                    latestAt = info.SavedAtUtc;
                }
            }

            LatestSlot = latest;
        }

        private bool IsValidSlot(int slot) => slot >= 1 && slot <= config.SlotCount;

        private void NotifyLoadFailed() => notifications.Show(config.LoadFailedTitle);
    }
}
