// 职责：BOSS 三招式的数值与权重（纯值，测试不必创建 ScriptableObject）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:79-84`：
//   招式 1 普通攻击；招式 2 饮酒 +30 醉酒 / 回 10% 生命 / 下次招式 1 +30%；
//   招式 3 单次高额伤害，薄醉时加晕眩 1 回合；权重 6:3:1。
// 伤害数值原文 2026-10-16 已给：阶段一 BOSS 基础攻击 14、招式 1 14 点、招式 3 22 点
//（sp07:150、:157、:159；转写见 `docs/design/features-spotlight/09_BOSS战.md` R64–R65）——
// **`PlaceholderDefault` 已按原文值填**（与 `TurnBasedConfig` 的字段默认一致，由 `TurnBasedKernelTests` 钉住）。
// 名字保留 `PlaceholderDefault` 是沿用全仓 `*Settings.PlaceholderDefault` 的约定（= 代码内置的默认口径）。
// 本结构里**仍是占位的只有 `Skill2HealBase`**：原文没写百分比基数（等 C91）。

namespace Game.TurnBased
{
    /// <summary>BOSS 三招式的数值与权重。</summary>
    public readonly struct BossSkillSettings
    {
        /// <summary>构造。</summary>
        public BossSkillSettings(
            int skill1Damage,
            int skill2DrinkAddDrunk,
            int skill2HealPercent,
            HealthPercentBase skill2HealBase,
            int skill2NextSkill1DamageBonusPercent,
            int skill3Damage,
            int skill3StunRounds,
            int skill1Weight,
            int skill2Weight,
            int skill3Weight)
        {
            Skill1Damage = skill1Damage;
            Skill2DrinkAddDrunk = skill2DrinkAddDrunk;
            Skill2HealPercent = skill2HealPercent;
            Skill2HealBase = skill2HealBase;
            Skill2NextSkill1DamageBonusPercent = skill2NextSkill1DamageBonusPercent;
            Skill3Damage = skill3Damage;
            Skill3StunRounds = skill3StunRounds;
            Skill1Weight = skill1Weight;
            Skill2Weight = skill2Weight;
            Skill3Weight = skill3Weight;
        }

        /// <summary>招式 1 伤害（sp07:157 → 14 点，= 基础攻击力）。</summary>
        public int Skill1Damage { get; }

        /// <summary>招式 2 增加的醉酒值（07:80 → 30）。</summary>
        public int Skill2DrinkAddDrunk { get; }

        /// <summary>招式 2 回复生命的百分比（07:80 → 10）。</summary>
        public int Skill2HealPercent { get; }

        /// <summary>招式 2 回血的基数（基数原文仍没写——sp07:158 只给「回复自身 10% 最大生命 =22点」这个结果值，没说基数是上限还是当前生命；等 C91）。</summary>
        public HealthPercentBase Skill2HealBase { get; }

        /// <summary>招式 2 之后下次招式 1 的伤害加成百分比（07:80 → 30）。</summary>
        public int Skill2NextSkill1DamageBonusPercent { get; }

        /// <summary>招式 3 伤害（sp07:159 → 22 点；正常 / 微醺与薄醉同值，薄醉另加眩晕）。</summary>
        public int Skill3Damage { get; }

        /// <summary>招式 3 在薄醉态晕眩玩家的回合数（07:82 → 1）。</summary>
        public int Skill3StunRounds { get; }

        /// <summary>招式 1 权重（07:84 → 6）。</summary>
        public int Skill1Weight { get; }

        /// <summary>招式 2 权重（07:84 → 3）。</summary>
        public int Skill2Weight { get; }

        /// <summary>招式 3 权重（07:84 → 1）。</summary>
        public int Skill3Weight { get; }

        /// <summary>三个权重之和。</summary>
        public int TotalWeight => Skill1Weight + Skill2Weight + Skill3Weight;

        /// <summary>本工程当前的默认值（与 `TurnBasedConfig` 的字段默认一致）。数值出处见各属性注释。</summary>
        public static BossSkillSettings PlaceholderDefault =>
            new BossSkillSettings(14, 30, 10, HealthPercentBase.MaxHealth, 30, 22, 1, 6, 3, 1);
    }
}
