// 职责：一个 BOSS 的「战斗档案」——多形态、按 BOSS 覆盖开战效果、形态切换时清谁的怒气。
//
// 为什么要有这一层：阶段一 BOSS 是单形态、偷袭扣 20% 血（全局一份配置就够）；
// 阶段九 BOSS（`docs/design/spotlight/07_回合制作战文档.md:86-119`）是**人形态 → 龙形态**两条命、
// 每形态招式组不同、开局还有护心鳞，而且它**改写了「战斗通用」的开战效果**——
// `:90`「龙族感知敏锐，玩家通过偷袭攻击进入对战的效果和正面战斗进入对战的效果相同」。
// 原先是全局一份 `BattleEntrySettings`，按 BOSS 覆盖无处可放，故新建本类型。
//
// **默认（不配）= 单形态 + 用全局开战效果**，与加这一层之前的行为逐字一致（阶段一那条路不受影响）。
//
// 原文没写、做成配置项：形态切换时「怒气清空」清谁 → RageClearTarget（C94）。

using System;
using System.Collections.Generic;

namespace Game.TurnBased
{
    /// <summary>一个 BOSS 的多形态与开战效果覆盖。空档案 = 单形态、用全局开战效果。</summary>
    public readonly struct BossProfile
    {
        private readonly IReadOnlyList<BossPhaseSettings> phases;

        /// <param name="phases">按顺序排列的形态；第一个是开战时的形态。空 / null = 不分形态。</param>
        /// <param name="sneakHealthLossPercentOverride">按 BOSS 覆盖「偷袭扣 BOSS 生命百分比」；负 = 用全局那份（龙族写 0）。</param>
        /// <param name="rageClearOnPhaseChange">形态切换时清谁的怒气（原文没写，占位 None；C94）。</param>
        public BossProfile(
            IReadOnlyList<BossPhaseSettings> phases,
            int sneakHealthLossPercentOverride = -1,
            RageClearTarget rageClearOnPhaseChange = RageClearTarget.None)
        {
            this.phases = phases;
            SneakHealthLossPercentOverride = sneakHealthLossPercentOverride;
            RageClearOnPhaseChange = rageClearOnPhaseChange;
        }

        /// <summary>按顺序的形态表（可能为空表，永不为 null）。</summary>
        public IReadOnlyList<BossPhaseSettings> Phases => phases ?? Array.Empty<BossPhaseSettings>();

        /// <summary>这个 BOSS 分不分形态。</summary>
        public bool HasPhases => Phases.Count > 0;

        /// <summary>形态数量。</summary>
        public int PhaseCount => Phases.Count;

        /// <summary>偷袭扣 BOSS 生命的百分比覆盖值；负 = 用全局那份。</summary>
        public int SneakHealthLossPercentOverride { get; }

        /// <summary>
        /// 有没有按 BOSS 覆盖开战效果。
        /// <para>
        /// **必须同时要求 <see cref="HasPhases"/>**：`default(BossProfile)` 的
        /// <see cref="SneakHealthLossPercentOverride"/> 是 0（不是 -1），只看 `>= 0` 会把「没配档案」
        /// 误判成「配了 0%」——阶段一那条路就会凭空不扣那 20%。踩过一次。
        /// </para>
        /// </summary>
        public bool OverridesSneakPenalty => HasPhases && SneakHealthLossPercentOverride >= 0;

        /// <summary>形态切换时清谁的怒气。</summary>
        public RageClearTarget RageClearOnPhaseChange { get; }

        /// <summary>取第 <paramref name="index"/> 个形态；越界返回 false。</summary>
        public bool TryGetPhase(int index, out BossPhaseSettings phase)
        {
            IReadOnlyList<BossPhaseSettings> list = Phases;
            if (index < 0 || index >= list.Count)
            {
                phase = default;
                return false;
            }

            phase = list[index];
            return true;
        }

        /// <summary>每个形态都自洽才算一份写得对的档案。</summary>
        public bool IsWellFormed
        {
            get
            {
                IReadOnlyList<BossPhaseSettings> list = Phases;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].IsWellFormed)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>阶段九 BOSS 的档案（sp07:163-193 的数值；**只给测试与演示用**，正式数据填在资产里）。</summary>
        public static BossProfile StageNineDefault()
        {
            // 人形态（sp07:167-179）：260 血 / 基础攻击 16 / 护心鳞 1 层挡 1 次；
            // 招式 16 / 8（禁锢 2 回合）/ 26（只在玩家被禁锢时用，概率 1/3，命中给 BOSS 加 1 层护心鳞）；
            // 比例「招式1：招式2 = 5：1」。
            var humanSkills = new BossSkillSettings(
                skill1Damage: 16,
                skill2DrinkAddDrunk: 0,
                skill2HealPercent: 0,
                skill2HealBase: HealthPercentBase.MaxHealth,
                skill2NextSkill1DamageBonusPercent: 0,
                skill3Damage: 26,
                skill3StunRounds: 0,
                skill1Weight: 5,
                skill2Weight: 1,
                skill3Weight: 0,
                skill2Damage: 8);

            var human = new BossPhaseSettings(
                name: "人形态",
                maxHealth: 260,
                skills: humanSkills,
                shieldLayers: 1,
                shieldHitsPerLayer: 1,
                skill3RequiresPlayerLock: true,
                skill3ChancePercentWhenLocked: 33,
                skill3GrantsShieldLayers: 1,
                skillLockRounds: 2);

            // 龙形态（sp07:183-195）：260 血 / 基础攻击 21 / 护心鳞 1 层要挨 2 次；
            // 招式 21 / 自我禁锢（2 回合、招式 1 +40%、结束得 1 层护心鳞）/ 龙威 32（耗 1 层换 +50% = 48；没层则重获 1 层并回 10% 最大生命 = 26）；
            // 攻击后 20% 追加一次招式 1；比例 6:3:1。
            var dragonSkills = new BossSkillSettings(
                skill1Damage: 21,
                skill2DrinkAddDrunk: 0,
                skill2HealPercent: 0,
                skill2HealBase: HealthPercentBase.MaxHealth,
                skill2NextSkill1DamageBonusPercent: 0,
                skill3Damage: 32,
                skill3StunRounds: 0,
                skill1Weight: 6,
                skill2Weight: 3,
                skill3Weight: 1);

            var dragon = new BossPhaseSettings(
                name: "龙形态",
                maxHealth: 260,
                skills: dragonSkills,
                shieldLayers: 1,
                shieldHitsPerLayer: 2,
                attackEchoPercent: 20,
                skill3ShieldDamageBonusPercent: 50,
                skill3HealPercentWhenNoShield: 10,
                selfImprisonRounds: 2,
                selfImprisonAttackBonusPercent: 40);

            return new BossProfile(new[] { human, dragon }, sneakHealthLossPercentOverride: 0);
        }
    }
}
