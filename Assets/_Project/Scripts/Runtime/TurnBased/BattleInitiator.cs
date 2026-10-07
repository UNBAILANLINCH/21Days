// 职责：快照量「谁先动手」——进入战斗判定的输入之一。
// 出处：`docs/design/spotlight/07_回合制作战文档.md:15-28`：
//   玩家攻击 BOSS → 偷袭 / 正面攻击；玩家被 BOSS 攻击 → 被打（BOSS 先手）。

namespace Game.TurnBased
{
    /// <summary>这次接触是谁先动的手。<b>由接线侧从玩法事件翻译过来，本模块不认识任何怪物类型。</b></summary>
    public enum BattleInitiator
    {
        /// <summary>玩家先动手（攻击了 BOSS）。</summary>
        PlayerAttacked = 0,

        /// <summary>BOSS 先动手（玩家挨打）。</summary>
        BossAttacked = 1,
    }
}
