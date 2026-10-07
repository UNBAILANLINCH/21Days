// 职责：醉酒跳过回合时「屏幕中央闪现提示」的文案——逐字照抄策划原文，只搬不改。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:71`（微醺）、:72（薄醉）、:73（醉酒）。
// 注意 07:73 的文案用的是「醉酒状态」而不是「酩酊」，原文如此，本文件不改写。

namespace Game.TurnBased
{
    /// <summary>醉酒提示文案（表现层直接拿去显示，本模块不做界面）。</summary>
    public static class BattleHintTexts
    {
        /// <summary>微醺跳过回合的提示（07:71）。</summary>
        public const string TipsySkip = "怪物处于微醺状态，本回合无法行动。";

        /// <summary>薄醉跳过回合的提示（07:72）。</summary>
        public const string DrunkSkip = "怪物处于薄醉状态，本回合无法行动。";

        /// <summary>酩酊跳过回合的提示（07:73，原文用词是「醉酒状态」）。</summary>
        public const string DeadDrunkSkip = "怪物处于醉酒状态，本回合无法行动。";

        /// <summary>按跳过原因取文案；没跳过返回 null。</summary>
        public static string ForReason(DrunkSkipReason reason)
        {
            switch (reason)
            {
                case DrunkSkipReason.Tipsy:
                    return TipsySkip;
                case DrunkSkipReason.Drunk:
                    return DrunkSkip;
                case DrunkSkipReason.DeadDrunk:
                    return DeadDrunkSkip;
                default:
                    return null;
            }
        }
    }
}
