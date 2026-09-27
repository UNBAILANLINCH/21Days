// 职责：某个物资箱已被打开、奖励已入背包这个事实事件。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；Loot 模块首次落地，没有可复用的事件。

namespace Game.Loot
{
    /// <summary>物资箱已打开。发布方：<see cref="LootService.TryCollect"/>，在存档分区写入、任务上报、通知之后发布。</summary>
    public readonly struct CrateCollectedEvent
    {
        public CrateCollectedEvent(string key, int itemId, int count)
        {
            Key = key;
            ItemId = itemId;
            Count = count;
        }

        /// <summary>箱子键（<see cref="SupplyCrate.Key"/>）。</summary>
        public string Key { get; }

        /// <summary>奖励物品 id（tbitem 主键）。</summary>
        public int ItemId { get; }

        public int Count { get; }
    }
}
