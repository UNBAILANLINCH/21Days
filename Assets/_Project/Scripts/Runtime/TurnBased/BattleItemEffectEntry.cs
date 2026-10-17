// 职责：`BattleItemEffect` 的可序列化镜像——ScriptableObject 里存一张道具效果表用（Unity 序列化要字段，不要只读属性）。
//
// 为什么单独一个类型：#1 项目约定一个文件一个类；#2 纯值结构 `BattleItemEffect` 是 readonly struct + 属性，
// Unity 的序列化器读不了，硬把它改成可变字段会把「纯值、测试不依赖资产」这条设计弄丢（turnbased-module-guide「配置与占位值」）。
// 转换只在这一处：`ToEffect()`。

using System;
using UnityEngine;

namespace Game.TurnBased
{
    /// <summary>一条道具效果的 Inspector 形状。区间见 <see cref="BattleItemEffect.IsWellFormed"/>。</summary>
    [Serializable]
    public sealed class BattleItemEffectEntry
    {
        [Tooltip("道具 id（tbitem 主键的十进制字符串，与注入的 IBattleItemInventory 同一套）。")]
        [SerializeField] private string itemId = string.Empty;

        [Tooltip("效果种类；None = 这件道具只记账、只置暗。")]
        [SerializeField] private BattleItemEffectKind kind = BattleItemEffectKind.None;

        [Tooltip("数值：HealFixed = 回复量；RageAdd = 怒气点数；DamageReduction = 减伤百分比；"
                 + "AttackBonus = 加攻值；Lifesteal = 每次命中回复量；Regen = 每回合回复量。")]
        [SerializeField] private int amount;

        [Tooltip("持续回合数（DamageReduction / Lifesteal / Regen 用）。")]
        [SerializeField] private int rounds;

        [Tooltip("持续次数（AttackBonus = 几次攻击；Shield = 几层护盾）。")]
        [SerializeField] private int charges;

        /// <summary>序列化用。</summary>
        public BattleItemEffectEntry()
        {
        }

        /// <summary>代码构造（默认表用）。</summary>
        public BattleItemEffectEntry(string itemId, BattleItemEffectKind kind, int amount, int rounds = 0, int charges = 0)
        {
            this.itemId = itemId ?? string.Empty;
            this.kind = kind;
            this.amount = amount;
            this.rounds = rounds;
            this.charges = charges;
        }

        /// <summary>转成纯值效果。</summary>
        public BattleItemEffect ToEffect() => new BattleItemEffect(itemId, kind, amount, rounds, charges);
    }
}
