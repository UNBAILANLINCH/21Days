// 职责：一只 BOSS 的定义——id、显示名、生命上限、战斗外醉酒值、本场玩家生命、舞台外观预制体；BossRosterConfig 里的一行。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：TurnBased 内核只吃快照（BossBattleSnapshot），不认识「哪只 BOSS」；MonsterConfig 管的是巡逻怪的
//      巡逻 / 感知 / 攻击，而且 Runtime/Monster 本波禁改。
//   2. 扩展不行：PRP/turnbased-battle D3 定「本期不进 Luban 表」（另一会话有未提交的表数据，加 schema 要全表重生成），
//      所以既有表的 bean 也扩不了。等 C90 / C91 拍板后迁表时，这个类按字段一一对应搬走即可。
// 数值出处：生命原文没写（09_BOSS战.md:239），战斗外醉酒值的口径未明（02:109 写 BOSS 100），一律占位、等 C91。
// 本场玩家生命（W2a 主窗口定）：战斗血量与探索血量分开——探索里玩家只有 3 血，BOSS 重击 3、药水 30% 取整为 0，演示不出东西。
//   07 没写玩家血量，也没说与探索血量挂钩，所以按 BOSS 配一份「这一场玩家多少血」，占位 10，等 C91。
using System;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>BOSS 定义（只读）。id 就是剧情 Battle 阶段的 payload。</summary>
    [Serializable]
    public sealed class BossDefinition
    {
        /// <summary>本场玩家生命的占位默认值（等 C91）。</summary>
        public const int PlaceholderPlayerHealth = 10;

        [Tooltip("BOSS 定义 id = 剧情 Battle 阶段的 payload（Tables/Data/narrative/*.json 的 payload 列）。")]
        [SerializeField] private string id = string.Empty;

        [Tooltip("界面上显示的名字（BOSS 血条上方）。")]
        [SerializeField] private string displayName = string.Empty;

        [Tooltip("生命上限，每次开战都是满血。原文没写（09_BOSS战.md:239），占位，等 C91。")]
        [SerializeField, Min(1)] private int maxHealth = 1;

        [Tooltip("战斗外醉酒值，开战时继承（07_回合制作战文档.md:66）。口径未明（02:109 写 BOSS 100），占位，等 C91。")]
        [SerializeField, Min(0)] private int outOfBattleDrunk;

        [Tooltip("本场玩家生命（开战满血、战后不回写）。占位，等 C91；07 没写玩家血量，也没说与探索血量挂钩。")]
        [SerializeField, Min(1)] private int playerHealth = PlaceholderPlayerHealth;

        [Tooltip("战斗舞台上的外观预制体（纸片角色，Prefabs/Battle/）。空 = 舞台用场景里的占位外观。")]
        [SerializeField] private GameObject stagePrefab;

        /// <summary>序列化用。</summary>
        public BossDefinition()
        {
        }

        /// <summary>代码构造（测试 / 将来从表迁入）。</summary>
        public BossDefinition(string id, string displayName, int maxHealth, int outOfBattleDrunk,
            int playerHealth = PlaceholderPlayerHealth, GameObject stagePrefab = null)
        {
            this.id = id ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.maxHealth = maxHealth;
            this.outOfBattleDrunk = outOfBattleDrunk;
            this.playerHealth = playerHealth;
            this.stagePrefab = stagePrefab;
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
    }
}
