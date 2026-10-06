// 职责：进入战斗被拒的原因（判定为纯函数，拒绝路径必须点名原因，不许静默）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:13-28`：三种方式的触发条件各自是合取 / 析取，
// 条件不成立就不该进战斗——不静默、不默认进正面战斗。

namespace Game.TurnBased
{
    /// <summary>进入战斗判定的拒绝原因。</summary>
    public enum BattleEntryReject
    {
        /// <summary>没被拒（判定通过）。</summary>
        None = 0,

        /// <summary>三种触发条件一条都不成立：不在警戒 / 敌对区域，怪物也未察觉（07:22、:27）。</summary>
        NoTrigger = 1,

        /// <summary>玩家在潜行但偷袭没成功，且其他条件也不成立（07:17「偷袭怪物成功」）。</summary>
        SneakStrikeNotLanded = 2,

        /// <summary>输入自相矛盾：偷袭成功却由 BOSS 先动手（07:18 与 :28 不可能同真）。</summary>
        InconsistentInitiator = 3,

        /// <summary>快照本身不合法（例如 BOSS 生命上限非正）。</summary>
        InvalidSnapshot = 4,
    }
}
