// 职责：怪物按种类掉落的纯规则——把「这只怪掉哪几个 item id」累加进存档分区的物品表；不认识存档服务、事件与场景。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootRules 现有的 Collect 是「按箱子键幂等 + 加物品」这一件事，键与幂等是物资箱的性质；
//      怪是按种类掉、同一只种类会死很多次、没有「箱子键」这种东西，硬塞会让 Collect 长出第二套语义。
//   2. 扩展不行：把掉落判定塞进 LootService 会让规则离不开存档 / 通知 / MessagePipe，无法脱离容器单测
//      （同 LootRules / LootService 的分法）。
// 数据来源：Tables/Data/yao/<id>.json 的 drop_items（经 Game.Mirror.YaoCatalog 只读查询）。
//   本文件只认「一批 item id」，不认妖物表，也不认道具类别枚举（EItemCategory 与 item 表归道具侧）。
using System.Collections.Generic;

namespace Game.Loot
{
    /// <summary>怪物掉落的纯规则。全部静态；非法参数返回 false，不抛。</summary>
    public static class MonsterLootRules
    {
        /// <summary>
        /// 把一次击杀的掉落累加进 <see cref="LootSaveData.Items"/>。
        /// <paramref name="items"/> 为 null / 空、<paramref name="data"/> 为 null 时返回 false 且不改数据；
        /// 列表里的非正数 id 跳过（数据坏的那一半由 <c>YaoCatalog</c> 的列校验与生成期拦住，这里只是不崩）。
        /// 只要有至少一个合法 id 被累加就返回 true。
        /// <para>
        /// <b>不管幂等</b>：怪物掉落没有「箱子键」这种东西，同一只种类会死很多次、每次都要掉。
        /// 「一只怪只结算一次」由调用方（死亡那一处）保证，见 <see cref="LootService.SettleMonsterDrop"/> 的注释。
        /// </para>
        /// </summary>
        public static bool Grant(LootSaveData data, int yaoId, IReadOnlyList<int> items)
        {
            if (data == null || yaoId <= 0 || items == null || items.Count == 0)
            {
                return false;
            }

            data.Items ??= new Dictionary<int, int>();
            bool granted = false;
            for (int i = 0; i < items.Count; i++)
            {
                int itemId = items[i];
                if (itemId <= 0)
                {
                    continue;
                }

                data.Items.TryGetValue(itemId, out int owned);
                data.Items[itemId] = owned + 1;
                granted = true;
            }

            return granted;
        }

        /// <summary>这次掉落的物品 id 里有没有 <paramref name="itemId"/>；用于「这只怪掉不掉某件关键道具」的纯判定。</summary>
        public static bool Drops(IReadOnlyList<int> items, int itemId)
        {
            if (items == null || itemId <= 0)
            {
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == itemId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
