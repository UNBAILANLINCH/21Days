// 职责：任务目标的判定类别——上报时按 (类别, 键) 匹配当前目标。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

namespace Game.Quest
{
    public enum QuestObjectiveKind
    {
        /// <summary>与某角色对话；键是角色 id（整数字符串）。</summary>
        TalkTo = 0,

        /// <summary>抵达某地点；键是地点键。</summary>
        ReachLocation = 1,

        /// <summary>通用计数；键由上报方约定，需累计到 RequiredCount。</summary>
        Counter = 2
    }
}
