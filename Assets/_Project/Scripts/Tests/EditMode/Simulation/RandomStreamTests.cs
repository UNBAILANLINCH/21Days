// 职责：钉住确定性随机的四条命根子——同种子同序列、State 存取无损往返、logic 与 view 流互不干扰、
//   Range / Value01 的边界与非法入参。
// 为什么新建：Core/Simulation/ 的随机层一条测试都没有，而它错了不会抛异常——
//   只会让某次重放在第几万个 tick 上悄悄给出另一个暴击结果，事后根本无从追。
//   为什么和 SimulationRunnerTests 分开：被测类不同（XorShiftRandomStream / RandomService），
//   按「测试类 = <被测类>Tests」的约定就该各占一个文件。

using System;
using System.Collections.Generic;
using Game.Core.Simulation;
using NUnit.Framework;

namespace Game.Tests.EditMode.Simulation
{
    /// <summary>
    /// <see cref="XorShiftRandomStream"/> 与 <see cref="RandomService"/> 的 EditMode 测试。
    /// 全程只用固定种子，不碰 <c>UnityEngine.Random</c>、不碰时间，因此完全确定性。
    /// </summary>
    public sealed class RandomStreamTests
    {
        /// <summary>测试用的固定主种子。取一个高低位都有内容的值，免得低熵种子掩盖问题。</summary>
        private const ulong Seed = 0x21DA25F00D1234UL;

        /// <summary>每次比对抽多少个数。64 个足够让「只对了头几个」的实现露馅。</summary>
        private const int SampleCount = 64;

        private const string LogicStream = RandomService.LogicPrefix + "damage";
        private const string ViewStream = RandomService.ViewPrefix + "hit_shake";

        [Test]
        public void NextUInt_WithTheSameSeed_ProducesTheSameSequence()
        {
            uint[] first = Draw(new XorShiftRandomStream(Seed), SampleCount);
            uint[] second = Draw(new XorShiftRandomStream(Seed), SampleCount);

            Assert.That(second, Is.EqualTo(first), "同一种子的两个独立实例必须逐个相同");

            // 非空判据：发生器坏成「恒定吐同一个值」时，上面那条断言照样通过，所以这里挡一道
            Assert.That(
                CountDistinct(first),
                Is.EqualTo(SampleCount),
                "序列里出现了重复值——发生器要么退化了，要么根本没在推进状态");
        }

        [Test]
        public void NextUInt_WithDifferentSeeds_ProducesDifferentSequences()
        {
            uint[] fromSeed = Draw(new XorShiftRandomStream(Seed), SampleCount);
            uint[] fromOtherSeed = Draw(new XorShiftRandomStream(Seed + 1UL), SampleCount);

            Assert.That(fromOtherSeed, Is.Not.EqualTo(fromSeed), "种子差一位就该是另一条序列（构造时过了一遍雪崩）");
            Assert.That(fromOtherSeed[0], Is.Not.EqualTo(fromSeed[0]), "连第一个数都一样的话，雪崩没起作用");
        }

        [Test]
        public void State_WhenRestored_ResumesTheExactSameSequence()
        {
            XorShiftRandomStream stream = new XorShiftRandomStream(Seed);

            // 先抽掉几个再取快照：从初始状态取快照测不出「实现偷偷从头开始」这类错
            Draw(stream, 7);
            ulong snapshot = stream.State;
            Assert.That(snapshot, Is.Not.EqualTo(0UL), "正常运行中状态不可能是 0");

            uint[] afterSnapshot = Draw(stream, SampleCount);

            stream.State = snapshot;
            uint[] afterRestore = Draw(stream, SampleCount);

            Assert.That(
                afterRestore,
                Is.EqualTo(afterSnapshot),
                "State 设回去之后，后续序列必须与「压根没中断过」逐个相同——快照续跑全靠这一条");
            Assert.That(CountDistinct(afterSnapshot), Is.EqualTo(SampleCount), "对照序列本身得是变化的，否则断言是废话");
        }

        [Test]
        public void Stream_WhenViewStreamIsDrawnFromHeavily_LeavesTheLogicStreamUntouched()
        {
            // 对照组：同一个主种子，全程只抽 logic 流
            RandomService baselineService = new RandomService(Seed);
            uint[] expected = Draw(baselineService.Stream(LogicStream), SampleCount);

            // 实验组：每抽一个逻辑数，就在表现流上狂抽 16 个（模拟「这一 tick 多播了一堆特效」）
            RandomService mixedService = new RandomService(Seed);
            IRandomStream logic = mixedService.Stream(LogicStream);
            IRandomStream view = mixedService.Stream(ViewStream);
            uint[] actual = new uint[SampleCount];
            for (int i = 0; i < SampleCount; i++)
            {
                actual[i] = logic.NextUInt();
                Draw(view, 16);
            }

            Assert.That(
                actual,
                Is.EqualTo(expected),
                "表现流多抽少抽都不许让逻辑流错位——两条流共用序列的话，一个特效就能把暴击判定挪走");
            Assert.That(CountDistinct(expected), Is.EqualTo(SampleCount), "对照序列本身得是变化的，否则断言是废话");
        }

