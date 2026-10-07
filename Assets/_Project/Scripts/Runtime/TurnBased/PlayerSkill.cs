// 职责：玩家的三个招式（07 二、战斗过程 · 2 战斗规则）。
// 出处：`docs/design/spotlight/07_回合制作战文档.md:48-54`；转写见
// `docs/design/features-spotlight/09_BOSS战.md:190`（R42）。

namespace Game.TurnBased
{
    /// <summary>玩家招式。<see cref="None"/> 是非法值，用来抓「没给招式却要求结算」的调用。</summary>
    public enum PlayerSkill
    {
        /// <summary>不是任何招式。</summary>
        None = 0,

        /// <summary>招式 1：无消耗，使用后 +1 怒气，造成伤害（07:50）。</summary>
        Skill1 = 1,

        /// <summary>招式 2：消耗 1 点怒气，造成伤害并减少对方 50% 治疗效果（07:52）。</summary>
        Skill2 = 2,

        /// <summary>招式 3：消耗 3 点怒气，造成伤害，且接下来 3 次攻击附带额外伤害（07:54）。</summary>
        Skill3 = 3,
    }
}
