// 职责：钉住 InputCommand 的字节契约——往返逐字段相等、定长 SerializedSize、带 offset 写入不越界不污染、
//   按钮位掩码逐位读写正确（含 bit31 的 QA 打点位）。
// 为什么新建：这条命令的字节布局是录像文件的格式本身，改一个偏移量老录像就全废，
//   而这种错不会编译失败、也不会抛异常，只会让重放读出一串看着像那么回事的垃圾值。
//   为什么和另两个测试文件分开：被测类不同，按「测试类 = <被测类>Tests」的约定各占一个文件。

using System;
using Game.Core.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Simulation
{
    /// <summary>
    /// <see cref="InputCommand"/> 的 EditMode 测试。纯值类型 + byte[]，不碰场景、不碰资产。
    /// </summary>
    public sealed class InputCommandTests
    {
        /// <summary>
        /// 当前冻结的序列化长度。**故意写成字面量**：录制端按「第 N 条 = N × SerializedSize」随机寻址，
        /// 这个数一改老录像就全废，所以改格式必须先看见这条测试红。
        /// </summary>
        private const int FrozenSerializedSize = 31;

        [Test]
        public void SerializedSize_IsFrozenAtTheCurrentWireFormat()
        {
            Assert.That(
                InputCommand.SerializedSize,
                Is.EqualTo(FrozenSerializedSize),
                "改了字节布局就等于换了录像格式，老录像读不了——确认是有意为之再同步改这条判据");
        }

        [Test]
        public void ButtonConstants_MapToTheDocumentedBitPositions()
        {
            Assert.That(InputCommand.ButtonConfirm, Is.EqualTo(1u << 0));
            Assert.That(InputCommand.ButtonCancel, Is.EqualTo(1u << 1));
            Assert.That(InputCommand.ButtonPause, Is.EqualTo(1u << 2));
            Assert.That(InputCommand.ButtonQaMarker, Is.EqualTo(1u << 31), "QA 打点位固定在最高位，与动作位拉开最大距离");
        }

        [Test]
        public void ReadFrom_AfterWriteTo_RestoresEveryFieldExactly()
        {
            InputCommand original = CreateFullyPopulated();
            byte[] buffer = new byte[InputCommand.SerializedSize];

            original.WriteTo(buffer, 0);
            InputCommand restored = InputCommand.ReadFrom(buffer, 0);

            // 逐字段断言而不是只比 Equals：Equals 自己写错的话，只比 Equals 就成了同义反复。
            // 浮点用 Is.EqualTo 的默认零容差比较（NUnit 不给 Within 就是精确比），
            // 这正是这里要的——位模式往返，差一位都算坏。
            Assert.That(restored.Axis0.x, Is.EqualTo(original.Axis0.x), "Axis0.x 没能原样往返");
            Assert.That(restored.Axis0.y, Is.EqualTo(original.Axis0.y), "Axis0.y 没能原样往返");
            Assert.That(restored.Axis1.x, Is.EqualTo(original.Axis1.x), "Axis1.x 没能原样往返");
            Assert.That(restored.Axis1.y, Is.EqualTo(original.Axis1.y), "Axis1.y 没能原样往返");
            Assert.That(restored.Buttons, Is.EqualTo(original.Buttons), "Buttons 没能原样往返");
            Assert.That(restored.Pointer.x, Is.EqualTo(original.Pointer.x), "Pointer.x 没能原样往返");
            Assert.That(restored.Pointer.y, Is.EqualTo(original.Pointer.y), "Pointer.y 没能原样往返");
            Assert.That(restored.Flags, Is.EqualTo(original.Flags), "Flags 没能原样往返");

            Assert.That(restored, Is.EqualTo(original), "逐字段都对了，Equals 也该判相等");
        }

        [Test]
        public void WriteTo_WithOffset_TouchesExactlySerializedSizeBytes()
        {
            const int Offset = 5;
            const byte Sentinel = 0xCD;
            const int TailPadding = 7;

            byte[] buffer = new byte[Offset + InputCommand.SerializedSize + TailPadding];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = Sentinel;
            }

            InputCommand original = CreateFullyPopulated();
            original.WriteTo(buffer, Offset);

            for (int i = 0; i < Offset; i++)
            {
                Assert.That(buffer[i], Is.EqualTo(Sentinel), $"写入点之前的第 {i} 字节被污染了");
            }

            for (int i = Offset + InputCommand.SerializedSize; i < buffer.Length; i++)
            {
                Assert.That(buffer[i], Is.EqualTo(Sentinel), $"写入点之后的第 {i} 字节被污染了");
            }

            // Reserved 尾字节按约定恒写 0（≠ 哨兵），以此证明整段 SerializedSize 字节都落了笔，
            // 而不是只写了前面几个字段就收手
            Assert.That(
                buffer[Offset + InputCommand.SerializedSize - 1],
                Is.EqualTo((byte)0),
                "最后一个 Reserved 字节没被写成 0，写入长度不足 SerializedSize");

            Assert.That(InputCommand.ReadFrom(buffer, Offset), Is.EqualTo(original), "带 offset 写进去的该能带 offset 读回来");
        }

        [Test]
        public void WriteTo_WithBadBufferOrOffset_ThrowsWithoutWriting()
        {
            InputCommand command = CreateFullyPopulated();
            byte[] exactFit = new byte[InputCommand.SerializedSize];

            Assert.That(() => command.WriteTo(null, 0), Throws.TypeOf<ArgumentNullException>(), "buffer 为 null 要拒绝");
            Assert.That(() => command.WriteTo(exactFit, -1), Throws.TypeOf<ArgumentOutOfRangeException>(), "负 offset 要拒绝");
            Assert.That(
                () => command.WriteTo(exactFit, 1),
                Throws.TypeOf<ArgumentOutOfRangeException>(),
                "剩余空间差一个字节也要拒绝，不能写出去半条命令");

            // 被拒之后缓冲区该是干净的：半条命令比写不进去更难查
            for (int i = 0; i < exactFit.Length; i++)
            {
                Assert.That(exactFit[i], Is.EqualTo((byte)0), $"入参被拒，第 {i} 字节却已经被改了");
            }
        }

        [Test]
        public void ReadFrom_WithBadBufferOrOffset_ThrowsArgumentException()
        {
            byte[] exactFit = new byte[InputCommand.SerializedSize];

            Assert.That(() => InputCommand.ReadFrom(null, 0), Throws.TypeOf<ArgumentNullException>(), "buffer 为 null 要拒绝");
            Assert.That(
                () => InputCommand.ReadFrom(exactFit, -1),
                Throws.TypeOf<ArgumentOutOfRangeException>(),
                "负 offset 要拒绝");
            Assert.That(
                () => InputCommand.ReadFrom(exactFit, 1),
                Throws.TypeOf<ArgumentOutOfRangeException>(),
                "剩余字节差一个也要拒绝，不能读出半条命令");
        }

        [Test]
        public void HasButton_ForEveryBit_ReadsBackExactlyTheBitThatWasSet()
        {
            byte[] buffer = new byte[InputCommand.SerializedSize];

            for (int bit = 0; bit < 32; bit++)
            {
                uint mask = 1u << bit;
                InputCommand command = new InputCommand(Vector2.zero, Vector2.zero, mask, Vector2.zero, 0);

                Assert.That(command.HasButton(mask), Is.True, $"bit{bit} 已置位，HasButton 该为 true");
                Assert.That(command.HasButton(~mask), Is.False, $"只置了 bit{bit}，其余 31 位不该被读成按下");

                command.WriteTo(buffer, 0);
                InputCommand restored = InputCommand.ReadFrom(buffer, 0);

                Assert.That(restored.Buttons, Is.EqualTo(mask), $"bit{bit} 没能原样往返（第 16~19 字节的小端写入有问题）");
                Assert.That(restored.HasButton(mask), Is.True, $"bit{bit} 往返之后读不出来了");
            }
        }

        [Test]
        public void HasButton_WithCombinedMask_ReportsOnlyThePressedActions()
        {
            InputCommand command = new InputCommand(
                Vector2.zero,
                Vector2.zero,
                InputCommand.ButtonConfirm | InputCommand.ButtonQaMarker,
                Vector2.zero,
                0);

            Assert.That(command.HasButton(InputCommand.ButtonConfirm), Is.True);
            Assert.That(command.HasButton(InputCommand.ButtonQaMarker), Is.True, "QA 打点位和动作位互不打扰");
            Assert.That(command.HasButton(InputCommand.ButtonCancel), Is.False);
            Assert.That(command.HasButton(InputCommand.ButtonPause), Is.False);
        }

        /// <summary>
        /// 每个字段都取非默认值的一条命令。Axis1 与 Pointer 现在虽然恒为零，
        /// 这里照样填上：槽位的字节位置得被测到，将来接上动作才不会发现偏移量早就写错了。
        /// </summary>
        private static InputCommand CreateFullyPopulated()
        {
            return new InputCommand(
                new Vector2(0.1f, -12345.6789f),
                new Vector2(3.5f, -0.0009765625f),
                InputCommand.ButtonConfirm | InputCommand.ButtonPause | InputCommand.ButtonQaMarker | (1u << 17),
                new Vector2(1920.5f, -1080.25f),
                0xA5);
        }
    }
}
