// 职责：BOSS 侧的战斗状态——生命、醉酒（转发给 BossDrunkRules）、减疗、招式 1 的增伤挂起。
//
// 为什么新建：`Monster` 模块只管一只怪的巡逻 / 感知 / 血量（`09_BOSS战.md:271` 明确写缺口
// 「没有 BOSS 种类；没有多阶段……没有削弱、跳过阶段」），而 `Runtime/Monster/` 是并行任务目录，
// 本次一个字都不动。回合制需要的这四项状态在既有类型里没有位置，故新建。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:52`（玩家招式 2 减对方 50% 治疗效果）、
// :66-73（醉酒）、:79-82（BOSS 招式效果）。

using Game.Core.Simulation;

namespace Game.TurnBased
{
    /// <summary>BOSS 在战斗内的状态。</summary>
    public sealed class BossBattleRules
    {
        private readonly BossSkillSettings skillSettings;
        private int health;
        private bool nextSkill1DamageBonusPending;
        private int healReductionPercent;
        private int healReductionRoundsRemaining;
        private bool healReductionPermanent;

        /// <summary>
        /// 用血量 / 醉酒快照与数值配置构造。
        /// </summary>
        /// <param name="snapshot">生命与战斗外醉酒值。</param>
        /// <param name="skillSettings">BOSS 招式数值。</param>
        /// <param name="drunkSettings">醉酒四档数值。</param>
        /// <param name="inheritsDrunkValue">是否继承战斗外的醉酒值（07:66；配置开关，占位 true）。</param>
        public BossBattleRules(
            in BossBattleSnapshot snapshot,
            in BossSkillSettings skillSettings,
            in DrunkSettings drunkSettings,
            bool inheritsDrunkValue)
        {
            this.skillSettings = skillSettings;
            MaxHealth = snapshot.MaxHealth;
            health = snapshot.Health;
            Drunk = new BossDrunkRules(inheritsDrunkValue ? snapshot.DrunkValue : 0, drunkSettings);
        }

        /// <summary>生命上限。</summary>
        public int MaxHealth { get; }

        /// <summary>当前生命。</summary>
        public int Health => health;

        /// <summary>是否已被打倒（07 用「血量归零」表达 BOSS 战结束，本模块只如实标记）。</summary>
        public bool IsDefeated => health <= 0;

        /// <summary>醉酒值状态机（07:66-73）。</summary>
        public BossDrunkRules Drunk { get; }

        /// <summary>当前醉酒值。</summary>
        public int DrunkValue => Drunk.Value;

        /// <summary>当前醉酒档位。</summary>
        public DrunkTier Tier => Drunk.Tier;

        /// <summary>「下次招式 1 伤害 +30%」是否挂着（07:80，用掉一次就没了）。</summary>
        public bool NextSkill1DamageBonusPending => nextSkill1DamageBonusPending;

        /// <summary>当前受到的治疗削减百分比（玩家招式 2，07:52）。</summary>
        public int HealReductionPercent => healReductionPercent;

        /// <summary>减疗还剩几个怪物回合（0 = 已失效或持续到战斗结束）。</summary>
        public int HealReductionRoundsRemaining => healReductionRoundsRemaining;

        /// <summary>减疗是否持续到战斗结束（玩家招式 2 的持续回合数配成 0 时）。</summary>
        public bool HealReductionIsPermanent => healReductionPermanent;

        /// <summary>挨伤害。</summary>
        public void ApplyDamage(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            health = GameMath.Clamp(health - amount, 0, MaxHealth);
        }

        /// <summary>按生命百分比算一个数（偷袭扣血、饮酒回血共用；整数向下取整）。</summary>
        public int PercentOfHealth(int percent, HealthPercentBase basis)
        {
            if (percent <= 0)
            {
                return 0;
            }

            int baseValue = basis == HealthPercentBase.CurrentHealth ? health : MaxHealth;
            return baseValue * percent / 100;
        }

        /// <summary>
        /// 回复生命（BOSS 招式 2 饮酒，07:80 回 10%），会先被玩家招式 2 的减疗削弱（07:52）。
        /// </summary>
        /// <returns>实际回复的生命。</returns>
        public int Heal(int percent, HealthPercentBase basis)
        {
            int raw = PercentOfHealth(percent, basis);
            if (raw <= 0)
            {
                return 0;
            }

            int effective = healReductionPercent > 0
                ? raw - raw * healReductionPercent / 100
                : raw;

            int before = health;
            health = GameMath.Clamp(health + effective, 0, MaxHealth);
            return health - before;
        }

        /// <summary>
        /// 给 BOSS 挂上治疗削减（玩家招式 2，07:52）。
        /// <para>
        /// 重复施放是**覆盖**而不是叠加：原文没写叠加规则，占位取覆盖（见交付报告「待拍板」）。
        /// <paramref name="rounds"/> 传 0 表示持续到战斗结束。
        /// </para>
        /// </summary>
        public void ReceiveHealReduction(int percent, int rounds)
        {
            healReductionPercent = GameMath.Clamp(percent, 0, 100);
            healReductionPermanent = rounds <= 0;
            healReductionRoundsRemaining = rounds > 0 ? rounds : 0;
        }

        /// <summary>怪物回合结束时推进减疗计时。</summary>
        public void TickHealReduction()
        {
            if (healReductionPercent <= 0 || healReductionPermanent)
            {
                return;
            }

            healReductionRoundsRemaining--;
            if (healReductionRoundsRemaining <= 0)
            {
                healReductionPercent = 0;
                healReductionRoundsRemaining = 0;
            }
        }

        /// <summary>挂上「下次招式 1 伤害 +30%」（BOSS 招式 2，07:80）。</summary>
        public void GrantSkill1DamageBonus() => nextSkill1DamageBonusPending = true;

        /// <summary>吃掉「下次招式 1 伤害 +30%」，返回这次是否吃到（07:80 的「下次」只用一次）。</summary>
        public bool ConsumeSkill1DamageBonus()
        {
            if (!nextSkill1DamageBonusPending)
            {
                return false;
            }

            nextSkill1DamageBonusPending = false;
            return true;
        }

        /// <summary>招式 1 这次打出的伤害（含挂起的 +30%）。</summary>
        public int Skill1DamageNow() => BossSkillRules.Skill1Damage(skillSettings, nextSkill1DamageBonusPending);

        /// <summary>招式 3 这次打出的伤害。</summary>
        public int Skill3DamageNow() => BossSkillRules.Skill3Damage(skillSettings);
    }
}
