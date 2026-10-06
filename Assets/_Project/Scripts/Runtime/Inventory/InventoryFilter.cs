// 职责：背包面板的类别筛选档位（全部 / 物品 / 线索）。
// 为什么新建：一个文件一个类型；表里的 EItemCategory 是八类（Material / Consumable / Clue / Key / Skin / Mask / Pass / Document），
//   面板只分三档（物品 = 材料 + 消耗品 + 关键物 + 皮 + 面具 + 钥匙；线索 = 线索 + 文书），两者粒度不同，不能直接复用表枚举。
// 与 sp04「关键道具 / 材料 / 文书」三分的关系：本枚举是已验收的白盒 UI 口径，本波次只补新类别、不推翻；
//   要不要改成 sp04 三分待策划拍板（见交付报告「待策划拍板」）。

namespace Game.Inventory
{
    /// <summary>背包面板的筛选档位。映射规则见 <see cref="InventoryRules.Matches"/>。</summary>
    public enum InventoryFilter
    {
        /// <summary>全部（含表里查不到的未知 id）。</summary>
        All = 0,

        /// <summary>物品：材料 + 消耗品 + 关键物 + 皮 + 面具 + 钥匙。</summary>
        Items = 1,

        /// <summary>线索：线索 + 文书（都是「信息」类）。</summary>
        Clues = 2,
    }
}
