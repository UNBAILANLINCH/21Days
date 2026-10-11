// 职责：回合制作战内核（`Game.TurnBased`）的全部可调数值，集中一处便于策划拍板后直接改。
// 每个字段的注释都写明「出处 / 待拍板编号」；原文没给的数一律是**占位值**，注明等哪条拍板。
//
// 为什么新建（不并进 MonsterConfig / PlayerConfig）：
// - 复用：`MonsterConfig` 管某只怪的巡逻 / 感知 / 血量，`PlayerConfig` 管玩家的移动 / 攻击 / 生命，
//   谁都不管「怒气、三招式、道具一场一次、BOSS 醉酒四档、BOSS 三招权重」这一层；
//   而且这两份配置正被并行波次修改，本次任务明令不动。
// - 扩展：没有一份既有配置的职责能容下回合制这一整套数值。
// - 新建：以上两条都不成立，故新建一份模块级配置 + 一份默认资产（`Data/TurnBased/TurnBasedConfig.asset`）。
//
// 出处（真源，逐行对着写）：
// - `docs/design/spotlight/07_回合制作战文档.md`（全篇 86 行；本文件的每个数值都标了行号）
// - `docs/design/features-spotlight/09_BOSS战.md` §3.9（R40–R48）、§4「数值」段（哪些数原文没写）
// - `docs/design/features-spotlight/待策划拍板问题.md:1256` C91（回合制的数值与胜负条件）
// - `docs/design/features-spotlight/00_功能总览.md:414` §8.1 #7（BOSS 战形式与「本体打不过任何怪」并存）
using UnityEngine;

namespace Game.TurnBased
{
    /// <summary>
    /// 回合制作战内核的数值配置。所有「原文没写」的项都标了待拍板编号（C91），拍板后改这一处即可，
    /// 规则代码一行不用动。
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

        [Tooltip("上面这 20% 的基数（上限还是当前生命）。原文没写百分比作用在什么基数上，" +
                 "出处 09_BOSS战.md:239；占位 MaxHealth，等 C91。")]
        [SerializeField] private HealthPercentBase sneakLossBase = HealthPercentBase.MaxHealth;

        [Header("玩家招式（出处 07_回合制作战文档.md:48-54）")]
        [Tooltip("怒气上限。原文没写（07:40 只写「怒气值满时」），等 C91；占位 3——招式 3 消耗 3 点，" +
                 "上限至少要有 3 才跑得起来。")]
        [SerializeField] private int rageMax = 3;

        [Tooltip("招式 1 的怒气消耗。出处 07:50「无消耗」。")]
        [SerializeField] private int playerSkill1RageCost;

        [Tooltip("招式 1 使用后获得的怒气。出处 07:50「使用后增加1点怒气」。")]
        [SerializeField] private int playerSkill1RageGain = 1;

        [Tooltip("招式 1 伤害。原文只说「造成伤害」没给数（07:50；09:239），等 C91；占位 1（最小可跑值）。")]
        [SerializeField] private int playerSkill1Damage = 1;

        [Tooltip("招式 2 的怒气消耗。出处 07:52「消耗1点怒气」。")]
        [SerializeField] private int playerSkill2RageCost = 1;

        [Tooltip("招式 2 伤害。原文只说「造成伤害」没给数（07:52；09:239），等 C91；占位 2。")]
        [SerializeField] private int playerSkill2Damage = 2;

        [Tooltip("招式 2 减少对方治疗效果的百分比。出处 07:52「减少对方50%治疗效果」。")]
        [SerializeField] private int playerSkill2HealReductionPercent = 50;

        [Tooltip("减疗持续几个怪物回合；0 = 到战斗结束。原文没写持续多久（07:52；09:239），等 C91；占位 0。")]
        [SerializeField] private int playerSkill2HealReductionRounds;

        [Tooltip("招式 3 的怒气消耗。出处 07:54「消耗3点怒气」。")]
        [SerializeField] private int playerSkill3RageCost = 3;

        [Tooltip("招式 3 伤害。原文只说「重击」没给数（07:54；09:239），等 C91；占位 3。")]
        [SerializeField] private int playerSkill3Damage = 3;

