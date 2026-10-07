// 职责：玩家三招式的数值参数（纯值，测试不必创建 ScriptableObject）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:50`（招式 1）、:52（招式 2）、:54（招式 3）；
// 伤害、怒气上限、额外伤害值原文都没写（`docs/design/features-spotlight/09_BOSS战.md:237-239`），
// 一律占位，等 C91。

namespace Game.TurnBased
{
    /// <summary>玩家三招式的数值。</summary>
    public readonly struct PlayerSkillSettings
    {
        /// <summary>构造。</summary>
        public PlayerSkillSettings(
            int rageMax,
            int skill1RageCost,
            int skill1RageGain,
            int skill1Damage,
            int skill2RageCost,
            int skill2Damage,
            int skill2HealReductionPercent,
            int skill2HealReductionRounds,
            int skill3RageCost,
            int skill3Damage,
            int skill3ExtraDamage,
            int skill3ExtraDamageAttacks,
            bool skill3ExtraDamageStacks)
        {
            RageMax = rageMax;
            Skill1RageCost = skill1RageCost;
            Skill1RageGain = skill1RageGain;
            Skill1Damage = skill1Damage;
            Skill2RageCost = skill2RageCost;
            Skill2Damage = skill2Damage;
            Skill2HealReductionPercent = skill2HealReductionPercent;
            Skill2HealReductionRounds = skill2HealReductionRounds;
            Skill3RageCost = skill3RageCost;
            Skill3Damage = skill3Damage;
            Skill3ExtraDamage = skill3ExtraDamage;
            Skill3ExtraDamageAttacks = skill3ExtraDamageAttacks;
            Skill3ExtraDamageStacks = skill3ExtraDamageStacks;
        }

        /// <summary>怒气上限（原文没写，等 C91）。</summary>
        public int RageMax { get; }

        /// <summary>招式 1 怒气消耗（07:50 无消耗 → 0）。</summary>
        public int Skill1RageCost { get; }

        /// <summary>招式 1 使用后的怒气产出（07:50 → +1）。</summary>
        public int Skill1RageGain { get; }

        /// <summary>招式 1 伤害（占位，等 C91）。</summary>
        public int Skill1Damage { get; }

        /// <summary>招式 2 怒气消耗（07:52 → 1）。</summary>
        public int Skill2RageCost { get; }

        /// <summary>招式 2 伤害（占位，等 C91）。</summary>
        public int Skill2Damage { get; }

        /// <summary>招式 2 的减疗百分比（07:52 → 50）。</summary>
        public int Skill2HealReductionPercent { get; }

        /// <summary>招式 2 减疗持续回合数（0 = 到战斗结束；原文没写，等 C91）。</summary>
        public int Skill2HealReductionRounds { get; }

        /// <summary>招式 3 怒气消耗（07:54 → 3）。</summary>
        public int Skill3RageCost { get; }

        /// <summary>招式 3 伤害（占位，等 C91）。</summary>
        public int Skill3Damage { get; }

        /// <summary>招式 3 增益每次攻击附带的额外伤害（占位，等 C91）。</summary>
        public int Skill3ExtraDamage { get; }

        /// <summary>招式 3 增益覆盖的攻击次数（07:54 → 3）。</summary>
        public int Skill3ExtraDamageAttacks { get; }

        /// <summary>两次招式 3 的增益是累加（true）还是重置（false）；原文没写，占位 true。</summary>
        public bool Skill3ExtraDamageStacks { get; }

        /// <summary>
        /// 本工程当前的占位默认值（与 `TurnBasedConfig` 的字段默认完全一致，测试与文档都用它对齐）。
        /// </summary>
        public static PlayerSkillSettings PlaceholderDefault =>
            new PlayerSkillSettings(3, 0, 1, 1, 1, 2, 50, 0, 3, 3, 1, 3, true);
    }
}
