// 职责：追踪中的任务变化时发布的事实事件。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

namespace Game.Quest
{
    public readonly struct QuestTrackingChangedEvent
    {
        public QuestTrackingChangedEvent(int questId)
        {
            QuestId = questId;
        }

        /// <summary>新的追踪任务 id；0 = 无追踪。</summary>
        public int QuestId { get; }
    }
}
