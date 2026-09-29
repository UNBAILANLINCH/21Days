// 职责：锁定打字中连点补全计数——窗口内满次返回 true 并清零、间隔恰等于窗口仍算连点、超窗从 1 重计、Reset 清零、
//   次数为 1 时每下都补全、非法参数抛异常。对白 / 演出按阶段的用法分别由 DialoguePlaybackPolicyTests / PerformanceServiceWorldTests 钉住。
// 新建原因：TapRevealCounter 是独立的纯 C# 类型，按「被测类 + Tests」单独成文件（同 TypingCadenceTests）。
using System;
using Game.Core.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    public sealed class TapRevealCounterTests
    {
        // 窗口 0.5 秒、三连点：与 DialogueConfig / PerformanceConfig 的默认值一致。时间都取二进制可精确表示的值，避免浮点边界抖动。
        private TapRevealCounter counter;

        [SetUp]
        public void SetUp() => counter = new TapRevealCounter(3, 0.5f);

        [Test]
        public void RegisterTypingTap_ThreeTapsInWindow_RevealsOnThirdAndRestarts()
        {
            Assert.That(counter.RegisterTypingTap(0f), Is.False, "第一下不补全");
            Assert.That(counter.RegisterTypingTap(0.25f), Is.False, "第二下不补全");
            Assert.That(counter.RegisterTypingTap(0.375f), Is.True, "窗口内第三下补全");
            Assert.That(counter.Count, Is.EqualTo(0), "补全后清零");
            Assert.That(counter.RegisterTypingTap(0.5f), Is.False, "补全后从 1 重新计");
        }

        [Test]
        public void RegisterTypingTap_GapEqualToWindow_StillCounts()
        {
            counter.RegisterTypingTap(0f);
            counter.RegisterTypingTap(0.5f);
            Assert.That(counter.RegisterTypingTap(1f), Is.True, "相邻间隔恰等于窗口仍算连点");
        }

        [Test]
        public void RegisterTypingTap_TapOutsideWindow_RestartsCountFromOne()
        {
            counter.RegisterTypingTap(0f);
            counter.RegisterTypingTap(0.25f);
            Assert.That(counter.RegisterTypingTap(2f), Is.False, "超窗的这一下从 1 重新计");
            Assert.That(counter.Count, Is.EqualTo(1));
            Assert.That(counter.RegisterTypingTap(2.125f), Is.False);
            Assert.That(counter.RegisterTypingTap(2.25f), Is.True);
        }

        [Test]
        public void Reset_AfterTwoTaps_NextTapCountsFromOne()
        {
            counter.RegisterTypingTap(0f);
            counter.RegisterTypingTap(0.125f);
            counter.Reset();
            Assert.That(counter.Count, Is.EqualTo(0));
            Assert.That(counter.RegisterTypingTap(0.25f), Is.False, "清零后窗口内再点一下只算第 1 下");
            Assert.That(counter.RegisterTypingTap(0.375f), Is.False);
            Assert.That(counter.RegisterTypingTap(0.5f), Is.True);
        }

        [Test]
        public void RegisterTypingTap_WhenCountIsOne_EveryTapReveals()
        {
            var single = new TapRevealCounter(1, 0.5f);
            Assert.That(single.RegisterTypingTap(0f), Is.True);
            Assert.That(single.RegisterTypingTap(10f), Is.True);
        }

        [Test]
        public void Constructor_WhenArgumentsInvalid_Throws()
        {
            Assert.Throws<ArgumentException>(() => new TapRevealCounter(0, 0.5f));
            Assert.Throws<ArgumentException>(() => new TapRevealCounter(-1, 0.5f));
            Assert.Throws<ArgumentException>(() => new TapRevealCounter(3, 0f));
            Assert.Throws<ArgumentException>(() => new TapRevealCounter(3, -0.5f));
            Assert.Throws<ArgumentException>(() => new TapRevealCounter(3, float.NaN));
            Assert.DoesNotThrow(() => new TapRevealCounter(1, 0.05f));
        }

        [Test]
        public void Constructor_KeepsParameters()
        {
            Assert.That(counter.RevealTapCount, Is.EqualTo(3));
            Assert.That(counter.TapWindowSeconds, Is.EqualTo(0.5f));
            Assert.That(counter.Count, Is.EqualTo(0));
        }
    }
}
