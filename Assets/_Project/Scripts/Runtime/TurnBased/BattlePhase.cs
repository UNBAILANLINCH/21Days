// 职责：战斗当前轮到谁。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:48`（招式只能在自己回合使用）、
// :56（玩家在施放招式后进入敌方回合）——也就是「一个回合 = 玩家出一招 + 敌方行动一次」
// （转写：`docs/design/features-spotlight/09_BOSS战.md:191` R43）。

namespace Game.TurnBased
{
    /// <summary>战斗阶段。</summary>
    public enum BattlePhase
    {
        /// <summary>战斗已结束。</summary>
        Ended = 0,

        /// <summary>玩家回合。</summary>
        PlayerTurn = 1,

        /// <summary>敌方回合。</summary>
        BossTurn = 2,
    }
}
