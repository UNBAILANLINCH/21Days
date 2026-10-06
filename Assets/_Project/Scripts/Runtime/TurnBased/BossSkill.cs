// 职责：BOSS 的三个招式（07 三、1. 阶段1BOSS · BOSS招式）。
// 出处：`docs/design/spotlight/07_回合制作战文档.md:79-84`；转写见
// `docs/design/features-spotlight/09_BOSS战.md:198`（R47）。

namespace Game.TurnBased
{
    /// <summary>BOSS 招式。</summary>
    public enum BossSkill
    {
        /// <summary>不是任何招式（选了 0 权重或加权值非法时的兜底）。</summary>
        None = 0,

        /// <summary>招式 1：普通攻击，造成一定伤害（07:79）。</summary>
        Skill1 = 1,

        /// <summary>招式 2：饮酒，+30 醉酒值，回复 10% 生命，下次招式 1 伤害 +30%（07:80）。</summary>
        Skill2 = 2,

        /// <summary>招式 3：正常 / 微醺时单次高额伤害；薄醉时高额伤害并晕眩玩家 1 回合（07:81-82）。</summary>
        Skill3 = 3,
    }
}
