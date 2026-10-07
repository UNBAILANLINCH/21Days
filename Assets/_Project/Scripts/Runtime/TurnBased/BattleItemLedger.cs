// 职责：一场战斗里「哪些道具已经用过了」的账本——道具使用规则里唯一带状态的部分。
//
// 为什么单独一个类：`ItemUseRules` 是纯判定（可以逐条测），而「本场用过没有」是随战斗存活的状态；
// 把它放在会话里会让会话同时管账本与回合，测试也得先跑半个回合才能验一条道具规则。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:42`（用过了则置暗；在之前战斗时用过了则不显示）、
// :58（施放招式之前用道具）；转写：`docs/design/features-spotlight/09_BOSS战.md:193`（R45）。

using System.Collections.Generic;

namespace Game.TurnBased
{
    /// <summary>一场战斗内的道具使用账本。</summary>
    public sealed class BattleItemLedger
    {
        private readonly HashSet<string> usedItemIds = new HashSet<string>();
        private readonly bool oncePerBattle;

        /// <param name="oncePerBattle">一件道具一场是否只能用一次（配置项，07:42 的口径，占位 true）。</param>
        public BattleItemLedger(bool oncePerBattle)
        {
            this.oncePerBattle = oncePerBattle;
        }

        /// <summary>本场已经用过的道具 id（只读视图）。</summary>
        public IReadOnlyCollection<string> UsedItemIds => usedItemIds;

        /// <summary>本场已经用过的道具件数。</summary>
        public int UsedCount => usedItemIds.Count;

        /// <summary>这件道具本场是否已经用过。</summary>
        public bool IsUsed(string itemId) => !string.IsNullOrEmpty(itemId) && usedItemIds.Contains(itemId);

        /// <summary>只判定能不能用，不记账。</summary>
        public ItemUseDecision CanUse(string itemId, bool owned)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return ItemUseDecision.Refuse(ItemUseReject.InvalidItem);
            }

            return ItemUseRules.Decide(owned, IsUsed(itemId), oncePerBattle);
        }

        /// <summary>判定能不能用；能用就记账（标记为已用）。</summary>
        public ItemUseDecision TryUse(string itemId, bool owned)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return ItemUseDecision.Refuse(ItemUseReject.InvalidItem);
            }

            ItemUseDecision decision = ItemUseRules.Decide(owned, IsUsed(itemId), oncePerBattle);
            if (decision.Allowed)
            {
                usedItemIds.Add(itemId);
            }

            return decision;
        }

        /// <summary>判定能不能用；能用就记账。持有与否问注入的背包口。</summary>
        public ItemUseDecision TryUse(string itemId, IBattleItemInventory inventory)
        {
            bool owned = !string.IsNullOrEmpty(itemId) && inventory != null && inventory.Owns(itemId);
            return TryUse(itemId, owned);
        }

        /// <summary>清空账本（新一场战斗用）。</summary>
        public void Clear() => usedItemIds.Clear();
    }
}
