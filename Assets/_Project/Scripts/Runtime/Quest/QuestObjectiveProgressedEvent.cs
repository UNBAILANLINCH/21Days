// 职责：某条任务的当前目标被推进（计数增加或目标完成）时发布的事实事件。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

namespace Game.Quest
{
    public readonly struct QuestObjectiveProgressedEvent
    {
        public QuestObjectiveProgressedEvent(int questId, int objectiveIndex, int count, int required, bool objectiveCompleted)
        {
            QuestId = questId;
            ObjectiveIndex = objectiveIndex;
            Count = count;
            Required = required;
            ObjectiveCompleted = objectiveCompleted;
        }

        public int QuestId { get; }

        /// <summary>被推进的目标下标（推进前的当前目标）。</summary>
        public int ObjectiveIndex { get; }

        /// <summary>推进后该目标的累计次数。</summary>
        public int Count { get; }

        public int Required { get; }

        public bool ObjectiveCompleted { get; }
    }
}