        [Test]
        public void Stream_WithTheSameName_ReturnsTheSameInstanceAndKeepsAdvancing()
        {
            RandomService service = new RandomService(Seed);
            IRandomStream first = service.Stream(LogicStream);
            IRandomStream second = service.Stream(LogicStream);

            Assert.That(second, Is.SameAs(first), "同名必须返回同一个实例");

            // 语义判据：连着三次「取流 → 抽一个数」，结果应等于一条流连抽三个，
            // 而不是三个一模一样的数（每次 Stream() 新建就会是后者）
            RandomService reference = new RandomService(Seed);
            uint[] expected = Draw(reference.Stream(LogicStream), 3);

            RandomService reopened = new RandomService(Seed);
            uint[] actual =
            {
                reopened.Stream(LogicStream).NextUInt(),
                reopened.Stream(LogicStream).NextUInt(),
                reopened.Stream(LogicStream).NextUInt(),
            };

            Assert.That(actual, Is.EqualTo(expected), "每次 Stream() 都新建的话，这条流就永远只吐第一个数");
        }

        [Test]
        public void Stream_WithDifferentNames_ProducesDifferentSequences()
        {
            RandomService service = new RandomService(Seed);
            uint[] logic = Draw(service.Stream(LogicStream), SampleCount);
            uint[] view = Draw(service.Stream(ViewStream), SampleCount);

            Assert.That(view, Is.Not.EqualTo(logic), "不同流名必须派生出不同的序列");

            // 不只是「整体不相等」：逐位都不该撞上（撞一位的概率约 64 × 2^-32）
            int collisions = 0;
            for (int i = 0; i < SampleCount; i++)
            {
                if (logic[i] == view[i])
                {
                    collisions++;
                }
            }

            Assert.That(collisions, Is.EqualTo(0), "两条流在同一位上取到了相同的值，派生策略可能没把流名吃进去");
        }

        [Test]
        public void Range_WhenMinIsNotLessThanMax_ThrowsArgumentOutOfRangeException()
        {
            IRandomStream stream = new XorShiftRandomStream(Seed);

            Assert.That(() => stream.Range(5, 5), Throws.TypeOf<ArgumentOutOfRangeException>(), "空区间要拒绝");
            Assert.That(() => stream.Range(5, 4), Throws.TypeOf<ArgumentOutOfRangeException>(), "倒区间要拒绝");
        }

        [Test]
        public void Range_WhenCalledRepeatedly_StaysInsideTheHalfOpenInterval()
        {
            const int MinInclusive = -7;
            const int MaxExclusive = 11;
            const int Draws = 10000;

            IRandomStream stream = new XorShiftRandomStream(Seed);
            bool sawLowerBound = false;
            bool sawUpperBoundMinusOne = false;

            for (int i = 0; i < Draws; i++)
            {
                int value = stream.Range(MinInclusive, MaxExclusive);
                Assert.That(value, Is.InRange(MinInclusive, MaxExclusive - 1), $"第 {i} 次抽到了区间外的 {value}");
                sawLowerBound |= value == MinInclusive;
                sawUpperBoundMinusOne |= value == MaxExclusive - 1;
            }

            // 含下界不含上界：下界必须抽得到，上界只能由 max-1 顶着
            Assert.That(sawLowerBound, Is.True, "下界是闭的，1 万次里必须抽到过 minInclusive");
            Assert.That(sawUpperBoundMinusOne, Is.True, "maxExclusive - 1 必须抽得到，否则上界被多砍了一格");
        }

        [Test]
        public void Range_WithWidthOfOne_AlwaysReturnsMinInclusive()
        {
            const int Only = 42;

            IRandomStream stream = new XorShiftRandomStream(Seed);
            for (int i = 0; i < 128; i++)
            {
                Assert.That(stream.Range(Only, Only + 1), Is.EqualTo(Only), "宽度为 1 的区间只有一个合法值");
            }
        }

        [Test]
        public void Range_WhenCalled_ConsumesExactlyOneNextUInt()
        {
            // 每次调用消耗的随机数个数固定，是「回放对不上时，分叉点能直接指向出错那行」的前提；
            // 一旦改成拒绝采样，消耗个数就随抽到的值浮动，差异会顺着序列一路放大。
            XorShiftRandomStream viaRange = new XorShiftRandomStream(Seed);
            XorShiftRandomStream viaRaw = new XorShiftRandomStream(Seed);

            for (int i = 0; i < 32; i++)
            {
                viaRange.Range(0, 1000);
                viaRaw.NextUInt();
            }

            Assert.That(viaRange.State, Is.EqualTo(viaRaw.State), "32 次 Range 该和 32 次 NextUInt 把状态推到同一处");
        }

        [Test]
        public void Value01_WhenCalledRepeatedly_StaysInZeroInclusiveToOneExclusive()
        {
            const int Draws = 10000;

            IRandomStream stream = new XorShiftRandomStream(Seed);
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < Draws; i++)
            {
                float value = stream.Value01();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f), $"第 {i} 次抽到了负数 {value}");
                Assert.That(value, Is.LessThan(1f), $"第 {i} 次抽到了 {value}，上界必须是开的");
                min = value < min ? value : min;
                max = value > max ? value : max;
            }

            // 非空判据：恒返回 0 也能通过上面两条，所以要求结果真的铺满区间
            Assert.That(min, Is.LessThan(0.01f), "1 万次里没抽到过接近 0 的值，分布不对");
            Assert.That(max, Is.GreaterThan(0.99f), "1 万次里没抽到过接近 1 的值，分布不对");
        }

        /// <summary>连抽 <paramref name="count"/> 个数。</summary>
        private static uint[] Draw(IRandomStream stream, int count)
        {
            uint[] values = new uint[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = stream.NextUInt();
            }

            return values;
        }

        /// <summary>序列里有多少个互不相同的值，用来挡住「恒定值」这类让断言变废话的退化实现。</summary>
        private static int CountDistinct(uint[] values)
        {
            return new HashSet<uint>(values).Count;
        }
    }
}
