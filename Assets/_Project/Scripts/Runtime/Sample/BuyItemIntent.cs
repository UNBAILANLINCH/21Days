// 职责：一个「买某个道具若干个」的意图对象——UI 与输入产生它，规则类消费它。
// 为什么新建：architecture.md 第 7 节要求「状态改变走命令」——输入与 UI 产生意图对象，
//   由规则类应用，不直接改字段（将来联网时意图对象就是网络消息）。
//   1. 复用不行：工程里没有任何意图 / 命令类型。
//   2. 扩展不行：塞进 SampleRules.cs 会违反「一个文件一个类，文件名等于类名」
//      （csharp-code.md #命名与结构）；而且意图是**数据**，规则是**行为**，
//      将来意图要序列化发给服务端，规则要搬到服务端，两者生命周期不同。
//
// 文件名说明：波 4 派单里管这个文件叫 SampleIntents.cs，按上面那条命名规则改成了类名同名。

namespace Game.Sample
{
    /// <summary>
    /// 购买意图：买 <see cref="Count"/> 个 id 为 <see cref="ItemId"/> 的道具。
    /// <para>
    /// 是 <c>readonly struct</c>：不可变、无堆分配，和事件类型一个路子
    /// （区别在于事件描述**已经发生的事实**，意图描述**想让什么发生**，走的通道也不同——
    /// 事件走 MessagePipe，意图直接传给规则类）。
    /// </para>
    /// <para>
    /// **不在构造函数里校验**：<c>default(BuyItemIntent)</c> 绕得过构造函数，校验写在这里会给人
    /// 「构造出来就一定合法」的错觉。合法性由消费它的规则类在用的时候判
    /// （见 <see cref="SampleRules.GetOrderTotal"/>）。
    /// </para>
    /// </summary>
    public readonly struct BuyItemIntent
    {
        public BuyItemIntent(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        /// <summary>要买的道具 id，对应配置表 <c>TbItem</c> 的主键。</summary>
        public int ItemId { get; }

        /// <summary>数量，必须为正。</summary>
        public int Count { get; }

        public override string ToString() => $"买 {Count} 个道具 #{ItemId}";
    }
}
