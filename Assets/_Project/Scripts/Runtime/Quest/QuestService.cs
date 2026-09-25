// 职责：任务系统对外门面（根作用域单例）——启动时建规则、读档、激活可接任务；转发上报 / 追踪，
//   每次状态变化后写回存档分区并按固定顺序发布 MessagePipe 事件。
// 为什么新建：QuestRules 是纯 C# 规则，不认识存档服务与 MessagePipe；把这些接线塞进规则会让它无法脱离容器单测。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using MessagePipe;

namespace Game.Quest
{
    /// <summary>
    /// 任务服务。内容表非法时 <see cref="InitializeAsync"/> 记 Error 后正常返回（不炸启动），<see cref="IsReady"/> 保持 false，
    /// 此后查询返回空 / 0 / false，上报被忽略。
    /// <para>
    /// 事件不在规则回调里直接发布：先收集，待一次操作（初始化 / 上报 / 追踪）结束、存档分区写回后，
    /// 按 Activated → Progressed → Completed → TrackingChanged 顺序统一发布，订阅者读到的一定是操作后的完整状态。
    /// </para>
    /// </summary>
    public sealed class QuestService : IGameService, IDisposable
    {
        private static readonly QuestProgress[] Empty = Array.Empty<QuestProgress>();

        private readonly QuestCatalog catalog;
        private readonly ISaveService saves;
        private readonly IPublisher<QuestActivatedEvent> activatedPublisher;
        private readonly IPublisher<QuestObjectiveProgressedEvent> progressedPublisher;
        private readonly IPublisher<QuestCompletedEvent> completedPublisher;
        private readonly IPublisher<QuestTrackingChangedEvent> trackingPublisher;
        private readonly ITelemetryScope telemetry;

        private readonly List<QuestActivatedEvent> pendingActivated = new List<QuestActivatedEvent>();
        private readonly List<QuestObjectiveProgressedEvent> pendingProgressed = new List<QuestObjectiveProgressedEvent>();
        private readonly List<QuestCompletedEvent> pendingCompleted = new List<QuestCompletedEvent>();
        private readonly List<QuestTrackingChangedEvent> pendingTracking = new List<QuestTrackingChangedEvent>();

        // Flush 重入护栏：发布事件时订阅者可能在回调里同步再调 Report/Track/Untrack，
        // 若直接遍历 pending 再 Clear，嵌套 Flush 会在外层还在发布时把同一批列表清空，外层剩余事件被静默丢弃。
        // 做法：每轮把 pending 换到 batch 再发布，pending 继续收集新事件；flushing 标记防止重入方重复跑发布循环，
        // 外层 while 会接着处理重入期间新收集到的 pending，事件不丢、也不会无限递归。
        private bool flushing;
        private readonly List<QuestActivatedEvent> batchActivated = new List<QuestActivatedEvent>();
        private readonly List<QuestObjectiveProgressedEvent> batchProgressed = new List<QuestObjectiveProgressedEvent>();
        private readonly List<QuestCompletedEvent> batchCompleted = new List<QuestCompletedEvent>();
        private readonly List<QuestTrackingChangedEvent> batchTracking = new List<QuestTrackingChangedEvent>();

        private QuestRules rules;

        public QuestService(
            QuestCatalog catalog,
            ISaveService saves,
            IPublisher<QuestActivatedEvent> activated,
            IPublisher<QuestObjectiveProgressedEvent> progressed,
            IPublisher<QuestCompletedEvent> completed,
            IPublisher<QuestTrackingChangedEvent> tracking,
            ITelemetryScope telemetry)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            activatedPublisher = activated ?? throw new ArgumentNullException(nameof(activated));
            progressedPublisher = progressed ?? throw new ArgumentNullException(nameof(progressed));
            completedPublisher = completed ?? throw new ArgumentNullException(nameof(completed));
            trackingPublisher = tracking ?? throw new ArgumentNullException(nameof(tracking));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>内容合法且初始化完成后为 true。</summary>
        public bool IsReady => rules != null;

        /// <summary>任务内容；未就绪时抛 <see cref="InvalidOperationException"/>。</summary>
        public QuestContent Content
        {
            get
            {
                if (!IsReady) throw new InvalidOperationException("QuestService 尚未就绪（未初始化或任务表非法），不能取 Content");
                return catalog.Content;
            }
        }

        /// <summary>0 = 无追踪或未就绪。</summary>
        public int TrackedId => IsReady ? rules.TrackedId : 0;

        /// <summary>唯一进行中的主线 id；无或未就绪为 0。</summary>
        public int CurrentMainId => IsReady ? rules.CurrentMainId : 0;

        /// <summary>进行中的任务，按激活序号升序；未就绪为空。上报后内部缓存会重建，不要跨上报持有遍历。</summary>
        public IReadOnlyList<QuestProgress> InProgress => IsReady ? rules.InProgress : Empty;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            try
            {
                rules = new QuestRules(catalog.Content, telemetry);
            }
            catch (ArgumentException e)
            {
                Log.Error("QuestService：任务表校验失败，任务系统本次不可用（查询返回空、上报被忽略）。修表后重启。原因：" + e.Message);
                telemetry.TrackError("initialize_failed", e.Message);
                return UniTask.CompletedTask;
            }

