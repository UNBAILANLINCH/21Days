// 职责：某条任务被激活为进行中时发布的事实事件。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

namespace Game.Quest
{
    public readonly struct QuestActivatedEvent
    {
        public QuestActivatedEvent(int questId)
        {
            QuestId = questId;
        }

        public int QuestId { get; }
    }
}
