// 职责：身份 id 的强类型与格式约束——id 会拼进剧情事实键 `identity.<id>`（字典 §4.1），
//   格式必须在类型里定死，否则一个大写带空格的 id 会拼出一个永不命中的键，而且完全静默。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里唯一的跨模块标识是 Narrative 的 `StoryFlag` 裸 `string`，没有任何校验点；
//      Quest 用 `int` 自增 id，语义完全不同。拿裸 string 当身份 id，格式漂移无处可拦。
//   2. 扩展不行：不能给 `string` 加约束，也不该往 `EncounterContext` 加枚举值——
//      `ai-docs/docs/story-facts.md` §2 的 C-1/C-2 已把表结构与「只有布尔谓词」定死，
//      身份事实一律走点分键。

using System;

namespace Game.Identity
{
    /// <summary>
    /// 身份 id（被借用的角色标识）。取值形如 <c>yuezheng</c>：只允许小写字母、数字、下划线，
    /// <b>不含点号</b>——点号是事实键的层级分隔符（<c>ai-docs/docs/story-facts.md</c> §3.2），
    /// id 里带点会把 <c>identity.&lt;id&gt;</c> 顶成三段、被校验器当成档位键。
    /// <para>
    /// <see cref="None"/> 表示<b>本体</b>（没有借用任何身份，见 <c>docs/design/features-spotlight/01_换皮与附身.md:24</c>
    /// 术语表「本体」行），它是正常状态而不是错误值；格式不合法的字符串则由
    /// <see cref="From"/> 直接抛异常、由 <see cref="TryParse"/> 返回 false——两者行为都写在这里，不靠调用方自觉。
    /// </para>
    /// </summary>
    public readonly struct IdentityId : IEquatable<IdentityId>
    {
        /// <summary>id 的字符数上限。事实键会进存档与配置表，给一个上限免得有人把一句话当 id。</summary>
        public const int MaxLength = 32;

        /// <summary>本体：没有借用任何身份。</summary>
        public static readonly IdentityId None = default;

        private readonly string value;

        private IdentityId(string value)
        {
            this.value = value;
        }

        /// <summary>id 文本；本体为空字符串（不是 null，调用方不用判空）。</summary>
        public string Value => value ?? string.Empty;

        /// <summary>是否是一个真实的身份（本体为 false）。</summary>
        public bool IsValid => !string.IsNullOrEmpty(value);

        /// <summary>
        /// 从文本构造。<paramref name="raw"/> 为 null / 空 / 全空白时返回 <see cref="None"/>（本体），
        /// 格式不合法（大写、点号、空格、中文、数字开头、超长）时抛 <see cref="FormatException"/>——
        /// 内容侧写错 id 要在接线期就炸，不能等到条件永远为假时才发现。
        /// </summary>
        public static IdentityId From(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return None;
            }

            if (!IsWellFormed(raw))
            {
                throw new FormatException(
                    $"身份 id 格式不合法：\"{raw}\"。只允许小写字母、数字、下划线，首字符必须是字母，"
                    + $"长度不超过 {MaxLength}，且不含点号（点号是剧情事实键的层级分隔符，见 ai-docs/docs/story-facts.md §3.2）。");
            }

            return new IdentityId(raw);
        }

        /// <summary>宽松版：不是合法身份 id 时返回 false，并把 <paramref name="id"/> 置为 <see cref="None"/>。</summary>
        public static bool TryParse(string raw, out IdentityId id)
        {
            if (string.IsNullOrWhiteSpace(raw) || !IsWellFormed(raw))
            {
                id = None;
                return false;
            }

            id = new IdentityId(raw);
            return true;
        }

        /// <summary>文本是否符合身份 id 的格式要求（空串不算合法，空串是「没有 id」）。</summary>
        public static bool IsWellFormed(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength)
            {
                return false;
            }

            if (raw[0] < 'a' || raw[0] > 'z')
            {
                return false;
            }

            for (int i = 1; i < raw.Length; i++)
            {
                char c = raw[i];
                bool allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        public bool Equals(IdentityId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is IdentityId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value;

        public static bool operator ==(IdentityId left, IdentityId right) => left.Equals(right);

        public static bool operator !=(IdentityId left, IdentityId right) => !left.Equals(right);
    }
}
