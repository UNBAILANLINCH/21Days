// 职责：`BossPhaseSettings` 的可序列化镜像——BOSS 名册资产里配「一个形态」用。
//
// 为什么单独一个类型：同 BossSkillData（纯值结构 Unity 序列化器读不了）。转换在 `ToPhase()`。
// 各字段的原文出处见 `TurnBased.BossPhaseSettings` 的注释（sp07:86-119 与 :163-193）。

using System;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>一个 BOSS 形态的 Inspector 形状。</summary>
    [Serializable]
    public sealed class BossPhaseData
    {
        [Tooltip("形态名（白盒面板与事件里显示，如「人形态」「龙形态」）。")]
        [SerializeField] private string name = string.Empty;

        [Tooltip("这个形态的生命上限（阶段九两种形态都是 260）。")]
        [SerializeField] private int maxHealth = 1;

        [Tooltip("这个形态的招式数值与权重。")]
        [SerializeField] private BossSkillData skills = new BossSkillData();

        [Tooltip("护心鳞初始层数（阶段九两种形态都写 1）。")]
        [SerializeField] private int shieldLayers;

        [Tooltip("一层护心鳞要挨几次玩家攻击才碎（人形态 1、龙形态 2）。")]
        [SerializeField] private int shieldHitsPerLayer = 1;

        [Tooltip("攻击后附带一次招式 1 的概率（龙形态 20；0 = 没有）。")]
        [SerializeField] private int attackEchoPercent;

        [Tooltip("招式 3 是否只在玩家被「金锁禁锢」时才能用（人形态勾上）。")]
        [SerializeField] private bool skill3RequiresPlayerLock;

        [Tooltip("玩家被禁锢时改用招式 3 的概率（人形态 33 = 三分之一）。")]
        [SerializeField] private int skill3ChancePercentWhenLocked;

        [Tooltip("招式 3 命中后给 BOSS 加几层护心鳞（人形态 1）。")]
        [SerializeField] private int skill3GrantsShieldLayers;

        [Tooltip("招式 3 消耗一层护心鳞时的伤害加成百分比（龙形态 50）。")]
        [SerializeField] private int skill3ShieldDamageBonusPercent;

        [Tooltip("招式 3 在没有护心鳞时回复最大生命的百分比（龙形态 10）。")]
        [SerializeField] private int skill3HealPercentWhenNoShield;

        [Tooltip("招式 2 自我禁锢的回合数（龙形态 2；0 = 这个形态没有自我禁锢）。")]
        [SerializeField] private int selfImprisonRounds;

        [Tooltip("自我禁锢期间招式 1 的伤害加成百分比（龙形态 40）。")]
        [SerializeField] private int selfImprisonAttackBonusPercent;

        [Tooltip("招式 2 把玩家「金锁禁锢」几回合（人形态 2；0 = 这个形态没有禁锢）。")]
        [SerializeField] private int skillLockRounds;

        /// <summary>序列化用。</summary>
        public BossPhaseData()
        {
        }

        /// <summary>转成纯值设置。</summary>
        public TurnBased.BossPhaseSettings ToPhase() =>
            new TurnBased.BossPhaseSettings(
                name, maxHealth, skills != null ? skills.ToSettings() : default,
                shieldLayers, shieldHitsPerLayer, attackEchoPercent,
                skill3RequiresPlayerLock, skill3ChancePercentWhenLocked, skill3GrantsShieldLayers,
                skill3ShieldDamageBonusPercent, skill3HealPercentWhenNoShield,
                selfImprisonRounds, selfImprisonAttackBonusPercent, skillLockRounds);
    }
}
