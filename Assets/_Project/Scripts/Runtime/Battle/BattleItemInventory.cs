// 职责：回合制内核的背包口适配器（IBattleItemInventory）——把内核的字符串道具 id 与背包的 int id 互转，
//   只认消耗品（PRP/turnbased-battle D10：道具格只显示背包里有的 Consumable），并负责「用一次扣 1」与道具格清单。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：TurnBased 只定义了一方法接口（turnbased-module-guide「接线清单·背包口」明说要接线侧写适配器）；
//      Inventory 的 ItemUseRules 是另一套「道具 → 效果标识」规则，不回答「战斗里能不能用」。
//   2. 扩展不行：塞进 Loot / Inventory 会让它们认识回合制内核（依赖方向是 Battle → Loot / TurnBased）。
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.TurnBased;

namespace Game.Battle
{
    /// <summary>战斗道具口（根作用域单例；同一个实例既喂内核 <see cref="Owns"/>，又给界面列道具格）。</summary>
    public sealed class BattleItemInventory : IBattleItemInventory
    {
        private readonly IBattleBackpack backpack;
        private readonly SortedSet<int> idBuffer = new SortedSet<int>();

        public BattleItemInventory(IBattleBackpack backpack)
        {
            this.backpack = backpack ?? throw new ArgumentNullException(nameof(backpack));
        }

        /// <summary>内核问「有没有」：id 合法、背包里件数 &gt; 0、且是消耗品才算有。</summary>
        public bool Owns(string itemId) =>
            TryParse(itemId, out int id) && backpack.Items.TryGetValue(id, out int count) && count > 0 && backpack.IsConsumable(id);

        /// <summary>用掉一件：从背包扣 1。id 非法或扣不动返回 false。</summary>
        public bool TryConsume(string itemId) => TryParse(itemId, out int id) && backpack.TryConsumeOne(id);

        /// <summary>
        /// 道具格清单（覆盖写进 <paramref name="into"/>）：背包里件数 &gt; 0 的消耗品，加上本场用过的（哪怕已经扣到 0，07:42「用过了则置暗」）；
        /// 没有的不列（07:42「未拥有则不显示」）。按 id 升序，界面顺序稳定。
        /// </summary>
        public void ListSlots(BattleItemLedger ledger, List<BattleItemSlot> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            idBuffer.Clear();
            IReadOnlyDictionary<int, int> items = backpack.Items;
            foreach (KeyValuePair<int, int> pair in items)
                if (pair.Value > 0 && backpack.IsConsumable(pair.Key)) idBuffer.Add(pair.Key);
            if (ledger != null)
                foreach (string used in ledger.UsedItemIds)
                    if (TryParse(used, out int id) && backpack.IsConsumable(id)) idBuffer.Add(id);

            foreach (int id in idBuffer)
            {
                string key = ToKey(id);
                items.TryGetValue(id, out int count);
                into.Add(new BattleItemSlot(key, backpack.NameOf(id), count, ledger != null && ledger.IsUsed(key)));
            }
        }

        /// <summary>背包 int id → 内核字符串 id（十进制、不受区域设置影响）。</summary>
        public static string ToKey(int itemId) => itemId.ToString(CultureInfo.InvariantCulture);

        /// <summary>内核字符串 id → 背包 int id。只认纯十进制正整数（"1004"），空 / 负数 / 带空白都算非法。</summary>
        public static bool TryParse(string itemId, out int id) =>
            int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
