// 职责：一条战斗事件在舞台上演成什么（出招冲上去再退回、饮酒、跳过回合闪现提示……）。
// 为什么新建：一个类型一个文件；BattleCueRules 把 BattleEvent 翻成它，表现层按它分派到舞台与界面。
namespace Game.Battle
{
    /// <summary>演出种类。</summary>
    public enum BattleCueKind
    {
        /// <summary>没有演出（未知事件，表现层跳过）。</summary>
        None = 0,

        /// <summary>偷袭开战，BOSS 原地挨一下（07:18）。</summary>
        SneakHit = 1,

        /// <summary>玩家出招：闪身到敌人身前 → 命中 → 退回（07:50-54）；重招 = 招式 3。</summary>
        PlayerStrike = 2,

        /// <summary>玩家用道具（原地，回血飘绿字）。</summary>
        ItemUse = 3,

        /// <summary>BOSS 醉得跳过回合：屏幕中央闪现 07 原文提示（07:71-73）。</summary>
        BossSkip = 4,

        /// <summary>BOSS 出招：冲上去打再退回（07:79 / :81）；重招 = 招式 3。</summary>
        BossStrike = 5,

        /// <summary>BOSS 饮酒：原地「饮酒」字样、醉酒条上涨、回血飘绿字（07:80）。</summary>
        BossDrink = 6,

        /// <summary>玩家被晕眩：头上出晕眩标记（07:82）。</summary>
        PlayerStun = 7,

        /// <summary>玩家因晕眩被跳过本回合：中央提示。</summary>
        PlayerStunSkip = 8,

        /// <summary>玩家倒下（战败）。</summary>
        PlayerDown = 9,

        /// <summary>BOSS 倒下（战胜）。</summary>
        BossDown = 10,

        /// <summary>打到回合上限：中央提示。</summary>
        RoundLimit = 11,
    }
}
