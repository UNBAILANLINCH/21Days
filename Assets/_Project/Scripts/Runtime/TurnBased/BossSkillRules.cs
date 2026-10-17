// 职责：BOSS 选招的纯函数——按 6:3:1 的权重随机选，只走注入的 IRandomStream。
//
// 为什么用「一次 Range 取加权区间」而不是三次抽样：每次选招**恰好消耗一个随机数**，
// 这样回放对不上时能一眼看出是第几次选招开始分叉（同 XorShiftRandomStream.Range 的取舍说明）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:84`「技能施放比例为 招式1：招式2：招式3 = 6：3：1」；
// 转写：`docs/design/features-spotlight/09_BOSS战.md:239`（R47）。

using Game.Core.Simulation;

namespace Game.TurnBased
{
    /// <summary>BOSS 招式选择的纯函数规则。</summary>
    public static class BossSkillRules
    {
        /// <summary>
        /// 按权重选一个招式。调用恰好消耗一个随机数；权重全为 0 时抛异常（配置校验会先拦住）。
        /// </summary>
        /// <exception cref="System.InvalidOperationException">三个权重之和不为正。</exception>
        public static BossSkill SelectWeighted(in BossSkillSettings settings, IRandomStream random)
        {
            int total = settings.TotalWeight;
            if (total <= 0)
            {
                throw new System.InvalidOperationException("BOSS 三个招式的权重之和必须为正，当前 " + total);
            }

            int roll = random.Range(0, total);

            if (roll < settings.Skill1Weight)
            {
                return BossSkill.Skill1;
            }

            if (roll < settings.Skill1Weight + settings.Skill2Weight)
            {
                return BossSkill.Skill2;
            }

            return BossSkill.Skill3;
        }

        /// <summary>
        /// 按**形态**选招（阶段九 BOSS，sp07:167-195）。与 <see cref="SelectWeighted"/> 的差别是两条附加规则：
        /// ① 人形态的招式 3 **只在玩家被「金锁禁锢」时**才可能用（sp07:177），且概率另配（sp07:107「三分之一」）；
        /// ② 不满足条件时退回招式 1 / 招式 2 的权重里选（人形态配 5:1）。
        /// 随机数消耗：需要判定招式 3 时**恰好两个**（先掷触发、再掷选招），否则**恰好一个**——回放对不上时按这个数数。
        /// </summary>
        public static BossSkill SelectForPhase(in BossPhaseSettings phase, bool playerSkillLocked, IRandomStream random)
        {
            int total = phase.Skills.TotalWeight;
            if (total <= 0)
            {
                throw new System.InvalidOperationException("BOSS 形态「" + phase.Name + "」的三个招式权重之和必须为正，当前 " + total);
            }

            // 人形态：玩家被禁锢时先掷一次「要不要放招式 3」（原文没写它和 5:1 怎么叠，占位「先判触发」；见 C95）。
            if (phase.Skill3RequiresPlayerLock && playerSkillLocked && phase.Skill3ChancePercentWhenLocked > 0)
            {
                if (random.Range(0, 100) < phase.Skill3ChancePercentWhenLocked)
                {
                    return BossSkill.Skill3;
                }

                // 没触发招式 3 时，在招式 1 / 招式 2 之间按权重选（这次掷数已用掉，接着掷选招那一次）。
                return SelectWeighted(phase.Skills, random);
            }

            return SelectWeighted(phase.Skills, random);
        }

        /// <summary>
        /// 招式 1 的实际伤害：吃到「下次招式 1 +30%」时按百分比加成（07:80）。
        /// 整数运算、向下取整（原文没写取整口径，见交付报告「待拍板」）。
        /// </summary>
        public static int Skill1Damage(in BossSkillSettings settings, bool hasDamageBonus) =>
            hasDamageBonus
                ? settings.Skill1Damage + settings.Skill1Damage * settings.Skill2NextSkill1DamageBonusPercent / 100
                : settings.Skill1Damage;

        /// <summary>招式 3 的伤害（07:81-82，数值占位，等 C91）。</summary>
        public static int Skill3Damage(in BossSkillSettings settings) => settings.Skill3Damage;

        /// <summary>招式 3 在薄醉态会晕眩玩家几回合（07:82 → 1；其他档位 0）。</summary>
        public static int Skill3StunRounds(DrunkTier tierAtCast, in BossSkillSettings settings) =>
            tierAtCast == DrunkTier.Drunk ? settings.Skill3StunRounds : 0;

        /// <summary>招式的中文描述。</summary>
        public static string Describe(BossSkill skill)
        {
            switch (skill)
            {
                case BossSkill.Skill1:
                    return "BOSS 招式1（普通攻击）";
                case BossSkill.Skill2:
                    return "BOSS 招式2（饮酒：+30 醉酒、回 10% 生命、下次招式1 +30%）";
                case BossSkill.Skill3:
                    return "BOSS 招式3（高额伤害；薄醉时额外晕眩玩家 1 回合）";
                default:
                    return "未知招式";
            }
        }
    }
}
