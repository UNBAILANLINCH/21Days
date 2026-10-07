// 职责：BOSS 名册的纯 C# 查询——按 id（剧情 payload）查 BOSS 定义，构造时校验整份名册。
// 为什么新建：BossRosterConfig 是 ScriptableObject，测试与战斗流程不该依赖资产（同 TurnBasedConfig → BattleSettings 的拆法）；
//   校验写在这里，名册写坏时构造即抛，而不是等到开战那一刻才发现某一行是空的。
using System;
using System.Collections.Generic;

namespace Game.Battle
{
    /// <summary>BOSS 名册（只读）。</summary>
    public sealed class BossRoster
    {
        private readonly Dictionary<string, BossDefinition> byId = new Dictionary<string, BossDefinition>(StringComparer.Ordinal);

        /// <summary>用一组定义构造并校验。</summary>
        /// <exception cref="ArgumentException">有空行、缺 id、id 重复、生命上限 / 本场玩家生命非正或醉酒值为负。</exception>
        public BossRoster(IEnumerable<BossDefinition> source)
        {
            foreach (BossDefinition boss in source ?? throw new ArgumentNullException(nameof(source)))
            {
                if (boss == null) throw new ArgumentException("BOSS 名册里有空行");
                if (string.IsNullOrWhiteSpace(boss.Id)) throw new ArgumentException("BOSS 定义缺 id（id 就是剧情 Battle 阶段的 payload）");
                if (boss.MaxHealth <= 0) throw new ArgumentException("BOSS 生命上限必须大于 0：" + boss.Id);
                if (boss.OutOfBattleDrunk < 0) throw new ArgumentException("BOSS 战斗外醉酒值不可为负：" + boss.Id);
                if (boss.PlayerHealth <= 0) throw new ArgumentException("本场玩家生命必须大于 0：" + boss.Id);
                if (!byId.TryAdd(boss.Id, boss)) throw new ArgumentException("BOSS id 重复：" + boss.Id);
            }
        }

        /// <summary>空名册：任何 payload 都查不到（名册资产写坏时的退路，查不到会在开战时报错）。</summary>
        public static BossRoster Empty => new BossRoster(Array.Empty<BossDefinition>());

        /// <summary>定义条数。</summary>
        public int Count => byId.Count;

        /// <summary>按 id 查；id 为空或查不到返回 false。</summary>
        public bool TryGet(string id, out BossDefinition boss)
        {
            boss = null;
            return !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out boss);
        }
    }
}
