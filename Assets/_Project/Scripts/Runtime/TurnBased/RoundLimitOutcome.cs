// 职责：打到回合上限时算赢还是算输——原文没写，做成配置项。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:43`（界面写「玩家状态右下角显示剩余回合」，
// 说明有回合上限）与 :44（上限是多少、到上限算赢算输都没写）；
// `待策划拍板问题.md:1269` C91 的可选方向：A 赢 / B 输 / C 看剩余血量 / D 其他。占位 C。

namespace Game.TurnBased
{
    /// <summary>回合上限到达时的胜负口径。</summary>
    public enum RoundLimitOutcome
    {
        /// <summary>按双方剩余生命的比例判（占位口径；相等时判玩家输，取保守解释）。</summary>
        CompareHealth = 0,

        /// <summary>直接判玩家赢。</summary>
        Victory = 1,

        /// <summary>直接判玩家输。</summary>
        Defeat = 2,
    }
}