            rules.OnActivated += HandleActivated;
            rules.OnProgressed += HandleProgressed;
            rules.OnCompleted += HandleCompleted;
            rules.OnTrackingChanged += HandleTrackingChanged;

            rules.Restore(saves.Get<QuestSaveData>());
            rules.ActivateAvailable();
            Flush();

            telemetry.Track("initialized", ("quests", rules.InProgress.Count));
            return UniTask.CompletedTask;
        }

        /// <summary>上报一次目标事实，返回本次被推进的目标数；未就绪返回 0。</summary>
        public int Report(QuestObjectiveKind kind, string key, int amount = 1)
        {
            if (!IsReady)
            {
                Log.Warn("QuestService 未就绪，忽略任务上报。");
                telemetry.TrackWarn("report_ignored", TelemetryProps.Of(("kind", kind.ToString()), ("reason", "not_ready")));
                return 0;
            }

            int advanced = rules.Report(kind, key, amount);
            Flush();
            return advanced;
        }

        /// <summary>追踪一条进行中的任务；不存在、不在进行中或未就绪返回 false。</summary>
        public bool Track(int id)
        {
            if (!IsReady) return false;

            bool ok = rules.Track(id);
            Flush();
            return ok;
        }

        public void Untrack()
        {
            if (!IsReady) return;

            rules.Untrack();
            Flush();
        }

        public bool TryGet(int id, out QuestProgress progress)
        {
            if (!IsReady)
            {
                progress = null;
                return false;
            }

            return rules.TryGet(id, out progress);
        }

        public bool TryGetTracked(out QuestProgress progress)
        {
            if (!IsReady || rules.TrackedId == 0)
            {
                progress = null;
                return false;
            }

            return rules.TryGet(rules.TrackedId, out progress);
        }

        /// <summary>清空后填入：当前主线（若有）→ 其余进行中任务按激活序号升序；未就绪时只清空。</summary>
        public void GetOrdered(List<QuestProgress> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            if (!IsReady)
            {
                buffer.Clear();
                return;
            }

            rules.GetOrdered(buffer);
        }

        public void Dispose()
        {
            if (rules == null) return;

            rules.OnActivated -= HandleActivated;
            rules.OnProgressed -= HandleProgressed;
            rules.OnCompleted -= HandleCompleted;
            rules.OnTrackingChanged -= HandleTrackingChanged;
        }

        // 每次重新 Get：读档 Commit 会整体替换分区实例，缓存旧实例会把进度写进一份没人读的对象。
        // 重入护栏：订阅者可能在事件回调里同步再调 Report/Track/Untrack，导致 Flush 嵌套调用。
        // 每轮把 pending 换到 batch 再发布，pending 继续收集重入期间产生的新事件；flushing 标记防止
        // 重入方重复跑发布循环（写回分区仍然每次都做），外层 while 会接着处理这些新事件，不丢也不递归。
        private void Flush()
        {
            rules.CaptureInto(saves.Get<QuestSaveData>());

            if (flushing) return;

            flushing = true;
            try
            {
                while (HasPending())
                {
                    SwapIntoBatch();

                    for (int i = 0; i < batchActivated.Count; i++) activatedPublisher.Publish(batchActivated[i]);
                    batchActivated.Clear();

                    for (int i = 0; i < batchProgressed.Count; i++) progressedPublisher.Publish(batchProgressed[i]);
                    batchProgressed.Clear();

                    for (int i = 0; i < batchCompleted.Count; i++) completedPublisher.Publish(batchCompleted[i]);
                    batchCompleted.Clear();

                    for (int i = 0; i < batchTracking.Count; i++) trackingPublisher.Publish(batchTracking[i]);
                    batchTracking.Clear();
                }
            }
            finally
            {
                flushing = false;
            }
        }

        private bool HasPending() =>
            pendingActivated.Count > 0 || pendingProgressed.Count > 0 || pendingCompleted.Count > 0 || pendingTracking.Count > 0;

        private void SwapIntoBatch()
        {
            batchActivated.AddRange(pendingActivated);
            pendingActivated.Clear();

            batchProgressed.AddRange(pendingProgressed);
            pendingProgressed.Clear();

            batchCompleted.AddRange(pendingCompleted);
            pendingCompleted.Clear();

            batchTracking.AddRange(pendingTracking);
            pendingTracking.Clear();
        }

        private void HandleActivated(int questId) => pendingActivated.Add(new QuestActivatedEvent(questId));

        private void HandleProgressed(int questId, int objectiveIndex, int count, int required, bool objectiveCompleted) =>
            pendingProgressed.Add(new QuestObjectiveProgressedEvent(questId, objectiveIndex, count, required, objectiveCompleted));

        private void HandleCompleted(int questId) => pendingCompleted.Add(new QuestCompletedEvent(questId));

        private void HandleTrackingChanged(int questId) => pendingTracking.Add(new QuestTrackingChangedEvent(questId));
    }
}
