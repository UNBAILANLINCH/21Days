// 职责：把「战斗怎么结束」定成有限枚举 + 可选的 BOSS 形态，替代散落的字符串结果码。
// 为什么新建：NarrativeIntent.Result 是自由字符串，战斗侧写什么就进什么，
// 表里的出口拼错只能等到运行时 Apply 返回 false 才发现；NarrativeContent.Stage 的职责是阶段结构，不该再挂结果词表。
using System;

namespace Game.Narrative
{
    /// <summary>
    /// 战斗阶段的结果词汇。表里 <c>battleResults</c> 与出口的 result 键都写它的代码，
    /// 翻译期即校验，未知词直接拒绝——不用自由字符串是 roadmap C5 的明确要求。
    /// </summary>
    public static class BattleOutcome
    {
        /// <summary>本体被击倒：聚光灯里挨打的终点是倒地不是死亡（特征见 roadmap §6 风险 3）。</summary>
        public const string Downed = "Downed";

        /// <summary>身份暴露：违反身份规则后被看穿（阶段六怀疑度、阶段三账簿同属这条后果链）。</summary>
        public const string Exposed = "Exposed";

        /// <summary>BOSS 换形态未结束：钱塘君「血量归零时转换阶段」这类，战斗还在继续。</summary>
        public const string BossPhaseChanged = "BossPhaseChanged";

        /// <summary>战斗以胜利收束（小怪被清、BOSS 被收押）。</summary>
        public const string Victory = "Victory";
    }

    /// <summary>
    /// 一次战斗结果的强类型表示。代码只认 <typeparamref name="Kind"/>（枚举字面量，取值封闭）；
    /// <see cref="Variant"/> 是 BOSS 换到哪个形态，属于内容侧可增的名字，不是行为枚举，
    /// 但缺了它 <see cref="BattleOutcome.BossPhaseChanged"/> 无法表达「转到哪个形态」，所以非空强制校验。
    /// </summary>
    public readonly struct BattleResult : IEquatable<BattleResult>
    {
        public const char VariantSeparator = ':';

        public BattleResult(string kind, string variant = null)
        {
            if (!IsDefinedKind(kind))
                throw new ArgumentException("战斗结果必须是 " + KnownKinds + " 之一：" + (kind ?? "<null>"), nameof(kind));
            if (!string.IsNullOrEmpty(variant) && kind != BattleOutcome.BossPhaseChanged)
                throw new ArgumentException("只有 " + BattleOutcome.BossPhaseChanged + " 可以带 BOSS 形态：" + kind, nameof(variant));
            if (kind == BattleOutcome.BossPhaseChanged && string.IsNullOrWhiteSpace(variant))
                throw new ArgumentException(BattleOutcome.BossPhaseChanged + " 必须写明转到哪个形态", nameof(variant));
            Kind = kind;
            Variant = variant ?? string.Empty;
        }

        /// <summary>行为枚举：只取 <see cref="BattleOutcome"/> 的四个常量之一。</summary>
        public string Kind { get; }

        /// <summary>BOSS 形态名；非 <see cref="BattleOutcome.BossPhaseChanged"/> 时为空串。</summary>
        public string Variant { get; }

        /// <summary>阶段出口键；<c>BattleOutcome.cs</c> 是它唯一的产出方，别再手拼。</summary>
        public string ExitKey => string.IsNullOrEmpty(Variant) ? Kind : Kind + VariantSeparator + Variant;

        /// <summary>已知行为枚举，用于报错信息，避免两处手写同一串常量。</summary>
        private static string KnownKinds => string.Join(" / ", BattleOutcome.Downed, BattleOutcome.Exposed,
            BattleOutcome.BossPhaseChanged, BattleOutcome.Victory);

        private static bool IsDefinedKind(string kind) =>
            kind == BattleOutcome.Downed || kind == BattleOutcome.Exposed ||
            kind == BattleOutcome.BossPhaseChanged || kind == BattleOutcome.Victory;

        /// <summary>严格解析：非法键抛 <see cref="ArgumentException"/>，内容错误不静默吞掉。</summary>
        public static BattleResult Parse(string key)
        {
            if (TryParse(key, out BattleResult result)) return result;
            throw new ArgumentException("未知战斗结果：" + (key ?? "<null>"), nameof(key));
        }

        /// <summary>宽松解析：只认 <c>Kind</c> 或 <c>Kind:形态</c>，形态为空（如 <c>BossPhaseChanged:</c>）视为非法。</summary>
        public static bool TryParse(string key, out BattleResult result)
        {
            result = default;
            if (string.IsNullOrEmpty(key)) return false;
            int separator = key.IndexOf(VariantSeparator);
            string kind = separator < 0 ? key : key.Substring(0, separator);
            string variant = separator < 0 ? null : key.Substring(separator + 1);
            if (!IsDefinedKind(kind)) return false;
            if (variant == null)
            {
                if (kind == BattleOutcome.BossPhaseChanged) return false;
            }
            else if (kind != BattleOutcome.BossPhaseChanged || string.IsNullOrWhiteSpace(variant)) return false;
            result = new BattleResult(kind, variant);
            return true;
        }

        public bool Equals(BattleResult other) => string.Equals(ExitKey, other.ExitKey, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is BattleResult other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ExitKey);
        public override string ToString() => ExitKey;
    }
}
