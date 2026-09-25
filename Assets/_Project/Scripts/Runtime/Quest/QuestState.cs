// 职责：单条任务的生命周期状态；数值会写进存档，只追加不改值。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

namespace Game.Quest
{
    public enum QuestState
    {
        Inactive = 0,
        InProgress = 1,
        Completed = 2
    }
}
