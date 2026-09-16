// 职责：一个逻辑 tick 的输入快照——把「此刻的设备状态」压成定长、可序列化、可精确比较的值类型。
//   录制端按每 tick 一条顺序追加它，重放端按 tick 逐条喂回去。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何「一帧输入」的数据类型。Input System 的 InputAction / InputControl
//      读到的是**此刻的设备状态**——它随设备实时变、不定长、不可序列化，也不能脱离设备存在，
//      拿它当录制单元等于什么都没录。
//   2. 扩展不行：不能把它塞进 Core/Input/IInputService。那个接口的职责是 GameInput 动作集的启停
//      （EnableMap / DisableMap），它暴露的是**动作集本身**而不是一帧输入；把定长快照连同它的
//      字节布局兼容责任塞进一个启停接口，会让「改动作图」和「改录制格式」变成同一处改动，
//      而这两件事的兼容要求完全不同（动作图随时改，录制格式一改老录像就全废）。

using System;
using UnityEngine;

namespace Game.Core.Simulation
{
    /// <summary>
    /// 一个逻辑 tick 的输入命令。<b>值语义、定长</b>：序列化后恒为 <see cref="SerializedSize"/> 字节，
    /// 字段顺序固定、小端写入，所以录像体积可以直接用「tick 数 × <see cref="SerializedSize"/>」估出来
    /// （60 tick/s 下约 1.8 KB/s，10 分钟约 1.1 MB）。
    /// <para>
    /// 槽位映射（对应 <c>Data/Input/GameInput.inputactions</c> 的 Gameplay 动作图）：
    /// <see cref="Axis0"/> ← Move；<see cref="Buttons"/> bit0/1/2 ← Confirm/Cancel/Pause；
    /// bit31 ← QA 打点标记（不来自动作图，见 <see cref="ButtonQaMarker"/>）。
    /// <see cref="Axis1"/> 与 <see cref="Pointer"/> 当前**没有对应动作，恒为零**，玩法定了再映射——
    /// 槽位先占住，将来加动作不改字节布局，老录像还能读。
    /// </para>
    /// </summary>
    public readonly struct InputCommand : IEquatable<InputCommand>
    {
        /// <summary>Confirm 动作的按钮位。</summary>
        public const uint ButtonConfirm = 1u << 0;

        /// <summary>Cancel 动作的按钮位。</summary>
        public const uint ButtonCancel = 1u << 1;

        /// <summary>Pause 动作的按钮位。</summary>
        public const uint ButtonPause = 1u << 2;

        /// <summary>
        /// QA 打点标记位。**不来自动作图**，<see cref="LiveInputSource"/> 永远不会置它；
        /// 由录制系统的热键在采样之后按到这条命令上，用来在录像里标出「就是这一帧出的问题」。
        /// 放在最高位是为了跟动作位（从 bit0 往上长）留出最大的互不打扰空间。
        /// </summary>
        public const uint ButtonQaMarker = 1u << 31;

        // 字节布局（小端，偏移量固定，改动即破坏老录像的兼容性）：
        //   [ 0..3 ] Axis0.x    [ 4..7 ] Axis0.y
        //   [ 8..11] Axis1.x    [12..15] Axis1.y
        //   [16..19] Buttons
        //   [20..23] Pointer.x  [24..27] Pointer.y
        //   [  28  ] Flags
        //   [29..30] Reserved（恒写 0，读时忽略）
        private const int OffsetAxis0 = 0;
        private const int OffsetAxis1 = 8;
        private const int OffsetButtons = 16;
        private const int OffsetPointer = 20;
        private const int OffsetFlags = 28;
        private const int OffsetReserved = 29;
        private const int ReservedSize = 2;

        /// <summary>序列化后的固定字节数。定长是录制系统按 tick 随机寻址（第 N 条 = N × 本值）的前提。</summary>
        public const int SerializedSize = OffsetReserved + ReservedSize;

        /// <summary>构造一条输入命令。</summary>
        /// <param name="axis0">主移动轴，来自 Gameplay/Move。</param>
        /// <param name="axis1">副轴，当前无对应动作，传 <c>Vector2.zero</c>。</param>
        /// <param name="buttons">按钮位掩码，见 <see cref="ButtonConfirm"/> 等常量。</param>
        /// <param name="pointer">指针位置，当前无对应动作，传 <c>Vector2.zero</c>。</param>
        /// <param name="flags">预留标志位，当前恒为 0。</param>
        public InputCommand(Vector2 axis0, Vector2 axis1, uint buttons, Vector2 pointer, byte flags)
        {
            Axis0 = axis0;
            Axis1 = axis1;
            Buttons = buttons;
            Pointer = pointer;
            Flags = flags;
        }

        /// <summary>主移动轴，来自 Gameplay/Move（Vector2）。</summary>
        public Vector2 Axis0 { get; }

        /// <summary>副轴。当前动作图里没有对应动作，恒为零；槽位先留着，玩法定了再映射。</summary>
        public Vector2 Axis1 { get; }

        /// <summary>按钮位掩码。bit0=Confirm，bit1=Cancel，bit2=Pause，bit31=QA 打点标记。</summary>
        public uint Buttons { get; }

        /// <summary>指针位置。当前动作图里没有对应动作，恒为零。</summary>
        public Vector2 Pointer { get; }

        /// <summary>预留标志位。当前恒为 0，留给将来的「这条命令的元信息」（比如丢帧补齐标记）。</summary>
        public byte Flags { get; }

        /// <summary>空命令：什么都没按、轴全零。重放越界或输入源缺失时返回它。</summary>
        public static InputCommand Empty => default;

        /// <summary>按钮是否按下。<paramref name="mask"/> 传 <see cref="ButtonConfirm"/> 这类常量。</summary>
        public bool HasButton(uint mask)
        {
            return (Buttons & mask) != 0u;
        }

        /// <summary>
        /// 把这条命令写进 <paramref name="buffer"/> 的 <paramref name="offset"/> 处，恒占
        /// <see cref="SerializedSize"/> 字节。调用方自备缓冲区复用，这里不分配。
        /// </summary>
        /// <exception cref="ArgumentNullException">buffer 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">offset 为负，或剩余空间不足一条命令。</exception>
        public void WriteTo(byte[] buffer, int offset)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || buffer.Length - offset < SerializedSize)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "缓冲区剩余空间放不下一条 InputCommand");
            }

            WriteVector2(buffer, offset + OffsetAxis0, Axis0);
            WriteVector2(buffer, offset + OffsetAxis1, Axis1);
            WriteUInt32(buffer, offset + OffsetButtons, Buttons);
            WriteVector2(buffer, offset + OffsetPointer, Pointer);
            buffer[offset + OffsetFlags] = Flags;

            for (int i = 0; i < ReservedSize; i++)
            {
                buffer[offset + OffsetReserved + i] = 0;
            }
        }

        /// <summary>
        /// 从 <paramref name="buffer"/> 的 <paramref name="offset"/> 处读出一条命令。
        /// Reserved 两字节读时忽略——老版本写的是 0，新版本写别的也不该让旧读法崩掉。
        /// </summary>
        /// <exception cref="ArgumentNullException">buffer 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">offset 为负，或剩余字节不足一条命令。</exception>
        public static InputCommand ReadFrom(byte[] buffer, int offset)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || buffer.Length - offset < SerializedSize)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "缓冲区剩余字节不足一条 InputCommand");
            }

            return new InputCommand(
                ReadVector2(buffer, offset + OffsetAxis0),
                ReadVector2(buffer, offset + OffsetAxis1),
                ReadUInt32(buffer, offset + OffsetButtons),
                ReadVector2(buffer, offset + OffsetPointer),
                buffer[offset + OffsetFlags]);
        }

        /// <summary>
        /// 逐位精确比较。**故意不用 <c>Vector2 ==</c>**：那个重载是带容差的近似比较
        /// （差值小于 1e-5 就算相等），拿它比对录制与重放的命令，会把已经开始分叉的两条判成相同，
        /// 正好掩盖掉这个系统要抓的那类 bug。
        /// </summary>
        public bool Equals(InputCommand other)
        {
            return Axis0.x == other.Axis0.x
                && Axis0.y == other.Axis0.y
                && Axis1.x == other.Axis1.x
                && Axis1.y == other.Axis1.y
                && Buttons == other.Buttons
                && Pointer.x == other.Pointer.x
                && Pointer.y == other.Pointer.y
                && Flags == other.Flags;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is InputCommand other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Axis0.x.GetHashCode();
                hash = (hash * 31) + Axis0.y.GetHashCode();
                hash = (hash * 31) + Axis1.x.GetHashCode();
                hash = (hash * 31) + Axis1.y.GetHashCode();
                hash = (hash * 31) + Buttons.GetHashCode();
                hash = (hash * 31) + Pointer.x.GetHashCode();
                hash = (hash * 31) + Pointer.y.GetHashCode();
                hash = (hash * 31) + Flags.GetHashCode();
                return hash;
            }
        }

        /// <summary>逐位相等，语义见 <see cref="Equals(InputCommand)"/>。</summary>
        public static bool operator ==(InputCommand left, InputCommand right)
        {
            return left.Equals(right);
        }

        /// <summary>逐位不等，语义见 <see cref="Equals(InputCommand)"/>。</summary>
        public static bool operator !=(InputCommand left, InputCommand right)
        {
            return !left.Equals(right);
        }

        private static void WriteVector2(byte[] buffer, int offset, Vector2 value)
        {
            WriteSingle(buffer, offset, value.x);
            WriteSingle(buffer, offset + sizeof(float), value.y);
        }

        private static Vector2 ReadVector2(byte[] buffer, int offset)
        {
            return new Vector2(
                ReadSingle(buffer, offset),
                ReadSingle(buffer, offset + sizeof(float)));
        }

        // 用位模式转整数再逐字节写，而不是 BitConverter.GetBytes：后者每次调用都 new 一个 byte[4]，
        // 而这条路径是每 tick 走一次的录制热路径。手写移位顺带固定了小端字节序，
        // 不跟着 BitConverter.IsLittleEndian 走——录像要能跨机器读。
        private static void WriteSingle(byte[] buffer, int offset, float value)
        {
            WriteUInt32(buffer, offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        private static float ReadSingle(byte[] buffer, int offset)
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32(buffer, offset)));
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return buffer[offset]
                | ((uint)buffer[offset + 1] << 8)
                | ((uint)buffer[offset + 2] << 16)
                | ((uint)buffer[offset + 3] << 24);
        }
    }
}
