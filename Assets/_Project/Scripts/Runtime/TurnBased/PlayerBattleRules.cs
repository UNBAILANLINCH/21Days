// 职责：玩家侧的回合状态机——怒气、生命、晕眩、「本回合已出招」、招式 3 的额外伤害次数。
//
// 为什么新建（复用 → 扩展 → 新建）：
// - 复用：`Player` 模块没有怒气、招式、道具这一层（`docs/design/features-spotlight/09_BOSS战.md:391`
//   明确写「缺口：没有回合制（无怒气、招式、道具使用）」），能直接拿来的类型不存在。
// - 扩展：`Runtime/Player/` 是并行任务目录，本次明令不动，也不许把回合制塞进 `PlayerSnapshot`。
// - 新建：以上两条都不成立，故新建本类；它只管玩家在**战斗内**的状态，战斗外的玩家状态不归它管。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:48-56`（招式只能在自己回合用、施放后进敌方回合）、
// :82（薄醉的 BOSS 招式 3 晕眩玩家 1 回合）；晕眩期间怎么过回合原文没写（09:231），等 C91。

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

        // ── 道具带来的状态（sp07:199-211，2026-10-16 落地）────────────────────────────────
        private int damageReductionPercent;   // 铁壁护符：受伤降低的百分比
        private int damageReductionRounds;    // 还剩几个玩家回合
        private int attackBonus;              // 锐锋油膏：攻击加的固定伤害
        private int attackBonusCharges;       // 还剩几次攻击
        private int lifestealAmount;          // 浴血药剂：每次命中回复的生命
        private int lifestealRounds;          // 还剩几个玩家回合
        private int shieldCharges;            // 坚韧斗篷：护盾层数，每层挡一次任意来源伤害
        private int regenAmount;              // 固本甘露：每回合回复的生命
        private int regenRounds;              // 还剩几个玩家回合
        private bool extraActionPending;      // 迅击粉末：本回合可以额外释放一次招式

        // ── 「金锁禁锢」（阶段九 BOSS 人形态招式 2，sp07:176）──────────────────────────────
        private int skillLockRoundsRemaining;  // 还剩几个玩家回合只能出招式 1、不能用道具
        private bool skillLockGrantedThisRound; // 刚挂上、还没轮到玩家——本回合不该扣它一格

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

        /// <summary>是否已被打倒（生命归零，07 没有写玩家怎么输，按 09:225 R44 / C91 待定，本模块只如实标记）。</summary>
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

        // ── 道具状态的只读视图（白盒面板与测试用；出处 sp07:199-211）────────────────────────

        /// <summary>当前减伤百分比（铁壁护符，0 = 没有）。</summary>
        public int DamageReductionPercent => damageReductionRounds > 0 ? damageReductionPercent : 0;

        /// <summary>减伤还剩几个玩家回合。</summary>
        public int DamageReductionRounds => damageReductionRounds;

        /// <summary>当前加攻值（锐锋油膏，0 = 没有）。</summary>
        public int AttackBonus => attackBonusCharges > 0 ? attackBonus : 0;

        /// <summary>加攻还剩几次攻击。</summary>
        public int AttackBonusCharges => attackBonusCharges;

        /// <summary>当前每次命中回复的生命（浴血药剂，0 = 没有）。</summary>
        public int LifestealAmount => lifestealRounds > 0 ? lifestealAmount : 0;

        /// <summary>吸血还剩几个玩家回合。</summary>
        public int LifestealRounds => lifestealRounds;

        /// <summary>护盾层数（坚韧斗篷，每层挡一次任意来源伤害）。</summary>
        public int ShieldCharges => shieldCharges;

        /// <summary>当前每回合回复的生命（固本甘露，0 = 没有）。</summary>
        public int RegenAmount => regenRounds > 0 ? regenAmount : 0;

        /// <summary>回春还剩几个玩家回合。</summary>
        public int RegenRounds => regenRounds;

        /// <summary>本回合是否还留着「额外释放一次招式」的机会（迅击粉末）。</summary>
        public bool ExtraActionPending => extraActionPending;

        /// <summary>「金锁禁锢」还剩几个玩家回合（0 = 没被禁锢）。</summary>
        public int SkillLockRoundsRemaining => skillLockRoundsRemaining;

        /// <summary>现在是否处于「金锁禁锢」：只能出招式 1，招式 2 / 3 与全部道具禁用（sp07:176）。</summary>
        public bool IsSkillLocked => skillLockRoundsRemaining > 0;

        /// <summary>这件招式在「金锁禁锢」下能不能用（只有招式 1 能，sp07:176「其他招式置暗」）。</summary>
        public bool CanCastWhileLocked(PlayerSkill skill) => skill == PlayerSkill.Skill1;

        /// <summary>被「金锁禁锢」若干回合（阶段九 BOSS 人形态招式 2，sp07:176 → 2 回合）。</summary>
        public void ApplySkillLock(int rounds)
        {
            if (rounds > 0)
            {
                skillLockRoundsRemaining += rounds;
                skillLockGrantedThisRound = true;
            }
        }

        /// <summary>清空怒气（形态切换时的「怒气清空」，sp07:94；清谁原文没写，见 RageClearTarget / C94）。</summary>
        public void ClearRage() => rage = 0;

        /// <summary>现在能不能出招。</summary>
        public bool CanCast => !IsDefeated && !cannotActThisTurn && stunRoundsRemaining == 0 && !hasCastThisTurn;

        /// <summary>
        /// 进入玩家回合：清掉「本回合已出招」，消耗一格晕眩。
        /// </summary>
        /// <returns>true = 本回合可以行动；false = 本回合被晕眩跳过（07:82）。</returns>
        public bool BeginTurn()
        {
            hasCastThisTurn = false;
            extraActionPending = false;

            if (stunRoundsRemaining > 0)
            {
                stunRoundsRemaining--;
                cannotActThisTurn = true;
                TickItemDurations();
                return false;
            }

            cannotActThisTurn = false;
            TickItemDurations();
            return true;
        }

        /// <summary>
        /// 回合开始时的道具计时：回春先生效，再给按回合计的效果扣一格。
        /// <para>
        /// 「接下来 N 回合」的口径：在**第 N 个玩家回合**仍然生效，之后失效。所以是先结算、后扣数。
        /// </para>
        /// <para>
        /// [推断] 固本甘露原文写「回合结束时回复」，但本内核没有「回合结束」这个钩子——按玩家回合开始时结算，
        /// 回合数与总回复量一致，只是结算时点差一次敌方行动（原文没写时点，记在这里备查）。
        /// </para>
        /// </summary>
        private void TickItemDurations()
        {
            if (regenRounds > 0 && regenAmount > 0)
            {
                Heal(regenAmount);
            }

            damageReductionRounds = StepDown(damageReductionRounds);
            lifestealRounds = StepDown(lifestealRounds);
            regenRounds = StepDown(regenRounds);

            // 金锁禁锢是**敌方回合挂上的**，挂上的那一刻不该马上扣一格——否则「下 2 个回合」实际只锁 1 个回合。
            if (skillLockGrantedThisRound)
            {
                skillLockGrantedThisRound = false;
            }
            else
            {
                skillLockRoundsRemaining = StepDown(skillLockRoundsRemaining);
            }
        }

        private static int StepDown(int rounds) => rounds > 0 ? rounds - 1 : 0;

        /// <summary>被晕眩若干回合（BOSS 招式 3 在薄醉态使用，07:82 → 1 回合）。</summary>
        public void ApplyStun(int rounds)
        {
            if (rounds > 0)
            {
                stunRoundsRemaining += rounds;
            }
        }

        /// <summary>
        /// 挨伤害。走完整条链路：**护盾 → 减伤 → 扣血**（sp07:212「生成 1 层护盾，抵挡 1 次任意来源伤害」、
        /// :204「接下来 2 回合，受到全部伤害降低 40%」）。两层都没有时与逐点扣血等价。
        /// </summary>
        /// <returns>实际扣掉的生命（被护盾挡掉时是 0）。</returns>
        public int TakeDamage(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            if (shieldCharges > 0)
            {
                // 护盾「抵挡 1 次任意来源伤害」：整次挡掉，不看伤害高低。
                shieldCharges--;
                return 0;
            }

            int effective = damageReductionRounds > 0 && damageReductionPercent > 0
                ? amount - amount * damageReductionPercent / 100
                : amount;
            if (effective <= 0)
            {
                return 0;
            }

            int before = health;
            health = GameMath.Clamp(health - effective, 0, MaxHealth);
            return before - health;
        }

        /// <summary>挨伤害（不关心实际扣多少时的入口，等同 <see cref="TakeDamage"/> 并丢弃返回值）。</summary>
        public void ApplyDamage(int amount) => TakeDamage(amount);

        /// <summary>回血（<paramref name="amount"/> 点为定值），封顶到生命上限；已被打倒时不回。</summary>
        /// <returns>实际回复的生命。</returns>
        public int Heal(int amount)
        {
            if (amount <= 0 || IsDefeated)
            {
                return 0;
            }

            int before = health;
            health = GameMath.Clamp(health + amount, 0, MaxHealth);
            return health - before;
        }

        /// <summary>加怒气（瞬怒晶石 / 狂怒结晶 / 招式 1 都用它），封顶到上限。</summary>
        /// <returns>实际加了多少。</returns>
        public int GainRage(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int before = rage;
            rage = GameMath.Clamp(rage + amount, 0, settings.RageMax);
            return rage - before;
        }

        /// <summary>怒气补满到上限（狂怒结晶，sp07:200）。</summary>
        public int FillRage() => GainRage(settings.RageMax - rage);

        /// <summary>给一层护盾（坚韧斗篷，sp07:212）。</summary>
        public void GrantShield(int charges)
        {
            if (charges > 0)
            {
                shieldCharges += charges;
            }
        }

        /// <summary>接下来若干回合受伤降低若干百分比（铁壁护符，sp07:204）。重复使用取覆盖（同减疗的占位口径）。</summary>
        public void GrantDamageReduction(int percent, int rounds)
        {
            if (percent > 0 && rounds > 0)
            {
                damageReductionPercent = percent;
                damageReductionRounds = rounds;
            }
        }

        /// <summary>接下来若干次攻击加固定伤害（锐锋油膏，sp07:205）。重复使用取覆盖。</summary>
        public void GrantAttackBonus(int amount, int charges)
        {
            if (amount > 0 && charges > 0)
            {
                attackBonus = amount;
                attackBonusCharges = charges;
            }
        }

        /// <summary>接下来若干回合每次命中回复生命（浴血药剂，sp07:206）。重复使用取覆盖。</summary>
        public void GrantLifesteal(int amount, int rounds)
        {
            if (amount > 0 && rounds > 0)
            {
                lifestealAmount = amount;
                lifestealRounds = rounds;
            }
        }

        /// <summary>接下来若干回合每回合回复生命（固本甘露，sp07:211）。重复使用取覆盖。</summary>
        public void GrantRegen(int amount, int rounds)
        {
            if (amount > 0 && rounds > 0)
            {
                regenAmount = amount;
                regenRounds = rounds;
            }
        }

        /// <summary>本回合可以额外释放一次招式（迅击粉末，sp07:210）。</summary>
        public void GrantExtraAction() => extraActionPending = true;

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

            if (hasCastThisTurn && !extraActionPending)
            {
                return SkillCastResult.Refuse(skill, SkillCastReject.AlreadyCastThisTurn, rageBefore, chargesBefore);
            }

            // 金锁禁锢：只能出招式 1，其他招式置暗（阶段九 BOSS 人形态招式 2，07:176）。
            if (skillLockRoundsRemaining > 0 && !CanCastWhileLocked(skill))
            {
                return SkillCastResult.Refuse(skill, SkillCastReject.SkillLocked, rageBefore, chargesBefore);
            }

            int cost = PlayerSkillRules.RageCost(skill, settings);
            if (rage < cost)
            {
                return SkillCastResult.Refuse(skill, SkillCastReject.NotEnoughRage, rageBefore, chargesBefore);
            }

            // 伤害：基础值 + 招式 3 的额外伤害（如果还有次数）+ 锐锋油膏的加攻（如果还有次数）。
            int damage = PlayerSkillRules.BaseDamage(skill, settings);
            bool extraApplied = false;
            if (extraDamageCharges > 0 && settings.Skill3ExtraDamage > 0)
            {
                damage += settings.Skill3ExtraDamage;
                extraDamageCharges--;
                extraApplied = true;
            }

            if (attackBonusCharges > 0 && attackBonus > 0)
            {
                damage += attackBonus;
                attackBonusCharges--;
            }

            // 招式 3 自己再补上「接下来 3 次攻击」的次数（07:54）。
            if (skill == PlayerSkill.Skill3)
            {
                int granted = PlayerSkillRules.GrantedExtraDamageAttacks(skill, settings);
                extraDamageCharges = settings.Skill3ExtraDamageStacks ? extraDamageCharges + granted : granted;
            }

            rage = GameMath.Clamp(rage - cost + PlayerSkillRules.RageGain(skill, settings), 0, settings.RageMax);
            hasCastThisTurn = true;
            extraActionPending = false;

            // 浴血药剂：命中后回血（sp07:206）。
            if (lifestealRounds > 0 && lifestealAmount > 0)
            {
                Heal(lifestealAmount);
            }

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

        /// <summary>
        /// 执行一条道具效果（sp07:199-211 的 10 件）。回血 / 怒气类是即时的，其余挂成持续状态。
        /// 没有效果的 <see cref="BattleItemEffectKind.None"/> 什么都不做（只记账、只置暗）。
        /// </summary>
        /// <returns>即时可报的数值（回复的生命 / 加的怒气；持续类返回 0，状态看各自的只读视图）。</returns>
        public int ApplyItemEffect(in BattleItemEffect effect)
        {
            if (!effect.HasEffect || IsDefeated)
            {
                return 0;
            }

            switch (effect.Kind)
            {
                case BattleItemEffectKind.HealFixed:
                    return Heal(effect.Amount);
                case BattleItemEffectKind.RageToMax:
                    return FillRage();
                case BattleItemEffectKind.RageAdd:
                    return GainRage(effect.Amount);
                case BattleItemEffectKind.Shield:
                    GrantShield(effect.Charges);
                    return 0;
                case BattleItemEffectKind.DamageReduction:
                    GrantDamageReduction(effect.Amount, effect.Rounds);
                    return 0;
                case BattleItemEffectKind.AttackBonus:
                    GrantAttackBonus(effect.Amount, effect.Charges);
                    return 0;
                case BattleItemEffectKind.Lifesteal:
                    GrantLifesteal(effect.Amount, effect.Rounds);
                    return 0;
                case BattleItemEffectKind.Regen:
                    GrantRegen(effect.Amount, effect.Rounds);
                    return 0;
                case BattleItemEffectKind.ExtraAction:
                    GrantExtraAction();
                    return 0;
                default:
                    return 0;
            }
        }
    }
}
