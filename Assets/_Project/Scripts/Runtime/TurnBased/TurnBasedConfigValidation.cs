// 职责：TurnBasedConfig 的校验逻辑，写成纯函数，脱离 Unity 引擎也能测（不需要创建资产）。
// 为什么单独一个文件：`StealthConfig` 也是这样拆的（`StealthConfigValidation`），
// 好处是配置校验的测试不必碰 ScriptableObject；另外配置文件本身只该管「字段 + 取值」。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:70-84`（四档区间与 6:3:1 权重是硬约束）、
// `docs/design/features-spotlight/待策划拍板问题.md:1256` C91（数值待拍板，但内部一致性必须成立）。

namespace Game.TurnBased
{
    /// <summary>回合制作战配置的纯函数校验：返回 null 表示没问题，否则是第一条问题的中文描述。</summary>
    public static class TurnBasedConfigValidation
    {
        /// <summary>逐项校验。顺序固定，返回第一条不通过的描述。</summary>
        public static string Validate(in BattleSettings settings)
        {
            string issue = ValidateEntry(settings.Entry);
            if (issue != null)
            {
                return issue;
            }

            issue = ValidateDrunk(settings.Drunk);
            if (issue != null)
            {
                return issue;
            }

            issue = ValidatePlayerSkills(settings.PlayerSkills);
            if (issue != null)
            {
                return issue;
            }

            issue = ValidateBossSkills(settings.BossSkills);
            if (issue != null)
            {
                return issue;
            }

            return ValidateFlow(settings.Flow);
        }

        private static string ValidateEntry(BattleEntrySettings entry)
        {
            if (entry.SneakBossHealthLossPercent < 0 || entry.SneakBossHealthLossPercent > 100)
            {
                return "偷袭扣血百分比必须落在 0..100，当前 " + entry.SneakBossHealthLossPercent;
            }

            return null;
        }

        private static string ValidateDrunk(DrunkSettings drunk)
        {
            if (drunk.TipsyThreshold <= 0)
            {
                return "微醺档下界必须大于 0，当前 " + drunk.TipsyThreshold;
            }

            if (drunk.DrunkThreshold <= drunk.TipsyThreshold)
            {
                return "薄醉档下界必须大于微醺档下界（07:71-72 是 50-79 / 80-99）";
            }

            if (drunk.DeadDrunkThreshold <= drunk.DrunkThreshold)
            {
                return "酩酊档下界必须大于薄醉档下界（07:73 是 100）";
            }

            if (drunk.MaxDrunkValue < drunk.DeadDrunkThreshold)
            {
                return "醉酒值上限不能低于酩酊档下界，否则酩酊态永远进不去";
            }

            if (!IsPercent(drunk.TipsySkipPercent) || !IsPercent(drunk.DrunkSkipPercent) || !IsPercent(drunk.DeadDrunkSkipPercent))
            {
                return "跳过回合的概率必须落在 0..100（07:71-73 是 20 / 40 / 100）";
            }

            if (drunk.DeadDrunkDropValue < 0)
            {
                return "酩酊时醉酒值下降量不可为负，当前 " + drunk.DeadDrunkDropValue;
            }

            if (drunk.DeadDrunkDurationRounds <= 0)
            {
                return "酩酊持续回合数必须大于 0（07:73「持续2回合」），当前 " + drunk.DeadDrunkDurationRounds;
            }

            return null;
        }

        private static string ValidatePlayerSkills(PlayerSkillSettings skills)
        {
            if (skills.RageMax <= 0)
            {
                return "怒气上限必须大于 0，当前 " + skills.RageMax;
            }

            if (skills.RageMax < skills.Skill3RageCost)
            {
                return "怒气上限（" + skills.RageMax + "）小于招式 3 的消耗（" + skills.Skill3RageCost +
                       "），招式 3 永远放不出来（07:54 写招式 3 消耗 3 点）";
            }

            if (skills.Skill1RageCost != 0)
            {
                return "招式 1 是无消耗招式（07:50），怒气消耗必须为 0，当前 " + skills.Skill1RageCost;
            }

            if (skills.Skill1RageGain < 0 || skills.Skill2RageCost < 0 || skills.Skill3RageCost < 0)
            {
                return "招式怒气消耗 / 产出不可为负";
            }

            if (skills.Skill1Damage < 0 || skills.Skill2Damage < 0 || skills.Skill3Damage < 0)
            {
                return "招式伤害不可为负";
            }

            if (skills.Skill3ExtraDamage < 0 || skills.Skill3ExtraDamageAttacks < 0)
            {
                return "招式 3 的额外伤害与次数不可为负";
            }

            if (!IsPercent(skills.Skill2HealReductionPercent))
            {
                return "招式 2 的减疗百分比必须落在 0..100（07:52「减少对方50%治疗效果」）";
            }

            if (skills.Skill2HealReductionRounds < 0)
            {
                return "招式 2 的减疗持续回合数不可为负（0 表示到战斗结束）";
            }

            return null;
        }

        private static string ValidateBossSkills(BossSkillSettings skills)
        {
            if (skills.Skill1Weight < 0 || skills.Skill2Weight < 0 || skills.Skill3Weight < 0)
            {
                return "BOSS 招式权重不可为负（07:84 是 6:3:1）";
            }

            if (skills.TotalWeight <= 0)
            {
                return "BOSS 三个招式的权重不能全为 0，否则选不出招式";
            }

            if (skills.Skill1Damage < 0 || skills.Skill3Damage < 0)
            {
                return "BOSS 招式伤害不可为负";
            }

            if (skills.Skill2DrinkAddDrunk < 0)
            {
                return "BOSS 饮酒增加的醉酒值不可为负（07:80「增加30醉酒值」）";
            }

            if (!IsPercent(skills.Skill2HealPercent))
            {
                return "BOSS 饮酒的回复百分比必须落在 0..100（07:80「回复10%生命值」）";
            }

            if (!IsPercent(skills.Skill2NextSkill1DamageBonusPercent))
            {
                return "BOSS 招式 2 之后普攻的加成百分比必须落在 0..100（07:80「+30%」）";
            }

            if (skills.Skill3StunRounds < 0)
            {
                return "BOSS 招式 3 的晕眩回合数不可为负（07:82「晕眩玩家1回合」）";
            }

            return null;
        }

        private static string ValidateFlow(BattleFlowSettings flow)
        {
            if (flow.RoundLimit < 0)
            {
                return "回合上限不可为负（0 表示不限）";
            }

            return null;
        }

        private static bool IsPercent(int value) => value >= 0 && value <= 100;
    }
}
