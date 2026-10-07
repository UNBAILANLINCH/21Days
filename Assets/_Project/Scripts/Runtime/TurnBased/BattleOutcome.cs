// 职责：一场战斗怎么结束。
//
// 出处：BOSS「生命归零结束」（`docs/design/features-spotlight/09_BOSS战.md:208` 阶段一 BOSS 行、
// sp03「血量归零时转换阶段」的同一套血量口径）；玩家怎么输、到回合上限算赢算输原文没写
// （09:192 R44、09:218），等 C91。

namespace Game.TurnBased
{
    /// <summary>战斗结果。</summary>
    public enum BattleOutcome
    {
        /// <summary>还没结束。</summary>
        None = 0,

        /// <summary>玩家赢（BOSS 生命归零）。</summary>
        Victory = 1,

        /// <summary>玩家输（玩家生命归零，或按回合上限口径判负）。</summary>
        Defeat = 2,
    }
}
