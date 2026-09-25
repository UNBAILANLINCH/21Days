// 职责：把世界里发生的事实翻译成任务上报——对白结束上报 TalkTo，玩家进入地点半径上报 ReachLocation。
// 为什么新建：QuestService 是纯门面，不该认识对白服务与场景坐标；QuestSceneBinder 只登记不判定，塞进去会让它变成 ITickable。
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Telemetry;
using Game.Dialogue;
using UnityEngine;
using VContainer.Unity;

namespace Game.Quest
{
    /// <summary>
    /// 目标上报驱动。对白被跳过也算完成（不看 <see cref="DialogueEndedEvent.Skipped"/>）。
    /// 抵达判定每帧最多上报一次：上报会重建进行中缓存，剩下的下一帧再判。
    /// </summary>
    public sealed class QuestObjectiveDriver : IStartable, ITickable, IDisposable
    {
        private readonly QuestService service;
        private readonly QuestSceneBinder binder;
        private readonly DialogueService dialogue;
        private readonly ITelemetryScope telemetry;
        private bool subscribed;

        public QuestObjectiveDriver(QuestService service, QuestSceneBinder binder, DialogueService dialogue, ITelemetryScope telemetry)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public void Start()
        {
            if (subscribed) return;
            dialogue.OnEnded += HandleDialogueEnded;
            subscribed = true;
        }

        // 无分配、无日志、只比较平方距离；上报后立刻返回。
        public void Tick()
        {
            if (!service.IsReady) return;

            Transform anchor = binder.PlayerAnchor;
            if (anchor == null) return;

            Vector3 origin = anchor.position;
            IReadOnlyList<QuestProgress> inProgress = service.InProgress;
            for (int i = 0; i < inProgress.Count; i++)
            {
                QuestProgress progress = inProgress[i];
                if (!progress.HasCurrentObjective) continue;

                QuestObjectiveDefinition objective = progress.CurrentObjective;
                if (objective.Kind != QuestObjectiveKind.ReachLocation) continue;
                if (!binder.TryGetLocation(objective.Key, out QuestLocation location)) continue;

                float radius = location.Radius;
                if ((location.Position - origin).sqrMagnitude > radius * radius) continue;

                service.Report(QuestObjectiveKind.ReachLocation, objective.Key);
                return;
            }
        }

        public void Dispose()
        {
            if (!subscribed) return;
            dialogue.OnEnded -= HandleDialogueEnded;
            subscribed = false;
        }

        // int → string 只在对白结束时发生一次，不在每帧路径上。
        private void HandleDialogueEnded(DialogueEndedEvent e)
        {
            int advanced = service.Report(QuestObjectiveKind.TalkTo, e.DialogueId.ToString(CultureInfo.InvariantCulture));
            if (advanced == 0) return;

            telemetry.Track("talk_reported", ("dialogue", e.DialogueId), ("advanced", advanced));
        }
    }
}
