// 职责：一次施放招式的结算结果——伤害多少、怒气怎么变、给对方挂上什么、自己的增益还剩几次。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:50-54`（三招式的效果）。
// 值本身待拍板（09:239 / C91），本结构只负责把「这次施放发生了什么」如实带出来，不自己算数。

namespace Game.TurnBased
{
    /// <summary>一次施放招式的结果。被拒时 <see cref="Reject"/> 非 None，其余字段保持施放前的值。</summary>
    public readonly struct SkillCastResult
    {
        private SkillCastResult(
            bool accepted,
            SkillCastReject reject,
            PlayerSkill skill,
            int damage,
            bool extraDamageApplied,
            int rageBefore,
            int rageAfter,
            int healReductionPercent,
            int healReductionRounds,
            int extraDamageChargesAfter)
        {
            Accepted = accepted;
            Reject = reject;
            Skill = skill;
            Damage = damage;
            ExtraDamageApplied = extraDamageApplied;
            RageBefore = rageBefore;
            RageAfter = rageAfter;
            HealReductionPercent = healReductionPercent;
            HealReductionRounds = healReductionRounds;
            ExtraDamageChargesAfter = extraDamageChargesAfter;
        }

        /// <summary>是否施放成功。</summary>
        public bool Accepted { get; }

        /// <summary>被拒原因；成功时为 <see cref="SkillCastReject.None"/>。</summary>
        public SkillCastReject Reject { get; }

        /// <summary>这次施放的招式。</summary>
        public PlayerSkill Skill { get; }

        /// <summary>这次打出的伤害（已含招式 3 增益带来的额外伤害）。</summary>
        public int Damage { get; }

        /// <summary>这次伤害里是否吃到了招式 3 的额外伤害（同时消耗一次次数）。</summary>
        public bool ExtraDamageApplied { get; }

        /// <summary>施放前的怒气。</summary>
        public int RageBefore { get; }

        /// <summary>施放后的怒气。</summary>
        public int RageAfter { get; }

        /// <summary>这次施放给对方挂上的减疗百分比（只有招式 2 非 0）。</summary>
        public int HealReductionPercent { get; }

        /// <summary>减疗持续几个怪物回合（0 = 到战斗结束）。</summary>
        public int HealReductionRounds { get; }

        /// <summary>施放后剩余的「额外伤害次数」。</summary>
        public int ExtraDamageChargesAfter { get; }

        /// <summary>构造成功结果。</summary>
        public static SkillCastResult Success(
            PlayerSkill skill,
            int damage,
            bool extraDamageApplied,
            int rageBefore,
            int rageAfter,
            int healReductionPercent,
            int healReductionRounds,
            int extraDamageChargesAfter) =>
            new SkillCastResult(
                true,
                SkillCastReject.None,
                skill,
                damage,
                extraDamageApplied,
                rageBefore,
                rageAfter,
                healReductionPercent,
                healReductionRounds,
                extraDamageChargesAfter);

        /// <summary>构造被拒结果（怒气等字段保持施放前的值）。</summary>
        public static SkillCastResult Refuse(
            PlayerSkill skill,
            SkillCastReject reject,
            int rageBefore,
            int extraDamageChargesAfter) =>
            new SkillCastResult(
                false,
                reject,
                skill,
                0,
                false,
                rageBefore,
                rageBefore,
                0,
                0,
                extraDamageChargesAfter);
    }
}
