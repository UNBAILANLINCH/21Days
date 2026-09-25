// 职责：任务类别——主线同一时刻最多一条进行中，支线不受此限。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

namespace Game.Quest
{
    public enum QuestKind
    {
        Main = 0,
        Side = 1
    }
}
