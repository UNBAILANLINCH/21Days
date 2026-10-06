// 职责：怪物回合的醉酒结算结果——跳过没跳过、为什么、醉酒值变成多少、给屏幕中央的提示文案。
//
// 为什么要有它：07:71-73 要求跳过回合时「出现屏幕中央闪现提示」，本模块不做界面，
// 但必须把这句话（以及可判定的原因）**当成结果带出来**，否则接线侧拿不到该显示什么。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:71-73`；文案逐字照抄原文。

namespace Game.TurnBased
{
    /// <summary>怪物回合开始时的醉酒结算结果。</summary>
    public readonly struct DrunkTurnOutcome
    {
        private DrunkTurnOutcome(
            bool skipped,
            DrunkSkipReason reason,
            DrunkTier tierAtTurnStart,
            int drunkValueAfter,
            bool drunkValueDropped,
            int deadDrunkRoundsRemaining,
            string hintText)
        {
            Skipped = skipped;
            Reason = reason;
            TierAtTurnStart = tierAtTurnStart;
            DrunkValueAfter = drunkValueAfter;
            DrunkValueDropped = drunkValueDropped;
            DeadDrunkRoundsRemaining = deadDrunkRoundsRemaining;
            HintText = hintText;
        }

        /// <summary>本回合怪物是否跳过行动。</summary>
        public bool Skipped { get; }

        /// <summary>跳过原因（没跳过时是 <see cref="DrunkSkipReason.None"/>）。</summary>
        public DrunkSkipReason Reason { get; }

        /// <summary>回合开始那一刻的档位（07:70-73 的四档按回合开始的值判）。</summary>
        public DrunkTier TierAtTurnStart { get; }

        /// <summary>结算后的醉酒值。</summary>
        public int DrunkValueAfter { get; }

        /// <summary>这次结算是否让醉酒值下降了（酩酊的 -50）。</summary>
        public bool DrunkValueDropped { get; }

        /// <summary>酩酊还剩几个怪物回合（含已算上本回合之后的剩余）。</summary>
        public int DeadDrunkRoundsRemaining { get; }

        /// <summary>屏幕中央要闪现的提示文案；没跳过时是 null。</summary>
        public string HintText { get; }

        /// <summary>正常行动。</summary>
        public static DrunkTurnOutcome Act(DrunkTier tier, int drunkValueAfter) =>
            new DrunkTurnOutcome(false, DrunkSkipReason.None, tier, drunkValueAfter, false, 0, null);

        /// <summary>跳过回合。</summary>
        public static DrunkTurnOutcome Skip(
            DrunkSkipReason reason,
            DrunkTier tierAtTurnStart,
            int drunkValueAfter,
            bool drunkValueDropped,
            int deadDrunkRoundsRemaining,
            string hintText) =>
            new DrunkTurnOutcome(
                true,
                reason,
                tierAtTurnStart,
                drunkValueAfter,
                drunkValueDropped,
                deadDrunkRoundsRemaining,
                hintText);
    }
}
