// 职责：某只怪已死、按种类掉落已入背包这个事实事件。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；Loot 模块原有事件只覆盖物资箱
//   （CrateCollectedEvent 带的是箱子键），怪物掉落没有箱子键，语义不同不能复用同一个结构体。
// 与剧情事实字典（ai-docs/docs/story-facts.md）的关系：本事件只交「掉了哪些 item id」，
//   **不写任何事实键**。`item.<id>.owned` / `item.tooth.owned` 这类键的 owner 是 Inventory（见该文 4.1），
//   由后续波次按 item id 映射后写入，本模块不越界。
using System;
using System.Collections.Generic;

namespace Game.Loot
{
    /// <summary>
    /// 怪物掉落已入背包。发布方：<see cref="LootService.SettleMonsterDrop"/>，在物品累加与通知之后发布。
    /// </summary>
    public readonly struct MonsterDroppedEvent
    {
        public MonsterDroppedEvent(int yaoId, IReadOnlyList<int> itemIds)
        {
            YaoId = yaoId;
            ItemIds = itemIds ?? Array.Empty<int>();
        }

        /// <summary>这只怪在妖物表（tbyao）里的 id；tbyao 的 drop_items 就是按它查出来的。</summary>
        public int YaoId { get; }

        /// <summary>
        /// 本次掉落的 item id（tbitem 主键），按表里的顺序；没有任何掉落时是空列表。
        /// 只读视图，订阅方不要改。
        /// </summary>
        public IReadOnlyList<int> ItemIds { get; }
    }
}