        [Tooltip("招式 3 之后每次攻击附带的额外伤害值。原文只说「额外伤害」没给数（07:54；09:239），" +
                 "等 C91；占位 1。")]
        [SerializeField] private int playerSkill3ExtraDamage = 1;

        [Tooltip("招式 3 之后附带额外伤害的攻击次数。出处 07:54「玩家接下来的3次攻击附带额外伤害」。")]
        [SerializeField] private int playerSkill3ExtraDamageAttacks = 3;

        [Tooltip("再放一次招式 3 时，剩余的额外伤害次数是累加还是重置。原文没写（07:54），" +
                 "占位 true = 累加（两次招式 3 得到 6 次）。")]
        [SerializeField] private bool playerSkill3ExtraDamageStacks = true;

        [Header("道具（出处 07_回合制作战文档.md:42、:58）")]
        [Tooltip("一件道具在同一场战斗里只能用一次（07:42「用过了则置暗」，09:193 R45）。" +
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
        [Tooltip("BOSS 招式 1 伤害。原文只说「造成一定伤害」没给数（07:79；09:239），等 C91；占位 1。")]
        [SerializeField] private int bossSkill1Damage = 1;

        [Tooltip("BOSS 招式 2 饮酒增加的醉酒值。出处 07:80「增加30醉酒值」。")]
        [SerializeField] private int bossSkill2DrinkAddDrunk = 30;

        [Tooltip("BOSS 招式 2 回复生命的百分比。出处 07:80「回复10%生命值」。")]
        [SerializeField] private int bossSkill2HealPercent = 10;

        [Tooltip("上面这 10% 的基数（上限还是当前生命）。原文没写（09:239），等 C91；占位 MaxHealth。")]
        [SerializeField] private HealthPercentBase bossSkill2HealBase = HealthPercentBase.MaxHealth;

        [Tooltip("BOSS 招式 2 之后，下次招式 1 的伤害加成百分比。出处 07:80「下次招式1攻击伤害+30%」。")]
        [SerializeField] private int bossSkill2NextSkill1DamageBonusPercent = 30;

        [Tooltip("BOSS 招式 3 伤害。原文只说「单次高额伤害」没给数（07:81；09:239），等 C91；占位 3。")]
        [SerializeField] private int bossSkill3Damage = 3;

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
                 "（07:43 界面写「显示剩余回合」说明有上限；09:192 R44 / Q18），等 C91；占位 0。")]
        [SerializeField] private int roundLimit;

        [Tooltip("打到回合上限时算赢还是算输。原文没写（09:192 R44），等 C91；占位 CompareHealth。")]
        [SerializeField] private RoundLimitOutcome roundLimitOutcome = RoundLimitOutcome.CompareHealth;

        [Tooltip("玩家被晕眩时这一回合怎么过。原文没写（09:198 R47 / Q18），等 C91；" +
                 "占位 SkipTurn（整个玩家回合跳过）。")]
        [SerializeField] private StunTurnPolicy stunTurnPolicy = StunTurnPolicy.SkipTurn;

        [Header("道具效果（占位，等 C91；PRP/turnbased-battle D10）")]
        [Tooltip("治疗道具的 item id（背包 tbitem 主键，按字符串比较）。07 没写道具效果（07:42、:58 只写能用 / 置暗），" +
                 "占位 1004 治疗药水；留空 = 不设治疗道具（删掉这条占位），等 C91。")]
        [SerializeField] private string healItemId = "1004";

        [Tooltip("治疗道具回复玩家生命的百分比（0..100）。原文没写，占位 30，等 C91。")]
        [SerializeField] private int healItemPercent = 30;

        [Tooltip("上面这个百分比的基数（上限还是当前生命）。原文没写，占位 MaxHealth（向下取整，同 07 其余百分比口径），等 C91。")]
        [SerializeField] private HealthPercentBase healItemBase = HealthPercentBase.MaxHealth;

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

        /// <summary>道具效果（占位，等 C91）。</summary>
        public BattleItemSettings Items => new BattleItemSettings(healItemId, healItemPercent, healItemBase);

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
