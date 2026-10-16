// 职责：BOSS 名册的纯 C# 查询——按 id（剧情 payload）查 BOSS 定义，按怪物种类查回合制战斗数据，构造时校验整份名册。
// 为什么新建：BossRosterConfig 是 ScriptableObject，测试与战斗流程不该依赖资产（同 TurnBasedConfig → BattleSettings 的拆法）；
//   校验写在这里，名册写坏时构造即抛，而不是等到开战那一刻才发现某一行是空的。
// 2026-10-14（PRP/combat-iteration D3）：加「按种类查询」——实时遭遇升级回合制时，战斗侧按怪物种类找这一仗用哪一行；
//   非 0 的种类 id 不许重复（同一种怪两行数据，开哪一行就成了注册顺序的巧合）。
// 2026-10-14（PRP/combat-iteration D6）：开了主动发难的行，冲刺速度 / 发难半径 / 反应时间必须是正的有限值、拴绳 ≥ 0
//   （关着时不校验，字段只是占位）。
using System;
using System.Collections.Generic;

namespace Game.Battle
{
    /// <summary>BOSS 名册（只读）。</summary>
    public sealed class BossRoster
    {
        private readonly Dictionary<string, BossDefinition> byId = new Dictionary<string, BossDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<int, BossDefinition> byKind = new Dictionary<int, BossDefinition>();

        /// <summary>用一组定义构造并校验。</summary>
        /// <exception cref="ArgumentException">
        /// 有空行、缺 id、id 重复、生命上限 / 本场玩家生命非正、醉酒值为负、怪物种类 id 为负或（非 0 时）重复、
        /// 开了主动发难但冲刺速度 / 发难半径 / 反应时间不是正的有限值、或拴绳距离为负 / 非有限。
        /// </exception>
        public BossRoster(IEnumerable<BossDefinition> source)
        {
            foreach (BossDefinition boss in source ?? throw new ArgumentNullException(nameof(source)))
            {
                if (boss == null) throw new ArgumentException("BOSS 名册里有空行");
                if (string.IsNullOrWhiteSpace(boss.Id)) throw new ArgumentException("BOSS 定义缺 id（id 就是剧情 Battle 阶段的 payload）");
                if (boss.MaxHealth <= 0) throw new ArgumentException("BOSS 生命上限必须大于 0：" + boss.Id);
                if (boss.OutOfBattleDrunk < 0) throw new ArgumentException("BOSS 战斗外醉酒值不可为负：" + boss.Id);
                if (boss.PlayerHealth <= 0) throw new ArgumentException("本场玩家生命必须大于 0：" + boss.Id);
                if (boss.MonsterKindId < 0) throw new ArgumentException("怪物种类 id 不可为负（0 = 不是怪物行）：" + boss.Id);
                if (boss.AggroEnabled) ValidateAggro(boss);
                if (!byId.TryAdd(boss.Id, boss)) throw new ArgumentException("BOSS id 重复：" + boss.Id);
                if (boss.MonsterKindId != 0 && !byKind.TryAdd(boss.MonsterKindId, boss))
                    throw new ArgumentException("怪物种类 id 重复：" + boss.MonsterKindId + "（" + boss.Id + "）");
            }
        }

        // PRP/combat-iteration D6（2026-10-15 补半径 / 反应时间 / 拴绳）：只校验开了发难的行，关着时这几项只是占位。
        //   冲刺速度 / 发难半径 / 反应时间非正或非有限 = BOSS 原地「冲刺」、永不警觉或立刻冲，表现像卡死或失控；
        //   拴绳 0 是合法的「自动 = 半径 × 2」，负数与非有限值不合法。
        //   「发难半径要大于交互半径」名册里校验不了（交互半径在场景的 NarrativeTrigger 上），由 BossAggro 绑定时告警。
        private static void ValidateAggro(BossDefinition boss)
        {
            if (!IsPositiveFinite(boss.AggroChargeSpeed))
                throw new ArgumentException("开了主动发难的 BOSS 冲刺速度必须是大于 0 的有限值：" + boss.Id);
            if (!IsPositiveFinite(boss.AggroRadius))
                throw new ArgumentException("开了主动发难的 BOSS 发难半径必须是大于 0 的有限值：" + boss.Id);
            if (!IsPositiveFinite(boss.AggroReactionSeconds))
                throw new ArgumentException("开了主动发难的 BOSS 反应时间必须是大于 0 的有限值：" + boss.Id);
            float leash = boss.AggroLeashDistanceSetting;
            if (!(leash >= 0f) || float.IsInfinity(leash))
                throw new ArgumentException("BOSS 拴绳距离必须是 ≥ 0 的有限值（0 = 发难半径 × 2）：" + boss.Id);
        }

        private static bool IsPositiveFinite(float value) => value > 0f && !float.IsInfinity(value);

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

        /// <summary>
        /// 按怪物种类查回合制战斗数据（PRP/combat-iteration D3）；<paramref name="kindId"/> 非正或查不到返回 false。
        /// 0 是「不是怪物行」的保留值，<b>永远查不到</b>——没接种类表的怪（种类 id 0）因此不会被升级。零分配，可在 tick 里调。
        /// </summary>
        public bool TryGetByKind(int kindId, out BossDefinition boss)
        {
            boss = null;
            return kindId > 0 && byKind.TryGetValue(kindId, out boss);
        }
    }
}
