// 职责：玩家施放招式被拒的原因。拒绝路径必须点名，不许静默吞掉。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:48`（招式只能在自己回合使用）、
// :50/:52/:54（三个招式各自的怒气消耗）、
// `docs/design/features-spotlight/09_BOSS战.md:198`（晕眩期间不能行动的原文没写 → 等 C91）。

namespace Game.TurnBased
{
    /// <summary>施放招式被拒的原因。</summary>
    public enum SkillCastReject
    {
        /// <summary>没被拒（施放成功）。</summary>
        None = 0,

        /// <summary>不是玩家回合（07:48「都只能在自己回合使用」）。</summary>
        NotPlayerTurn = 1,

        /// <summary>玩家已被打倒，战斗结束了。</summary>
        Defeated = 2,

        /// <summary>玩家处于晕眩，本回合不能行动（07:82）。</summary>
        Stunned = 3,

        /// <summary>本回合已经出过招了（07:56「玩家在施放招式后，进入敌方回合」）。</summary>
        AlreadyCastThisTurn = 4,

        /// <summary>怒气不够（07:52 消耗 1、07:54 消耗 3）。</summary>
        NotEnoughRage = 5,

        /// <summary>招式值非法（<see cref="PlayerSkill.None"/> 或枚举外的值）。</summary>
        UnknownSkill = 6,
    }
}
