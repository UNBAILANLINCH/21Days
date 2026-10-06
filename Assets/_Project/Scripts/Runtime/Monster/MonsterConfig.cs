// 职责：怪物**全局默认值**（全体怪物共用一份）与按种类数值的兜底；现有配置只服务各自框架职责。
// 字段归属（真源 docs/design/features-spotlight/06_怪物分层.md，逐条依据见下面每个字段的注释与文件末的汇总）：
//   「按种类」的字段不在这份 SO 里，在 Tables/Defines/monster_species.xml → Game.Monster.MonsterKind：
//     生命、伤害、攻击距离 / 冷却、视野角度、橙区半径、背后近距半径、警戒升满时长、丢失目标时长。
//   「全局」的字段留在本类，同时充当**兜底**：没有种类配置时（旧场景、旧测试、独立原型场景）
//     一律取本类的值，行为与拆分前逐位一致（MonsterRules 的 Resolve* 系列）。
using UnityEngine;

namespace Game.Monster
{
    /// <summary>
    /// 怪物全局配置。资产放 <c>Data/Monster/MonsterConfig.asset</c>，拖到 Boot 场景 <c>GameBootstrap</c> 的
    /// <see cref="MonsterInstaller"/> 上。运行时只读。
    /// <para>
    /// <b>为什么还要留全局那一份</b>：巡逻与转态节奏（<c>06_怪物分层.md:119</c> R7：原文没区分「驻地 / 驻守 / 守卫 / 守门」
    /// 这几类行为，工程先按一套节奏跑）、敌对半径、速度倍率这些还没有按种类的依据；等策划拍了再逐层拆进种类表，
    /// 拆的时候加列，不要改本类的语义。同时它也是**兜底**：没配种类时全部取这里，见 <see cref="MonsterRules"/>。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "21Days/Monster/Monster Config")]
    public sealed class MonsterConfig : ScriptableObject
    {
        // ↓↓↓ 全局：巡逻、转态与速度。按种类拆分暂无依据（06_怪物分层.md:119 R7）。 ↓↓↓
        [Tooltip("巡逻速度（单位/秒）。全局：原文没给按种类/按层分速度的依据。")]
        [SerializeField] private float patrolSpeed = 2f;

        [Tooltip("警戒状态的速度倍率。全局。")]
        [SerializeField] private float alertSpeedMultiplier = 1.1f;

        [Tooltip("敌对状态的速度倍率。全局。")]
        [SerializeField] private float hostileSpeedMultiplier = 1.25f;

        [Tooltip("敌对（红区）半径。全局：它决定感知优先级里的红区，按种类改会改语义，所以不开放给种类表。")]
        [SerializeField] private float hostileRadius = 2f;

        [Tooltip("警戒升满所需秒数的兜底值；种类表填了就用种类表。")]
        [SerializeField] private float alertFillSeconds = 4f;

        [Tooltip("脱离后警戒衰减到零的秒数。全局：衰减曲线不是种类的差异点。")]
        [SerializeField] private float alertFallSeconds = 6f;

        [Tooltip("敌对后丢失目标多少秒转回满值警戒的兜底值；种类表填了就用种类表。")]
        [SerializeField] private float hostileLoseSeconds = 2f;

        [Tooltip("巡逻停步时长（秒）。全局。")]
        [SerializeField] private float patrolPauseSeconds = 2f;

        [Tooltip("随机停步区间下限（秒）。全局。")]
        [SerializeField] private int patrolMinSeconds = 7;

        [Tooltip("随机停步区间上限（秒）。全局。")]
        [SerializeField] private int patrolMaxSeconds = 10;

        // ↓↓↓ 按种类：种类表（monster_species）为主，这里的值只在没配种类时兜底。 ↓↓↓
        [Tooltip("视野扇区总角度（度）的兜底值；种类表填了就用种类表。")]
        [SerializeField] private float visionAngle = 75f;

        [Tooltip("橙区（警戒）半径的兜底值；种类表填了就用种类表。必须大于敌对半径。")]
        [SerializeField] private float alertRadius = 6f;

        [Tooltip("背后近距察觉半径的兜底值；种类表填了就用种类表。")]
        [SerializeField] private float nearSenseRadius = 1.5f;

        [Tooltip("攻击距离（米）的兜底值；种类表填了就用种类表。")]
        [SerializeField] private float attackRange = 0.8f;

        [Tooltip("攻击冷却（秒）的兜底值；种类表填了就用种类表。")]
        [SerializeField] private float attackCooldown = 1f;

        [Tooltip("初始与最大生命的兜底值；种类表填了就用种类表。")]
        [SerializeField] private int maxHealth = 3;

        [Tooltip("每次命中伤害的兜底值；种类表填了就用种类表。")]
        [SerializeField] private int attackDamage = 1;

        // ↓↓↓ 全局字段的取值 ↓↓↓
        public float PatrolSpeed => patrolSpeed;
        public float AlertSpeedMultiplier => alertSpeedMultiplier;
        public float HostileSpeedMultiplier => hostileSpeedMultiplier;
        public float HostileRadius => hostileRadius;
        public float AlertFallSeconds => alertFallSeconds;
        public float PatrolPauseSeconds => patrolPauseSeconds;
        public int PatrolMinSeconds => patrolMinSeconds;
        public int PatrolMaxSeconds => patrolMaxSeconds;

        // ↓↓↓ 兜底取值：MonsterRules 经 Resolve* 取到「种类优先、否则全局」的最终值，不要直接用这几个属性配怪。 ↓↓↓
        public float VisionAngle => visionAngle;
        public float AlertRadius => alertRadius;
        public float NearSenseRadius => nearSenseRadius;
        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;
        public float AlertFillSeconds => alertFillSeconds;
        public float HostileLoseSeconds => hostileLoseSeconds;
        public int MaxHealth => maxHealth;
        public int AttackDamage => attackDamage;

        // 下面这组是「按种类优先、没有种类就用全局」的唯一入口。种类为 null 就是「这份资产没接种类表」——
        // 旧场景、旧测试、独立原型场景都落在这一支，取值与拆分前逐位一致。
        // 种类不为 null 时直接用种类值、不再比较：种类表每一列都是必填（Tables/Defines/monster_species.xml），
        // 取值合法性由 MonsterKindCatalog 在读表那一次判，到不了这里。
        public float ResolveVisionAngle(MonsterKind kind) => kind == null ? visionAngle : kind.VisionAngle;
        public float ResolveAlertRadius(MonsterKind kind) => kind == null ? alertRadius : kind.AlertRadius;
        public float ResolveNearSenseRadius(MonsterKind kind) => kind == null ? nearSenseRadius : kind.NearSenseRadius;
        public float ResolveAttackRange(MonsterKind kind) => kind == null ? attackRange : kind.AttackRange;
        public float ResolveAttackCooldown(MonsterKind kind) => kind == null ? attackCooldown : kind.AttackCooldown;
        public float ResolveAlertFillSeconds(MonsterKind kind) => kind == null ? alertFillSeconds : kind.AlertFillSeconds;
        public float ResolveHostileLoseSeconds(MonsterKind kind) => kind == null ? hostileLoseSeconds : kind.HostileLoseSeconds;
        public int ResolveMaxHealth(MonsterKind kind) => kind == null ? maxHealth : kind.MaxHealth;
        public int ResolveAttackDamage(MonsterKind kind) => kind == null ? attackDamage : kind.AttackDamage;
    }
}
