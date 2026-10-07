// 职责：一次「使用道具」判定的结果数据——状态 + 命中的使用定义 + 背包现有件数。
// 为什么新建：一个文件一个类型；调用方要同时知道「成不成」「效果标识是什么」「这次要扣几件」「现在有几件」，
//   只回 bool 的话执行者拿不到效果，只回标识的话区分不了「没有这件道具」和「件数不够」。

namespace Game.Inventory
{
    /// <summary>
    /// 使用判定结果。只描述「能不能用、用出来是什么效果」，不执行效果、不扣件数
    /// （执行者与落盘见 <see cref="ItemUseRules"/> 文件头）。
    /// </summary>
    public readonly struct ItemUseResult
    {
        private ItemUseResult(ItemUseStatus status, ItemUseSpec spec, int ownedCount)
        {
            Status = status;
            Spec = spec;
            OwnedCount = ownedCount;
        }

        /// <summary>结果码。</summary>
        public ItemUseStatus Status { get; }

        /// <summary>命中的使用定义；未命中（<see cref="ItemUseStatus.UnknownItem"/>）时为默认 <see cref="ItemUseSpec"/>。</summary>
        public ItemUseSpec Spec { get; }

        /// <summary>判定时背包里这件道具的件数；未知 id 时为 0。</summary>
        public int OwnedCount { get; }

        /// <summary>效果标识的快捷读法；未命中时为空串。</summary>
        public string EffectId => Spec.EffectId ?? string.Empty;

        /// <summary>这次使用要扣掉的件数；未命中时为 0。</summary>
        public int ConsumeCount => Spec.ConsumeCount;

        /// <summary>是否可以使用。</summary>
        public bool IsOk => Status == ItemUseStatus.Ok;

        internal static ItemUseResult Succeeded(ItemUseSpec spec, int ownedCount)
            => new ItemUseResult(ItemUseStatus.Ok, spec, ownedCount);

        internal static ItemUseResult Failed(ItemUseStatus status, ItemUseSpec spec, int ownedCount)
            => new ItemUseResult(status, spec, ownedCount);
    }
}
