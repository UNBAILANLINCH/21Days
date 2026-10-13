// 职责：占位治疗效果——用掉一件治疗类道具，玩家在探索里回复固定点数生命。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里唯一现成的治疗是 `Game.TurnBased.PlayerBattleRules.Heal`（按百分比、作用在战斗快照上），
//      它只活在回合制战斗会话里，够不着探索期的 `Game.Player.PlayerModel`；BattleItemSettings 那套占位
//      也是战斗侧的（BattleItemSettings.cs:10 写明「PRP/turnbased-battle D10 定占位」）。
//   2. 扩展不行：把治疗写进 ItemUseRules 会让纯规则依赖玩家与场景；写进 LootService 会让 Loot 认识玩家生命。
// 数值出处：原文没给治疗量。docs/design/features-spotlight/05_皮面具与道具.md 的道具总表里**根本没有治疗药水**
//   （该表的「消耗品」是量尺、暗杀用的药、特殊效果道具，:171-173），药水这条占位来自
//   `PRP/turnbased-battle/prp.md` D10「1004 治疗药水回复 30% 生命」。
//   **本类是占位，等 `05_皮面具与道具.md` 的类型/数值拍板**（该文档 :288 Q2 还在问「消耗品放哪一页」）。
//   本类按「绝对点数」而不是百分比：探索期玩家只有 PlayerConfig.maxHealth = 3 点血，30% 取整为 0，演示不出东西
//   （同 BossDefinition.cs:8 里记的那条口径）。
using Game.Core.Logging;

namespace Game.Inventory
{
    /// <summary>
    /// 治疗效果（占位，等 05 拍板数值）。默认 <see cref="PlaceholderHealAmount"/> 点，配到 3 点血上限时等于一次回满。
    /// </summary>
    public sealed class HealPlayerEffect : IItemEffect
    {
        /// <summary>占位治疗量（点）。等 05_皮面具与道具.md 给出药水数值后改这里或改成从配置读。</summary>
        public const int PlaceholderHealAmount = 3;

        /// <summary>占位效果标识：与 <see cref="InventoryConfig"/> 里占位使用表的 EffectId 一致。</summary>
        public const string PlaceholderEffectId = "heal.player_hp";

        private readonly int amount;

        /// <param name="amount">一次回复多少点；非正会退化成默认占位值（0 点的「治疗」没有意义，且会让玩家白扣一件道具）。</param>
        public HealPlayerEffect(int amount = PlaceholderHealAmount)
        {
            this.amount = amount > 0 ? amount : PlaceholderHealAmount;
        }

        /// <inheritdoc />
        public string EffectId => PlaceholderEffectId;

        /// <summary>一次回复的点数。</summary>
        public int Amount => amount;

        /// <inheritdoc />
        public ItemEffectOutcome Apply(ItemEffectContext context)
        {
            if (context == null)
            {
                return ItemEffectOutcome.BadTarget("没有可用的效果目标");
            }

            IItemEffectTarget target = context.Target;
            if (target == null)
            {
                // 目标口没接进来：正式流程里由 InventoryInstaller 注册 PlayerItemEffectTarget，正常跑不会走到这支；
                // 会走到的是**独立测试作用域**（显式传 null）与将来「没有玩家的场景」。留一条 Error 是刻意的
                // ——「必须留痕、不许静默失败」；但文案写的是**当前真实语义**，不再让人去补已经补好的东西
                // （2026-10-07 已补：PlayerRules.Heal + PlayerItemEffectTarget）。
                Log.Error("HealPlayerEffect：玩家生命口（IItemEffectTarget）没接进来，道具的治疗效果无法生效。"
                          + "正式流程由 InventoryInstaller 注册 PlayerItemEffectTarget；这一支出现在独立测试作用域或没有玩家的场景。");
                return ItemEffectOutcome.BadTarget("治疗口还没接上");
            }

            // 先读生命再治：TryHeal 只回「实际回了多少」，0 既可能是满血也可能是已倒下，
            // 靠治完再读 CurrentHealth 区分不出这两种，提示就会说错话。
            int before = target.CurrentHealth;
            int healed = target.TryHeal(amount);
            if (healed <= 0)
            {
                return ItemEffectOutcome.Rejected(before <= 0 ? "你已经倒下了" : "生命已经满了");
            }

            return ItemEffectOutcome.Applied(healed);
        }
    }
}
