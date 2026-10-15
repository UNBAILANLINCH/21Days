// 职责：道具使用表与配方表的**配置资产**（Data/Inventory/InventoryConfig.asset）——道具 id → 效果标识 / 原料 → 产物。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有第二份道具使用 / 配方数据；LootConfig 管的是箱子半径与文案，TurnBasedConfig 管战斗数值。
//   2. 扩展不行：**本波次不建 Luban 表**——item / yao 那批表是 Excel 表（Tables/Data/__beans__.xlsx / item.xlsx），
//      工程约定不手改，且第三路正在往 Tables/Data/yao/*.json 填掉落数据，这时加新表 schema 要全表重生成、
//      必然冲突。这与 `BossRosterConfig`（Battle/BossRosterConfig.cs:3-5）、`TurnBasedConfig` 的先例一致：
//      先用 SO 资产 + 代码里的占位默认值顶着，等拍板后再迁表、删掉本资产。
//   **商品化前必须迁表**：本资产里的配方与使用表是占位，等 05_皮面具与道具.md 的 Q2 / Q3 / R8 拍板。
// 运行时只读：读出来的列表每次现建一份（配置只读，运行时不改 SO 字段）。
using System.Collections.Generic;
using UnityEngine;

namespace Game.Inventory
{
    /// <summary>
    /// 道具使用表 + 配方表。挂在 <see cref="InventoryInstaller"/> 上；字段留空时用代码里的占位默认值顶上
    /// （占位出处见 <see cref="ItemUseEntry.PlaceholderHealPotion"/> / <see cref="ItemRecipeEntry.PlaceholderOfficeKey"/>）。
    /// </summary>
    [CreateAssetMenu(menuName = "21Days/Inventory/Inventory Config", fileName = "InventoryConfig")]
    public sealed class InventoryConfig : ScriptableObject
    {
        [Tooltip("使用表：道具 id → 效果标识 + 一次消耗几件。空 = 用占位默认（1004 治疗药水 → heal.player_hp）。")]
        [SerializeField] private ItemUseEntry[] uses = System.Array.Empty<ItemUseEntry>();

        [Tooltip("配方表：原料 → 产物。空 = 用占位默认（水痕 + 湿皮 → 进入废弃官署的钥匙）。")]
        [SerializeField] private ItemRecipeEntry[] recipes = System.Array.Empty<ItemRecipeEntry>();

        [Tooltip("合成成功后是否扣掉原料。**用户 2026-10-10 拍板 R8：扣**（资产里 consumeIngredients 已勾上），"
                 + "05_皮面具与道具.md:114 R8 原先未定，按「原料就是消耗品」落定；湿皮 / 空白皮 / 泾龙丹一物两用"
                 + "（:143）的取用顺序由玩法侧决定，不在这一步兜。")]
        [SerializeField] private bool consumeIngredients;

        /// <summary>
        /// 使用表（运行时形状）。没配任何行时回一条占位行——**不返回空表**：
        /// 空表会让「使用」按钮对每件道具都报「没有执行者」，看起来像坏了，实际是配置没接。
        /// </summary>
        public IReadOnlyList<ItemUseSpec> CreateUseSpecs()
        {
            ItemUseEntry[] rows = uses;
            if (rows == null || rows.Length == 0)
            {
                return new[] { ItemUseEntry.PlaceholderHealPotion().ToSpec() };
            }

            var specs = new List<ItemUseSpec>(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] != null) specs.Add(rows[i].ToSpec());
            }

            return specs;
        }

        /// <summary>配方表（运行时形状）。没配任何行时回一条占位配方（理由同上）。</summary>
        public IReadOnlyList<ItemRecipe> CreateRecipes()
        {
            ItemRecipeEntry[] rows = recipes;
            if (rows == null || rows.Length == 0)
            {
                return new[] { ItemRecipeEntry.PlaceholderOfficeKey().ToRecipe() };
            }

            var list = new List<ItemRecipe>(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] != null) list.Add(rows[i].ToRecipe());
            }

            return list;
        }

        /// <summary>合成成功后是否扣掉原料；true = 扣（用户 2026-10-10 拍板 R8，正式口径，见字段 Tooltip）。</summary>
        public bool ConsumeIngredients => consumeIngredients;

        /// <summary>原始使用表行数（0 表示配置里没填、运行时用占位默认）。</summary>
        public int ConfiguredUseCount => uses == null ? 0 : uses.Length;

        /// <summary>原始配方行数（0 表示配置里没填、运行时用占位默认）。</summary>
        public int ConfiguredRecipeCount => recipes == null ? 0 : recipes.Length;
    }
}
