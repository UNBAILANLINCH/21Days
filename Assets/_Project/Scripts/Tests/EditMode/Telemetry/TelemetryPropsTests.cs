// 职责：守住属性容器的往返——六种值类型进去是什么，出来还得是什么，槽位与顺序不许乱。
// 为什么新建：PropValue 是手写的判别联合（int / long 走整数、float / double 走浮点、bool 存在 double 里），
//   这类手写联合一旦某个隐式转换挂错了分支，编译器不会吭声，只会让日志里那一列值悄悄变成 0 或 true。
//   没有并进 TelemetryServiceTests：那份测的是服务策略，这份测的是值类型自身。

using Game.Core.Telemetry;
using NUnit.Framework;

namespace Game.Tests.EditMode.Telemetry
{
    /// <summary>TelemetryProps / PropValue 的 EditMode 测试。纯值类型，不碰 Unity。</summary>
    public sealed class TelemetryPropsTests
    {
        [Test]
        public void Empty_HasNoSlots()
        {
            TelemetryProps props = TelemetryProps.Empty;

            Assert.That(props.Count, Is.EqualTo(0));
            Assert.That(props.KeyAt(0), Is.Null);
            Assert.That(props.ValueAt(0).Kind, Is.EqualTo(PropKind.None));
        }

        [Test]
        public void Of_WithIntAndLong_RoundTripsAsInteger()
        {
            TelemetryProps props = TelemetryProps.Of(("id", 1001), ("big", 4000000000L));

            Assert.That(props.Count, Is.EqualTo(2));
            Assert.That(props.KeyAt(0), Is.EqualTo("id"));
            Assert.That(props.ValueAt(0).Kind, Is.EqualTo(PropKind.Integer));
            Assert.That(props.ValueAt(0).Integer, Is.EqualTo(1001L));
            Assert.That(props.ValueAt(1).Kind, Is.EqualTo(PropKind.Integer));
            Assert.That(props.ValueAt(1).Integer, Is.EqualTo(4000000000L));
        }

        [Test]
        public void Of_WithFloatAndDouble_RoundTripsAsFloat()
        {
            TelemetryProps props = TelemetryProps.Of(("a", 0.5f), ("b", 2.25d));

            Assert.That(props.ValueAt(0).Kind, Is.EqualTo(PropKind.Float));
            Assert.That(props.ValueAt(0).Number, Is.EqualTo(0.5d));
            Assert.That(props.ValueAt(1).Kind, Is.EqualTo(PropKind.Float));
            Assert.That(props.ValueAt(1).Number, Is.EqualTo(2.25d));
        }

        [Test]
        public void Of_WithStringAndBool_RoundTripsWithoutMixingSlots()
        {
            TelemetryProps props = TelemetryProps.Of(("key", "SampleScene_Game"), ("ok", true), ("bad", false));

            Assert.That(props.Count, Is.EqualTo(3));
            Assert.That(props.ValueAt(0).Kind, Is.EqualTo(PropKind.String));
            Assert.That(props.ValueAt(0).Text, Is.EqualTo("SampleScene_Game"));
            Assert.That(props.ValueAt(1).Kind, Is.EqualTo(PropKind.Bool));
            Assert.That(props.ValueAt(1).Bool, Is.True);
            Assert.That(props.ValueAt(2).Kind, Is.EqualTo(PropKind.Bool));
            Assert.That(props.ValueAt(2).Bool, Is.False);
        }

        [Test]
        public void Of_WithFourProps_KeepsOrderAndFillsAllSlots()
        {
            TelemetryProps props = TelemetryProps.Of(("a", 1), ("b", 2), ("c", 3), ("d", 4));

            Assert.That(props.Count, Is.EqualTo(TelemetryProps.Capacity));
            for (int i = 0; i < props.Count; i++)
            {
                Assert.That(props.KeyAt(i), Is.EqualTo(((char)('a' + i)).ToString()));
                Assert.That(props.ValueAt(i).Integer, Is.EqualTo(i + 1));
            }

            Assert.That(props.KeyAt(TelemetryProps.Capacity), Is.Null, "越界要返回空，不能抛");
        }
    }
}
