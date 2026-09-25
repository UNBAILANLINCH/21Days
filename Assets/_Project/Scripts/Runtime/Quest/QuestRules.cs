// 职责：任务系统的纯 C# 规则——激活（前置 + 主线唯一）、目标上报推进、完成连锁、追踪切换、存档捕获与恢复。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

using System;
using System.Collections.Generic;
using Game.Core.Telemetry;

namespace Game.Quest
{
    public sealed class QuestRules
    {
        private static readonly Comparison<QuestProgress> ByAcceptOrder = (a, b) => a.AcceptOrder.CompareTo(b.AcceptOrder);

        private readonly ITelemetryScope telemetry;
        private readonly QuestProgress[] byIdOrder;
        private readonly Dictionary<int, QuestProgress> byId;
        private readonly List<QuestProgress> inProgress = new List<QuestProgress>();
        private readonly List<QuestProgress> reportScratch = new List<QuestProgress>();
        private int nextAcceptOrder;
        private int currentMainId;

        public QuestRules(QuestContent content, ITelemetryScope telemetry)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;

            IReadOnlyList<QuestDefinition> all = content.All;
            byIdOrder = new QuestProgress[all.Count];
            byId = new Dictionary<int, QuestProgress>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                var progress = new QuestProgress(all[i]);
                byIdOrder[i] = progress;
                byId.Add(progress.Id, progress);
            }

