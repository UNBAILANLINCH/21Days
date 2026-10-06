// 职责：进入战斗判定的输出——进没进、以哪种方式进、谁先手、要不要先扣 BOSS 血。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:18 / :23 / :28`
// （偷袭：先手 + 生命 -20%；正面攻击：先手；被打：BOSS 先手）。

namespace Game.TurnBased
{
    /// <summary>进入战斗的判定结果。被拒时 <see cref="Kind"/> 是 <see cref="BattleEntryKind.None"/>。</summary>
    public readonly struct BattleEntryDecision
    {
        private BattleEntryDecision(
            bool accepted,
            BattleEntryKind kind,
            BattleInitiative initiative,
            int bossHealthLossPercent,
            HealthPercentBase bossHealthLossBase,
            BattleEntryReject reject)
        {
            Accepted = accepted;
            Kind = kind;
            Initiative = initiative;
            BossHealthLossPercent = bossHealthLossPercent;
            BossHealthLossBase = bossHealthLossBase;
            Reject = reject;
        }

        /// <summary>是否进入战斗。</summary>
        public bool Accepted { get; }

        /// <summary>进入方式；被拒时为 <see cref="BattleEntryKind.None"/>。</summary>
        public BattleEntryKind Kind { get; }

        /// <summary>先手权；被拒时为 <see cref="BattleInitiative.None"/>。</summary>
        public BattleInitiative Initiative { get; }

        /// <summary>开战前先扣掉 BOSS 的生命的百分比（偷袭 20%，其余 0）。</summary>
        public int BossHealthLossPercent { get; }

        /// <summary>上面那个百分比的基数（配置项，原文没写，等 C91）。</summary>
        public HealthPercentBase BossHealthLossBase { get; }

        /// <summary>被拒原因；通过时为 <see cref="BattleEntryReject.None"/>。</summary>
        public BattleEntryReject Reject { get; }

        /// <summary>构造一个「进入战斗」的结果。</summary>
        public static BattleEntryDecision Enter(
            BattleEntryKind kind,
            BattleInitiative initiative,
            int bossHealthLossPercent,
            HealthPercentBase bossHealthLossBase) =>
            new BattleEntryDecision(true, kind, initiative, bossHealthLossPercent, bossHealthLossBase, BattleEntryReject.None);

        /// <summary>构造一个「没进入战斗」的结果。</summary>
        public static BattleEntryDecision Refuse(BattleEntryReject reject) =>
            new BattleEntryDecision(false, BattleEntryKind.None, BattleInitiative.None, 0, HealthPercentBase.MaxHealth, reject);

        /// <summary>中文描述，用于日志与测试失败信息。</summary>
        public string Describe()
        {
            if (!Accepted)
            {
                return "未进入战斗：" + BattleEntryRules.Describe(Reject);
            }

            string initiative = Initiative == BattleInitiative.Player ? "玩家先手" : "BOSS 先手";
            string loss = BossHealthLossPercent > 0
                ? "，开战前 BOSS 生命 -" + BossHealthLossPercent + "%"
                : string.Empty;
            return "进入战斗：" + BattleEntryRules.Describe(Kind) + "，" + initiative + loss;
        }
    }
}
