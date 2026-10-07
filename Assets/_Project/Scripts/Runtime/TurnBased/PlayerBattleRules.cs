// 职责：玩家侧的回合状态机——怒气、生命、晕眩、「本回合已出招」、招式 3 的额外伤害次数。
//
// 为什么新建（复用 → 扩展 → 新建）：
// - 复用：`Player` 模块没有怒气、招式、道具这一层（`docs/design/features-spotlight/09_BOSS战.md:272`
//   明确写「缺口：没有回合制（无怒气、招式、道具使用）」），能直接拿来的类型不存在。
// - 扩展：`Runtime/Player/` 是并行任务目录，本次明令不动，也不许把回合制塞进 `PlayerSnapshot`。
// - 新建：以上两条都不成立，故新建本类；它只管玩家在**战斗内**的状态，战斗外的玩家状态不归它管。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:48-56`（招式只能在自己回合用、施放后进敌方回合）、
// :82（薄醉的 BOSS 招式 3 晕眩玩家 1 回合）；晕眩期间怎么过回合原文没写（09:198），等 C91。

using Game.Core.Simulation;

namespace Game.TurnBased
{
    /// <summary>玩家在战斗内的状态机。只被 `BattleSession` 驱动，不订阅任何 Unity 生命周期。</summary>
    public sealed class PlayerBattleRules
    {
        private readonly PlayerSkillSettings settings;
        private int rage;
        private int health;
        private int stunRoundsRemaining;
        private int extraDamageCharges;
        private bool cannotActThisTurn;
        private bool hasCastThisTurn;

        /// <summary>用数值配置与血量快照构造。</summary>
        public PlayerBattleRules(in PlayerSkillSettings settings, in PlayerBattleSnapshot snapshot)
        {
            this.settings = settings;
            MaxHealth = snapshot.MaxHealth;
            health = snapshot.Health;
        }

        /// <summary>生命上限。</summary>
        public int MaxHealth { get; }

        /// <summary>当前生命。</summary>
        public int Health => health;

        /// <summary>是否已被打倒（生命归零，07 没有写玩家怎么输，按 09:192 R44 / C91 待定，本模块只如实标记）。</summary>
        public bool IsDefeated => health <= 0;

        /// <summary>当前怒气。</summary>
        public int Rage => rage;

        /// <summary>怒气上限（配置项，原文没写，等 C91）。</summary>
        public int RageMax => settings.RageMax;

        /// <summary>还剩几个回合要被晕眩（未来回合）。</summary>
        public int StunRoundsRemaining => stunRoundsRemaining;

        /// <summary>本回合是否被晕眩跳过（07:82「晕眩玩家1回合」）。</summary>
        public bool CannotActThisTurn => cannotActThisTurn;

        /// <summary>本回合是否已经出过招（07:56：施放招式后进入敌方回合，所以一回合只能出一招）。</summary>
        public bool HasCastThisTurn => hasCastThisTurn;

        /// <summary>招式 3 留下的「接下来 3 次攻击附带额外伤害」还剩几次（07:54）。</summary>
        public int ExtraDamageCharges => extraDamageCharges;

        /// <summary>现在能不能出招。</summary>
        public bool CanCast => !IsDefeated && !cannotActThisTurn && stunRoundsRemaining == 0 && !hasCastThisTurn;

        /// <summary>
        /// 进入玩家回合：清掉「本回合已出招」，消耗一格晕眩。
        /// </summary>
        /// <returns>true = 本回合可以行动；false = 本回合被晕眩跳过（07:82）。</returns>
        public bool BeginTurn()
        {
            hasCastThisTurn = false;

            if (stunRoundsRemaining > 0)
            {
                stunRoundsRemaining--;
                cannotActThisTurn = true;
                return false;
            }

            cannotActThisTurn = false;
            return true;
        }

        /// <summary>被晕眩若干回合（BOSS 招式 3 在薄醉态使用，07:82 → 1 回合）。</summary>
        public void ApplyStun(int rounds)
        {
            if (rounds > 0)
            {
                stunRoundsRemaining += rounds;
            }
        }

        /// <summary>挨伤害。</summary>
        public void ApplyDamage(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            health = GameMath.Clamp(health - amount, 0, MaxHealth);
        }

        /// <summary>
        /// 施放一个招式并结算。判定顺序：被打倒 → 晕眩 → 本回合已出招 → 怒气不足。
        /// <para>
        /// 怒气按「先扣消耗、再加产出」结算；招式 1 的 +1 与它的 0 消耗互不影响（07:50）。
        /// </para>
        /// </summary>
        public SkillCastResult TryCast(PlayerSkill skill)
        {
            int rageBefore = rage;
            int chargesBefore = extraDamageCharges;

            if (!PlayerSkillRules.IsKnown(skill))
            {
                return SkillCastResult.Refuse(skill, SkillCastReject.UnknownSkill, rageBefore, chargesBefore);
            }

            if (IsDefeated)
            {
                return SkillCastResult.Refuse(skill, SkillCastReject.Defeated, rageBefore, chargesBefore);
            }

            if (cannotActThisTurn || stunRoundsRemaining > 0)
            {
                return SkillCastResult.Refuse(skill, SkillCastReject.Stunned, rageBefore, chargesBefore);
            }

            if (hasCastThisTurn)
            {
                return SkillCastResult.Refuse(skill, SkillCastReject.AlreadyCastThisTurn, rageBefore, chargesBefore);
            }

            int cost = PlayerSkillRules.RageCost(skill, settings);
            if (rage < cost)
            {
                return SkillCastResult.Refuse(skill, SkillCastReject.NotEnoughRage, rageBefore, chargesBefore);
            }

            // 伤害：基础值 + 吃到一次招式 3 的额外伤害（如果还有次数）。
            int damage = PlayerSkillRules.BaseDamage(skill, settings);
            bool extraApplied = false;
            if (extraDamageCharges > 0 && settings.Skill3ExtraDamage > 0)
            {
                damage += settings.Skill3ExtraDamage;
                extraDamageCharges--;
                extraApplied = true;
            }

            // 招式 3 自己再补上「接下来 3 次攻击」的次数（07:54）。
            if (skill == PlayerSkill.Skill3)
            {
                int granted = PlayerSkillRules.GrantedExtraDamageAttacks(skill, settings);
                extraDamageCharges = settings.Skill3ExtraDamageStacks ? extraDamageCharges + granted : granted;
            }

            rage = GameMath.Clamp(rage - cost + PlayerSkillRules.RageGain(skill, settings), 0, settings.RageMax);
            hasCastThisTurn = true;

            return SkillCastResult.Success(
                skill,
                damage,
                extraApplied,
                rageBefore,
                rage,
                PlayerSkillRules.HealReductionPercent(skill, settings),
                skill == PlayerSkill.Skill2 ? settings.Skill2HealReductionRounds : 0,
                extraDamageCharges);
        }
    }
}
