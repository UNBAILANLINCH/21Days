// 职责：单条任务进度的存档 DTO（纯数据，状态存枚举整数值）。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

namespace Game.Quest
{
    public sealed class QuestProgressData
    {
        public int Id { get; set; }

        /// <summary><see cref="QuestState"/> 的整数值。</summary>
        public int State { get; set; }

        public int ObjectiveIndex { get; set; }

        public int Count { get; set; }

        public int AcceptOrder { get; set; }
    }
}
