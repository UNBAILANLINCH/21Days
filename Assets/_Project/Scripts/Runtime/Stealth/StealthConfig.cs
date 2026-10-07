// 职责：感知与潜行内核（`Game.Stealth`）的全部可调数值，集中一处便于策划拍板后直接改。
// 每个字段的注释都写明「出处 / 待拍板编号」；没拍板的写「占位，等 §8.1 #N」。
//
// 为什么新建（不并进 `MonsterConfig` / `PlayerConfig`）：
// - 复用：现有两份配置各管一侧，`MonsterConfig` 管某只怪的巡逻 / 感知 / 血量，
//   `PlayerConfig` 管玩家的移动 / 攻击 / 生命，谁都不管「遮挡几何、绕背窗口、击倒时长、
//   追逐策略、编队召唤」这一层；本内核的数值不属于任何一只具体怪物，也不属于玩家本体。
// - 扩展：`MonsterConfig` 正被并行任务修改，本次任务明令不动；`PlayerConfig` 也不许改。
// - 新建：以上两条都不成立，故新建一份模块级配置 + 一份默认资产。
//
// 出处（真源）：
// - `docs/design/features-spotlight/03_潜行与暗杀.md`（潜行 / 暗杀 / 击倒 / 视线遮挡）
// - `docs/design/features-spotlight/04_追逐.md`（追逐 / 摆脱 / 失败判定 / 召唤 / 集群巡逻）
// - `docs/design/features-spotlight/00_功能总览.md` §5 矛盾表（C4/C5/C6）与 §8.1 阻塞问题
// - `docs/roadmap.md:348` 风险 3（击倒与 BOSS 血量怎么并存还没定）
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>
    /// 感知与潜行内核的数值配置。所有「原文没写」的项都标了待拍板编号，拍板后改这一处即可。
    /// <para>
    /// 本资产**只提供数值**，不提供「做不做」的开关：掩体、暗杀、固定追逐做不做由接线侧决定，
    /// 没接线时对应的事实键恒不写（见 <see cref="SightSettings.WritesCoverFact"/>）。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "21Days/Stealth/Stealth Config")]
    public sealed class StealthConfig : ScriptableObject
    {
        [Header("遮挡（视线遮挡 / 掩体）")]
        [Tooltip("视线被挡时写 stealth.cover。出处 03_潜行与暗杀.md:129-136（R24-R28）；做不做未定，等 00 功能总览 §8.1 #6。")]
        [SerializeField] private bool coverWritesFact = true;

        [Tooltip("视线被挡是否等于隐身（额外写 stealth.hidden）。原文没写掩体怎么起作用，等 03 R25 / 00 §8.1 #6；占位 false。")]
        [SerializeField] private bool coverImpliesHidden;

        [Header("暗杀（绕背处决）")]
        [Tooltip("背后锥总张角（度），与 MonsterConfig.visionAngle 同口径。出处 03:90 R5；范围多大未定，等 00 §8.1 #5。占位 120。")]
        [SerializeField] private float assassinateRearAngle = 120f;

        [Tooltip("暗杀距离上限。出处 03:177「背后近距的半径原文没写」；等 00 §8.1 #5。占位 1.2。")]
        [SerializeField] private float assassinateMaxDistance = 1.2f;

        [Tooltip("暗杀是否要求玩家潜行。原文只说「绕到背后处决」，没说必须潜行；占位 false。")]
        [SerializeField] private bool assassinateRequiresSneak;

        [Header("击倒（挨打的后果）")]
        [Tooltip("倒地期时长（秒）。出处 03:85 R3；时长原文没写（03:86 R4 / 03:178），等 00 §8.1 #4。占位 1.5。")]
        [SerializeField] private float knockdownDownedSeconds = 1.5f;

        [Tooltip("挣扎期（缓慢移动）时长（秒）。待拍板 03:178；03:214 要求与 04 的摆脱条件一起定，等 00 §8.1 #4。占位 3。")]
        [SerializeField] private float knockdownCrawlSeconds = 3f;

        [Tooltip("挣扎期移动速度倍率（相对玩家步行）。待拍板 03:178。占位 0.35。")]
        [SerializeField] private float knockdownCrawlSpeedMultiplier = 0.35f;

        [Tooltip("击倒中再挨打是否重置计时。待拍板 03:86 R4。占位 true。")]
        [SerializeField] private bool knockdownResetOnHitWhileDowned = true;

        [Tooltip("击倒中重复命中的节流窗口（秒），窗口内只计数不重置。待拍板 03:86 R4。占位 0.5。")]
        [SerializeField] private float knockdownHitThrottleSeconds = 0.5f;

        [Tooltip("挨打口径：1=只击倒（sp00 口径）、2=生命归零即死（mai 口径）、3=先击倒血尽才死（折中）。" +
                 "出处 00 §5 C5（原文矛盾 C5），等 00 §8.1 #4；占位 3。")]
        [SerializeField] private DownedHitPolicy downedHitPolicy = DownedHitPolicy.KnockdownThenDeath;

        [Tooltip("玩家生命上限，用于判「血尽才死」。必须与 PlayerConfig.maxHealth 一致。占位 3。")]
        [SerializeField] private int playerMaxHealth = 3;

        [Tooltip("击倒次数低档上界（含）。阈值不进事实表，档位由代码算出后写 stealth.knockdownCount.low/mid/high；" +
                 "出处 00 §8.1 #4。占位 1。")]
        [SerializeField] private int knockdownCountLowMax = 1;

        [Tooltip("击倒次数中档上界（含）。超过它算高档。占位 3。")]
        [SerializeField] private int knockdownCountMidMax = 3;

        [Header("追逐")]
        [Tooltip("追兵速度（单位/秒）。必须高于玩家步行、且不高于玩家奔跑，否则起追判定会以配置错误拒绝。" +
                 "出处 04:165 现状问题（2.5 低于步行 3）；追兵移速原文没写（04:143）。占位 3.6。")]
        [SerializeField] private float chaseSpeed = 3.6f;

        [Tooltip("玩家步行速度基线，用于校验追兵速度。出处 PlayerConfig.moveSpeed。占位 3。")]
        [SerializeField] private float playerWalkSpeed = 3f;

        [Tooltip("玩家奔跑速度基线，用于校验追兵速度上限。出处 PlayerConfig.runSpeed。占位 5。")]
        [SerializeField] private float playerRunSpeed = 5f;

        [Tooltip("额外触发半径：距离超过它不起追（0 = 不限）。待拍板 04:192 Q4，等 00 §8.1 #3。占位 0。")]
        [SerializeField] private float chaseTriggerRadius;

        [Tooltip("跟丢多久算摆脱其一（秒）。已拍板：2 秒，出处 04:104 R23 / mai 验收 3；须与 MonsterConfig.hostileLoseSeconds 一致。")]
        [SerializeField] private float chaseLoseSightGraceSeconds = 2f;

        [Tooltip("摆脱距离。原文没写摆脱条件（04:192 Q4），等 00 §8.1 #3。占位 8。")]
        [SerializeField] private float chaseEscapeDistance = 8f;

        [Tooltip("追踪记忆时长（秒）。原文没写，对应警戒回落 6 秒（04:104 R23）。占位 6。")]
        [SerializeField] private float chaseTrackMemorySeconds = 6f;

        [Tooltip("摆脱是否要求先断视线。待拍板 04:192 Q4。占位 true。")]
        [SerializeField] private bool chaseEscapeRequiresLineOfSightLost = true;

        [Tooltip("被抓的后果口径：1=只击倒、2=变回原主死亡、3=扣血死亡、4=脚本化结局、5=先击倒血尽才死。" +
                 "出处 04:108-112 R26/R27 与 00 §5 C1，等 00 §8.1 #3 / #4；占位 5。")]
        [SerializeField] private ChaseCaughtPolicy chaseCaughtPolicy = ChaseCaughtPolicy.KnockdownThenDeath;

        [Tooltip("同一个追逐里被抓几次算最终失败。原文没写，等 00 §8.1 #4。占位 2。")]
        [SerializeField] private int chaseCaughtFailCount = 2;

        [Header("编队与召唤（巡逻队）")]
        [Tooltip("召唤半径。出处 04:26/04:30（龙族、都统在玩家周围召唤巡逻队）；半径原文没写，等 04:196 Q10。占位 8。")]
        [SerializeField] private float summonRadius = 8f;

        [Tooltip("一群巡逻队的规模下限。已拍板：3，出处 04:29「通常 3-4 集群」。")]
        [SerializeField] private int summonGroupMin = 3;

        [Tooltip("一群巡逻队的规模上限。已拍板：4，出处 04:29「通常 3-4 集群」。")]
        [SerializeField] private int summonGroupMax = 4;

        [Tooltip("固定追逐路线是否循环。出处 04:112 R28 / 04:204「能不能反复触发」未定，等 04:193 Q6。占位 true。")]
        [SerializeField] private bool fixedChaseLoops = true;

        /// <summary>遮挡配置（几何旋钮）。</summary>
        public SightSettings Sight => new SightSettings(coverWritesFact, coverImpliesHidden);

        /// <summary>暗杀配置。</summary>
        public AssassinationSettings Assassination =>
            new AssassinationSettings(assassinateRearAngle, assassinateMaxDistance, assassinateRequiresSneak);

        /// <summary>击倒配置。</summary>
        public KnockdownSettings Knockdown => new KnockdownSettings(
            knockdownDownedSeconds,
            knockdownCrawlSeconds,
            knockdownCrawlSpeedMultiplier,
            knockdownResetOnHitWhileDowned,
            knockdownHitThrottleSeconds,
            downedHitPolicy,
            playerMaxHealth);

        /// <summary>追逐配置。</summary>
        public ChaseSettings Chase => new ChaseSettings(
            chaseTriggerRadius,
            chaseLoseSightGraceSeconds,
            chaseEscapeDistance,
            chaseTrackMemorySeconds,
            chaseEscapeRequiresLineOfSightLost);

        /// <summary>被抓后果配置。</summary>
        public ChaseOutcomeSettings ChaseOutcome =>
            new ChaseOutcomeSettings(chaseCaughtPolicy, playerMaxHealth, chaseCaughtFailCount);

        /// <summary>召唤配置。</summary>
        public SummonSettings Summon => new SummonSettings(summonRadius, summonGroupMin, summonGroupMax);

        /// <summary>速度基线（校验追兵速度用）。</summary>
        public ChaseBaseline Baseline => new ChaseBaseline(playerWalkSpeed, playerRunSpeed);

        /// <summary>追兵速度。</summary>
        public float ChaseSpeed => chaseSpeed;

        /// <summary>固定追逐路线是否循环。</summary>
        public bool FixedChaseLoops => fixedChaseLoops;

        /// <summary>击倒次数低档上界。</summary>
        public int KnockdownCountLowMax => knockdownCountLowMax;

        /// <summary>击倒次数中档上界。</summary>
        public int KnockdownCountMidMax => knockdownCountMidMax;

        /// <summary>玩家生命上限（与击倒口径配套）。</summary>
        public int PlayerMaxHealth => playerMaxHealth;

        /// <summary>
        /// 校验整份配置。返回 null 表示没问题，否则返回第一条问题的中文描述。
        /// <para>
        /// 判断逻辑在纯函数 <see cref="StealthConfigValidation.Validate"/> 里，这里只负责把字段拼成参数——
        /// 这样校验本身能脱离 Unity 引擎测（不需要创建资产）。
        /// </para>
        /// </summary>
        public string Validate() => StealthConfigValidation.Validate(
            Knockdown,
            Chase,
            Baseline,
            Summon,
            chaseSpeed,
            knockdownCountLowMax,
            knockdownCountMidMax,
            chaseCaughtFailCount);
    }
}
