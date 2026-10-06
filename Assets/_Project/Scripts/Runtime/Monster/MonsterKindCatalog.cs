// 职责：怪物种类数值表（Tables/Defines/monster_species.xml）的只读查询——把每种种类的数值读出来并按种类 id 建索引，
//   顺带在读表那一次把数值与跨表引用校验掉。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：cfg.monster_species.MonsterSpecies 是 Luban 一次性的行对象，没有「按种类 id 反查」，
//      而且 TbMonsterSpecies 在配置服务初始化完成前访问会抛，各处自己 try/catch 会把容错散到每个调用方。
//   2. 扩展不行：MonsterConfig 是 ScriptableObject（全局默认值，见该文件头的分工），不该认识配置表；
//      MonsterRules 是确定性规则，不该做「表读没读到」这种 IO 容错；判据同 DialogueCatalog / QuestCatalog / YaoCatalog：
//      表适配单独一层。
// 依赖只到 Game.Core.Config（IConfigService）+ 生成代码 cfg + Game.Mirror.YaoCatalog，不引用场景、存档、容器。
using System;
using System.Collections.Generic;
using Game.Core.Config;
using Game.Mirror;

namespace Game.Monster
{
    /// <summary>
    /// 怪物种类目录（根作用域单例，只读）。首次访问任一成员时才读表（惰性）：<see cref="IConfigService.Tables"/>
    /// 在配置服务初始化完成前访问会抛，所以构造时不碰表。
    /// <para>
    /// <b>未知种类的行为（二选一，这里选「显式抛」）</b>：
    /// <see cref="Get"/> 查不到直接抛 <see cref="KeyNotFoundException"/>，<see cref="TryGet"/> 是它的宽容版本。
    /// 理由：表是生成物，查不到一定是「id 写错」或「表没跟上」，静默兜底会把数据错误变成一只数值不对的怪，
    /// 在玩法里表现为「这怪怎么打不死」，离真因最远（同 <c>YaoCatalog.Get</c> 的取舍）。
    /// 需要「没配就按全局默认跑」的调用方（<see cref="MonsterRules"/>）自己走 <see cref="TryGet"/>，
    /// 让兜底是调用方**显式**的选择，而不是查询层的默认行为。
    /// </para>
    /// <para>
    /// <b>表没就绪不算异常</b>：<see cref="IsReady"/> 为 false、<see cref="Count"/> 为 0、<see cref="TryGet"/>
    /// 一律 false（同 <c>YaoCatalog</c>）。启动早期要读表先探 <see cref="IsReady"/>，
    /// 别把「配置还没加载完」当成「没有这个种类」。
    /// </para>
    /// <para>
    /// <b>校验只做一次</b>：全部行的数值与跨表引用在读表那一次校验完（<see cref="EnsureTableRead"/>），
    /// 之后查询直接读缓存。数据坏了当场抛 <see cref="ArgumentException"/>（消息带种类 id 与原始值）——
    /// 表是只读生成物，运行期没有旁路改写，重复校验换不来新信息。测试换过表数据后调 <see cref="Invalidate"/>
    /// 重读并重校验。
    /// </para>
    /// </summary>
    public sealed class MonsterKindCatalog
    {
        private readonly IConfigService config;
        private readonly YaoCatalog yaoCatalog;
        private readonly float globalHostileRadius;
        private Dictionary<int, MonsterKind> byId;

        /// <summary>
        /// 建目录。
        /// </summary>
        /// <param name="config">配置表服务，取 <c>Tables.TbMonsterSpecies</c>。</param>
        /// <param name="yaoCatalog">妖物表只读查询，用来判定 <c>yao_id</c> 真的存在；为 null 时跳过该条校验（只读数值）。</param>
        /// <param name="globalHostileRadius">
        /// 全局敌对半径（<c>MonsterConfig.HostileRadius</c>）。种类橙区半径必须大于它，否则「红区优先」的语义会被压掉：
        /// 红区判定写在 <c>MonsterRules.Sense</c>，红区半径来自全局配置，橙区半径按种类配；
        /// 种类橙区 ≤ 全局红区时，站在橙区里的人已经在红区里，永远轮不到警戒累积。
        /// </param>
        public MonsterKindCatalog(IConfigService config, YaoCatalog yaoCatalog, float globalHostileRadius)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.yaoCatalog = yaoCatalog;
            this.globalHostileRadius = globalHostileRadius;
        }

        /// <summary>配置表是否已就绪；未就绪时其余成员按「查不到」处理，不抛异常。</summary>
        public bool IsReady
        {
            get
            {
                try
                {
                    EnsureTableRead();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // 配置服务用它自己的 InvalidOperationException 报「还没初始化完」，这里翻成人话给查询方。
                    return false;
                }
            }
        }

        /// <summary>表里的种类条数；表没就绪时为 0。</summary>
        public int Count => IsReady ? byId.Count : 0;

