// 职责：合成的纯规则——拿「背包（tbitem id → 件数）」和配方，判定原料齐不齐，齐了就回一个「用哪条配方、出什么产物」的结果。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootRules 只管开箱写入（幂等键 + 数量累加），不认识「多原料 → 产物」；InventoryRules 只管展示合并与筛选。
//   2. 扩展不行：把配方判定塞进 InventoryRules 会让「展示口径」（排序 / 筛选 / 中文标签）和「玩法规则」（配方）挤在一个类里，
//      职责尺子说不通；塞进 Loot 又会让依赖方向反过来（依赖方向是 Inventory → Loot）。
// 本文件只判定、不写背包：命中后扣原料、加产物、落盘归调用方（见下）。
// 谁来回调这个结果（本波次不做，[05]:107 R8、:289 Q3 未定「在哪合、原料合成后是否消失」）：
//   · 合成界面 / 合成点：sp04「UI」第 19 行只写了背包三个标签，没有合成界面（05_皮面具与道具.md:72）；
//     在哪合是待拍板项，落地时由承载它的模块调本方法，再自己弹界面。
//   · 写入：产物与扣料一律走 Loot 的公开方法 / 将来的背包服务（roadmap 第 2 节 E1 落盘存档），
//     Inventory 不新增第二条写入路径（inventory-module-guide.md「禁止事项」）。
//   · 配方从哪来：本波次没有配方表，配方由调用方以 ItemRecipe 列表传入（测试里手搓）。
//     要搬进 Luban 表时改 Tables/Data/__beans__.xlsx + __tables__.xlsx 加一张配方表，本文件签名不用动。
using System.Collections.Generic;

namespace Game.Inventory
{
    /// <summary>合成规则。全部静态、只读输入，不抛异常（非法参数落成结果码）。</summary>
    public static class ItemCraftRules
    {
        /// <summary>
        /// 按产物 id 在 <paramref name="recipes"/> 里找第一条命中的配方，再走 <see cref="TryCraft(IReadOnlyDictionary{int, int}, ItemRecipe)"/>。
        /// 找不到（含 recipes 为 null / 空）返回 <see cref="ItemCraftStatus.NoRecipe"/>。
        /// </summary>
        public static ItemCraftResult TryCraft(IReadOnlyDictionary<int, int> items, IReadOnlyList<ItemRecipe> recipes, int productId)
        {
            if (recipes == null || productId <= 0) return ItemCraftResult.Failed(ItemCraftStatus.NoRecipe);
            for (int i = 0; i < recipes.Count; i++)
            {
                ItemRecipe candidate = recipes[i];
                if (candidate != null && candidate.ProductId == productId) return TryCraft(items, candidate);
            }

            return ItemCraftResult.Failed(ItemCraftStatus.NoRecipe);
        }

        /// <summary>
        /// 判定一条配方能不能用 <paramref name="items"/> 合成。判定顺序（先写的先判）：
        /// 配方为 null → <see cref="ItemCraftStatus.NoRecipe"/>；原料为空 → <see cref="ItemCraftStatus.EmptyIngredients"/>；
        /// 配方写坏 → <see cref="ItemCraftStatus.InvalidRecipe"/>；缺料 → <see cref="ItemCraftStatus.MissingIngredient"/>；否则 Ok。
        /// <para><paramref name="items"/> 为 null 视作空背包（结果同样是缺料）。同一种原料写多行时件数按行相加。</para>
        /// </summary>
        public static ItemCraftResult TryCraft(IReadOnlyDictionary<int, int> items, ItemRecipe recipe)
        {
            if (recipe == null) return ItemCraftResult.Failed(ItemCraftStatus.NoRecipe);

            IReadOnlyList<ItemStack> ingredients = recipe.Ingredients;
            if (ingredients == null || ingredients.Count == 0) return ItemCraftResult.Failed(ItemCraftStatus.EmptyIngredients);
            if (!recipe.IsValid) return ItemCraftResult.Failed(ItemCraftStatus.InvalidRecipe);

            for (int i = 0; i < ingredients.Count; i++)
            {
                int itemId = ingredients[i].ItemId;
                if (SeenEarlier(ingredients, i, itemId)) continue; // 同一 id 只判一次，件数在 RequiredCount 里已经加过
                if (Owned(items, itemId) < recipe.RequiredCount(itemId)) return ItemCraftResult.Missing(itemId);
            }

            return ItemCraftResult.Succeeded(recipe);
        }

        // 背包里有多少件；items 为 null 或查不到都算 0。
        private static int Owned(IReadOnlyDictionary<int, int> items, int itemId)
        {
            if (items == null) return 0;
            return items.TryGetValue(itemId, out int owned) ? owned : 0;
        }

        private static bool SeenEarlier(IReadOnlyList<ItemStack> ingredients, int index, int itemId)
        {
            for (int i = 0; i < index; i++)
            {
                if (ingredients[i].ItemId == itemId) return true;
            }

            return false;
        }
    }
}
