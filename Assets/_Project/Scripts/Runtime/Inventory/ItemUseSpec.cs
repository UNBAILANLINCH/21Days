// 职责：一件道具「用出去」会发生什么的数据形状——道具 id → 效果标识 + 一次消耗几件。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootRules / InventoryRules 只有「id → 数量」，没有任何「使用」语义；cfg.Item 是表行，也没有效果列。
//   2. 扩展不行：往 cfg.Item 加效果列要先改 __beans__.xlsx 再改 item.xlsx，而「一件道具能不能用、用几次、
//      效果叫什么」在 [05] 里还是待定项（见交付报告「待策划拍板」），先落成代码形状才不会把待定项钉进表结构。
// 谁读它：ItemUseRules.TryUse；效果标识的实际执行者见 ItemUseRules 的文件头。

namespace Game.Inventory
{
    /// <summary>
    /// 一条「使用」定义。效果标识是冻结字面量，本类型不解释它、也不查白名单。
    /// 出处：docs/design/features-spotlight/05_皮面具与道具.md:211（「使用」这一行动词的原文例子：
    /// 普通皮「使用后可以进入坊市店铺」、籍中吏处「使用泾龙丹进行伪装」）。
    /// </summary>
    public readonly struct ItemUseSpec
    {
        /// <param name="itemId">道具 tbitem 主键。</param>
        /// <param name="effectId">效果标识，约定写成「域.动作」（如 <c>shop.enter_with_skin</c>）；空串视为写坏。</param>
        /// <param name="consumeCount">使用一次消耗几件，默认 1（一次性道具就是靠它表达，[05]:111 R9）。</param>
        public ItemUseSpec(int itemId, string effectId, int consumeCount = 1)
        {
            ItemId = itemId;
            EffectId = effectId ?? string.Empty;
            ConsumeCount = consumeCount;
        }

        /// <summary>道具 tbitem 主键。</summary>
        public int ItemId { get; }

        /// <summary>效果标识；谁认这个标识见 <see cref="ItemUseRules"/> 文件头。</summary>
        public string EffectId { get; }

        /// <summary>使用一次消耗的件数。</summary>
        public int ConsumeCount { get; }

        /// <summary>id 为正、效果标识非空、消耗件数为正才算一条合法的使用定义。</summary>
        public bool IsValid => ItemId > 0 && !string.IsNullOrEmpty(EffectId) && ConsumeCount > 0;

        public override string ToString() => "#" + ItemId + " → " + EffectId + "（消耗 " + ConsumeCount + "）";
    }
}
