// 职责：玩家三招式的数值表——消耗多少怒气、打出多少伤害、留下什么效果。全是纯函数。
//
// 为什么与 PlayerBattleRules 分开：这里的每一条都是「配置怎么读」，没有状态；
// 状态机（怒气、次数、晕眩）在 `PlayerBattleRules` 里。分开之后「表」和「机」各自可测。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:50`（招式 1）、:52（招式 2）、:54（招式 3）。

namespace Game.TurnBased
{
    /// <summary>玩家招式的数值表。</summary>
    public static class PlayerSkillRules
    {
        /// <summary>这个招式是否合法。</summary>
        public static bool IsKnown(PlayerSkill skill) =>
            skill == PlayerSkill.Skill1 || skill == PlayerSkill.Skill2 || skill == PlayerSkill.Skill3;

        /// <summary>怒气消耗（07:50 / :52 / :54）。未知招式返回 0。</summary>
        public static int RageCost(PlayerSkill skill, in PlayerSkillSettings settings)
        {
            switch (skill)
            {
                case PlayerSkill.Skill1:
                    return settings.Skill1RageCost;
                case PlayerSkill.Skill2:
                    return settings.Skill2RageCost;
                case PlayerSkill.Skill3:
                    return settings.Skill3RageCost;
                default:
                    return 0;
            }
        }

        /// <summary>怒气产出（只有招式 1 有，07:50「使用后增加1点怒气」）。</summary>
        public static int RageGain(PlayerSkill skill, in PlayerSkillSettings settings) =>
            skill == PlayerSkill.Skill1 ? settings.Skill1RageGain : 0;

        /// <summary>基础伤害（不含招式 3 增益带来的额外伤害）。</summary>
        public static int BaseDamage(PlayerSkill skill, in PlayerSkillSettings settings)
        {
            switch (skill)
            {
                case PlayerSkill.Skill1:
                    return settings.Skill1Damage;
                case PlayerSkill.Skill2:
                    return settings.Skill2Damage;
                case PlayerSkill.Skill3:
                    return settings.Skill3Damage;
                default:
                    return 0;
            }
        }

        /// <summary>这次施放会给对方挂上的减疗百分比（只有招式 2 非 0，07:52）。</summary>
        public static int HealReductionPercent(PlayerSkill skill, in PlayerSkillSettings settings) =>
            skill == PlayerSkill.Skill2 ? settings.Skill2HealReductionPercent : 0;

        /// <summary>这次施放会给自己加多少次「额外伤害」次数（只有招式 3，07:54 → 3 次）。</summary>
        public static int GrantedExtraDamageAttacks(PlayerSkill skill, in PlayerSkillSettings settings) =>
            skill == PlayerSkill.Skill3 ? settings.Skill3ExtraDamageAttacks : 0;

        /// <summary>招式的中文描述。</summary>
        public static string Describe(PlayerSkill skill)
        {
            switch (skill)
            {
                case PlayerSkill.Skill1:
                    return "招式1（无消耗，+1 怒气，直接攻击）";
                case PlayerSkill.Skill2:
                    return "招式2（消耗 1 怒气，重击并减少对方 50% 治疗效果）";
                case PlayerSkill.Skill3:
                    return "招式3（消耗 3 怒气，重击并让接下来 3 次攻击附带额外伤害）";
                default:
                    return "未知招式";
            }
        }
    }
}
