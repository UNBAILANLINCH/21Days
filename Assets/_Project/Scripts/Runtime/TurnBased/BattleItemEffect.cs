// 职责：一条战斗内道具效果（纯值）——哪件道具、什么效果、数值多少、持续几回合 / 几次。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:199-211`（10 件道具两张表）；种类见 BattleItemEffectKind。
// 「回合」与「次数」两种计量在原文里是分开写的（铁壁护符 / 浴血药剂 / 固本甘露按回合，锐锋油膏按次数），
// 所以这里两个字段都给，按种类取用哪一个由规则侧决定。

using System;

namespace Game.TurnBased
{
    /// <summary>一条道具效果。id 为空或种类为 <see cref="BattleItemEffectKind.None"/> 视为没效果。</summary>
    [Serializable]
    public readonly struct BattleItemEffect
    {
        /// <param name="itemId">道具 id（与注入的 <see cref="IBattleItemInventory"/> 同一套字符串 id）。</param>
        /// <param name="kind">效果种类。</param>
        /// <param name="amount">数值：回复量 / 怒气点数 / 减伤百分比 / 加攻值 / 护盾层数；按种类解释。</param>
        /// <param name="rounds">持续回合数（按回合计的效果用；0 = 不按回合）。</param>
        /// <param name="charges">持续次数（按次计的效果用；0 = 不按次）。</param>
        public BattleItemEffect(string itemId, BattleItemEffectKind kind, int amount, int rounds = 0, int charges = 0)
        {
            ItemId = itemId ?? string.Empty;
            Kind = kind;
            Amount = amount;
            Rounds = rounds;
            Charges = charges;
        }

        /// <summary>道具 id；空串 = 没挂效果。</summary>
        public string ItemId { get; }

        /// <summary>效果种类。</summary>
        public BattleItemEffectKind Kind { get; }

        /// <summary>数值（含义随种类）。</summary>
        public int Amount { get; }

        /// <summary>持续回合数（0 = 不按回合计）。</summary>
        public int Rounds { get; }

        /// <summary>持续次数（0 = 不按次计）。</summary>
        public int Charges { get; }

        /// <summary>这条是不是有效果可执行。</summary>
        public bool HasEffect => Kind != BattleItemEffectKind.None && !string.IsNullOrEmpty(ItemId);

        /// <summary>数值为正才算一条写得对的效果（HealFixed / RageAdd / DamageReduction / AttackBonus / Lifesteal / Shield / Regen 都要求正数）。</summary>
        public bool IsWellFormed =>
            Kind switch
            {
                BattleItemEffectKind.None => true,
                BattleItemEffectKind.RageToMax => true,
                BattleItemEffectKind.ExtraAction => true,
                BattleItemEffectKind.HealFixed => Amount > 0,
                BattleItemEffectKind.RageAdd => Amount > 0,
                BattleItemEffectKind.Shield => Charges > 0,
                BattleItemEffectKind.DamageReduction => Amount > 0 && Amount <= 100 && Rounds > 0,
                BattleItemEffectKind.AttackBonus => Amount > 0 && Charges > 0,
                BattleItemEffectKind.Lifesteal => Amount > 0 && Rounds > 0,
                BattleItemEffectKind.Regen => Amount > 0 && Rounds > 0,
                _ => false,
            };

        public override string ToString() =>
            ItemId + " → " + Kind + "(" + Amount + (Rounds > 0 ? "，" + Rounds + " 回合" : string.Empty)
            + (Charges > 0 ? "，" + Charges + " 次" : string.Empty) + ")";
    }
}
