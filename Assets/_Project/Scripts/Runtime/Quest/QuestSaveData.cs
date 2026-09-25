// 职责：任务系统的存档分区——是否已初始化、追踪中的任务、激活序号计数器与所有非 Inactive 任务进度。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Quest
{
    public sealed class QuestSaveData : ISaveData
    {
        public int Version => 1;

        public bool Initialized { get; set; }

        /// <summary>0 = 无追踪。</summary>
        public int TrackedId { get; set; }

        public int NextAcceptOrder { get; set; }

        /// <summary>只存非 Inactive 的任务；缺席即 Inactive。</summary>
        public List<QuestProgressData> Quests { get; set; } = new();

        public void Migrate(int fromVersion)
        {
            // 第 1 版无迁移。
        }
    }
}
