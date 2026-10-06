// 职责：战斗结果 → 剧情出口键。本模块只**产出**键，不写任何剧情状态。
//
// 为什么只给字符串常量、不引用 Narrative 的类型：`PRP/battle-to-narrative/prp.md` §2.1 定了一条唯一写入方
// ——战斗侧结算出结果、由遭遇流程（`Runtime/Monster/EncounterStep`）交给
// `NarrativeRules.CompleteBattle`。回合制内核若反过来 `using Game.Narrative`，依赖方向就倒了
// （Narrative 是消费方）。所以这里只提供与 `BattleOutcome` 词汇一致的键名，
// 接线由遭遇流程完成（见交付报告「建议补丁」）。
//
// 键名出处：`PRP/battle-to-narrative/prp.md:17`（`BattleResult` 的四个常量 Downed / Exposed /
// BossPhaseChanged / Victory）与 §2.3 的流程语义；`ai-docs/docs/story-facts.md:151-153` 的 `combat.*` 键
// 写入方是 Monster / Inventory，**不是本模块**（§6.1「一个键一个写入方」）。

namespace Game.TurnBased
{
    /// <summary>战斗结果对应的剧情出口键（PRP 的 `BattleResult` 词汇）。</summary>
    public static class BattleExitKeys
    {
        /// <summary>胜利：推进到下一阶段（PRP §2.3）。</summary>
        public const string Victory = "Victory";

        /// <summary>被击倒（玩家输）：走剧情侧的失败出口（PRP §2.3）。</summary>
        public const string Downed = "Downed";
    }
}
