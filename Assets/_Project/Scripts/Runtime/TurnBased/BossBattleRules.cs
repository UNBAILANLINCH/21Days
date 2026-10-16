// 职责：BOSS 侧的战斗状态——生命、醉酒（转发给 BossDrunkRules）、减疗、招式 1 的增伤挂起。
//
// 为什么新建：`Monster` 模块只管一只怪的巡逻 / 感知 / 血量（`09_BOSS战.md:390` 明确写缺口
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
        private BossSkillSettings skillSettings;
        private readonly BossProfile profile;
        private readonly bool hasPhases;
        private int phaseIndex;
        private int phaseMaxHealth;
        private int shieldLayers;
        private int shieldHitsPerLayer;
        private int shieldHitsRemaining;
        private int selfImprisonRoundsRemaining;
        private int health;
        private bool nextSkill1DamageBonusPending;
        private int healReductionPercent;
        private int healReductionRoundsRemaining;
        private bool healReductionPermanent;

        /// <summary>
        /// 用血量 / 醉酒快照与数值配置构造。
        /// </summary>
        /// <param name="snapshot">生命与战斗外醉酒值。</param>
        /// <param name="skillSettings">BOSS 招式数值（不分形态时用它）。</param>
        /// <param name="drunkSettings">醉酒四档数值。</param>
        /// <param name="inheritsDrunkValue">是否继承战斗外的醉酒值（07:66；配置开关，占位 true）。</param>
        /// <param name="profile">多形态档案（阶段九 BOSS）；不传 / 空 = 单形态，行为与加这一层之前逐字一致。</param>
        public BossBattleRules(
            in BossBattleSnapshot snapshot,
            in BossSkillSettings skillSettings,
            in DrunkSettings drunkSettings,
            bool inheritsDrunkValue,
            in BossProfile profile = default)
        {
            this.skillSettings = skillSettings;
            this.profile = profile;
            hasPhases = profile.HasPhases;

            phaseMaxHealth = snapshot.MaxHealth;
            health = snapshot.Health;

            // 分了形态就以第一个形态的血量与招式组开局（人形态，sp07:94）。
            if (hasPhases && profile.TryGetPhase(0, out BossPhaseSettings first))
            {
                this.skillSettings = first.Skills;
                phaseMaxHealth = first.MaxHealth > 0 ? first.MaxHealth : snapshot.MaxHealth;
                health = phaseMaxHealth;
                GrantShield(first.ShieldLayers, first.ShieldHitsPerLayer);
            }

            Drunk = new BossDrunkRules(inheritsDrunkValue ? snapshot.DrunkValue : 0, drunkSettings);
        }

        /// <summary>当前形态的序号（0 起；不分形态时恒为 0）。</summary>
        public int PhaseIndex => phaseIndex;

        /// <summary>分不分形态。</summary>
        public bool HasPhases => hasPhases;

        /// <summary>当前形态名（不分形态时是空串）。</summary>
        public string PhaseName =>
            hasPhases && profile.TryGetPhase(phaseIndex, out BossPhaseSettings phase) ? phase.Name : string.Empty;

        /// <summary>后面还有没有下一个形态（生命归零时用它判断「转形态」还是「打倒」）。</summary>
        public bool HasNextPhase => hasPhases && phaseIndex + 1 < profile.PhaseCount;

        /// <summary>护心鳞还剩几层（sp07:169、:185）。</summary>
        public int ShieldLayers => shieldLayers;

        /// <summary>当前这一层护心鳞还剩几次攻击才碎（人形 1、龙形 2）。</summary>
        public int ShieldHitsRemaining => shieldHitsRemaining;

        /// <summary>自我禁锢还剩几个怪物回合（龙形态招式 2，sp07:192；0 = 没有）。</summary>
        public int SelfImprisonRoundsRemaining => selfImprisonRoundsRemaining;

        /// <summary>生命上限（随形态变化）。</summary>
        public int MaxHealth => phaseMaxHealth;

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

        /// <summary>招式 1 这次打出的伤害（含挂起的 +30% 与自我禁锢期间的 +40%，sp07:80、:192）。</summary>
        public int Skill1DamageNow()
        {
            int damage = BossSkillRules.Skill1Damage(skillSettings, nextSkill1DamageBonusPending);
            int percent = CurrentPhase().SelfImprisonAttackBonusPercent;
            if (selfImprisonRoundsRemaining > 0 && percent > 0)
            {
                damage += damage * percent / 100;
            }

            return damage;
        }

        /// <summary>招式 3 这次打出的伤害（含龙形态「消耗一层护心鳞换 +50%」那一档，sp07:193）。</summary>
        public int Skill3DamageNow()
        {
            int damage = BossSkillRules.Skill3Damage(skillSettings);
            int percent = CurrentPhase().Skill3ShieldDamageBonusPercent;
            if (percent > 0 && shieldLayers > 0)
            {
                damage += damage * percent / 100;
            }

            return damage;
        }

        /// <summary>当前形态的设置（不分形态时是 default，各附加项都是 0）。</summary>
        public BossPhaseSettings CurrentPhase() =>
            hasPhases && profile.TryGetPhase(phaseIndex, out BossPhaseSettings phase) ? phase : default;

        /// <summary>给 BOSS 若干层护心鳞（人形态招式 3「增加一次」、龙形态自我禁锢结束、大招无层时重获）。</summary>
        public void GrantShield(int layers, int hitsPerLayer)
        {
            if (layers <= 0)
            {
                return;
            }

            shieldLayers += layers;
            shieldHitsPerLayer = hitsPerLayer > 0 ? hitsPerLayer : 1;
            shieldHitsRemaining = shieldHitsPerLayer;
        }

        /// <summary>
        /// 玩家的一次招式打过来：有护心鳞就先吃它（sp07:92「可以抵挡一次玩家的任何招式」；
        /// 龙形态「需要两次攻击才能被击碎」= 一层要挨两次）。
        /// </summary>
        /// <returns>true = 这一招被挡下（不扣血）。</returns>
        public bool TryAbsorbPlayerAttack()
        {
            if (shieldLayers <= 0)
            {
                return false;
            }

            shieldHitsRemaining--;
            if (shieldHitsRemaining <= 0)
            {
                shieldLayers--;
                shieldHitsRemaining = shieldLayers > 0 ? shieldHitsPerLayer : 0;
            }

            return true;
        }

        /// <summary>进入自我禁锢若干回合（龙形态招式 2，sp07:192）。</summary>
        public void BeginSelfImprison(int rounds)
        {
            if (rounds > 0)
            {
                selfImprisonRoundsRemaining = rounds;
            }
        }

        /// <summary>整层消耗掉一层护心鳞（龙形态招式 3「消耗掉1次护心鳞」，sp07:193）。</summary>
        /// <returns>true = 确实消耗了一层。</returns>
        public bool ConsumeShieldLayer()
        {
            if (shieldLayers <= 0)
            {
                return false;
            }

            shieldLayers--;
            shieldHitsRemaining = shieldLayers > 0 ? shieldHitsPerLayer : 0;
            return true;
        }

        /// <summary>
        /// 怪物回合结束时推进自我禁锢：结束的那一刻按形态配置获得护心鳞（sp07:192「在自我禁锢效果结束后，BOSS获得护心麟」）。
        /// </summary>
        /// <returns>这一次是不是刚好结束（true 时调用方可以报一个事件）。</returns>
        public bool TickSelfImprison()
        {
            if (selfImprisonRoundsRemaining <= 0)
            {
                return false;
            }

            selfImprisonRoundsRemaining--;
            if (selfImprisonRoundsRemaining > 0)
            {
                return false;
            }

            BossPhaseSettings phase = CurrentPhase();
            GrantShield(phase.ShieldLayers > 0 ? phase.ShieldLayers : 1,
                phase.ShieldHitsPerLayer > 0 ? phase.ShieldHitsPerLayer : 1);
            return true;
        }

        /// <summary>
        /// 生命归零时转下一个形态（sp07:94「在人形态生命值耗尽后为龙形态……生命值恢复为100%」）。
        /// 血回满到下一个形态的上限、护心鳞按新形态刷一遍、自我禁锢清掉。
        /// </summary>
        /// <returns>true = 已经转到下一个形态；false = 没有下一个形态了（该判胜）。</returns>
        public bool TryAdvancePhase()
        {
            if (!HasNextPhase || !profile.TryGetPhase(phaseIndex + 1, out BossPhaseSettings next))
            {
                return false;
            }

            phaseIndex++;
            skillSettings = next.Skills;
            phaseMaxHealth = next.MaxHealth > 0 ? next.MaxHealth : phaseMaxHealth;
            health = phaseMaxHealth;
            shieldLayers = 0;
            shieldHitsRemaining = 0;
            selfImprisonRoundsRemaining = 0;
            GrantShield(next.ShieldLayers, next.ShieldHitsPerLayer);
            return true;
        }

        /// <summary>形态切换时按配置清怒气（原文没写清谁，占位 None；C94）。调用方按返回的目标去清。</summary>
        public RageClearTarget RageClearOnPhaseChange => profile.RageClearOnPhaseChange;

        // ── 当前形态（或全局）招式 2 / 3 的参数出口：会话层不必知道分不分形态 ──────────────────

        /// <summary>招式 2 附带的伤害（阶段九人形态「金锁禁锢」= 8 点，sp07:176；阶段一饮酒 0）。</summary>
        public int Skill2DamageNow() => skillSettings.Skill2Damage;

        /// <summary>招式 2「饮酒」增加的醉酒值（阶段一 07:80；分形态的 BOSS 一般为 0）。</summary>
        public int Skill2DrinkAddDrunk => skillSettings.Skill2DrinkAddDrunk;

        /// <summary>招式 2「饮酒」回复生命的百分比。</summary>
        public int Skill2HealPercent => skillSettings.Skill2HealPercent;

        /// <summary>招式 2「饮酒」回血的基数。</summary>
        public HealthPercentBase Skill2HealBase => skillSettings.Skill2HealBase;

        /// <summary>招式 2 之后下次招式 1 的增伤百分比（07:80）。</summary>
        public int Skill2NextSkill1DamageBonusPercent => skillSettings.Skill2NextSkill1DamageBonusPercent;

        /// <summary>招式 3 在薄醉态晕眩玩家几回合（07:82；分形态的 BOSS 一般为 0）。</summary>
        public int Skill3StunRounds(DrunkTier tierAtCast) =>
            BossSkillRules.Skill3StunRounds(tierAtCast, skillSettings);

        /// <summary>招式 3 有没有「消耗一层护心鳞换伤害」这一档（龙形态 07:193）。</summary>
        public bool Skill3ConsumesShieldLayer => CurrentPhase().Skill3ShieldDamageBonusPercent > 0;

        /// <summary>招式 3 在没有护心鳞时重新获得几层 + 回复最大生命的百分比（龙形态 07:193）。</summary>
        public int Skill3HealPercentWhenNoShield => CurrentPhase().Skill3HealPercentWhenNoShield;

        /// <summary>招式 3 命中后给 BOSS 加几层护心鳞（人形态 07:177）。</summary>
        public int Skill3GrantsShieldLayers => CurrentPhase().Skill3GrantsShieldLayers;

        /// <summary>当前形态的护心鳞「一层挨几次」。</summary>
        public int ShieldHitsPerLayer => shieldHitsPerLayer;

        /// <summary>攻击后附带一次招式 1 的概率（龙形态 20%，07:94；0 = 没有）。</summary>
        public int AttackEchoPercent => CurrentPhase().AttackEchoPercent;
    }
}
