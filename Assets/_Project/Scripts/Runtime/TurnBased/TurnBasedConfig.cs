// 职责：回合制作战内核（`Game.TurnBased`）的全部可调数值，集中一处便于策划改数。
// 每个字段的注释都写明「出处 / 待拍板编号」。**2026-10-16 起分两类**：
//   ① 策划原文已给具体值的，注释直接写出处行号（sp07:行号）——这些是**原文值**，改要照原文改；
//   ② 原文仍没写的（基数、醉酒值上限、回合上限、晕眩口径、道具的 id 与携带规则），仍是**占位值**并注明等哪条拍板。
//
// 为什么新建（不并进 MonsterConfig / PlayerConfig）：
// - 复用：`MonsterConfig` 管某只怪的巡逻 / 感知 / 血量，`PlayerConfig` 管玩家的移动 / 攻击 / 生命，
//   谁都不管「怒气、三招式、道具一场一次、BOSS 醉酒四档、BOSS 三招权重」这一层；
//   而且这两份配置正被并行波次修改，本次任务明令不动。
// - 扩展：没有一份既有配置的职责能容下回合制这一整套数值。
// - 新建：以上两条都不成立，故新建一份模块级配置 + 一份默认资产（`Data/TurnBased/TurnBasedConfig.asset`）。
//
// 出处（真源，逐行对着写）：
// - `docs/design/spotlight/07_回合制作战文档.md`（215 行；第 1–120 行是老内容，:121-211 是 2026-10-16 新增的「四、数值相关」「五、道具相关」）
// - `docs/design/features-spotlight/09_BOSS战.md` §3.9（R40–R48）、R62–R68（数值原文）、§7「数值那一层」对照表
// - `docs/design/features-spotlight/待策划拍板问题.md` C91（回合制的数值与胜负条件；2026-10-16 起大半已答）
// - `docs/design/features-spotlight/00_功能总览.md` §8.1 #7（BOSS 战形式与「本体打不过任何怪」并存）
using UnityEngine;

namespace Game.TurnBased
{
    /// <summary>
    /// 回合制作战内核的数值配置。原文已给的照抄原文（注释标 sp07 行号）；原文没写的仍是占位并标待拍板编号（C91），
    /// 改这一处即可，规则代码一行不用动。
    /// <para>
    /// 本资产**只提供数值**，不提供「做不做」的开关（唯一例外见
    /// <see cref="inheritsDrunkValue"/>：它对应「进战斗要不要继承战斗外醉酒值」这条接线选择）。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "21Days/TurnBased/TurnBased Config")]
    public sealed class TurnBasedConfig : ScriptableObject
    {
        [Header("进入战斗")]
        [Tooltip("偷袭开战时扣掉 BOSS 的生命百分比。出处 07_回合制作战文档.md:18「BOSS生命值减少20%」。")]
        [SerializeField] private int sneakBossHealthLossPercent = 20;

        [Tooltip("上面这 20% 的基数（上限还是当前生命）。原文 2026-10-16 已给偷袭扣 20% 与各招式伤害值"
                 + "（sp07:129-193），但**百分比作用在什么基数上仍没写**（出处 09_BOSS战.md:354）；"
                 + "本字段仍是占位 MaxHealth，等 C91。")]
        [SerializeField] private HealthPercentBase sneakLossBase = HealthPercentBase.MaxHealth;

        [Header("玩家招式（出处 07_回合制作战文档.md:48-54）")]
        [Tooltip("怒气上限。出处 sp07:132「最大怒气 3 点（招式消耗上限，怒气不能超过 3）」。")]
        [SerializeField] private int rageMax = 3;

        [Tooltip("招式 1 的怒气消耗。出处 07:50「无消耗」。")]
        [SerializeField] private int playerSkill1RageCost;

        [Tooltip("招式 1 使用后获得的怒气。出处 07:50「使用后增加1点怒气」。")]
        [SerializeField] private int playerSkill1RageGain = 1;

        [Tooltip("招式 1 伤害。出处 sp07:139「招式 1：0 怒气，100% 基础攻击（18 点）」；基础攻击力 18 见 sp07:131。")]
        [SerializeField] private int playerSkill1Damage = 18;

        [Tooltip("招式 2 的怒气消耗。出处 07:52「消耗1点怒气」。")]
        [SerializeField] private int playerSkill2RageCost = 1;

