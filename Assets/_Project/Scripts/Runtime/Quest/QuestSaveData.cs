// 职责：任务系统的存档分区——是否已初始化、追踪中的任务、激活序号计数器与所有非 Inactive 任务进度。
// 为什么新建：任务系统首次落地（PRP/quest-system），框架里没有任务概念，无可复用 / 扩展之处。

using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Quest
{
    public sealed class QuestSaveData : ISaveData
    {
        public int Version => 3;

        public bool Initialized { get; set; }

        /// <summary>0 = 无追踪。</summary>
        public int TrackedId { get; set; }

        public int NextAcceptOrder { get; set; }

        /// <summary>只存非 Inactive 的任务；缺席即 Inactive。</summary>
        public List<QuestProgressData> Quests { get; set; } = new();

        public void Migrate(int fromVersion)
        {
            if (fromVersion >= 3 || Quests == null) return;
            foreach (var quest in Quests)
            {
                if (quest == null || quest.Id != 900) continue;
                // 移除七项被误当任务的人间对白检查点，妖界成果及后续进度保留。
                if (fromVersion < 2)
                {
                    if (quest.ObjectiveIndex < 7) quest.Count = 0;
                    quest.ObjectiveIndex = quest.ObjectiveIndex < 7 ? 0 : quest.ObjectiveIndex - 7;
                }
                // 旧末项已在会面之后，迁成真实完成，不能重新激活整条任务。
                if (quest.ObjectiveIndex >= 4)
                {
                    quest.ObjectiveIndex = 4; quest.Count = 0; quest.State = (int)QuestState.Completed;
                }
            }
        }
    }
}
