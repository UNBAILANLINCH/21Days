// 职责：一次「使用道具」的**执行结果**——判定结果（ItemUseResult）+ 执行者状态 + 本次实际扣了几件。
// 为什么新建：ItemUseResult 是纯规则（ItemUseRules）的产物，它不该知道「效果有没有执行者」这件事——
//   那要读注册表、要碰 Unity 侧的目标，属于执行侧；把两者混成一个类型会让纯规则的测试被迫构造执行环境。
//   调用方（背包面板控制器）要一次拿齐「成不成 / 为什么不成 / 扣了几件 / 效果执行者怎么说」才拼得出提示。
namespace Game.Inventory
{
    /// <summary>
    /// 使用 + 执行的合并结果。由 <see cref="ItemUseService.TryUse"/> 产出。
    /// </summary>
    public readonly struct ItemUseOutcome
    {
        private ItemUseOutcome(ItemUseStatus status, ItemUseResult use, ItemEffectOutcome effect, int consumedCount)
        {
            Status = status;
            Use = use;
            Effect = effect;
            ConsumedCount = consumedCount;
        }

        /// <summary>最终结果码。判定失败时是 <see cref="ItemUseResult.Status"/>；执行阶段失败时是
        /// <see cref="ItemUseStatus.NoExecutor"/> 或 <see cref="ItemUseStatus.EffectRejected"/>。</summary>
        public ItemUseStatus Status { get; }

        /// <summary>纯规则那一步的结果（件数判定、效果标识、现有件数）。</summary>
        public ItemUseResult Use { get; }

        /// <summary>执行者的结果；没走到执行阶段时为默认值（<see cref="ItemEffectStatus.None"/>）。</summary>
        public ItemEffectOutcome Effect { get; }

        /// <summary>本次实际从背包扣掉的件数；没成功扣任何一件时为 0。</summary>
        public int ConsumedCount { get; }

        /// <summary>整件事是否成功（效果已执行、件数已扣）。</summary>
        public bool IsOk => Status == ItemUseStatus.Ok;

        internal static ItemUseOutcome Failed(ItemUseStatus status, ItemUseResult use)
            => new ItemUseOutcome(status, use, default, 0);

        internal static ItemUseOutcome Rejected(ItemUseResult use, ItemEffectOutcome effect)
            => new ItemUseOutcome(ItemUseStatus.EffectRejected, use, effect, 0);

        internal static ItemUseOutcome Succeeded(ItemUseResult use, ItemEffectOutcome effect, int consumedCount)
            => new ItemUseOutcome(ItemUseStatus.Ok, use, effect, consumedCount);
    }
}
