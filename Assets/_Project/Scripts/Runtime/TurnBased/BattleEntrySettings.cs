// 职责：进入战斗判定用得上的那几个数（从 TurnBasedConfig 拆出来，纯值，测试不必创建资产）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:18`「BOSS生命值减少20%」；
// 基数原文没写（`docs/design/features-spotlight/09_BOSS战.md:239`），等 C91。

namespace Game.TurnBased
{
    /// <summary>进入战斗的判定参数。</summary>
    public readonly struct BattleEntrySettings
    {
        /// <summary>构造。</summary>
        /// <param name="sneakBossHealthLossPercent">偷袭开战扣掉 BOSS 的生命百分比（07:18，20）。</param>
        /// <param name="sneakLossBase">上面百分比的基数（原文没写，等 C91）。</param>
        public BattleEntrySettings(int sneakBossHealthLossPercent, HealthPercentBase sneakLossBase)
        {
            SneakBossHealthLossPercent = sneakBossHealthLossPercent;
            SneakLossBase = sneakLossBase;
        }

        /// <summary>偷袭开战扣掉 BOSS 的生命百分比。</summary>
        public int SneakBossHealthLossPercent { get; }

        /// <summary>偷袭扣血的基数。</summary>
        public HealthPercentBase SneakLossBase { get; }

        /// <summary>本工程当前的占位默认值（与 TurnBasedConfig 的字段默认一致）。</summary>
        public static BattleEntrySettings PlaceholderDefault =>
            new BattleEntrySettings(20, HealthPercentBase.MaxHealth);
    }
}
