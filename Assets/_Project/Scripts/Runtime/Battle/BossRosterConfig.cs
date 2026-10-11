// 职责：BOSS 名册配置资产（Data/Battle/BossRosterConfig.asset）——剧情 Battle 阶段的 payload → BOSS 定义。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：TurnBasedConfig 只管回合制数值，不认识「哪只 BOSS、叫什么、多少血、外观」；MonsterConfig 是巡逻怪且禁改。
//   2. 扩展不行：PRP/turnbased-battle D3 定「本期不进 Luban 表」——工作区里另一会话有未提交的表数据，
//      这时加新表 schema 要全表重生成，牵连太大。等 C90 / C91 拍板后迁表，届时删掉本资产、BossRoster 改从表建。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>BOSS 名册配置。运行时只读：开战时按 payload 查，不改字段。</summary>
    [CreateAssetMenu(menuName = "21Days/Battle/Boss Roster Config")]
    public sealed class BossRosterConfig : ScriptableObject
    {
        [Tooltip("BOSS 定义。id 要与剧情 Battle 阶段的 payload 一致（样例：sample_boss ← narrative/sample_boss_battle.json）。")]
        [SerializeField] private BossDefinition[] bosses = Array.Empty<BossDefinition>();

        /// <summary>全部定义（只读视图）。</summary>
        public IReadOnlyList<BossDefinition> Bosses => bosses ?? Array.Empty<BossDefinition>();

        /// <summary>建出可查询的名册；名册写坏（空行 / 缺 id / 重复 / 数值非法）时抛 <see cref="ArgumentException"/>。</summary>
        public BossRoster CreateRoster() => new BossRoster(Bosses);
    }
}