        /// <summary>全部种类（表顺序）；表没就绪时为空列表。</summary>
        public IReadOnlyList<MonsterKind> All
        {
            get
            {
                if (!IsReady)
                {
                    return Array.Empty<MonsterKind>();
                }

                IReadOnlyList<global::cfg.monster_species.MonsterSpecies> rows = config.Tables.TbMonsterSpecies.DataList;
                var result = new List<MonsterKind>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    result.Add(byId[rows[i].Id]);
                }

                return result;
            }
        }

        /// <summary>按种类 id 取数值；没有这个种类返回 false（表没就绪同样是 false）。</summary>
        public bool TryGet(int kindId, out MonsterKind kind)
        {
            kind = null;
            return IsReady && byId.TryGetValue(kindId, out kind);
        }

        /// <summary>按种类 id 取数值；没有这个种类时抛 <see cref="KeyNotFoundException"/>（见类文档「未知种类的行为」）。</summary>
        public MonsterKind Get(int kindId)
        {
            if (!TryGet(kindId, out MonsterKind kind))
            {
                throw new KeyNotFoundException(
                    $"怪物种类表里没有 id {kindId}。检查 Tables/Data/monster_species/ 下有没有这个种类的 JSON，"
                    + "改完跑一次 scripts/gen-tables.ps1。");
            }

            return kind;
        }

        /// <summary>
        /// 清掉惰性缓存。语义同 <c>YaoCatalog.Invalidate</c>：只在测试里换过表数据后用得到，
        /// 下一次访问会重新读表并重新校验一遍。
        /// </summary>
        public void Invalidate() => byId = null;

        // 读表 + 全表校验，只做一次。表里有一行写坏时这份状态不生效：异常当场抛出，byId 不落，
        // 下次访问重新校验并报同一个错。
        private void EnsureTableRead()
        {
            if (byId != null)
            {
                return;
            }

            IReadOnlyList<global::cfg.monster_species.MonsterSpecies> rows = config.Tables.TbMonsterSpecies.DataList;
            var map = new Dictionary<int, MonsterKind>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                global::cfg.monster_species.MonsterSpecies row = rows[i];
                var kind = new MonsterKind(row.Id, row.YaoId, row.Name, row.Health, row.AttackDamage,
                    row.AttackRange, row.AttackCooldown, row.VisionAngle, row.AlertRadius, row.NearSenseRadius,
                    row.AlertFillSeconds, row.HostileLoseSeconds, yaoCatalog);
                Validate(kind);
                // 主键重复在 Luban 生成期就会拦下，这里不重复校验，只照抄成字典。
                map.Add(kind.KindId, kind);
            }

            byId = map;
        }

        // 逐条校验。每一条都点名「哪一列、什么值、该满足什么」，数据坏了要当场看见。
        private void Validate(MonsterKind kind)
        {
            if (kind.KindId < 1)
            {
                throw new ArgumentException($"monster_species 的种类 id 必须 ≥ 1，实际 {kind.KindId}。");
            }

            if (kind.YaoId < 1)
            {
                throw new ArgumentException(
                    $"怪物种类 {kind.KindId}（{kind.Name}）的 yao_id 必须 ≥ 1，实际 {kind.YaoId}；"
                    + "它指向 Tables/Data/yao/<id>.json 的妖物 id。");
            }

            if (yaoCatalog != null && yaoCatalog.IsReady && !yaoCatalog.TryGet(kind.YaoId, out global::cfg.yao.Yao _))
            {
                throw new ArgumentException(
                    $"怪物种类 {kind.KindId}（{kind.Name}）的 yao_id = {kind.YaoId} 在妖物表里没有这一行；"
                    + "掉落、可否击杀、层级都按 yao_id 查，指错了就全查不到。");
            }

            if (kind.MaxHealth <= 0)
            {
                throw new ArgumentException($"怪物种类 {kind.KindId}（{kind.Name}）的 health 必须为正数，实际 {kind.MaxHealth}。");
            }

            if (kind.AttackDamage <= 0)
            {
                throw new ArgumentException($"怪物种类 {kind.KindId}（{kind.Name}）的 attack_damage 必须为正数，实际 {kind.AttackDamage}。");
            }

            if (kind.AttackRange <= 0f)
            {
                throw new ArgumentException($"怪物种类 {kind.KindId}（{kind.Name}）的 attack_range 必须为正数，实际 {kind.AttackRange}。");
            }

            if (kind.AttackCooldown < 0f)
            {
                throw new ArgumentException($"怪物种类 {kind.KindId}（{kind.Name}）的 attack_cooldown 不可为负，实际 {kind.AttackCooldown}。");
            }

            if (kind.VisionAngle <= 0f || kind.VisionAngle >= 360f)
            {
                throw new ArgumentException($"怪物种类 {kind.KindId}（{kind.Name}）的 vision_angle 须在 0 与 360 之间，实际 {kind.VisionAngle}。");
            }

            if (kind.NearSenseRadius <= 0f)
            {
                throw new ArgumentException($"怪物种类 {kind.KindId}（{kind.Name}）的 near_sense_radius 必须为正数，实际 {kind.NearSenseRadius}。");
            }

            if (kind.AlertRadius <= globalHostileRadius)
            {
                throw new ArgumentException(
                    $"怪物种类 {kind.KindId}（{kind.Name}）的 alert_radius = {kind.AlertRadius} 不大于全局敌对半径 "
                    + $"{globalHostileRadius}（MonsterConfig.HostileRadius）；红区优先于橙区，橙区不大过红区就永远轮不到警戒累积。");
            }

            if (kind.AlertFillSeconds <= 0f)
            {
                throw new ArgumentException($"怪物种类 {kind.KindId}（{kind.Name}）的 alert_fill_seconds 必须为正数，实际 {kind.AlertFillSeconds}。");
            }

            if (kind.HostileLoseSeconds <= 0f)
            {
                throw new ArgumentException($"怪物种类 {kind.KindId}（{kind.Name}）的 hostile_lose_seconds 必须为正数，实际 {kind.HostileLoseSeconds}。");
            }
        }
    }
}
