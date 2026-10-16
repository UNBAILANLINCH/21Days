// 职责：一只 BOSS 的定义——id、显示名、生命上限、战斗外醉酒值、本场玩家生命、舞台外观预制体；BossRosterConfig 里的一行。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：TurnBased 内核只吃快照（BossBattleSnapshot），不认识「哪只 BOSS」；MonsterConfig 管的是巡逻怪的
//      巡逻 / 感知 / 攻击，而且 Runtime/Monster 本波禁改。
//   2. 扩展不行：PRP/turnbased-battle D3 定「本期不进 Luban 表」（另一会话有未提交的表数据，加 schema 要全表重生成），
//      所以既有表的 bean 也扩不了。等 C90 / C91 拍板后迁表时，这个类按字段一一对应搬走即可。
// 数值出处：生命原文 2026-10-16 已给（阶段一 BOSS 220、阶段九 BOSS 两形态各 260，sp07:149、:167、:183；
//   转写见 09_BOSS战.md R64、R66、R67），资产 `BossRosterConfig.asset` 已照原文填 220；
//   战斗外醉酒值的口径仍未明（原文只写「由进入战斗前决定」，sp07:151；02:109 写 BOSS 100），仍是占位，等 C91。
// 本场玩家生命（W2a 主窗口定）：战斗血量与探索血量分开——探索里玩家只有 3 血，与回合制的 120 血不是一套。
//   原文 2026-10-16 给了玩家最大生命值 120（sp07:129-133），所以本字段的默认值改成 120；
//   但「每场是不是都满血开局」「打输之后怎么办」原文仍没写（待拍板 C98）。
// 主动发难（PRP/combat-iteration D6，2026-10-14；2026-10-15 用户拍板补「发难半径 + 反应时间 + 拴绳」）：
//   开关、冲刺速度、发难半径、反应时间、拴绳距离都放这一行——「这只 BOSS 会不会冲上来、多远察觉、愣多久、冲多快、追多远」
//   是 BOSS 的数据，不写死在场景组件 BossAggro 里；开关默认关 = 加字段之前的行为（pitfalls.md:243），关着时其余四项只是占位。
using System;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>BOSS 定义（只读）。id 就是剧情 Battle 阶段的 payload。</summary>
    [Serializable]
    public sealed class BossDefinition
    {
        /// <summary>本场玩家生命的默认值（sp07:129-133「最大生命值 120 / 当前生命值 120（战斗初始）」）。</summary>
        public const int DefaultPlayerHealth = 120;

        /// <summary>发难冲刺速度的占位默认值（米 / 秒，等 C91）。</summary>
        public const float PlaceholderAggroChargeSpeed = 3f;

        /// <summary>发难半径的占位默认值（米，以原位为圆心的水平距离；2026-10-15 用户拍板 5 米）。</summary>
        public const float PlaceholderAggroRadius = 5f;

        /// <summary>反应时间的占位默认值（秒；2026-10-15 用户拍板 3 秒）。</summary>
        public const float PlaceholderAggroReactionSeconds = 3f;

        /// <summary>拴绳距离 = 发难半径的几倍（<see cref="aggroLeashDistance"/> 填 0 时按它算）。</summary>
        public const float AutoLeashRadiusMultiplier = 2f;

        [Tooltip("BOSS 定义 id = 剧情 Battle 阶段的 payload（Tables/Data/narrative/*.json 的 payload 列）。")]
        [SerializeField] private string id = string.Empty;

        [Tooltip("界面上显示的名字（BOSS 血条上方）。")]
        [SerializeField] private string displayName = string.Empty;

        [Tooltip("生命上限，每次开战都是满血。出处 sp07:149（阶段一 BOSS 220）与 :167 / :183（阶段九 BOSS 人 / 龙形态各 260）；"
                 + "本字段默认 1 只是「没填就报错」的下限，实际值一律填在 `BossRosterConfig.asset` 里。")]
        [SerializeField, Min(1)] private int maxHealth = 1;

        [Tooltip("战斗外醉酒值，开战时继承（07_回合制作战文档.md:66）。口径未明（02:109 写 BOSS 100），占位，等 C91。")]
        [SerializeField, Min(0)] private int outOfBattleDrunk;

        [Tooltip("本场玩家生命（开战满血、战后不回写）。出处 sp07:129-133「最大生命值 120」。"
                 + "与探索血量（3 血）不是一套；「每场是否满血开局 / 打输怎么办」原文没写，待拍板 C98。")]
        [SerializeField, Min(1)] private int playerHealth = DefaultPlayerHealth;

        [Tooltip("战斗舞台上的外观预制体（纸片角色，Prefabs/Battle/）。空 = 舞台用场景里的占位外观。")]
        [SerializeField] private GameObject stagePrefab;

        // PRP/combat-iteration D3（2026-10-14）：「怪物的回合制战斗数据」复用 BOSS 定义的一行，不进 Luban 表
        //（沿用文件头「等 C90 / C91 再迁表」的口径）。代码默认 0 = 不是怪物行 = 加字段之前的行为（pitfalls.md:243）。
        [Tooltip("这一行同时是哪一种巡逻怪（monster_species 主键）的回合制战斗数据：实时遭遇里打到 / 被打到这种怪时按这一行开回合制。"
                 + "0 = 不是怪物行（只给剧情 Battle 阶段按 id 查）。非 0 的值在整份名册里不能重复。")]
        [SerializeField, Min(0)] private int monsterKindId;

        // PRP/combat-iteration D6：BOSS 主动发难。默认关 = 只能按 E 挑战（加字段之前的行为）。
        [Tooltip("主动发难：玩家进这只 BOSS 的发难半径后它先警觉（头顶出提示），愣满反应时间玩家还在半径里就冲上来，"
                 + "碰到就照常播嘲讽对白、以「被打、BOSS 先手」开战；反应时间内走到跟前按 E 仍是正面攻击。"
                 + "默认关 = 只能按 E 挑战。还要在场景里给 BOSS 物体挂 BossAggro（bossId 填本行 id）才生效。")]
        [SerializeField] private bool aggroEnabled;

        [Tooltip("发难时冲向玩家的速度（米 / 秒，地面平面上；撤回原位也用它）。占位 3，等 C91。开了发难时必须是正的有限值。")]
        [SerializeField, Min(0f)] private float aggroChargeSpeed = PlaceholderAggroChargeSpeed;

        [Tooltip("发难半径（米）：玩家与 BOSS **原位**的水平距离进到这里面，BOSS 就警觉。占位 5（用户 2026-10-15 拍板）。"
                 + "应大于剧情入口（NarrativeTrigger）的交互半径，否则玩家没有「走近按 E」的空档（绑定时校验，只告警不拦）。"
                 + "开了发难时必须是正的有限值。")]
        [SerializeField, Min(0f)] private float aggroRadius = PlaceholderAggroRadius;

        [Tooltip("反应时间（秒，受世界暂停）：警觉后愣这么久玩家还在发难半径里才冲；这段时间里走到跟前按 E = 正面攻击、玩家先手。"
                 + "占位 3（用户 2026-10-15 拍板）。守卫挡着（对白 / 演出 / 别的战斗 / 暂停 / 剧情忙）时不计时。开了发难时必须是正的有限值。")]
        [SerializeField, Min(0f)] private float aggroReactionSeconds = PlaceholderAggroReactionSeconds;

        [Tooltip("拴绳距离（米）：冲刺中 BOSS 离原位（水平）超过它就放弃、走回原位。"
                 + "0 = 按「发难半径 × 2」算（占位口径，发难半径 5 时即 10 米）；填正数则用填的值。不可为负。")]
        [SerializeField, Min(0f)] private float aggroLeashDistance;

        /// <summary>序列化用。</summary>
        public BossDefinition()
        {
        }

        /// <summary>代码构造（测试 / 将来从表迁入）。</summary>
        /// <param name="monsterKindId">怪物种类 id（PRP/combat-iteration D3）；默认 0 = 不是怪物行。</param>
        /// <param name="aggroEnabled">是否主动发难（PRP/combat-iteration D6）；默认关 = 只能按 E 挑战。</param>
        /// <param name="aggroChargeSpeed">发难冲刺速度（米 / 秒）；默认占位 3。</param>
        /// <param name="aggroRadius">发难半径（米，以原位为圆心）；默认占位 5。</param>
        /// <param name="aggroReactionSeconds">反应时间（秒）；默认占位 3。</param>
        /// <param name="aggroLeashDistance">拴绳距离（米）；默认 0 = 发难半径 × 2。</param>
        public BossDefinition(string id, string displayName, int maxHealth, int outOfBattleDrunk,
            int playerHealth = DefaultPlayerHealth, GameObject stagePrefab = null, int monsterKindId = 0,
            bool aggroEnabled = false, float aggroChargeSpeed = PlaceholderAggroChargeSpeed,
            float aggroRadius = PlaceholderAggroRadius, float aggroReactionSeconds = PlaceholderAggroReactionSeconds,
            float aggroLeashDistance = 0f)
        {
            this.id = id ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.maxHealth = maxHealth;
            this.outOfBattleDrunk = outOfBattleDrunk;
            this.playerHealth = playerHealth;
            this.stagePrefab = stagePrefab;
            this.monsterKindId = monsterKindId;
            this.aggroEnabled = aggroEnabled;
            this.aggroChargeSpeed = aggroChargeSpeed;
            this.aggroRadius = aggroRadius;
            this.aggroReactionSeconds = aggroReactionSeconds;
            this.aggroLeashDistance = aggroLeashDistance;
        }

        /// <summary>定义 id（= 剧情 Battle 阶段的 payload）。</summary>
        public string Id => id ?? string.Empty;

        /// <summary>显示名。</summary>
        public string DisplayName => displayName ?? string.Empty;

        /// <summary>生命上限。</summary>
        public int MaxHealth => maxHealth;

        /// <summary>战斗外醉酒值（07:66 开战继承）。</summary>
        public int OutOfBattleDrunk => outOfBattleDrunk;

        /// <summary>本场玩家生命（开战满血；与探索血量无关，占位等 C91）。</summary>
        public int PlayerHealth => playerHealth;

        /// <summary>舞台外观预制体；可能为空（舞台退回场景里的占位外观）。</summary>
        public GameObject StagePrefab => stagePrefab;

        /// <summary>这一行对应的怪物种类 id（实时遭遇升级回合制按它查，见 <see cref="BossRoster.TryGetByKind"/>）；0 = 不是怪物行。</summary>
        public int MonsterKindId => monsterKindId;

        /// <summary>是否主动发难（玩家进发难半径 → 警觉 → 愣满反应时间 → 冲上来、以「被打」开战，见 <see cref="BossAggro"/>）。</summary>
        public bool AggroEnabled => aggroEnabled;

        /// <summary>发难冲刺速度（米 / 秒）；<see cref="AggroEnabled"/> 为真时名册校验要求正的有限值。</summary>
        public float AggroChargeSpeed => aggroChargeSpeed;

        /// <summary>发难半径（米，玩家与 BOSS 原位的水平距离）；<see cref="AggroEnabled"/> 为真时名册校验要求正的有限值。</summary>
        public float AggroRadius => aggroRadius;

        /// <summary>反应时间（秒）；<see cref="AggroEnabled"/> 为真时名册校验要求正的有限值。</summary>
        public float AggroReactionSeconds => aggroReactionSeconds;

        /// <summary>序列化里填的拴绳距离（0 = 自动）。实际生效的值读 <see cref="AggroLeashDistance"/>。</summary>
        public float AggroLeashDistanceSetting => aggroLeashDistance;

        /// <summary>生效的拴绳距离（米）：填了正数用填的，0 时按 <see cref="AggroRadius"/> × <see cref="AutoLeashRadiusMultiplier"/>。</summary>
        public float AggroLeashDistance => aggroLeashDistance > 0f ? aggroLeashDistance : aggroRadius * AutoLeashRadiusMultiplier;
    }
}
