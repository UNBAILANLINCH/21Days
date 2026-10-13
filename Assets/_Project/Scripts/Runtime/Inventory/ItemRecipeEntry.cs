// 职责：一条合成配方的 Inspector 行——配方标识 + 原料清单 + 产物 id 与件数。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：ItemRecipe 是普通 C# 类（构造时复制原料、之后不可变），Unity 序列化不了它；
//      ItemStack 是 readonly struct，同样序列化不了。SO 里必须有一个可序列化的行形状。
//   2. 扩展不行：让 ItemRecipe 变成 [Serializable] 会让「运行时的不可变配方」多出一堆可变字段，
//      纯规则的 17 条用例就得跟着改；行形状与运行时形状分开是 BossDefinition / BossRoster 的既有分法。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Inventory
{
    /// <summary>一条占位配方（序列化形状）。运行时经 <see cref="ToRecipe"/> 转成 <see cref="ItemRecipe"/>。</summary>
    [Serializable]
    public sealed class ItemRecipeEntry
    {
        /// <summary>
        /// 未配置时用的占位配方：水痕 1 + 湿皮 1 → 进入废弃官署的钥匙 1。
        /// 出处：05_皮面具与道具.md:106 R7 与道具总表 :168（「水痕 + 收服户绝民的湿皮 → 进入废弃官署的钥匙」）。
        /// <b>占位</b>：件数原文没写；R8（:107）里「原料合成后是否消失」已按用户 2026-10-10 拍板落定为**消失**，
        /// 只剩「在哪合 / 用什么界面」未定。
        /// </summary>
        public static ItemRecipeEntry PlaceholderOfficeKey()
        {
            var entry = new ItemRecipeEntry("office_key", 1701, 1);
            entry.ingredients = new[]
            {
                new ItemStackEntry(1401, 1),
                new ItemStackEntry(1102, 1),
            };
            return entry;
        }

        [Tooltip("配方标识，进日志与埋点用。")]
        [SerializeField] private string id = string.Empty;

        [Tooltip("原料清单。同一件原料写两行是允许的，件数按行相加。")]
        [SerializeField] private ItemStackEntry[] ingredients = Array.Empty<ItemStackEntry>();

        [Tooltip("产物 tbitem 主键。")]
        [SerializeField] private int productId;

        [Tooltip("产物件数。")]
        [SerializeField, Min(1)] private int productCount = 1;

        /// <summary>序列化用。</summary>
        public ItemRecipeEntry()
        {
        }

        /// <summary>代码构造（占位默认值与测试用）。</summary>
        public ItemRecipeEntry(string id, int productId, int productCount = 1)
        {
            this.id = id ?? string.Empty;
            this.productId = productId;
            this.productCount = productCount;
        }

        /// <summary>配方标识。</summary>
        public string Id => id ?? string.Empty;

        /// <summary>原料清单（只读视图）。</summary>
        public IReadOnlyList<ItemStackEntry> Ingredients => ingredients ?? Array.Empty<ItemStackEntry>();

        /// <summary>产物 id。</summary>
        public int ProductId => productId;

        /// <summary>产物件数。</summary>
        public int ProductCount => productCount;

        /// <summary>代码里补原料用（占位默认值构造；Inspector 里直接填数组）。</summary>
        public void SetIngredients(params ItemStackEntry[] rows) => ingredients = rows ?? Array.Empty<ItemStackEntry>();

        /// <summary>转成纯规则用的配方。</summary>
        public ItemRecipe ToRecipe()
        {
            IReadOnlyList<ItemStackEntry> rows = Ingredients;
            var stacks = new ItemStack[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                stacks[i] = rows[i] == null ? default : rows[i].ToStack();
            }

            return new ItemRecipe(Id, stacks, productId, productCount);
        }

        /// <summary>
        /// 一行原料的可序列化形状：道具 id + 件数。
        /// 为什么单独一个类而不是直接放 <see cref="ItemStack"/>：<see cref="ItemStack"/> 是 readonly struct，
        /// Unity 序列化不了它（Inspector 只认字段）。
        /// </summary>
        [Serializable]
        public sealed class ItemStackEntry
        {
            [Tooltip("原料 tbitem 主键。")]
            [SerializeField] private int itemId;

            [Tooltip("这一行要几件。")]
            [SerializeField, Min(1)] private int count = 1;

            /// <summary>序列化用。</summary>
            public ItemStackEntry()
            {
            }

            /// <summary>代码构造。保存示例数据（例如把策划给的表搬进来）时用。</summary>
            public ItemStackEntry(int itemId, int count)
            {
                this.itemId = itemId;
                this.count = count;
            }

            /// <summary>原料 id。</summary>
            public int ItemId => itemId;

            /// <summary>件数。</summary>
            public int Count => count;

            /// <summary>转成规则用的量。</summary>
            public ItemStack ToStack() => new ItemStack(itemId, count);
        }
    }
}
