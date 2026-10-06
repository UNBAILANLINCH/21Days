// 职责：一次合成判定的结果码。
// 为什么新建：一个文件一个类型（csharp-code.md）；结果码与结果数据（ItemCraftResult）是两个关注点，
//   分开才能让调用方只 switch 状态、不被数据形状牵动。

namespace Game.Inventory
{
    /// <summary>合成判定的结果码。除 <see cref="Ok"/> 外都不产生任何产出。</summary>
    public enum ItemCraftStatus
    {
        /// <summary>原料齐了，可以合成。</summary>
        Ok = 0,

        /// <summary>没有这条配方：配方列表为空 / 按产物 id 找不到 / 传进来的配方为 null。</summary>
        NoRecipe = 1,

        /// <summary>配方本身写坏：标识为空、产物 id 或件数非正、某条原料的 id 或件数非正。</summary>
        InvalidRecipe = 2,

        /// <summary>配方一条原料都没有（「原料为空」边界）；先于 <see cref="InvalidRecipe"/> 判定。</summary>
        EmptyIngredients = 3,

        /// <summary>原料不够：背包里没有这件原料，或件数少于配方要的（缺哪件见 <see cref="ItemCraftResult.MissingItemId"/>）。</summary>
        MissingIngredient = 4,
    }
}