            // 按 id 升序遍历：多条主线同时可激活时取 id 最小的那条。
            Array.Sort(byIdOrder, (a, b) => a.Id.CompareTo(b.Id));
        }

        public event Action<int> OnActivated;

        /// <summary>(questId, objectiveIndex, count, required, objectiveCompleted)。</summary>
        public event Action<int, int, int, int, bool> OnProgressed;

        public event Action<int> OnCompleted;

        /// <summary>参数是新的追踪 id，0 = 无追踪。</summary>
        public event Action<int> OnTrackingChanged;

        /// <summary>0 = 无追踪。</summary>
        public int TrackedId { get; private set; }

        /// <summary>唯一进行中的主线 id；无则 0。</summary>
        public int CurrentMainId => currentMainId;

        /// <summary>首次 <see cref="ActivateAvailable"/> 是否已跑过（决定是否自动追踪主线）。</summary>
        public bool Initialized { get; private set; }

        /// <summary>进行中的任务，按激活序号升序。内部缓存，只在状态变化时重建。</summary>
        public IReadOnlyList<QuestProgress> InProgress => inProgress;

        public bool TryGet(int id, out QuestProgress progress) => byId.TryGetValue(id, out progress);

        public void ActivateAvailable()
        {
            for (int i = 0; i < byIdOrder.Length; i++)
            {
                QuestProgress progress = byIdOrder[i];
                if (progress.State != QuestState.Inactive || !PrerequisitesCompleted(progress.Definition)) continue;
                if (progress.Definition.Kind == QuestKind.Main && currentMainId != 0) continue;

                progress.State = QuestState.InProgress;
                progress.ObjectiveIndex = 0;
                progress.Count = 0;
                progress.AcceptOrder = ++nextAcceptOrder;
                RebuildInProgress();

                telemetry.Track("activated", ("quest", progress.Id), ("kind", progress.Definition.Kind.ToString()));
                OnActivated?.Invoke(progress.Id);
            }

            if (Initialized) return;

            Initialized = true;
            if (TrackedId == 0 && currentMainId != 0)
            {
                SetTracked(currentMainId);
            }
        }

        /// <summary>上报一次目标事实，返回本次被推进的目标数。</summary>
        public int Report(QuestObjectiveKind kind, string key, int amount = 1)
        {
            if (amount <= 0 || key == null)
            {
                telemetry.Track("report_ignored", ("kind", kind.ToString()), ("key", key ?? string.Empty), ("amount", amount));
                return 0;
            }

            // 快照一份再遍历：任务完成会重建 inProgress，不能边遍历边改。
            reportScratch.Clear();
            reportScratch.AddRange(inProgress);

            int advanced = 0;
            bool anyCompleted = false;
            for (int i = 0; i < reportScratch.Count; i++)
            {
                QuestProgress progress = reportScratch[i];
                if (!progress.HasCurrentObjective) continue;

                QuestObjectiveDefinition objective = progress.CurrentObjective;
                if (!objective.Matches(kind, key)) continue;

                advanced++;
                int objectiveIndex = progress.ObjectiveIndex;
                progress.Count += amount;
                int count = progress.Count;
                bool objectiveCompleted = count >= objective.RequiredCount;
                if (objectiveCompleted)
                {
                    progress.ObjectiveIndex++;
                    progress.Count = 0;
                }

                OnProgressed?.Invoke(progress.Id, objectiveIndex, count, objective.RequiredCount, objectiveCompleted);

                if (objectiveCompleted && progress.ObjectiveIndex >= progress.Definition.Objectives.Count)
                {
                    progress.State = QuestState.Completed;
                    anyCompleted = true;
                    RebuildInProgress();
                    telemetry.Track("completed", ("quest", progress.Id), ("kind", progress.Definition.Kind.ToString()));
                    OnCompleted?.Invoke(progress.Id);
                }
            }

            reportScratch.Clear();

            if (anyCompleted)
            {
                ActivateAvailable();
            }

            if (TrackedId != 0 && byId.TryGetValue(TrackedId, out QuestProgress tracked) && tracked.State == QuestState.Completed)
            {
                SetTracked(currentMainId);
            }

            return advanced;
        }

        /// <summary>追踪一条进行中的任务；任务不存在或不在进行中返回 false。</summary>
        public bool Track(int id)
        {
            if (!byId.TryGetValue(id, out QuestProgress progress) || progress.State != QuestState.InProgress) return false;
            if (TrackedId == id) return true;

            SetTracked(id);
            telemetry.Track("tracked", ("quest", id));
            return true;
        }

        public void Untrack()
        {
            if (TrackedId == 0) return;

            int previous = TrackedId;
            SetTracked(0);
            telemetry.Track("untracked", ("quest", previous));
        }

        /// <summary>清空后填入：当前主线（若有）→ 其余进行中任务按激活序号升序。</summary>
        public void GetOrdered(List<QuestProgress> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            if (currentMainId != 0)
            {
                buffer.Add(byId[currentMainId]);
            }

            for (int i = 0; i < inProgress.Count; i++)
            {
                if (inProgress[i].Id != currentMainId)
                {
                    buffer.Add(inProgress[i]);
                }
            }
        }

        public void CaptureInto(QuestSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            data.Initialized = Initialized;
            data.TrackedId = TrackedId;
            data.NextAcceptOrder = nextAcceptOrder;
            data.Quests.Clear();
            for (int i = 0; i < byIdOrder.Length; i++)
            {
                QuestProgress progress = byIdOrder[i];
                if (progress.State == QuestState.Inactive) continue;

                data.Quests.Add(new QuestProgressData
                {
                    Id = progress.Id,
                    State = (int)progress.State,
                    ObjectiveIndex = progress.ObjectiveIndex,
                    Count = progress.Count,
                    AcceptOrder = progress.AcceptOrder
                });
            }
        }

        /// <summary>重置为全 Inactive 后按存档恢复；不抛事件。</summary>
        public void Restore(QuestSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            for (int i = 0; i < byIdOrder.Length; i++)
            {
                byIdOrder[i].Reset();
            }

            Initialized = data.Initialized;
            nextAcceptOrder = data.NextAcceptOrder;

            List<QuestProgressData> quests = data.Quests;
            int count = quests == null ? 0 : quests.Count;
            for (int i = 0; i < count; i++)
            {
                QuestProgressData saved = quests[i];
                if (saved == null) continue;

                if (!byId.TryGetValue(saved.Id, out QuestProgress progress))
                {
                    telemetry.TrackWarn("restore_skipped", TelemetryProps.Of(("quest", saved.Id), ("reason", "unknown_id")));
                    continue;
                }

                var state = (QuestState)saved.State;
                if (state != QuestState.InProgress && state != QuestState.Completed)
                {
                    telemetry.TrackWarn("restore_skipped", TelemetryProps.Of(("quest", saved.Id), ("reason", "bad_state")));
                    continue;
                }

                if (state == QuestState.InProgress &&
                    (saved.ObjectiveIndex < 0 || saved.ObjectiveIndex >= progress.Definition.Objectives.Count))
                {
                    // 表改短了导致下标越界：宁可让它回到 Inactive 重新激活，也不留一个无目标的进行中任务。
                    telemetry.TrackWarn("restore_skipped", TelemetryProps.Of(("quest", saved.Id), ("reason", "bad_index")));
                    continue;
                }

                progress.State = state;
                progress.ObjectiveIndex = saved.ObjectiveIndex;
                progress.Count = saved.Count < 0 ? 0 : saved.Count;
                progress.AcceptOrder = saved.AcceptOrder;
                if (saved.AcceptOrder > nextAcceptOrder)
                {
                    nextAcceptOrder = saved.AcceptOrder;
                }
            }

            RebuildInProgress();

            TrackedId = data.TrackedId;
            if (TrackedId != 0 && (!byId.TryGetValue(TrackedId, out QuestProgress tracked) || tracked.State != QuestState.InProgress))
            {
                TrackedId = 0;
            }
        }

        private void SetTracked(int id)
        {
            if (TrackedId == id) return;

            TrackedId = id;
            OnTrackingChanged?.Invoke(id);
        }

        private bool PrerequisitesCompleted(QuestDefinition definition)
        {
            IReadOnlyList<int> prerequisites = definition.Prerequisites;
            for (int i = 0; i < prerequisites.Count; i++)
            {
                if (!byId.TryGetValue(prerequisites[i], out QuestProgress prerequisite) ||
                    prerequisite.State != QuestState.Completed)
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildInProgress()
        {
            inProgress.Clear();
            currentMainId = 0;
            for (int i = 0; i < byIdOrder.Length; i++)
            {
                QuestProgress progress = byIdOrder[i];
                if (progress.State != QuestState.InProgress) continue;

                inProgress.Add(progress);
                if (progress.Definition.Kind == QuestKind.Main && currentMainId == 0)
                {
                    currentMainId = progress.Id;
                }
            }

            inProgress.Sort(ByAcceptOrder);
        }
    }
}
