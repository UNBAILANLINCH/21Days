// 职责：先手权归谁。三种进入方式各给一个结果（07 一、进入战斗）。
// 出处：`docs/design/spotlight/07_回合制作战文档.md:18 / :23 / :28`。

namespace Game.TurnBased
{
    /// <summary>战斗第一回合由谁行动。</summary>
    public enum BattleInitiative
    {
        /// <summary>未定（没进入战斗时）。</summary>
        None = 0,

        /// <summary>玩家先手：偷袭（07:18）与正面攻击（07:23）。</summary>
        Player = 1,

        /// <summary>BOSS 先手：玩家受到攻击（07:28）。</summary>
        Boss = 2,
    }
}
