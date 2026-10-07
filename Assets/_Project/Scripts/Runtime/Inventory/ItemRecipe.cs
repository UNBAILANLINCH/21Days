// 职责：合成配方的数据形状——原料（道具 id + 件数）→ 产物（道具 id + 件数）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有「多原料 → 产物」的形状——LootRules 只做「箱子键幂等 + 数量累加」，
//      InventoryRules 只做展示合并与筛选，两者的输入都是「id → 数量」，表达不了「一条配方的原料清单」。
//   2. 扩展不行：往 ItemStack 里塞原料列表会让「一件道具 + 件数」这个最小形状承担配方结构，名实不符。
// 谁读它：ItemCraftRules.TryCraft。配方定义本波次写在代码里（见 ItemCraftRules 文件头「配方从哪来」），
//   将来搬进 Luban 表时，本类就是表行的运行时形状。
using System;
using System.Collections.Generic;

namespace Game.Inventory
{
    /// <summary>
    /// 一条合成配方。构造时把原料复制进内部数组，之后本对象不可变（外部改传入的列表不影响它）。
    /// 出处：docs/design/features-spotlight/05_皮面具与道具.md:106 R7（水痕 + 收服户绝民的湿皮 → 钥匙；泾龙丹 + 空白皮 → 泾龙面具）。
    /// </summary>
    public sealed class ItemRecipe
    {
        private readonly ItemStack[] ingredients;

        /// <param name="id">配方标识，进日志 / 埋点用；空串视为写坏。</param>
        /// <param name="ingredients">原料清单；空或 null 视为写坏（「原料为空」边界）。</param>
        /// <param name="productId">产物 tbitem 主键。</param>
        /// <param name="productCount">产物件数，默认 1。</param>
        public ItemRecipe(string id, IReadOnlyList<ItemStack> ingredients, int productId, int productCount = 1)
        {
            Id = id ?? string.Empty;
            this.ingredients = Copy(ingredients);
            ProductId = productId;
            ProductCount = productCount;
        }

        /// <summary>配方标识。</summary>
        public string Id { get; }

        /// <summary>原料，保持写入顺序；同一种原料写两行是允许的，件数按行相加。</summary>
        public IReadOnlyList<ItemStack> Ingredients => ingredients;

        /// <summary>产物 tbitem 主键。</summary>
        public int ProductId { get; }

        /// <summary>产物件数。</summary>
        public int ProductCount { get; }

        /// <summary>配方本身是否可用：标识非空、至少一条原料、每条原料合法、产物 id 与件数都为正。</summary>
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Id) || ProductId <= 0 || ProductCount <= 0) return false;
                if (ingredients.Length == 0) return false;
                for (int i = 0; i < ingredients.Length; i++)
                {
                    if (!ingredients[i].IsValid) return false;
                }

                return true;
            }
        }

        /// <summary>本配方一共要多少件 <paramref name="itemId"/>（同一 id 写多行时相加；没写的返回 0）。</summary>
        public int RequiredCount(int itemId)
        {
            int total = 0;
            for (int i = 0; i < ingredients.Length; i++)
            {
                if (ingredients[i].ItemId == itemId) total += ingredients[i].Count;
            }

            return total;
        }

        public override string ToString()
            => Id + "：" + ingredients.Length + " 种原料 → #" + ProductId + "×" + ProductCount;

        private static ItemStack[] Copy(IReadOnlyList<ItemStack> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<ItemStack>();
            var copy = new ItemStack[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
    }
}
