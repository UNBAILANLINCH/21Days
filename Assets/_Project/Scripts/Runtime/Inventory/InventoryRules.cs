// 职责：背包的纯规则——把「tbitem id → 数量」与物品表合并成排序后的条目列表，按筛选档位过滤，查不到表项的 id 用「#id」占位；
//   另给出类别的中文标签。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootRules 只管写分区（幂等记录、累加、清空），不认识物品表与显示。
//   2. 扩展不行：塞进 LootRules / LootService 会让 Loot 认识面板的排序与筛选，依赖方向应是 Inventory → Loot。
//   纯静态、不碰 Unity 与容器，EditMode 直接测。
using System;
using System.Collections.Generic;

namespace Game.Inventory
{
    public static class InventoryRules
    {
        /// <summary>表里查不到的 id 的类别值（枚举里没有 0）。</summary>
        public const global::cfg.EItemCategory UnknownCategory = 0;

        private const string UnknownPrefix = "#";

        private static readonly Comparison<InventoryEntry> Order = CompareEntries;

        /// <summary>
        /// 按表合并并排序：先按类别（材料 → 消耗品 → 线索 → 关键物，未知 id 最后），同类按 id 升序；
        /// 数量 ≤ 0 的条目跳过。<paramref name="lookup"/> 为空或返回 null 时名字为「#id」、类别为 <see cref="UnknownCategory"/>。
        /// 结果写进 <paramref name="result"/>（先清空），不另分配列表。
        /// </summary>
        public static void Build(IReadOnlyDictionary<int, int> items, Func<int, global::cfg.Item> lookup,
            InventoryFilter filter, List<InventoryEntry> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();
            if (items == null) return;

            foreach (KeyValuePair<int, int> pair in items)
            {
                if (pair.Value <= 0) continue;
                InventoryEntry entry = ToEntry(pair.Key, pair.Value, lookup == null ? null : lookup(pair.Key));
                if (Matches(entry.Category, filter)) result.Add(entry);
            }

            result.Sort(Order);
        }

        /// <summary>同上，直接吃 Luban 表；<paramref name="table"/> 为空（表未就绪）时全部按未知 id 处理。</summary>
        public static void Build(IReadOnlyDictionary<int, int> items, global::cfg.TbItem table,
            InventoryFilter filter, List<InventoryEntry> result)
        {
            Build(items, table == null ? null : new Func<int, global::cfg.Item>(table.GetOrDefault), filter, result);
        }

        /// <summary>类别是否落在筛选档位里：全部 = 一切（含未知）；物品 = 材料 + 消耗品 + 关键物；线索 = 线索。</summary>
        public static bool Matches(global::cfg.EItemCategory category, InventoryFilter filter)
        {
            switch (filter)
            {
                case InventoryFilter.All:
                    return true;
                case InventoryFilter.Items:
                    return category == global::cfg.EItemCategory.Material
                           || category == global::cfg.EItemCategory.Consumable
                           || category == global::cfg.EItemCategory.Key;
                case InventoryFilter.Clues:
                    return category == global::cfg.EItemCategory.Clue;
                default:
                    return false;
            }
        }

        /// <summary>类别的中文短标签（列表小标签与详情用）；未知类别返回「未知」。</summary>
        public static string CategoryLabel(global::cfg.EItemCategory category)
        {
            switch (category)
            {
                case global::cfg.EItemCategory.Material: return "材料";
                case global::cfg.EItemCategory.Consumable: return "消耗品";
                case global::cfg.EItemCategory.Clue: return "线索";
                case global::cfg.EItemCategory.Key: return "关键物";
                default: return "未知";
            }
        }

        private static InventoryEntry ToEntry(int id, int count, global::cfg.Item item)
        {
            if (item == null)
                return new InventoryEntry(id, UnknownPrefix + id, count, 0, UnknownCategory, string.Empty);
            string name = string.IsNullOrEmpty(item.Name) ? UnknownPrefix + id : item.Name;
            return new InventoryEntry(id, name, count, item.Quality, item.Category, item.Desc);
        }

        // 未知类别排最后：它不在枚举里，按 int 比会排到最前。
        private static int SortKey(global::cfg.EItemCategory category)
            => category == UnknownCategory ? int.MaxValue : (int)category;

        private static int CompareEntries(InventoryEntry a, InventoryEntry b)
        {
            int byCategory = SortKey(a.Category).CompareTo(SortKey(b.Category));
            return byCategory != 0 ? byCategory : a.Id.CompareTo(b.Id);
        }
    }
}
