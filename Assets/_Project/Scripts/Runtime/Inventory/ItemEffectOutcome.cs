// 职责：一次效果执行的结果——结局码 + 对玩家有意义的数值（实际回了多少血）+ 中文原因。
// 为什么新建：一个文件一个类型。调用方要的是一句话提示，不是只有码；把原因字符串放在结果里，
//   「未接执行者」这种必须让玩家看见的失败才有人负责把它写出来（不许静默失败）。
namespace Game.Inventory
{
    /// <summary>效果执行结果。<see cref="Amount"/> 的语义由具体效果定义（治疗是「实际回复的生命」）。</summary>
    public readonly struct ItemEffectOutcome
    {
        private ItemEffectOutcome(ItemEffectStatus status, int amount, string reason)
        {
            Status = status;
            Amount = amount;
            Reason = reason ?? string.Empty;
        }

        /// <summary>结局码。</summary>
        public ItemEffectStatus Status { get; }

        /// <summary>效果的数值结果；没有数值时为 0。</summary>
        public int Amount { get; }

        /// <summary>给玩家看的中文短原因（失败时非空，成功时为空串）。</summary>
        public string Reason { get; }

        /// <summary>效果是否真的执行了。</summary>
        public bool IsApplied => Status == ItemEffectStatus.Applied;

        /// <summary>已经执行完一次效果，带一个对玩家有意义的数值。</summary>
        public static ItemEffectOutcome Applied(int amount) => new ItemEffectOutcome(ItemEffectStatus.Applied, amount, string.Empty);

        /// <summary>执行者拒绝执行，<paramref name="reason"/> 是要展示给玩家的中文短句。</summary>
        public static ItemEffectOutcome Rejected(string reason) => new ItemEffectOutcome(ItemEffectStatus.Rejected, 0, reason);

        /// <summary>要的目标没接进来 / 参数非法；<paramref name="reason"/> 会进日志与埋点，也会给玩家看。</summary>
        public static ItemEffectOutcome BadTarget(string reason) => new ItemEffectOutcome(ItemEffectStatus.BadTarget, 0, reason);

        /// <summary>没有执行者（注册表里查不到这个效果标识）。</summary>
        public static ItemEffectOutcome NoExecutor() => new ItemEffectOutcome(ItemEffectStatus.NoExecutor, 0, "这个道具的效果还没有接上");
    }
}
