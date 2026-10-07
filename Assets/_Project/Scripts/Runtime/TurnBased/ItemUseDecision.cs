// 职责：一次道具使用请求的判定结果（能不能用 + 不能用是为什么）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:42、:58`；
// 转写：`docs/design/features-spotlight/09_BOSS战.md:193`（R45）。

namespace Game.TurnBased
{
    /// <summary>道具使用请求的判定结果。</summary>
    public readonly struct ItemUseDecision
    {
        private ItemUseDecision(bool allowed, ItemUseReject reject)
        {
            Allowed = allowed;
            Reject = reject;
        }

        /// <summary>是否可以使用。</summary>
        public bool Allowed { get; }

        /// <summary>被拒原因；可用时为 <see cref="ItemUseReject.None"/>。</summary>
        public ItemUseReject Reject { get; }

        /// <summary>允许使用。</summary>
        public static ItemUseDecision Allow() => new ItemUseDecision(true, ItemUseReject.None);

        /// <summary>拒绝使用。</summary>
        public static ItemUseDecision Refuse(ItemUseReject reject) => new ItemUseDecision(false, reject);

        /// <summary>中文描述，用于日志与测试失败信息。</summary>
        public string Describe() => Allowed ? "可以使用道具" : "不能使用道具：" + ItemUseRules.Describe(Reject);
    }
}
