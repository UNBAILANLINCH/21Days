// 职责：一次 BOSS 招式的结算结果——打了多少、回了多少、加了多少醉酒、晕眩玩家几回合。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:79-82`。

namespace Game.TurnBased
{
    /// <summary>一次 BOSS 招式的结算结果。</summary>
    public readonly struct BossSkillResult
    {
        private BossSkillResult(
            BossSkill skill,
            int damage,
            int drunkAdded,
            int healedAmount,
            int stunRounds,
            bool consumedSkill1Bonus,
            bool grantedSkill1Bonus)
        {
            Skill = skill;
            Damage = damage;
            DrunkAdded = drunkAdded;
            HealedAmount = healedAmount;
            StunRounds = stunRounds;
            ConsumedSkill1Bonus = consumedSkill1Bonus;
            GrantedSkill1Bonus = grantedSkill1Bonus;
        }

        /// <summary>这次用的招式。</summary>
        public BossSkill Skill { get; }

        /// <summary>对玩家造成的伤害。</summary>
        public int Damage { get; }

        /// <summary>增加的醉酒值（招式 2 饮酒）。</summary>
        public int DrunkAdded { get; }

        /// <summary>实际回复的生命（招式 2；已被玩家的减疗削弱过）。</summary>
        public int HealedAmount { get; }

        /// <summary>晕眩玩家的回合数（招式 3 在薄醉态）。</summary>
        public int StunRounds { get; }

        /// <summary>这次是否吃掉了「下次招式 1 +30%」的加成。</summary>
        public bool ConsumedSkill1Bonus { get; }

        /// <summary>这次是否重新挂上了「下次招式 1 +30%」（招式 2）。</summary>
        public bool GrantedSkill1Bonus { get; }

        /// <summary>构造。</summary>
        public static BossSkillResult Of(
            BossSkill skill,
            int damage,
            int drunkAdded,
            int healedAmount,
            int stunRounds,
            bool consumedSkill1Bonus,
            bool grantedSkill1Bonus) =>
            new BossSkillResult(skill, damage, drunkAdded, healedAmount, stunRounds, consumedSkill1Bonus, grantedSkill1Bonus);
    }
}