        [Tooltip("招式 2 伤害。出处 sp07:140「招式 2：1 怒气，130% 基础攻击（23 点）」——18×130% = 23.4，原文写 23（向下取整）。")]
        [SerializeField] private int playerSkill2Damage = 23;

        [Tooltip("招式 2 减少对方治疗效果的百分比。出处 07:52「减少对方50%治疗效果」。")]
        [SerializeField] private int playerSkill2HealReductionPercent = 50;

        [Tooltip("减疗持续几个怪物回合；0 = 到战斗结束。出处 sp07:140「目标 50% 治疗降低，持续 2 回合」。")]
        [SerializeField] private int playerSkill2HealReductionRounds = 2;

        [Tooltip("招式 3 的怒气消耗。出处 07:54「消耗3点怒气」。")]
        [SerializeField] private int playerSkill3RageCost = 3;

        [Tooltip("招式 3 伤害。出处 sp07:141「招式 3：3 怒气，180% 基础攻击（32 点）」——18×180% = 32.4，原文写 32（向下取整）。")]
        [SerializeField] private int playerSkill3Damage = 32;

        [Tooltip("招式 3 之后每次攻击附带的额外伤害值。出处 sp07:141「后续 3 次主角攻击 + 8 额外伤害」。")]
        [SerializeField] private int playerSkill3ExtraDamage = 8;

        [Tooltip("招式 3 之后附带额外伤害的攻击次数。出处 07:54「玩家接下来的3次攻击附带额外伤害」。")]
        [SerializeField] private int playerSkill3ExtraDamageAttacks = 3;

        [Tooltip("再放一次招式 3 时，剩余的额外伤害次数是累加还是重置。原文没写（07:54），" +
                 "占位 true = 累加（两次招式 3 得到 6 次）。")]
        [SerializeField] private bool playerSkill3ExtraDamageStacks = true;

        [Header("道具（出处 07_回合制作战文档.md:42、:58）")]
        [Tooltip("一件道具在同一场战斗里只能用一次（07:42「用过了则置暗」，09:226 R45）。" +
                 "持有与否由注入的 IBattleItemInventory 回答，本模块不读背包。")]
        [SerializeField] private bool itemOncePerBattle = true;

        [Header("BOSS 醉酒四档（出处 07_回合制作战文档.md:66、:70-73）")]
        [Tooltip("进战斗时是否继承战斗外的醉酒值。出处 07:66「BOSS在进入战斗时，会继承战斗场景外的醉酒值」。")]
        [SerializeField] private bool inheritsDrunkValue = true;

        [Tooltip("微醺档下界（含）。出处 07:71「状态2：微醺，50-79」。")]
        [SerializeField] private int tipsyThreshold = 50;

        [Tooltip("薄醉档下界（含）。出处 07:72「状态3：薄醉，80-99」。")]
        [SerializeField] private int drunkThreshold = 80;

        [Tooltip("酩酊档下界（含）。出处 07:73「状态4：酩酊，100」。")]
        [SerializeField] private int deadDrunkThreshold = 100;

        [Tooltip("醉酒值上限。原文没写上上限（07:66-73 只写了四档区间），等 C91；占位 100（=酩酊档值）。")]
        [SerializeField] private int maxDrunkValue = 100;

        [Tooltip("微醺档跳过回合的概率（百分比）。出处 07:71「有20%概率跳过回合」。")]
        [SerializeField] private int tipsySkipPercent = 20;

        [Tooltip("薄醉档跳过回合的概率（百分比）。出处 07:72「有40%概率跳过回合」。")]
        [SerializeField] private int drunkSkipPercent = 40;

        [Tooltip("酩酊档跳过回合的概率（百分比）。出处 07:73「怪物100%跳过回合」。")]
        [SerializeField] private int deadDrunkSkipPercent = 100;

        [Tooltip("进入酩酊时醉酒值下降多少。出处 07:73「且醉酒值下降50」。")]
        [SerializeField] private int deadDrunkDropValue = 50;

        [Tooltip("酩酊状态持续几个怪物回合。出处 07:73「持续2回合」。")]
        [SerializeField] private int deadDrunkDurationRounds = 2;

        [Tooltip("「醉酒值下降50」是每次酩酊回合都降，还是进入酩酊时降一次。原文没写（07:73），" +
                 "占位 true = 进入酩酊时降一次。")]
        [SerializeField] private bool deadDrunkLowersOncePerEntry = true;

