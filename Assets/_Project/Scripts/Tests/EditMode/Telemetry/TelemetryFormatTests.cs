// 职责：守住格式化器这个纯函数——数字怎么写、字符串怎么转义、没有属性时不留空的 p 对象。
// 为什么新建：TelemetryServiceTests 是从服务这一端看输出，覆盖不到边界值（NaN、负数、控制字符、
//   超长整数）；而这些恰恰是最容易把一行 JSON 写坏、让分析脚本整段解析失败的地方。
//   单独一份纯函数测试跑得快，也更容易定位是格式的问题还是策略的问题。

using System.Text;
using Game.Core.Telemetry;
using NUnit.Framework;

namespace Game.Tests.EditMode.Telemetry
{
    /// <summary>TelemetryFormat 的 EditMode 测试。纯字符串处理，不碰 Unity 也不碰服务。</summary>
    public sealed class TelemetryFormatTests
    {
        private StringBuilder builder;

        [SetUp]
        public void SetUp()
        {
            builder = new StringBuilder();
        }

        [Test]
        public void AppendNumber_UsesDotAndTrimsTrailingZeros()
        {
            Assert.That(Number(0d), Is.EqualTo("0"));
            Assert.That(Number(1234d), Is.EqualTo("1234"));
            Assert.That(Number(0.5d), Is.EqualTo("0.5"));
            Assert.That(Number(16.6667d), Is.EqualTo("16.667"), "保留三位小数，四舍五入");
            Assert.That(Number(0.105d), Is.EqualTo("0.105"), "中间的 0 不能被吃掉");
            Assert.That(Number(-2.5d), Is.EqualTo("-2.5"));
        }

        [Test]
        public void AppendNumber_WithNaNOrInfinity_WritesZero()
        {
            // JSON 没有 NaN / Infinity 字面量，直接写出去会让整行解析失败
            Assert.That(Number(double.NaN), Is.EqualTo("0"));
            Assert.That(Number(double.PositiveInfinity), Is.EqualTo("0"));
            Assert.That(Number(double.NegativeInfinity), Is.EqualTo("0"));
        }

        [Test]
        public void AppendJsonString_EscapesQuotesBackslashAndControlChars()
        {
            TelemetryFormat.AppendJsonString(builder, "a\"b\\c\nd\te\u0007", false);

            Assert.That(builder.ToString(), Is.EqualTo("\"a\\\"b\\\\c\\nd\\te\\u0007\""));
        }

        [Test]
        public void AppendJsonString_WhenFlattening_ReplacesNewlinesAndDropsCarriageReturn()
        {
            TelemetryFormat.AppendJsonString(builder, "第一行\r\n\t第二行", true);

            string result = builder.ToString();
            Assert.That(result, Is.EqualTo("\"第一行" + TelemetryFormat.NewlineReplacement + " 第二行\""));
            Assert.That(result.Contains("\n"), Is.False);
            Assert.That(result.Contains("\r"), Is.False);
        }

        [Test]
        public void AppendJsonString_WithNull_WritesEmptyString()
        {
            TelemetryFormat.AppendJsonString(builder, null, false);

            Assert.That(builder.ToString(), Is.EqualTo("\"\""));
        }

        [Test]
        public void Write_WithoutProps_OmitsThePObject()
        {
            TelemetryProps none = default;
            TelemetryEvent e = new TelemetryEvent(TelemetryLevel.Info, TelemetryKeys.Boot, TelemetryKeys.BootEvents.Ready, 12L, 3, 45, in none);

            TelemetryFormat.Write(builder, in e);

            Assert.That(builder.ToString(), Is.EqualTo("[Game][T] I core.boot/ready | {\"t\":12,\"s\":3,\"f\":45}"));
        }

        [Test]
        public void Write_WithMixedProps_WritesFlatOneLevelJson()
        {
            TelemetryProps props = TelemetryProps.Of(("key", "a"), ("n", 2), ("ms", 1.5f), ("ok", true));
            TelemetryEvent e = new TelemetryEvent(TelemetryLevel.Warn, TelemetryKeys.Save, TelemetryKeys.SaveEvents.Write, 1L, 2, 3, in props);

            TelemetryFormat.Write(builder, in e);

            Assert.That(
                builder.ToString(),
                Is.EqualTo("[Game][T] W core.save/write | {\"t\":1,\"s\":2,\"f\":3,\"p\":{\"key\":\"a\",\"n\":2,\"ms\":1.5,\"ok\":true}}"));
        }

        [Test]
        public void Write_WithRawProps_UsesThemVerbatim()
        {
            TelemetryProps none = default;
            TelemetryEvent e = new TelemetryEvent(
                TelemetryLevel.Info,
                TelemetryKeys.Core,
                TelemetryKeys.CoreEvents.SessionStart,
                0L,
                0,
                0,
                in none,
                null,
                null,
                "\"sid\":\"7f3a9c21\",\"mem\":16384");

            TelemetryFormat.Write(builder, in e);

            Assert.That(
                builder.ToString(),
                Is.EqualTo("[Game][T] I core/session_start | {\"t\":0,\"s\":0,\"f\":0,\"p\":{\"sid\":\"7f3a9c21\",\"mem\":16384}}"));
        }

        [Test]
        public void LevelChar_MapsEachLevelToContractCharacter()
        {
            Assert.That(TelemetryFormat.LevelChar(TelemetryLevel.Debug), Is.EqualTo('D'));
            Assert.That(TelemetryFormat.LevelChar(TelemetryLevel.Info), Is.EqualTo('I'));
            Assert.That(TelemetryFormat.LevelChar(TelemetryLevel.Warn), Is.EqualTo('W'));
            Assert.That(TelemetryFormat.LevelChar(TelemetryLevel.Error), Is.EqualTo('E'));
        }

        private string Number(double value)
        {
            builder.Clear();
            TelemetryFormat.AppendNumber(builder, value);
            return builder.ToString();
        }
    }
}
