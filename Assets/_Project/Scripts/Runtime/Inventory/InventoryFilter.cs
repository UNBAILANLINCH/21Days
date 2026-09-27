// 职责：背包面板的类别筛选档位（全部 / 物品 / 线索）。
// 为什么新建：一个文件一个类型；表里的 EItemCategory 是四类，面板只分三档（物品 = 材料 + 消耗品 + 关键物），
//   两者粒度不同，不能直接复用表枚举。

namespace Game.Inventory
{
    /// <summary>背包面板的筛选档位。映射规则见 <see cref="InventoryRules.Matches"/>。</summary>
    public enum InventoryFilter
    {
        /// <summary>全部（含表里查不到的未知 id）。</summary>
        All = 0,

        /// <summary>物品：材料 + 消耗品 + 关键物。</summary>
        Items = 1,

        /// <summary>线索。</summary>
        Clues = 2,
    }
}