        [Header("BOSS 招式（出处 07_回合制作战文档.md:79-84）")]
        [Tooltip("BOSS 招式 1 伤害。出处 sp07:157「招式 1 普通攻击：无消耗，造成 14 点伤害」；基础攻击 14 见 sp07:150。")]
        [SerializeField] private int bossSkill1Damage = 14;

        [Tooltip("BOSS 招式 2 饮酒增加的醉酒值。出处 07:80「增加30醉酒值」。")]
        [SerializeField] private int bossSkill2DrinkAddDrunk = 30;

        [Tooltip("BOSS 招式 2 回复生命的百分比。出处 07:80「回复10%生命值」。")]
        [SerializeField] private int bossSkill2HealPercent = 10;

        [Tooltip("上面这 10% 的基数（上限还是当前生命）。原文 2026-10-16 给了结果值「回复自身 10% 最大生命 =22点」"
                 + "（sp07:158），但**基数是上限还是当前生命仍没写**（09_BOSS战.md:354）；"
                 + "本字段仍是占位 MaxHealth，等 C91。")]
        [SerializeField] private HealthPercentBase bossSkill2HealBase = HealthPercentBase.MaxHealth;

        [Tooltip("BOSS 招式 2 之后，下次招式 1 的伤害加成百分比。出处 07:80「下次招式1攻击伤害+30%」。")]
        [SerializeField] private int bossSkill2NextSkill1DamageBonusPercent = 30;

        [Tooltip("BOSS 招式 3 伤害。出处 sp07:159「狂暴重击：1. 正常 / 微醺状态：高额伤害 22 点；"
                 + "2. 薄醉状态：22 点伤害 + 眩晕玩家 1 回合」——两种状态伤害同值。")]
        [SerializeField] private int bossSkill3Damage = 22;

        [Tooltip("BOSS 招式 3 在薄醉态晕眩玩家的回合数。出处 07:82「晕眩玩家1回合」。")]
        [SerializeField] private int bossSkill3StunRounds = 1;

        [Tooltip("BOSS 招式 1 的施放权重。出处 07:84「招式1：招式2：招式3 = 6：3：1」。")]
        [SerializeField] private int bossSkill1Weight = 6;

        [Tooltip("BOSS 招式 2 的施放权重。出处 07:84。")]
        [SerializeField] private int bossSkill2Weight = 3;

        [Tooltip("BOSS 招式 3 的施放权重。出处 07:84。")]
        [SerializeField] private int bossSkill3Weight = 1;

        [Header("回合与胜负（出处 07_回合制作战文档.md:43、:56；数等 C91）")]
        [Tooltip("回合上限（玩家回合 + BOSS 回合算一个回合）；0 = 不限。原文没写上限是多少" +
                 "（07:43 界面写「显示剩余回合」说明有上限；09:225 R44 / Q18），等 C91；占位 0。")]
        [SerializeField] private int roundLimit;

        [Tooltip("打到回合上限时算赢还是算输。原文没写（09:225 R44），等 C91；占位 CompareHealth。")]
        [SerializeField] private RoundLimitOutcome roundLimitOutcome = RoundLimitOutcome.CompareHealth;

        [Tooltip("玩家被晕眩时这一回合怎么过。原文没写（09:231 R47 / Q18），等 C91；" +
                 "占位 SkipTurn（整个玩家回合跳过）。")]
        [SerializeField] private StunTurnPolicy stunTurnPolicy = StunTurnPolicy.SkipTurn;

        [Header("道具效果（sp07:199-211 的 10 件，2026-10-16 落地）")]
        [Tooltip("道具 id → 效果表。id 是 tbitem 主键的十进制字符串（与注入的 IBattleItemInventory 同一套）；"
                 + "10 件战斗消耗品在 tbitem 里是 2001–2010（表内每类占一个 100 段的惯例，原文没给 id）。"
                 + "**原文没写的**：道具从哪来、一场带几件、用掉后还在不在（待拍板 C99）；"
                 + "「迅击粉末」的额外一招与回合结构怎么并存（C100）；护盾与护心鳞 / 减伤怎么叠（C101）。"
                 + "空表 = 所有道具都只记账、只置暗。")]
        [SerializeField] private BattleItemEffectEntry[] itemEffects = DefaultEffectEntries();

