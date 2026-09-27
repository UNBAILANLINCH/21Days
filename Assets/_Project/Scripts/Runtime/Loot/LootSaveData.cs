// 职责：物资箱模块的存档分区——已开过的箱子键与最小背包（物品 id → 数量）。
// 为什么新建：Loot 模块首次落地（PRP/exploration-whitebox 3.1），框架与既有模块里没有背包 / 拾取记录的分区；
//   塞进 QuestSaveData 会让任务存档混入与任务无关的物品数据。
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Loot
{
    public sealed class LootSaveData : ISaveData
    {
        public int Version => 1;

        /// <summary>已打开的箱子键（<see cref="SupplyCrate.Key"/>），按打开顺序。</summary>
        public List<string> CollectedCrates { get; set; } = new();

        /// <summary>最小背包：键 = tbitem 主键，值 = 累计数量。</summary>
        public Dictionary<int, int> Items { get; set; } = new();

        public void Migrate(int fromVersion)
        {
            // 第 1 版无迁移。
        }
    }
}
