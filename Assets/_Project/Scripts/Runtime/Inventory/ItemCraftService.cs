// 职责：把「合一件东西」这件事串起来——按产物 id 找配方（ItemCraftRules.TryCraft）→ 扣原料（可选）→ 产物入包（LootService.GrantItems）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：ItemCraftRules 只判定（文件头写明「只判定、不写背包」）；LootService 是背包写入口，
//      不认识配方；InventoryPanelController 是 UI 会话，把合成逻辑塞进去就没法在 EditMode 里测，也没法被将来的合成台复用。
//   2. 扩展不行：塞进 ItemUseService 会让「用道具」与「合道具」两件事共用一个类——它们的顺序、失败语义都不同。
// 扣原料与入包的顺序（理由写全）：
//   **先扣原料（如果配置要扣）、再入包产物。** 第一步之前已经把「原料够不够」判死了（TryCraft），
//   所以扣料这一步只可能因为「两步之间背包变了」而失败（同一帧别的东西扣了料）——那时直接中止、**不产出产物**，
//   并留一条 Error：宁可玩家看到「合成失败」，也不要「产物到手、原料还在」的凭空造物。
//   反过来（先入包、再扣料）失败时会留下「产物到手但原料没扣」的白拿，而且产物已经在背包里、退回去又要一步写操作。
//   为什么不做「部分成功回滚」：唯一可能的失败点（扣料）发生在产物入包之前，天然不需要回滚；
//   产物入包（LootRules.Grant）只在 itemId / count 非正时失败，而那条在进本方法前已经被 IsValid 挡住。
//   **扣料口径已定**（用户 2026-10-10 拍板 R8：合成消耗原料，`InventoryConfig.consumeIngredients` 资产里为 true），
//   所以这一段现在是**在走的代码路径**；顺序（先扣料、再入包）就是为这一天定的，改口径只改配置勾选。
// 写入路径：只走 Loot 的公开口（TryConsume / GrantItems）。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Loot;

namespace Game.Inventory
{
    /// <summary>
    /// 合成道具的门面。构造注入即可（根作用域单例）。
    /// <para>
    /// 配方来自 <see cref="InventoryConfig"/>（占位配置资产，等拍板后迁 Luban 表）。
    /// 背包里已有该产物时回 <see cref="ItemCraftStatus.AlreadyOwned"/>——本工程没有堆叠上限与堆叠 UI，
    /// 白盒阶段不希望凭空多出第二件同名物（理由见该状态值的注释）。
    /// </para>
    /// </summary>
    public sealed class ItemCraftService
    {
        /// <summary>埋点原因码（E 级事件 craft_consume_failed / craft_grant_failed 用）。</summary>
        private const string ConsumeFailed = "consume_failed";

        /// <summary>同上，产物入包失败。</summary>
        private const string GrantFailed = "grant_failed";

        private readonly IReadOnlyList<ItemRecipe> recipes;
        private readonly LootService loot;
        private readonly bool consumeIngredients;
        private readonly ITelemetryScope telemetry;

        public ItemCraftService(IReadOnlyList<ItemRecipe> recipes, LootService loot, bool consumeIngredients,
            ITelemetryScope telemetry)
        {
            this.recipes = recipes ?? Array.Empty<ItemRecipe>();
            this.loot = loot ?? throw new ArgumentNullException(nameof(loot));
            this.consumeIngredients = consumeIngredients;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>配方表（只读）。</summary>
        public IReadOnlyList<ItemRecipe> Recipes => recipes;

        /// <summary>是否有产物的配方（面板用它决定合成按钮与提示）。</summary>
        public bool HasRecipe(int productId)
        {
            if (productId <= 0) return false;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i] != null && recipes[i].ProductId == productId) return true;
            }

