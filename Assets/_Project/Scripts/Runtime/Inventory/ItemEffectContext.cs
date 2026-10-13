// 职责：一次效果执行的只读上下文——用掉的是哪件道具、效果标识是什么、目标口是谁。
// 为什么新建：把「哪件道具」和「目标是谁」一起交给执行者，执行者就不必认识 LootService / 背包，
//   也不必去容器里找目标；效果因此可以脱离容器单测（测试里塞一个假目标即可）。
namespace Game.Inventory
{
    /// <summary>效果执行的只读上下文。</summary>
    public sealed class ItemEffectContext
    {
        /// <param name="itemId">被使用的道具 id（tbitem 主键）。</param>
        /// <param name="effectId">效果标识（<see cref="ItemUseSpec.EffectId"/>）。</param>
        /// <param name="target">效果目标口；调用方没接时为 null，执行者要按 <see cref="ItemEffectStatus.BadTarget"/> 处理。</param>
        public ItemEffectContext(int itemId, string effectId, IItemEffectTarget target)
        {
            ItemId = itemId;
            EffectId = effectId ?? string.Empty;
            Target = target;
        }

        /// <summary>被使用的道具 id。</summary>
        public int ItemId { get; }

        /// <summary>效果标识。</summary>
        public string EffectId { get; }

        /// <summary>效果目标口；可能为 null（没接玩家生命口时）。</summary>
        public IItemEffectTarget Target { get; }
    }
}
