// 职责：背包里**多了**东西这个事实事件——让开着的背包面板重读并刷新列表。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：CrateCollectedEvent 带箱子键与奖励内容，只在开箱那条路发；MonsterDroppedEvent 带妖物 id，
//      只在怪物掉落那条路发。合成产物入包既没有箱子键也不是怪物掉的，硬套那两个事件会让订阅方
//      收到「语义不对的载荷」（面板只想知道「背包变了」，不关心怎么变的）。
//   2. 扩展不行：往 LootResetEvent 上加「只是加了一件」的语义名实不符——那个事件的含义是「进度被整体清空」，
//      背包面板订阅它是为了重建列表，语义完全不同。
// 谁发 / 谁订：发布方 LootService.GrantItems（入包成功之后）；订阅方 InventoryPanelController（面板开着时刷新）。
namespace Game.Loot
{
    /// <summary>背包新增物品。发布方：<see cref="LootService.GrantItems"/>。载荷是这次加的是哪件、加了几件。</summary>
    public readonly struct BackpackChangedEvent
    {
        /// <param name="itemId">这次加进背包的道具 id（tbitem 主键）。</param>
        /// <param name="count">这次加进去的件数。</param>
        public BackpackChangedEvent(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        /// <summary>加进背包的道具 id。</summary>
        public int ItemId { get; }

        /// <summary>加进去的件数。</summary>
        public int Count { get; }
    }
}
