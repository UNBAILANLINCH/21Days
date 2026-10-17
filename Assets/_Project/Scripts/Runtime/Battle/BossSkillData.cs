// 职责：`BossSkillSettings` 的可序列化镜像——BOSS 名册资产里给一个形态配招式用。
//
// 为什么单独一个类型：`BossSkillSettings` 是 readonly struct + 属性，Unity 序列化器读不了（同 BattleItemEffectEntry 的理由）。
// 转换只在这一处：`ToSettings()`。字段与 `BossSkillSettings` 一一对应，出处逐条见那边的注释（sp07:157-159、:167-195）。

using System;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>一个形态的招式数值与权重的 Inspector 形状。</summary>
    [Serializable]
    public sealed class BossSkillData
    {
        [Tooltip("招式 1 伤害（招式 1 就是普通攻击；阶段九人形态 16、龙形态 21）。")]
        [SerializeField] private int skill1Damage;

        [Tooltip("招式 2 附带的伤害（阶段九人形态「金锁禁锢」同时造成少量伤害 = 8；阶段一饮酒为 0）。")]
        [SerializeField] private int skill2Damage;

        [Tooltip("招式 2「饮酒」增加的醉酒值（阶段一 30；阶段九两种形态都是 0）。")]
        [SerializeField] private int skill2DrinkAddDrunk;

        [Tooltip("招式 2「饮酒」回复最大生命的百分比（阶段一 10；阶段九为 0）。")]
        [SerializeField] private int skill2HealPercent;

        [Tooltip("招式 2 之后下次招式 1 的增伤百分比（阶段一 30；阶段九为 0）。")]
        [SerializeField] private int skill2NextSkill1DamageBonusPercent;

        [Tooltip("招式 3 伤害（阶段九人形态 26、龙形态 32）。")]
        [SerializeField] private int skill3Damage;

        [Tooltip("招式 3 在薄醉态晕眩玩家的回合数（阶段一 1；阶段九为 0）。")]
        [SerializeField] private int skill3StunRounds;

        [Tooltip("招式 1 权重。")]
        [SerializeField] private int skill1Weight = 1;

        [Tooltip("招式 2 权重。")]
        [SerializeField] private int skill2Weight;

        [Tooltip("招式 3 权重。")]
        [SerializeField] private int skill3Weight;

        /// <summary>序列化用。</summary>
        public BossSkillData()
        {
        }

        /// <summary>代码构造（默认档案用）。</summary>
        public BossSkillData(
            int skill1Damage, int skill2Damage, int skill2DrinkAddDrunk, int skill2HealPercent,
            int skill2NextSkill1DamageBonusPercent, int skill3Damage, int skill3StunRounds,
            int skill1Weight, int skill2Weight, int skill3Weight)
        {
            this.skill1Damage = skill1Damage;
            this.skill2Damage = skill2Damage;
            this.skill2DrinkAddDrunk = skill2DrinkAddDrunk;
            this.skill2HealPercent = skill2HealPercent;
            this.skill2NextSkill1DamageBonusPercent = skill2NextSkill1DamageBonusPercent;
            this.skill3Damage = skill3Damage;
            this.skill3StunRounds = skill3StunRounds;
            this.skill1Weight = skill1Weight;
            this.skill2Weight = skill2Weight;
            this.skill3Weight = skill3Weight;
        }

        /// <summary>转成纯值设置。</summary>
        public TurnBased.BossSkillSettings ToSettings() =>
            new TurnBased.BossSkillSettings(
                skill1Damage, skill2DrinkAddDrunk, skill2HealPercent, TurnBased.HealthPercentBase.MaxHealth,
                skill2NextSkill1DamageBonusPercent, skill3Damage, skill3StunRounds,
                skill1Weight, skill2Weight, skill3Weight, skill2Damage);
    }
}
