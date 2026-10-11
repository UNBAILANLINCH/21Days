// 职责：战斗过程事件——本模块不做界面，但必须把「界面上该发生什么」如实带出来。
//
// 重点是被要求的那一条：怪物跳过回合时要能拿到「屏幕中央闪现提示」所需的结果标记
// （07:71-73 的三句文案见 `BattleHintTexts`）。其余事件供接线侧做动画、血条、怒气槽。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:34-44`（画面表现）、:70-84（醉酒与 BOSS 招式）。

namespace Game.TurnBased
{
    /// <summary>战斗事件种类。</summary>
    public enum BattleEventKind
    {
        /// <summary>没有事件。</summary>
        None = 0,

        /// <summary>偷袭开战先扣了 BOSS 的生命（07:18）。<c>Amount</c> = 扣掉的血量。</summary>
        SneakHealthLoss = 1,

        /// <summary>玩家施放招式（07:50-54）。<c>PlayerSkill</c> / <c>Amount</c> = 伤害。</summary>
        SkillCast = 2,

        /// <summary>玩家用掉一件道具（07:58）。<c>ItemId</c> = 道具 id，<c>Amount</c> = 道具效果实际回复的生命（占位效果，见 BattleItemSettings）。</summary>
        ItemUsed = 3,

        /// <summary>怪物醉得跳过回合（07:71-73）。带档位、原因与屏幕中央要闪现的文案。</summary>
        BossTurnSkipped = 4,

        /// <summary>BOSS 出招（07:79-82）。<c>BossSkill</c> / <c>Amount</c> = 伤害。</summary>
        BossSkillUsed = 5,

        /// <summary>BOSS 饮酒（07:80）。<c>Amount</c> = 实际增加的醉酒值，<c>SecondaryAmount</c> = 实际回血。</summary>
        BossDrank = 6,

        /// <summary>玩家被晕眩（07:82）。<c>Amount</c> = 晕眩回合数。</summary>
        PlayerStunned = 7,

        /// <summary>玩家因晕眩被跳过了一整个回合。<c>Amount</c> = 当时还剩的晕眩回合数。</summary>
        PlayerStunnedTurnSkipped = 8,

        /// <summary>玩家被打倒，战斗失败。</summary>
        PlayerDefeated = 9,

        /// <summary>BOSS 生命归零，战斗胜利。</summary>
        BossDefeated = 10,

        /// <summary>打到回合上限（07:43 的「剩余回合」用完）。<c>Outcome</c> = 判出来的胜负。</summary>
        RoundLimitReached = 11,
    }
}
