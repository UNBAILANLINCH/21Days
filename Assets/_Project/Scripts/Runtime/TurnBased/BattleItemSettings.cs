// 职责：战斗内道具效果的配置（纯值）。目前只有一条**占位**：某件治疗道具回复玩家生命百分比。
//
// 为什么新建（复用 → 扩展 → 新建）：
// - 复用：既有五份 *Settings 管的是进战斗、招式、醉酒、流程，没有一份管「道具用了有什么效果」；
//   `ItemUseRules` / `BattleItemLedger` 只判「能不能用」，不管效果。
// - 扩展：塞进 `PlayerSkillSettings` 会让招式表长出道具字段，职责说不通。
// - 新建：同其余 *Settings 的拆法——纯值结构，规则与测试都不依赖 ScriptableObject。
//
// 出处：07 原文没写道具效果（`docs/design/spotlight/07_回合制作战文档.md:42`、:58 只写「能用 / 用过置暗」），
// `PRP/turnbased-battle/prp.md` D10 定占位「1004 治疗药水回复 30% 生命」——**占位，等 C91**。

using System;

namespace Game.TurnBased
{
    /// <summary>战斗内道具效果（占位，等 C91）。</summary>
    public readonly struct BattleItemSettings
    {
        /// <summary>构造。</summary>
        /// <param name="healItemId">治疗道具的 id（与背包口 <see cref="IBattleItemInventory"/> 同一套字符串 id）；空 = 没有治疗道具。</param>
        /// <param name="healPercent">回复玩家生命的百分比（0..100）。</param>
        /// <param name="healBase">百分比的基数（生命上限 / 当前生命）。</param>
        public BattleItemSettings(string healItemId, int healPercent, HealthPercentBase healBase)
        {
            HealItemId = healItemId ?? string.Empty;
            HealPercent = healPercent;
            HealBase = healBase;
        }

        /// <summary>治疗道具 id；空串 = 没设治疗道具（等于删掉这条占位）。</summary>
        public string HealItemId { get; }

        /// <summary>回复玩家生命的百分比。</summary>
        public int HealPercent { get; }

        /// <summary>百分比基数。</summary>
        public HealthPercentBase HealBase { get; }

        /// <summary>这件道具是不是配置里的治疗道具（且百分比为正）。</summary>
        public bool Heals(string itemId) =>
            !string.IsNullOrEmpty(HealItemId) && HealPercent > 0 && string.Equals(itemId, HealItemId, StringComparison.Ordinal);

        /// <summary>本工程当前的占位默认值（与 `TurnBasedConfig` 的字段默认一致）：1004 治疗药水回复 30% 生命上限。</summary>
        public static BattleItemSettings PlaceholderDefault => new BattleItemSettings("1004", 30, HealthPercentBase.MaxHealth);
    }
}
