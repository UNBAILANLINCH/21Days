// 职责：把一条 TelemetryEvent 写成契约那一行文本——行首、JSON 转义、数字格式全在这里。
// 为什么新建：格式是**两侧共用的唯一契约**（analyze.py 按同一条正则解析），必须是一个纯函数：
//   给一条事件就能拿到一行文本，测试可以直接拿正则比对，不用先跑起服务、也不碰 Unity。
//   没有塞进 TelemetryService.cs：那里管策略（开关、过滤、限流、序号、会话生命周期），
//   策略会随需求改，格式不许改——两件事放一起早晚会被顺手改坏。
//   也没有塞进 TelemetryEvent.cs：那里是数据结构，让它认识 StringBuilder 等于把数据和输出绑死。

using System.Text;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点行格式化器（契约的可执行版本，见 docs/telemetry.md 第 1 节）：
    /// <code>[Game][T] I core.flow/state_enter | {"t":1234,"s":42,"f":1024,"p":{"from":"BootState"}}</code>
    /// <para>
    /// 全程只往调用方给的 <see cref="StringBuilder"/> 里追加，自己不分配：数字自己按位拼（顺带绕开
    /// <c>double.ToString()</c> 的文化差异——德语区的小数点是逗号，那会直接把 JSON 写坏），
    /// 字符串按 JSON 规则逐字符转义。唯一一次分配发生在调用方 <c>ToString()</c> 取走成品行的时候，
    /// 因为 <c>Debug.Log</c> 只吃 string，这一次躲不掉。
    /// </para>
    /// </summary>
    public static class TelemetryFormat
    {
        /// <summary>行首标记。分析脚本靠它挑出埋点行，桥接器靠它认出「这条是我自己打的」防递归。</summary>
        public const string LinePrefix = "[Game][T] ";

        /// <summary>堆栈里的换行替换成它。日志行不能跨行——跨行就没法按行解析。</summary>
        public const string NewlineReplacement = " \u23CE ";

        /// <summary>按位拼整数时能安全承载的上限（2^53 附近），超了先夹住再输出。</summary>
        private const double MaxSafeNumber = 9.0e15;

        private const string HexDigits = "0123456789abcdef";

        /// <summary>级别对应的行首字符。</summary>
        public static char LevelChar(TelemetryLevel level)
        {
            switch (level)
            {
                case TelemetryLevel.Debug: return 'D';
                case TelemetryLevel.Warn: return 'W';
                case TelemetryLevel.Error: return 'E';
                default: return 'I';
            }
        }

        /// <summary>
        /// 把一条事件写成一整行（不含换行符）。**不会先 Clear**，清空由调用方决定——
        /// 服务复用同一个 StringBuilder，清空时机跟着它的生命周期走。
        /// </summary>
        public static void Write(StringBuilder builder, in TelemetryEvent e)
        {
            if (builder == null)
            {
                return;
            }

            builder.Append(LinePrefix);
            builder.Append(LevelChar(e.Level));
            builder.Append(' ');
            builder.Append(e.Module);
            builder.Append('/');
            builder.Append(e.Event);
            builder.Append(" | {\"t\":");
            builder.Append(e.TimeMs);
            builder.Append(",\"s\":");
            builder.Append(e.Sequence);
            builder.Append(",\"f\":");
            builder.Append(e.Frame);

            AppendProps(builder, in e);

            if (e.Error != null)
            {
                builder.Append(",\"err\":");
                AppendJsonString(builder, e.Error, true);
            }

            if (e.Stack != null)
            {
                builder.Append(",\"st\":");
                AppendJsonString(builder, e.Stack, true);
            }

            builder.Append('}');
        }

        /// <summary>把一个属性值按 JSON 规则写出去（数字 / 字符串 / 布尔）。</summary>
        public static void AppendValue(StringBuilder builder, in PropValue value)
        {
            switch (value.Kind)
            {
                case PropKind.Integer:
                    builder.Append(value.Integer);
                    break;
                case PropKind.Float:
                    AppendNumber(builder, value.Number);
                    break;
                case PropKind.Bool:
                    builder.Append(value.Bool ? "true" : "false");
                    break;
                case PropKind.String:
                    AppendJsonString(builder, value.Text, false);
                    break;
                default:
                    builder.Append("null");
                    break;
            }
        }

        /// <summary>
        /// 写一个 JSON 字符串（含首尾引号）。<paramref name="flattenNewlines"/> 为 true 时
        /// 换行替换成 <see cref="NewlineReplacement"/>、制表符换成空格（给 err / st 用，人读更顺）；
        /// 为 false 时换行走标准的 <c>\n</c> 转义——两种走法都不会让这一行真的断成两行。
        /// </summary>
        public static void AppendJsonString(StringBuilder builder, string value, bool flattenNewlines)
        {
            builder.Append('"');
            if (value == null)
            {
                builder.Append('"');
                return;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append(flattenNewlines ? NewlineReplacement : "\\n");
                        break;
                    case '\r':
                        // 压平时直接丢掉：Windows 换行是 \r\n，留着会在 ⏎ 前面多出一个空洞
                        if (!flattenNewlines)
                        {
                            builder.Append("\\r");
                        }

                        break;
                    case '\t':
                        builder.Append(flattenNewlines ? " " : "\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            AppendUnicodeEscape(builder, c);
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        /// <summary>
        /// 写一个浮点数，最多三位小数，恒定用「.」做小数点。
        /// 自己按位拼而不用 <c>ToString()</c>：一来不分配，二来不受当前区域设置影响。
        /// NaN / 无穷按 0 输出——JSON 没有这两个字面量，写出去分析脚本会整行解析失败。
        /// </summary>
        public static void AppendNumber(StringBuilder builder, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                builder.Append('0');
                return;
            }

            bool negative = value < 0d;
            if (negative)
            {
                value = -value;
            }

            if (value > MaxSafeNumber)
            {
                value = MaxSafeNumber;
            }

            long scaled = (long)((value * 1000d) + 0.5d);
            if (negative && scaled != 0L)
            {
                builder.Append('-');
            }

            builder.Append(scaled / 1000L);

            int fraction = (int)(scaled % 1000L);
            if (fraction == 0)
            {
                return;
            }

            builder.Append('.');
            int hundreds = fraction / 100;
            int tens = (fraction / 10) % 10;
            int ones = fraction % 10;
            builder.Append((char)('0' + hundreds));
            if (tens == 0 && ones == 0)
            {
                return;
            }

            builder.Append((char)('0' + tens));
            if (ones != 0)
            {
                builder.Append((char)('0' + ones));
            }
        }

        private static void AppendProps(StringBuilder builder, in TelemetryEvent e)
        {
            if (e.RawProps != null)
            {
                builder.Append(",\"p\":{");
                builder.Append(e.RawProps);
                builder.Append('}');
                return;
            }

            TelemetryProps props = e.Props;
            if (props.Count <= 0)
            {
                return;
            }

            bool opened = false;
            for (int i = 0; i < props.Count; i++)
            {
                string key = props.KeyAt(i);
                if (key == null)
                {
                    continue;
                }

                builder.Append(opened ? "," : ",\"p\":{");
                opened = true;
                AppendJsonString(builder, key, false);
                builder.Append(':');
                PropValue value = props.ValueAt(i);
                AppendValue(builder, in value);
            }

            if (opened)
            {
                builder.Append('}');
            }
        }

        private static void AppendUnicodeEscape(StringBuilder builder, char c)
        {
            builder.Append("\\u");
            builder.Append(HexDigits[(c >> 12) & 0xF]);
            builder.Append(HexDigits[(c >> 8) & 0xF]);
            builder.Append(HexDigits[(c >> 4) & 0xF]);
            builder.Append(HexDigits[c & 0xF]);
        }
    }
}
