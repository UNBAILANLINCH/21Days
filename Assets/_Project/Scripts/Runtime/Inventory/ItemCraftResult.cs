// 职责：一次合成判定的结果数据——状态 + 命中的配方 + 缺哪件原料。
// 为什么新建：一个文件一个类型；结果里要同时带「成不成」「成的是哪条配方」「缺的是哪件」，
//   只回 bool 的话调用方拿不到产物也报不出缺料，只回配方的话区分不了失败原因。

namespace Game.Inventory
{
    /// <summary>
    /// 合成判定结果。只描述「能不能合、合出什么」，不写背包（写入路径见 <see cref="ItemCraftRules"/> 文件头）。
    /// </summary>
    public readonly struct ItemCraftResult
    {
        private ItemCraftResult(ItemCraftStatus status, ItemRecipe recipe, int missingItemId)
        {
            Status = status;
            Recipe = recipe;
            MissingItemId = missingItemId;
        }

        /// <summary>结果码。</summary>
        public ItemCraftStatus Status { get; }

        /// <summary>命中的配方；<see cref="Status"/> 不是 <see cref="ItemCraftStatus.Ok"/> 时为 null。</summary>
        public ItemRecipe Recipe { get; }

        /// <summary>缺的那件原料 id；只有 <see cref="ItemCraftStatus.MissingIngredient"/> 时非 0。</summary>
        public int MissingItemId { get; }

        /// <summary>产物（命中的配方算出来的 id + 件数）；未命中时为默认 <see cref="ItemStack"/>（<c>IsValid</c> 为 false）。</summary>
        public ItemStack Product => Recipe == null ? default : new ItemStack(Recipe.ProductId, Recipe.ProductCount);

        /// <summary>是否可合成。</summary>
        public bool IsOk => Status == ItemCraftStatus.Ok;

        internal static ItemCraftResult Succeeded(ItemRecipe recipe) => new ItemCraftResult(ItemCraftStatus.Ok, recipe, 0);

        internal static ItemCraftResult Failed(ItemCraftStatus status) => new ItemCraftResult(status, null, 0);

        internal static ItemCraftResult Missing(int itemId) => new ItemCraftResult(ItemCraftStatus.MissingIngredient, null, itemId);
    }
}
