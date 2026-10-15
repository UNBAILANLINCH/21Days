// 职责：一条「使用」定义的 Inspector 行——道具 id + 效果标识 + 一次消耗几件。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：ItemUseSpec 是 readonly struct + 只读属性，Unity 序列化不了（Inspector 只认字段），
//      直接放进 SO 会得到一份永远是默认值的空表。
//   2. 扩展不行：给 ItemUseSpec 补 public 字段会让「运行时规则值」长出第二套暴露面（csharp-code.md 禁 public 字段），
//      而且它是 readonly struct，序列化语义与「不可变值」这件事本身冲突。
// 形状照 BossDefinition（[Serializable] 类 + [SerializeField] private 字段 + 只读属性 + 代码构造）抄。
using System;
using UnityEngine;

namespace Game.Inventory
{
    /// <summary>占位使用表的一行。序列化用；运行时转成 <see cref="ItemUseSpec"/>。</summary>
    [Serializable]
    public sealed class ItemUseEntry
    {
        /// <summary>未配置时用的占位行：1004 治疗药水 → <see cref="HealPlayerEffect.PlaceholderEffectId"/>。</summary>
        public static ItemUseEntry PlaceholderHealPotion()
            => new ItemUseEntry(1004, HealPlayerEffect.PlaceholderEffectId, 1);

        [Tooltip("道具 tbitem 主键（占位默认 1004 治疗药水，见 05_皮面具与道具.md:178-180 的道具总表）。")]
        [SerializeField] private int itemId;

        [Tooltip("效果标识，写成「域.动作」。占位默认 heal.player_hp（占位，等 05_皮面具与道具.md 拍板）。")]
        [SerializeField] private string effectId = string.Empty;

        [Tooltip("使用一次消耗几件。一次性道具靠它表达（05_皮面具与道具.md:118 R9）。")]
        [SerializeField, Min(1)] private int consumeCount = 1;

        /// <summary>序列化用。</summary>
        public ItemUseEntry()
        {
        }

        /// <summary>代码构造（占位默认值与测试用）。</summary>
        public ItemUseEntry(int itemId, string effectId, int consumeCount = 1)
        {
            this.itemId = itemId;
            this.effectId = effectId ?? string.Empty;
            this.consumeCount = consumeCount;
        }

        /// <summary>道具 id。</summary>
        public int ItemId => itemId;

        /// <summary>效果标识。</summary>
        public string EffectId => effectId ?? string.Empty;

        /// <summary>一次消耗几件。</summary>
        public int ConsumeCount => consumeCount;

        /// <summary>转成纯规则用的使用定义。</summary>
        public ItemUseSpec ToSpec() => new ItemUseSpec(itemId, EffectId, consumeCount);
    }
}
