// 职责：背包面板一行的只读数据——物品 id、显示名、数量、品质、类别、描述。
// 为什么新建：Luban 的 cfg.Item 不带数量、也表示不了「表里查不到」的占位；面板要的是「表项 + 背包数量」合并后的快照，
//   放进 LootService 会让 Loot 认识面板，放进 View 又违反「View 只显示」。

namespace Game.Inventory
{
    /// <summary>背包条目快照，由 <c>InventoryRules.Build</c> 产出。</summary>
    public readonly struct InventoryEntry
    {
        public InventoryEntry(int id, string name, int count, global::cfg.EItemQuality quality,
            global::cfg.EItemCategory category, string desc)
        {
            Id = id;
            Name = name ?? string.Empty;
            Count = count;
            Quality = quality;
            Category = category;
            Desc = desc ?? string.Empty;
        }

        /// <summary>tbitem 主键。</summary>
        public int Id { get; }

        /// <summary>显示名；表里查不到时为「#id」。</summary>
        public string Name { get; }

        public int Count { get; }

        /// <summary>品质；表里查不到时为 0（未定义值，面板按兜底色画）。</summary>
        public global::cfg.EItemQuality Quality { get; }

        /// <summary>类别；表里查不到时为 0（<see cref="InventoryRules.UnknownCategory"/>），只在「全部」里出现。</summary>
        public global::cfg.EItemCategory Category { get; }

        /// <summary>描述，可空串。</summary>
        public string Desc { get; }
    }
}