            return false;
        }

        /// <summary>合成成功后是否扣掉原料（配置开关，占位默认 false）。</summary>
        public bool ConsumesIngredients => consumeIngredients;

        /// <summary>按产物 id 找配方；找不到返回 null（只读查询，界面拼提示也要用）。</summary>
        public ItemRecipe FindRecipe(int productId)
        {
            if (productId <= 0) return null;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i] != null && recipes[i].ProductId == productId) return recipes[i];
            }

            return null;
        }

        /// <summary>
        /// 找一条**把这件东西当原料**的配方（取第一条命中；找不到返回 null）。
        /// <para>
        /// 为什么需要它（2026-10-09）：背包面板的列表只列**已持有**的东西，而 <see cref="TryCraft"/> 在
        /// 「已经持有产物」时会回 <see cref="ItemCraftStatus.AlreadyOwned"/> —— 于是「选中产物再合成」这条路
        /// 走成了死循环：不持有就选不中，持有又被拒。加这个口之后，玩家可以**选中一件原料**、直接合成
        /// 用到它的那条配方，闭环才成立（回放 `InventoryShowcase` 的两条用例撞的就是这个）。
        /// </para>
        /// <para>
        /// <b>占位口径</b>：同一件原料出现在多条配方里时只取第一条，且不挑「原料已经凑齐」的那条。
        /// 「合成在哪里进行、要不要先列出可合成清单」是 `05_皮面具与道具.md:114 R8` 的待拍板项，
        /// 本波不替它决定；拍板后若改成「面板列出可合成产物」，本方法可以退役。
        /// </para>
        /// </summary>
        public ItemRecipe FindRecipeUsingIngredient(int itemId)
        {
            if (itemId <= 0) return null;
            for (int i = 0; i < recipes.Count; i++)
            {
                ItemRecipe candidate = recipes[i];
                if (candidate == null || candidate.Ingredients == null) continue;
                IReadOnlyList<ItemStack> ingredients = candidate.Ingredients;
                for (int j = 0; j < ingredients.Count; j++)
                {
                    if (ingredients[j].ItemId == itemId) return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 合一件产物：找配方 → 判定原料 → （按配置）扣原料 → 产物入包。
        /// 任何一步失败都返回带结果码的失败且不改背包（扣料失败时会留一条 Error，理由见文件头）。
        /// </summary>
        /// <param name="productId">要合成哪件产物（tbitem 主键）。</param>
        public ItemCraftResult TryCraft(int productId)
        {
            if (!HasRecipe(productId))
            {
                telemetry.TrackWarn("craft_rejected", TelemetryProps.Of(("product", productId), ("reason", "no_recipe")));
                return ItemCraftResult.Failed(ItemCraftStatus.NoRecipe);
            }

            if (Owned(productId) > 0)
            {
                // 负对照点：已经有一件就不再合出第二件（禁止凭空造物）。
                telemetry.TrackWarn("craft_rejected", TelemetryProps.Of(("product", productId), ("reason", "already_owned")));
                return ItemCraftResult.Failed(ItemCraftStatus.AlreadyOwned);
            }

            ItemCraftResult check = ItemCraftRules.TryCraft(loot.Items, recipes, productId);
            if (!check.IsOk)
            {
                telemetry.TrackWarn("craft_rejected",
                    TelemetryProps.Of(("product", productId), ("reason", check.Status.ToString())));
                return check;
            }

            ItemRecipe recipe = check.Recipe;
            if (consumeIngredients && !ConsumeIngredientsOf(recipe))
            {
                telemetry.TrackError("craft_consume_failed", ConsumeFailed,
                    TelemetryProps.Of(("product", productId), ("recipe", recipe.Id)));
                return ItemCraftResult.Failed(ItemCraftStatus.MissingIngredient);
            }

            if (!loot.GrantItems(recipe.ProductId, recipe.ProductCount))
            {
                Log.Error($"ItemCraftService：配方 {recipe.Id} 判定通过但产物 #{recipe.ProductId} 入包失败（参数非法）。");
                telemetry.TrackError("craft_grant_failed", GrantFailed,
                    TelemetryProps.Of(("product", productId), ("recipe", recipe.Id)));
                return ItemCraftResult.Failed(ItemCraftStatus.InvalidRecipe);
            }

            telemetry.Track("item_crafted", ("product", recipe.ProductId), ("recipe", recipe.Id), ("n", recipe.ProductCount));
            return check;
        }

        // 逐件扣原料；同一件原料写多行时按行扣（ItemRecipe.RequiredCount 的相加口径一致）。
        // 任一行扣不动就中止并返回 false —— 调用方据此不产出产物（见文件头的顺序说明）。
        private bool ConsumeIngredientsOf(ItemRecipe recipe)
        {
            IReadOnlyList<ItemStack> rows = recipe.Ingredients;
            for (int i = 0; i < rows.Count; i++)
            {
                ItemStack row = rows[i];
                if (!loot.TryConsume(row.ItemId, row.Count)) return false;
            }

            return true;
        }

        private int Owned(int itemId)
        {
            IReadOnlyDictionary<int, int> items = loot.Items;
            return items.TryGetValue(itemId, out int owned) ? owned : 0;
        }
    }
}
