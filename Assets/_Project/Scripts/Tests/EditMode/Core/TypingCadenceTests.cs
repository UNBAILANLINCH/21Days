// 职责：锁定打字节奏——无标点时与旧的「累加预算取整」一致、标点后停顿、停顿为 0 退化、倍速下停顿同比缩短、连续标点只停一次、句末不停。
// 新建原因：TypingCadence 是独立的纯 C# 类型，按「被测类 + Tests」单独成文件。
using System;
using System.Collections.Generic;
using Game.Core.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    public sealed class TypingCadenceTests
    {
        // 基础速度 16 字 / 秒、帧长 1/32 秒：x1 每帧预算 0.5 字、x2 每帧 1 字，都是二进制精确值，避免浮点边界抖动。
        private const float Cps = 16f;
        private const float Dt = 1f / 32f;
        private const string Punctuation = "，。！？…；：、,.!?";

        [Test]
        public void Advance_WhenNoPunctuation_MatchesFloorOfAccumulatedBudget()
        {
            var cadence = new TypingCadence(Cps, 0.5f, Punctuation);
            const string text = "abcdefghij";
            float[] budgets = { 0.25f, 0.5f, 0.75f, 1.5f, 0.25f, 2f, 0.125f, 0.875f, 3f, 5f };
            float progress = 0f;
            int visible = 0;
            foreach (float budget in budgets)
            {
                progress += budget;
                visible = cadence.Advance(text, visible, budget);
                Assert.That(visible, Is.EqualTo(Math.Min(text.Length, (int)progress)), $"累计预算 {progress}");
            }
        }

        [Test]
        public void Advance_AfterPunctuation_PausesForPauseSecondsBeforeNextCharacter()
        {
            // x1：4 个字 0.25 s + 停顿 0.5 s = 0.75 s = 24 帧；没有停顿时 8 帧。
            Assert.That(FramesToFinish("a，bc", 0.5f, 1f), Is.EqualTo(24));
            Assert.That(FramesToFinish("abcd", 0.5f, 1f), Is.EqualTo(8));
        }

        [Test]
        public void Advance_AfterPunctuation_HoldsVisibleCountDuringPause()
        {
            var cadence = new TypingCadence(Cps, 0.5f, Punctuation);
            int visible = cadence.Advance("a，bc", 0, 2f);
            Assert.That(visible, Is.EqualTo(2), "打出「a，」");
            Assert.That(cadence.Pausing, Is.True);
            // 停顿 = 0.5 s × 16 = 8 字预算：再给 7.5 仍停着。
            visible = cadence.Advance("a，bc", visible, 7.5f);
            Assert.That(visible, Is.EqualTo(2));
            visible = cadence.Advance("a，bc", visible, 1.5f);
            Assert.That(visible, Is.EqualTo(3), "停顿耗完后剩余预算继续出字");
        }

        [Test]
        public void Advance_WhenPauseIsZero_BehavesLikeNoPunctuation()
        {
            Assert.That(FramesToFinish("a，bc", 0f, 1f), Is.EqualTo(FramesToFinish("abcd", 0f, 1f)));
        }

        [Test]
        public void Advance_WhenPunctuationCharsEmpty_NeverPauses()
        {
            var cadence = new TypingCadence(Cps, 0.5f, null);
            Assert.That(cadence.Advance("a，bc", 0, 4f), Is.EqualTo(4));
        }

        /// <summary>
        /// 倍速语义：调用方给的预算已乘倍速（基础速度 × 倍速 × Δt），停顿按「停顿秒数 × 基础速度」个字符预算扣，
        /// 所以 x2 下停顿的实际秒数减半——与自动推进间隔 autoAdvanceSeconds / 倍速 一致。
        /// </summary>
        [Test]
        public void Advance_WhenDoubleSpeed_PauseAlsoHalves()
        {
            int x1 = FramesToFinish("a，bc", 0.5f, 1f);
            int x2 = FramesToFinish("a，bc", 0.5f, 2f);
            Assert.That(x1, Is.EqualTo(24));
            Assert.That(x2, Is.EqualTo(12), "x2：4 字 0.125 s + 停顿 0.25 s = 12 帧");
        }

        [Test]
        public void Advance_WhenConsecutivePunctuation_PausesOnceAfterRun()
        {
            // 「a……b」共 4 个字（… 是单字符）：4 字 + 一次停顿 8 = 12 字预算；逐个停会是 4 + 8 × 2 = 20。
            var cadence = new TypingCadence(Cps, 0.5f, Punctuation);
            int visible = cadence.Advance("a……b", 0, 11.5f);
            Assert.That(visible, Is.EqualTo(3), "11.5 预算：打出「a……」，停顿还差 0.5");
            cadence.Reset();
            Assert.That(cadence.Advance("a……b", 0, 12f), Is.EqualTo(4));
        }

        [Test]
        public void Advance_WhenPunctuationEndsLine_DoesNotPause()
        {
            var cadence = new TypingCadence(Cps, 0.5f, Punctuation);
            Assert.That(cadence.Advance("ab。", 0, 3f), Is.EqualTo(3));
            Assert.That(cadence.Pausing, Is.False);
        }

        [Test]
        public void Advance_WhenBudgetLargeInOneFrame_ConsumesPauseAndContinues()
        {
            var cadence = new TypingCadence(Cps, 0.5f, Punctuation);
            Assert.That(cadence.Advance("a，b", 0, 100f), Is.EqualTo(3));
        }

        [Test]
        public void Reset_ClearsCarryAndPause()
        {
            var cadence = new TypingCadence(Cps, 0.5f, Punctuation);
            cadence.Advance("a，bc", 0, 2.75f);
            cadence.Reset();
            Assert.That(cadence.Pausing, Is.False);
            Assert.That(cadence.Advance("abcd", 0, 0.5f), Is.EqualTo(0), "零头预算已清");
        }

        [Test]
        public void Constructor_WhenArgumentsInvalid_Throws()
        {
            Assert.Throws<ArgumentException>(() => new TypingCadence(0f, 0.1f, Punctuation));
            Assert.Throws<ArgumentException>(() => new TypingCadence(10f, -0.1f, Punctuation));
        }

        [Test]
        public void Constructor_WithPauseAndChars_UsesThem()
        {
            var cadence = new TypingCadence(Cps, 0.5f, "，");
            Assert.That(cadence.Advance("a，b", 0, 9.5f), Is.EqualTo(2), "2 字 + 停顿 8 未耗完（差 0.5）");
        }

        private static int FramesToFinish(string text, float pauseSeconds, float speed)
        {
            var cadence = new TypingCadence(Cps, pauseSeconds, Punctuation);
            var trace = new List<int>();
            int visible = 0;
            for (int frame = 1; frame <= 1000; frame++)
            {
                visible = cadence.Advance(text, visible, Cps * speed * Dt);
                trace.Add(visible);
                if (visible >= text.Length) return frame;
            }
            Assert.Fail("1000 帧内没打完：" + string.Join(",", trace));
            return -1;
        }
    }
}
