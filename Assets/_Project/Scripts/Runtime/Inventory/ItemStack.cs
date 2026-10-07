// 职责：一件道具 + 件数。合成配方的原料与产物、将来的「消耗 / 产出」接口共用的最小数据形状。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：InventoryEntry 是「配置表项 + 背包数量」合并出来的展示快照，带显示名 / 品质 / 类别 / 描述，
//      配方原料只要 id 与件数，复用它会让配方反向依赖配置表与面板口径。
//   2. 扩展不行：往 InventoryEntry 加一个「当原料用」的构造，等于让展示快照承担配方语义，名实不符。
// 纯 DTO、不碰 Unity 与容器，EditMode 直接测。

namespace Game.Inventory
{
    /// <summary>道具 id + 件数。件数非正的条目视为非法（<see cref="IsValid"/>），由调用方决定是忽略还是报错。</summary>
    public readonly struct ItemStack
    {
        public ItemStack(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        /// <summary>tbitem 主键。</summary>
        public int ItemId { get; }

        /// <summary>件数。</summary>
        public int Count { get; }

        /// <summary>id &gt; 0 且件数 &gt; 0 才算一条合法的量。</summary>
        public bool IsValid => ItemId > 0 && Count > 0;

        public override string ToString() => "#" + ItemId + "×" + Count;
    }
}
