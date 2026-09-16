// 职责：钉住回放数据层最底下那一层——状态字节缓冲的读写对称性、浮点位模式规范化、
//   哈希的敏感度与顺序敏感性、复用不残留、越界不静默。
// 为什么新建：Core/Replay/ 下的 StateBuffer 与 StateHasher 一条测试都没有，
//   而它们错了不会编译失败、也不会报错，只会让漂移检测**静默失灵**（误报或漏报）。
//   开发期用 execute_code 一次性验过的那几条，跑完就没了、不进回归，本文件把它们固化下来。
//   为什么不放进 Tests/EditMode/Core/：那个目录按单个服务分文件，回放是一整层
//   （序列化 + 文件格式 + 漂移检测），照 Tests/EditMode/Simulation/ 的先例单开一个目录。

using System;
using Game.Core.Replay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Replay
{
    /// <summary>
    /// <see cref="StateBuffer"/> 与 <see cref="StateHasher"/> 的 EditMode 测试。
    /// 全程只跟内存里的字节打交道：不依赖场景、不依赖资产路径、不依赖机器快慢。
    /// </summary>
    public sealed class StateBufferTests
    {
        /// <summary>+0.0f 的位模式，用来断言负零确实被归一了。</summary>
        private const int PositiveZeroBits = 0;

        /// <summary>-0.0f 的位模式。手工构造而不写 <c>-0.0f</c> 字面量，免得编译期常量折叠把负号吃掉。</summary>
        private const int NegativeZeroBits = unchecked((int)0x80000000);

        /// <summary>安静 NaN 的「标准」位模式（float.NaN 在 .NET 上通常就是它）。</summary>
        private const int QuietNaNBits = unchecked((int)0x7FC00000);

        /// <summary>
        /// 另一个 NaN 位模式（尾数最低位多一个 1）。
        /// <para>
        /// 故意手工构造而不是拿 <c>0f / 0f</c> 当对照：<c>0f / 0f</c> 在有些机器上算出来的位模式
        /// 恰好就等于 <see cref="float.NaN"/>，拿这两个做对照等于什么都没验到——
        /// 两个输入本来就逐位相同，哈希当然相等。
        /// </para>
        /// </summary>
        private const int AlternateNaNBits = unchecked((int)0x7FC00001);

        [Test]
        public void WriteThenRead_EverySupportedType_RoundTripsEachValueAndLeavesNothingUnread()
        {
            StateBuffer buffer = new StateBuffer();
            SampleRecord written = SampleRecord.CreateBaseline();
            written.WriteTo(buffer);

            Assert.That(buffer.Remaining, Is.EqualTo(buffer.Length), "写完还没读时，剩余可读字节应等于有效长度");

            // 逐个读回来比对：错一个字段，后面全部错位，所以必须逐项断言而不是只比最后的 Remaining。
            Assert.That(buffer.ReadBool(), Is.EqualTo(written.Flag), "bool 没读回原值");
            Assert.That(buffer.ReadByte(), Is.EqualTo(written.Byte), "byte 没读回原值");
            Assert.That(buffer.ReadShort(), Is.EqualTo(written.Short), "short 没读回原值（负数要靠补码原样回来）");
            Assert.That(buffer.ReadUShort(), Is.EqualTo(written.UShort), "ushort 没读回原值");
            Assert.That(buffer.ReadInt(), Is.EqualTo(written.Int), "int 没读回原值");
            Assert.That(buffer.ReadUInt(), Is.EqualTo(written.UInt), "uint 没读回原值");
            Assert.That(buffer.ReadLong(), Is.EqualTo(written.Long), "long 没读回原值");
            Assert.That(buffer.ReadULong(), Is.EqualTo(written.ULong), "ulong 没读回原值");
            Assert.That(buffer.ReadFloat(), Is.EqualTo(written.Float), "float 没读回原值");
            Assert.That(buffer.ReadVector2(), Is.EqualTo(written.Vector), "Vector2 没读回原值");

            Assert.That(
                buffer.Remaining,
                Is.EqualTo(0),
                "读完整份记录后必须恰好一个字节不剩：不为 0 就说明读写宽度对不上，"
                + "而那种错位在真实状态里只会读出一堆看起来合理的垃圾值");
        }

        [Test]
        public void WriteFloat_WhenValueIsNegativeZero_HashesTheSameAsPositiveZero()
        {
            float negativeZero = BitConverter.Int32BitsToSingle(NegativeZeroBits);
            float positiveZero = BitConverter.Int32BitsToSingle(PositiveZeroBits);

            // 先证明这两个输入确实是「数值相等、位模式不同」，否则下面的判据是空的。
            Assert.That(negativeZero, Is.EqualTo(positiveZero), "负零和正零在数值上必须相等");
            Assert.That(
                BitConverter.SingleToInt32Bits(negativeZero),
                Is.Not.EqualTo(BitConverter.SingleToInt32Bits(positiveZero)),
                "负零和正零的位模式必须不同，不然这条用例什么都没验到");

            StateBuffer negative = new StateBuffer();
            negative.WriteFloat(negativeZero);

            StateBuffer positive = new StateBuffer();
            positive.WriteFloat(positiveZero);

            Assert.That(
                StateHasher.Compute(negative),
                Is.EqualTo(StateHasher.Compute(positive)),
                "-0.0f 与 +0.0f 必须算出同一个哈希：速度衰减到停、位置减回原点都能随手产出负零，"
                + "不归一就会报一个根本不存在的漂移");

            negative.SeekToStart();
            Assert.That(
                BitConverter.SingleToInt32Bits(negative.ReadFloat()),
                Is.EqualTo(PositiveZeroBits),
                "负零应当以 +0.0f 的位模式落进缓冲");
        }

        [Test]
        public void WriteFloat_WhenNaNBitPatternsDiffer_HashesTheSame()
        {
            float quietNaN = BitConverter.Int32BitsToSingle(QuietNaNBits);
            float alternateNaN = BitConverter.Int32BitsToSingle(AlternateNaNBits);

            // 同上：先证明两个输入确实是两种不同的 NaN，这条用例才有意义。
            Assert.That(float.IsNaN(quietNaN), Is.True, "0x7FC00000 应当是 NaN");
            Assert.That(float.IsNaN(alternateNaN), Is.True, "0x7FC00001 应当是 NaN");
            Assert.That(
                BitConverter.SingleToInt32Bits(quietNaN),
                Is.Not.EqualTo(BitConverter.SingleToInt32Bits(alternateNaN)),
                "两个 NaN 的位模式必须不同，不然这条用例什么都没验到");

            StateBuffer first = new StateBuffer();
            first.WriteFloat(quietNaN);

            StateBuffer second = new StateBuffer();
            second.WriteFloat(alternateNaN);

            Assert.That(
                StateHasher.Compute(first),
                Is.EqualTo(StateHasher.Compute(second)),
                "NaN 是一整族位模式，表达的都是同一件事「这个数算坏了」，必须归一到同一个位模式再进哈希");
        }

        [Test]
        public void Compute_WhenAnySingleFieldChanges_ProducesADifferentHash()
        {
            SampleRecord baseline = SampleRecord.CreateBaseline();
            ulong baselineHash = HashOf(baseline);

            for (int field = 0; field < SampleRecord.FieldCount; field++)
            {
                SampleRecord mutated = baseline.CopyWithFieldChanged(field);

                Assert.That(
                    HashOf(mutated),
                    Is.Not.EqualTo(baselineHash),
                    $"只改了「{SampleRecord.FieldName(field)}」这一个字段，哈希却没变——"
                    + "这个字段的漂移从此永远查不出来");
            }
        }

        [Test]
        public void Compute_WhenFloatDiffersByOneUlp_ProducesADifferentHash()
        {
            float value = 1f;
            float oneUlpUp = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + 1);

            Assert.That(oneUlpUp, Is.Not.EqualTo(value), "差 1 ulp 的两个 float 必须是两个不同的值");

            StateBuffer original = new StateBuffer();
            original.WriteFloat(value);

            StateBuffer shifted = new StateBuffer();
            shifted.WriteFloat(oneUlpUp);

            Assert.That(
                StateHasher.Compute(shifted),
                Is.Not.EqualTo(StateHasher.Compute(original)),
                "规范化只该抹掉「同一个值的不同写法」（负零、NaN），绝不能顺手把真实的 1 ulp 精度差异也抹掉——"
                + "那正是这套系统要抓的东西");
        }

        [Test]
        public void Compute_WhenTwoFieldsSwapWriteOrder_ProducesADifferentHash()
        {
            StateBuffer inOrder = new StateBuffer();
            inOrder.WriteInt(7);
            inOrder.WriteInt(9);

            StateBuffer swapped = new StateBuffer();
            swapped.WriteInt(9);
            swapped.WriteInt(7);

            Assert.That(inOrder.Length, Is.EqualTo(swapped.Length), "两份记录的字节数应当一样，差别只在顺序");
            Assert.That(
                StateHasher.Compute(swapped),
                Is.Not.EqualTo(StateHasher.Compute(inOrder)),
                "字段顺序本身就是状态的一部分：顺序变了哈希必须变，否则「Serialize 的顺序被谁改了」这类事故查不出来");
        }

        [Test]
        public void Reset_WhenBufferIsReused_ProducesByteIdenticalResultToAFreshBuffer()
        {
            // 先写一份**更长**的记录，再 Reset 重写一份短的：上一轮的尾巴若没被有效长度挡住，
            // 就会混进哈希，让两份内容相同的状态算出不同的值。
            StateBuffer reused = new StateBuffer();
            SampleRecord.CreateBaseline().WriteTo(reused);
            reused.WriteULong(ulong.MaxValue);
            reused.WriteULong(ulong.MaxValue);

            reused.Reset();
            Assert.That(reused.Length, Is.EqualTo(0), "Reset 之后有效长度必须归零");
            Assert.That(reused.ReadPosition, Is.EqualTo(0), "Reset 之后读游标必须归零");

            reused.WriteInt(42);
            reused.WriteFloat(1.5f);

            StateBuffer fresh = new StateBuffer();
            fresh.WriteInt(42);
            fresh.WriteFloat(1.5f);

            Assert.That(reused.Length, Is.EqualTo(fresh.Length), "复用与全新写出的有效长度必须相同");

            byte[] reusedBytes = reused.GetBuffer();
            byte[] freshBytes = fresh.GetBuffer();
            for (int i = 0; i < fresh.Length; i++)
            {
                Assert.That(reusedBytes[i], Is.EqualTo(freshBytes[i]), $"第 {i} 个字节和全新缓冲不一致");
            }

            Assert.That(
                StateHasher.Compute(reused),
                Is.EqualTo(StateHasher.Compute(fresh)),
                "复用缓冲写出的哈希必须和全新缓冲逐位相同，否则「同一个世界算出两个哈希」");
        }

        [Test]
        public void ReadInt_WhenNotEnoughBytesRemain_ThrowsInsteadOfReturningDefault()
        {
            StateBuffer buffer = new StateBuffer();
            buffer.WriteUShort(1234);

            Assert.That(buffer.Remaining, Is.EqualTo(2), "这时只该剩 2 个字节可读");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => buffer.ReadInt(),
                "读不够字节必须抛：补个默认值继续读，只会把错位推到后面某个看不懂的地方再炸");

            Assert.That(error.Message, Does.Contain("越界"), "报错要说清是读越界，别只给一句通用消息");
        }

        /// <summary>对一份记录算哈希：每次都用全新缓冲，免得上一轮的状态参与进来。</summary>
        private static ulong HashOf(SampleRecord record)
        {
            StateBuffer buffer = new StateBuffer();
            record.WriteTo(buffer);
            return StateHasher.Compute(buffer);
        }

        /// <summary>
        /// 一份「每种支持的类型各来一个」的样本记录。写入顺序即字节布局，
        /// 改这里的顺序会让本文件多条用例的判据一起变，别随手动。
        /// </summary>
        private sealed class SampleRecord
        {
            /// <summary>字段个数，供「逐个字段改一下」的用例遍历。</summary>
            public const int FieldCount = 10;

            /// <summary>bool 字段。</summary>
            public bool Flag { get; private set; }

            /// <summary>byte 字段。</summary>
            public byte Byte { get; private set; }

            /// <summary>short 字段，取负值以覆盖补码。</summary>
            public short Short { get; private set; }

            /// <summary>ushort 字段。</summary>
            public ushort UShort { get; private set; }

            /// <summary>int 字段，取负值以覆盖补码。</summary>
            public int Int { get; private set; }

            /// <summary>uint 字段，取高位有值的数以覆盖第 4 个字节。</summary>
            public uint UInt { get; private set; }

            /// <summary>long 字段。</summary>
            public long Long { get; private set; }

            /// <summary>ulong 字段，取高位有值的数以覆盖第 8 个字节。</summary>
            public ulong ULong { get; private set; }

            /// <summary>float 字段，取二进制可精确表示的值，免得判据被舍入搅动。</summary>
            public float Float { get; private set; }

            /// <summary>Vector2 字段，两个分量取不同值以覆盖「两个分量写反了」。</summary>
            public Vector2 Vector { get; private set; }

            /// <summary>造一份基准记录。各字段取值互不相同，免得「写错字段」也能蒙混过关。</summary>
            public static SampleRecord CreateBaseline()
            {
                return new SampleRecord
                {
                    Flag = true,
                    Byte = 0xA5,
                    Short = -12345,
                    UShort = 54321,
                    Int = -123456789,
                    UInt = 0xDEADBEEFu,
                    Long = -1234567890123456789L,
                    ULong = 0xFEEDFACECAFEBEEFul,
                    Float = 1.5f,
                    Vector = new Vector2(2.25f, -3.75f),
                };
            }

            /// <summary>第 <paramref name="field"/> 个字段的名字，用于断言消息。</summary>
            public static string FieldName(int field)
            {
                switch (field)
                {
                    case 0: return "Flag(bool)";
                    case 1: return "Byte";
                    case 2: return "Short";
                    case 3: return "UShort";
                    case 4: return "Int";
                    case 5: return "UInt";
                    case 6: return "Long";
                    case 7: return "ULong";
                    case 8: return "Float";
                    default: return "Vector2";
                }
            }

            /// <summary>
            /// 复制一份，只把第 <paramref name="field"/> 个字段改成另一个值。
            /// 改动都取**最小可见差异**（差 1 / 差 1 ulp / 只动一个分量），
            /// 这样「哈希没变」就只可能是这个字段没进哈希，而不是改动太小被浮点吃掉。
            /// </summary>
            public SampleRecord CopyWithFieldChanged(int field)
            {
                SampleRecord copy = new SampleRecord
                {
                    Flag = Flag,
                    Byte = Byte,
                    Short = Short,
                    UShort = UShort,
                    Int = Int,
                    UInt = UInt,
                    Long = Long,
                    ULong = ULong,
                    Float = Float,
                    Vector = Vector,
                };

                switch (field)
                {
                    case 0:
                        copy.Flag = !Flag;
                        break;
                    case 1:
                        copy.Byte = (byte)(Byte + 1);
                        break;
                    case 2:
                        copy.Short = (short)(Short + 1);
                        break;
                    case 3:
                        copy.UShort = (ushort)(UShort + 1);
                        break;
                    case 4:
                        copy.Int = Int + 1;
                        break;
                    case 5:
                        copy.UInt = UInt + 1u;
                        break;
                    case 6:
                        copy.Long = Long + 1L;
                        break;
                    case 7:
                        copy.ULong = ULong + 1ul;
                        break;
                    case 8:
                        copy.Float = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(Float) + 1);
                        break;
                    default:
                        copy.Vector = new Vector2(Vector.x, Vector.y + 1f);
                        break;
                }

                return copy;
            }

            /// <summary>按固定顺序把各字段写进缓冲。</summary>
            public void WriteTo(IStateWriter writer)
            {
                writer.WriteBool(Flag);
                writer.WriteByte(Byte);
                writer.WriteShort(Short);
                writer.WriteUShort(UShort);
                writer.WriteInt(Int);
                writer.WriteUInt(UInt);
                writer.WriteLong(Long);
                writer.WriteULong(ULong);
                writer.WriteFloat(Float);
                writer.WriteVector2(Vector);
            }
        }
    }
}
