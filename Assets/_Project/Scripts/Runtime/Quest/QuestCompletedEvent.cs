// 职责：某条任务全部目标完成时发布的事实事件。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

namespace Game.Quest
{
    public readonly struct QuestCompletedEvent
    {
        public QuestCompletedEvent(int questId)
        {
            QuestId = questId;
        }

        public int QuestId { get; }
    }
}
