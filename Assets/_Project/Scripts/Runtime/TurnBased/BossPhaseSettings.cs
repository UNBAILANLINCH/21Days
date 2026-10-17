// 职责：一个 BOSS 形态的数值与招式组——阶段九 BOSS 的「人形态 / 龙形态」各一份。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:86-119`（三、BOSS设计 · 1. 阶段9BOSS，2026-10-14 新增）
//   与 `:163-193`（四、数值相关 · 3. 阶段9 BOSS数值，2026-10-16 新增）；转写见
//   `docs/design/features-spotlight/09_BOSS战.md` R49–R58、R66–R67。
//
// 原文没写、做成配置项的三处（不替策划决定，拍板后改这一处）：
//   · 「怒气清空」清谁的怒气 → `BossProfile.RageClearOnPhaseChange`（C94）
//   · 护心鳞「层」与「次」不是一个单位（人形 1 层挡 1 次、龙形 1 层要挨 2 次）→ `ShieldLayers` × `ShieldHitsPerLayer`
//   · 人形态「招式1：招式2 = 5：1」与「禁锢期间放招式3 的概率 1/3」怎么叠 → `Skill3ChancePercentWhenLocked`

namespace Game.TurnBased
{
    /// <summary>BOSS 一个形态的全部数值。默认（单形态）时用全局那份 <see cref="BossSkillSettings"/>，不必配本结构。</summary>
    public readonly struct BossPhaseSettings
    {
        /// <param name="name">形态名（白盒面板与事件里显示，如「人形态」「龙形态」）。</param>
        /// <param name="maxHealth">这个形态的生命上限（切换形态时回满到它）。</param>
        /// <param name="skills">这个形态的招式数值与权重。</param>
        /// <param name="shieldLayers">护心鳞初始层数（sp07:169、:185 都写 1）。</param>
        /// <param name="shieldHitsPerLayer">一层护心鳞要挨几次玩家攻击才碎（人形 1、龙形 2，sp07:169、:185 的备注）。</param>
        /// <param name="attackEchoPercent">攻击后附带一次招式 1 的概率（龙形态 20%，sp07:94；0 = 没有）。</param>
        /// <param name="skill3RequiresPlayerLock">招式 3 是否只能在玩家被「金锁禁锢」时使用（人形态 true，sp07:177）。</param>
        /// <param name="skill3ChancePercentWhenLocked">玩家被禁锢时改用招式 3 的概率（人形态 33 = 三分之一，sp07:107）。</param>
        /// <param name="skill3GrantsShieldLayers">招式 3 命中后给 BOSS 加几层护心鳞（人形态 1，sp07:177）。</param>
        /// <param name="skill3ShieldDamageBonusPercent">招式 3 消耗一层护心鳞时伤害加成的百分比（龙形态 50，sp07:193）。</param>
        /// <param name="skill3HealPercentWhenNoShield">招式 3 在没护心鳞时回复自身最大生命的百分比（龙形态 10，sp07:193）。</param>
        /// <param name="selfImprisonRounds">招式 2 自我禁锢的回合数（龙形态 2，sp07:192；0 = 这个形态没有自我禁锢）。</param>
        /// <param name="selfImprisonAttackBonusPercent">自我禁锢期间招式 1 的伤害加成（龙形态 40，sp07:192）。</param>
        /// <param name="skillLockRounds">招式 2 把玩家「金锁禁锢」几回合（人形态 2，sp07:176；0 = 这个形态没有禁锢）。</param>
        public BossPhaseSettings(
            string name,
            int maxHealth,
            in BossSkillSettings skills,
            int shieldLayers = 0,
            int shieldHitsPerLayer = 1,
            int attackEchoPercent = 0,
            bool skill3RequiresPlayerLock = false,
            int skill3ChancePercentWhenLocked = 0,
            int skill3GrantsShieldLayers = 0,
            int skill3ShieldDamageBonusPercent = 0,
            int skill3HealPercentWhenNoShield = 0,
            int selfImprisonRounds = 0,
            int selfImprisonAttackBonusPercent = 0,
            int skillLockRounds = 0)
        {
            Name = name ?? string.Empty;
            MaxHealth = maxHealth;
            Skills = skills;
            ShieldLayers = shieldLayers;
            ShieldHitsPerLayer = shieldHitsPerLayer;
            AttackEchoPercent = attackEchoPercent;
            Skill3RequiresPlayerLock = skill3RequiresPlayerLock;
            Skill3ChancePercentWhenLocked = skill3ChancePercentWhenLocked;
            Skill3GrantsShieldLayers = skill3GrantsShieldLayers;
            Skill3ShieldDamageBonusPercent = skill3ShieldDamageBonusPercent;
            Skill3HealPercentWhenNoShield = skill3HealPercentWhenNoShield;
            SelfImprisonRounds = selfImprisonRounds;
            SelfImprisonAttackBonusPercent = selfImprisonAttackBonusPercent;
            SkillLockRounds = skillLockRounds;
        }

        /// <summary>形态名。</summary>
        public string Name { get; }

        /// <summary>生命上限。</summary>
        public int MaxHealth { get; }

        /// <summary>招式数值与权重。</summary>
        public BossSkillSettings Skills { get; }

        /// <summary>护心鳞初始层数。</summary>
        public int ShieldLayers { get; }

        /// <summary>一层护心鳞要挨几次攻击。</summary>
        public int ShieldHitsPerLayer { get; }

        /// <summary>攻击后附带一次招式 1 的概率（百分比）。</summary>
        public int AttackEchoPercent { get; }

        /// <summary>招式 3 是否要求玩家处于金锁禁锢。</summary>
        public bool Skill3RequiresPlayerLock { get; }

        /// <summary>玩家被禁锢时改用招式 3 的概率（百分比）。</summary>
        public int Skill3ChancePercentWhenLocked { get; }

        /// <summary>招式 3 命中后给 BOSS 加几层护心鳞。</summary>
        public int Skill3GrantsShieldLayers { get; }

        /// <summary>招式 3 消耗护心鳞时的伤害加成（百分比）。</summary>
        public int Skill3ShieldDamageBonusPercent { get; }

        /// <summary>招式 3 在没有护心鳞时回复最大生命的百分比。</summary>
        public int Skill3HealPercentWhenNoShield { get; }

        /// <summary>自我禁锢的回合数（0 = 没有）。</summary>
        public int SelfImprisonRounds { get; }

        /// <summary>自我禁锢期间招式 1 的伤害加成（百分比）。</summary>
        public int SelfImprisonAttackBonusPercent { get; }

        /// <summary>招式 2 把玩家「金锁禁锢」几回合（0 = 没有）。</summary>
        public int SkillLockRounds { get; }

        /// <summary>
        /// 这个形态的招式 2 是哪一种（sp07:80 阶段一「饮酒」/ :176 人形态「金锁禁锢」/ :192 龙形态「自我禁锢」）。
        /// 判据就写在配置里：配了饮酒量 = 饮酒，配了自我禁锢回合 = 自我禁锢，配了禁锢回合 = 金锁禁锢。
        /// </summary>
        public BossPhaseSkill2Kind Skill2Kind
        {
            get
            {
                if (Skills.Skill2DrinkAddDrunk > 0 || Skills.Skill2HealPercent > 0)
                {
                    return BossPhaseSkill2Kind.Drink;
                }

                if (SelfImprisonRounds > 0)
                {
                    return BossPhaseSkill2Kind.SelfImprison;
                }

                return SkillLockRounds > 0 ? BossPhaseSkill2Kind.SkillLock : BossPhaseSkill2Kind.Drink;
            }
        }

        /// <summary>这个形态有没有配护心鳞。</summary>
        public bool HasShield => ShieldLayers > 0 && ShieldHitsPerLayer > 0;

        /// <summary>数值自洽：生命为正、至少一个招式权重为正、各百分比落在 0..100、层数次数的配套数值为正。</summary>
        public bool IsWellFormed =>
            MaxHealth > 0
            && Skills.TotalWeight > 0
            && ShieldLayers >= 0
            && (ShieldLayers == 0 || ShieldHitsPerLayer > 0)
            && AttackEchoPercent is >= 0 and <= 100
            && Skill3ChancePercentWhenLocked is >= 0 and <= 100
            && Skill3ShieldDamageBonusPercent is >= 0 and <= 200
            && Skill3HealPercentWhenNoShield is >= 0 and <= 100
            && SelfImprisonRounds >= 0
            && SelfImprisonAttackBonusPercent >= 0
            && SkillLockRounds >= 0;
    }
}