        /// <summary>进入战斗的判定参数。</summary>
        public BattleEntrySettings Entry => new BattleEntrySettings(sneakBossHealthLossPercent, sneakLossBase);

        /// <summary>玩家三招式的参数。</summary>
        public PlayerSkillSettings PlayerSkills => new PlayerSkillSettings(
            rageMax,
            playerSkill1RageCost,
            playerSkill1RageGain,
            playerSkill1Damage,
            playerSkill2RageCost,
            playerSkill2Damage,
            playerSkill2HealReductionPercent,
            playerSkill2HealReductionRounds,
            playerSkill3RageCost,
            playerSkill3Damage,
            playerSkill3ExtraDamage,
            playerSkill3ExtraDamageAttacks,
            playerSkill3ExtraDamageStacks);

        /// <summary>BOSS 招式的参数（含 6:3:1 权重）。</summary>
        public BossSkillSettings BossSkills => new BossSkillSettings(
            bossSkill1Damage,
            bossSkill2DrinkAddDrunk,
            bossSkill2HealPercent,
            bossSkill2HealBase,
            bossSkill2NextSkill1DamageBonusPercent,
            bossSkill3Damage,
            bossSkill3StunRounds,
            bossSkill1Weight,
            bossSkill2Weight,
            bossSkill3Weight);

        /// <summary>醉酒四档的参数。</summary>
        public DrunkSettings Drunk => new DrunkSettings(
            tipsyThreshold,
            drunkThreshold,
            deadDrunkThreshold,
            maxDrunkValue,
            tipsySkipPercent,
            drunkSkipPercent,
            deadDrunkSkipPercent,
            deadDrunkDropValue,
            deadDrunkDurationRounds,
            deadDrunkLowersOncePerEntry);

        /// <summary>回合流程与胜负的参数。</summary>
        public BattleFlowSettings Flow => new BattleFlowSettings(roundLimit, roundLimitOutcome, stunTurnPolicy);

        /// <summary>进战斗是否继承战斗外的醉酒值（07:66）。</summary>
        public bool InheritsDrunkValue => inheritsDrunkValue;

        /// <summary>一件道具同一场战斗是否只能用一次（07:42 / R45）。</summary>
        public bool ItemOncePerBattle => itemOncePerBattle;

        /// <summary>道具效果表（sp07:199-211 的 10 件）。</summary>
        public BattleItemSettings Items => new BattleItemSettings(ToEffects(itemEffects));

        // Inspector 里的可序列化条目 → 纯值效果。空 / 含 null 的项直接跳过（等于没配）。
        private static BattleItemEffect[] ToEffects(BattleItemEffectEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return System.Array.Empty<BattleItemEffect>();
            }

            var effects = new System.Collections.Generic.List<BattleItemEffect>(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null)
                {
                    effects.Add(entries[i].ToEffect());
                }
            }

            return effects.ToArray();
        }

        // 字段默认值 = 默认效果表（与 `BattleItemSettings.PlaceholderDefault` 同源，避免两处各写一遍）。
        private static BattleItemEffectEntry[] DefaultEffectEntries()
        {
            BattleItemEffect[] effects = BattleItemSettings.DefaultEffects();
            var entries = new BattleItemEffectEntry[effects.Length];
            for (int i = 0; i < effects.Length; i++)
            {
                BattleItemEffect effect = effects[i];
                entries[i] = new BattleItemEffectEntry(effect.ItemId, effect.Kind, effect.Amount, effect.Rounds, effect.Charges);
            }

            return entries;
        }

        /// <summary>整份配置打包成纯值设置，交给规则类与会话使用。</summary>
        public BattleSettings Settings => new BattleSettings(
            Entry,
            PlayerSkills,
            BossSkills,
            Drunk,
            Flow,
            itemOncePerBattle,
            inheritsDrunkValue,
            Items);

        /// <summary>
        /// 校验整份配置。返回 null 表示没问题，否则返回第一条问题的中文描述。
        /// <para>
        /// 判断逻辑在纯函数 <see cref="TurnBasedConfigValidation.Validate"/> 里，这里只负责把字段打包——
        /// 这样校验本身能脱离 Unity 引擎测（不需要创建资产）。
        /// </para>
        /// </summary>
        public string Validate() => TurnBasedConfigValidation.Validate(Settings);
    }
}
