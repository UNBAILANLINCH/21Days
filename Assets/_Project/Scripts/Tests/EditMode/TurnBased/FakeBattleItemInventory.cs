// 职责：测试用的假背包——只回答「有没有这件道具」。
//
// 为什么要它：回合制内核不许读 Inventory（那是别的模块），持有关系通过 IBattleItemInventory 注入；
// 测试用这个假实现替换它，从而验「未拥有 → 不能用」与「拥有 → 能用」两边的判定。

using System.Collections.Generic;
using Game.TurnBased;

namespace Game.Tests.EditMode.TurnBased
{
    /// <summary>假背包：按 id 集合回答持有关系。</summary>
    public sealed class FakeBattleItemInventory : IBattleItemInventory
    {
        private readonly HashSet<string> owned;

        /// <summary>用一串道具 id 构造。</summary>
        public FakeBattleItemInventory(params string[] itemIds)
        {
            owned = new HashSet<string>(itemIds ?? new string[0]);
        }

        /// <summary>查询次数，用来验「真的问过背包」。</summary>
        public int QueryCount { get; private set; }

        /// <summary>是否拥有。</summary>
        public bool Owns(string itemId)
        {
            QueryCount++;
            return itemId != null && owned.Contains(itemId);
        }

        /// <summary>补一件道具（模拟战斗内拾取）。</summary>
        public void Grant(string itemId) => owned.Add(itemId);
    }
}
