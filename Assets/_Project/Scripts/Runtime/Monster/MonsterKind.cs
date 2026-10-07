// 职责：一种怪的**数值**（按种类配的那部分），是 Tables/Defines/monster_species.xml 一行配置的只读视图。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：MonsterConfig 是全体怪物共用的 ScriptableObject，一份实例装不下多个种类；直接把 cfg 生成行
//      塞给 MonsterRules 又会让规则类认识 Luban 的 ByteBuf 行对象，配置坏的报错也散到玩法里。
//   2. 扩展不行：MonsterModel 承载的是「这一只怪的运行时状态」，配置是别的生命周期的东西，塞一起说不通。
// 分工（真源 docs/design/features-spotlight/06_怪物分层.md）：
//   **按种类**（本类）：生命、伤害、攻击距离 / 冷却、视野角度、橙区半径、背后近距半径、警戒升满时长、丢失目标时长。
//     依据：该文 :121 R9（同层怪的「可否击杀 / 怎么杀」各不相同）、:117 R5（C 层分 BOSS 与关键 NPC / 执法者两类）、
//     :184「较强」、:186「战斗难度极高」，以及 :113 R1 的 A / B / C 三层本身就是按层配数值的口径。
//   **全局**（留在 MonsterConfig）：巡逻与转态计时、速度倍率、敌对半径、停步与巡逻间隔。
//     依据：该文 :119 R7「『驻地』『驻守』『守卫』『守门』是不是同一类行为，原文没区分」——原文没给这些行为分级。
//   「这只怪是什么」（tier / killable / defeat_method / drop_items）不在这里，在 tbyao，经 Game.Mirror.YaoCatalog 查。
using Game.Mirror;

namespace Game.Monster
{
    /// <summary>
    /// 一种怪的按种类数值。由 <see cref="MonsterKindCatalog"/> 从 <c>cfg.monster_species.TbMonsterSpecies</c> 建出来。
    /// <para>
    /// <b>只读视图</b>：构造后不再变，运行期状态请放 <see cref="MonsterModel"/>，不要往这里挂。
    /// </para>
    /// <para>
    /// <b>跨表的列不复制</b>：<see cref="IsKillable"/> / <see cref="DefeatMethod"/> / <see cref="DropItemIds"/>
    /// 都转问 <see cref="YaoCatalog"/>（键是 <see cref="KindId"/> 指向的妖物 id），本类不自己存一份；
    /// 免得同一件事在 tbyao 与 monster_species 两处写、两处能写歪。
    /// </para>
    /// </summary>
    public sealed class MonsterKind
    {
        private readonly YaoCatalog yaoCatalog;

        /// <summary>
        /// 建一种怪。参数即 <c>Tables/Defines/monster_species.xml</c> 的列；
        /// 数值合法性由 <see cref="MonsterKindCatalog"/> 在读表那一次统一校验（同 YaoCatalog 的「校验只做一次」）。
        /// </summary>
        /// <param name="kindId">种类主键（monster_species 的 id）。</param>
        /// <param name="kindIdForYao">这只怪在 tbyao 里的妖物 id；掉落 / 可否击杀 / 层级都按它查。</param>
        /// <param name="name">种类名，只用于报错与调试，不参与玩法判定。</param>
        /// <param name="yaoCatalog">妖物表只读查询；为 null 时跨表的三个成员退化成「查不到」的默认值。</param>
        public MonsterKind(int kindId, int kindIdForYao, string name, int maxHealth, int attackDamage,
            float attackRange, float attackCooldown, float visionAngle, float alertRadius, float nearSenseRadius,
            float alertFillSeconds, float hostileLoseSeconds, YaoCatalog yaoCatalog)
        {
            KindId = kindId;
            YaoId = kindIdForYao;
            Name = name ?? string.Empty;
            MaxHealth = maxHealth;
            AttackDamage = attackDamage;
            AttackRange = attackRange;
            AttackCooldown = attackCooldown;
            VisionAngle = visionAngle;
            AlertRadius = alertRadius;
            NearSenseRadius = nearSenseRadius;
            AlertFillSeconds = alertFillSeconds;
            HostileLoseSeconds = hostileLoseSeconds;
            this.yaoCatalog = yaoCatalog;
        }

        /// <summary>种类主键（<c>monster_species.id</c>）。</summary>
        public int KindId { get; }

        /// <summary>这只怪在妖物表（tbyao）里的 id；掉落、可否击杀、层级都按它查。</summary>
        public int YaoId { get; }

        /// <summary>种类名，报错与调试用。</summary>
        public string Name { get; }

        /// <summary>初始与最大生命。</summary>
        public int MaxHealth { get; }

        /// <summary>每次命中对玩家造成的伤害。</summary>
        public int AttackDamage { get; }

        /// <summary>攻击距离（米）。</summary>
        public float AttackRange { get; }

        /// <summary>两次攻击之间的冷却（秒）。</summary>
        public float AttackCooldown { get; }

        /// <summary>前方扇区总角度（度）。</summary>
        public float VisionAngle { get; }

        /// <summary>橙区半径（米）；必须大于全局敌对半径，否则读表时抛（见 MonsterKindCatalog）。</summary>
        public float AlertRadius { get; }

        /// <summary>背后近距察觉半径（米）。</summary>
        public float NearSenseRadius { get; }

        /// <summary>橙区连续暴露多少秒把警戒升满。</summary>
        public float AlertFillSeconds { get; }

        /// <summary>敌对后丢失目标多少秒转回满值警戒。</summary>
        public float HostileLoseSeconds { get; }

        /// <summary>这只怪在 <c>06_怪物分层.md</c> 里的层级原文 A / B / C（转问妖物表；查不到返回 null）。</summary>
        public string Tier => yaoCatalog == null ? null : yaoCatalog.TierOf(YaoId);

        /// <summary>能否常规击杀（转问妖物表；查不到按 false）。</summary>
        public bool IsKillable => yaoCatalog != null && yaoCatalog.IsKillable(YaoId);

        /// <summary>
        /// 怎么杀 / 有没有替代途径（转问妖物表，取值只可能是
        /// <c>可击杀（方式没写）</c> / <c>暗杀</c> / <c>特殊条件</c> / <c>需收服</c> / <c>不可杀</c>；查不到返回 null）。
        /// 与 <see cref="IsKillable"/> 一起用才完整：查勘使是 false + 特殊条件，籍中吏是 false + 不可杀。
        /// </summary>
        public string DefeatMethod => yaoCatalog == null ? null : yaoCatalog.DefeatMethodOf(YaoId);

        /// <summary>
        /// 击杀 / 暗杀掉落的 item id（转问妖物表的 drop_items）；没定或查不到时为空列表。
        /// <b>只交 id</b>：道具类别枚举与 item 表归道具侧，本模块不判断 id 是什么类别。
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<int> DropItemIds =>
            yaoCatalog == null
                ? System.Array.Empty<int>()
                : yaoCatalog.DropItemsOf(YaoId);

        public override string ToString() => "MonsterKind#" + KindId + "(" + Name + " → yao " + YaoId + ")";
    }
}
