// 职责：物资箱拾取的纯规则——按箱子键幂等记录、物品数量累加、整体清空；不认识存档服务、事件与场景。
// 为什么新建：LootService 要接存档 / 任务 / 通知 / 事件，把规则抽成纯 C# 才能脱离容器单测（同 QuestRules 的分法）；
//   既有模块没有「按键幂等 + 累加背包」的规则可复用。
using System.Collections.Generic;

namespace Game.Loot
{
    /// <summary>物资箱规则。全部静态、无分配（除首次加入列表 / 字典的扩容）；非法参数返回 false，不抛。</summary>
    public static class LootRules
    {
        /// <summary>
        /// 记录一次开箱：<paramref name="key"/> 已开过返回 false 且不改数据；
        /// 否则加入 <see cref="LootSaveData.CollectedCrates"/>、<see cref="LootSaveData.Items"/> 按 <paramref name="itemId"/> 累加，返回 true。
        /// data 为 null、key 为空、count ≤ 0 返回 false。
        /// </summary>
        public static bool Collect(LootSaveData data, string key, int itemId, int count)
        {
            if (data == null || string.IsNullOrEmpty(key) || count <= 0) return false;
            if (IsCollected(data, key)) return false;

            data.CollectedCrates ??= new List<string>();
            data.Items ??= new Dictionary<int, int>();
            data.CollectedCrates.Add(key);
            data.Items.TryGetValue(itemId, out int owned);
            data.Items[itemId] = owned + count;
            return true;
        }

        /// <summary>该键是否已开过；data 为 null 或 key 为空返回 false。</summary>
        public static bool IsCollected(LootSaveData data, string key)
        {
            if (data == null || string.IsNullOrEmpty(key) || data.CollectedCrates == null) return false;
            List<string> collected = data.CollectedCrates;
            for (int i = 0; i < collected.Count; i++)
            {
                if (string.Equals(collected[i], key, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>清空已开记录与背包；data 为 null 时空操作。</summary>
        public static void Reset(LootSaveData data)
        {
            if (data == null) return;
            if (data.CollectedCrates == null) data.CollectedCrates = new List<string>();
            else data.CollectedCrates.Clear();
            if (data.Items == null) data.Items = new Dictionary<int, int>();
            else data.Items.Clear();
        }
    }
}
