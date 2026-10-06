// 职责：回合流程与胜负的配置（纯值）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:43`（剩余回合 → 有上限）、:56（施放招式后进敌方回合）；
// 上限多少、到上限算赢算输、晕眩时怎么过回合原文都没写（09:192、09:198），等 C91。

namespace Game.TurnBased
{
    /// <summary>回合流程与胜负的数值。</summary>
    public readonly struct BattleFlowSettings
    {
        /// <summary>构造。</summary>
        /// <param name="roundLimit">回合上限；0 = 不限（原文没写，占位 0，等 C91）。</param>
        /// <param name="roundLimitOutcome">到上限时的胜负口径（原文没写，占位 CompareHealth）。</param>
        /// <param name="stunTurnPolicy">玩家被晕眩时怎么过回合（原文没写，占位 SkipTurn）。</param>
        public BattleFlowSettings(int roundLimit, RoundLimitOutcome roundLimitOutcome, StunTurnPolicy stunTurnPolicy)
        {
            RoundLimit = roundLimit;
            RoundLimitOutcome = roundLimitOutcome;
            StunTurnPolicy = stunTurnPolicy;
        }

        /// <summary>回合上限；0 = 不限。</summary>
        public int RoundLimit { get; }

        /// <summary>到上限时的胜负口径。</summary>
        public RoundLimitOutcome RoundLimitOutcome { get; }

        /// <summary>玩家被晕眩时这一回合怎么过。</summary>
        public StunTurnPolicy StunTurnPolicy { get; }

        /// <summary>本工程当前的占位默认值（与 `TurnBasedConfig` 的字段默认一致）。</summary>
        public static BattleFlowSettings PlaceholderDefault =>
            new BattleFlowSettings(0, RoundLimitOutcome.CompareHealth, StunTurnPolicy.SkipTurn);
    }
}
