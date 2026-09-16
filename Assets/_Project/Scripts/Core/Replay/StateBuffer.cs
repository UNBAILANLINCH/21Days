// 职责：IStateWriter / IStateReader 的唯一实现——一段预分配、可复用的字节缓冲，
//   负责小端字节序、浮点位模式规范化、越界检查，以及把外部字节数组装进来供读取。
//   写出来的这一份字节同时是「快照的内容」和「哈希的输入」，两者永远是同一份，不存在第二条路径。
// 为什么读写合并在一个类里（接口拆两个、实现合一个，这不是前后矛盾）：
//   接口拆开是为了**约束调用方**——Serialize 只能写、Deserialize 只能读，谁也越不了界（见 IStateReader 文件头）。
//   实现合一是为了**保证对称性**：小端拼装、浮点规范化、越界判定这三件事，读和写必须是严格互逆的同一套规则；
//   拆成 StateWriter / StateReader 两个类，这套规则就有了两份副本，改一份漏一份的结果是
//   「写进去的和读回来的不是同一个世界」——而这恰恰是本系统要抓的那类 bug。
//   合在一起还顺带让「写完立刻读回来自检」成为一次调用就能做的事，测试与自检路径不用再搬字节。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何字节缓冲设施。Core/Save 的 JsonSaveService 走的是 JSON 字符串，
//      和这里的定长二进制没有一行可共用（理由见 IStateWriter 文件头）。
//   2. 扩展不行：不能建在 MemoryStream / List<byte> 之上。它们在写入途中按需扩容，
//      稳态也会因为一次意外的增长产生堆分配，而且拿不到「当前有效长度那一段的原始数组」去做零拷贝哈希
//      （MemoryStream.GetBuffer 给的是整个容量段，长度要另算，等于把最关键的不变量交给调用方维护）。
//      逐字节走它们的虚方法也比直接索引数组慢一截，而状态哈希是每秒都要跑一遍的路径。

using System;
using UnityEngine;

namespace Game.Core.Replay
{
    /// <summary>
    /// 可复用的状态字节缓冲，同时实现 <see cref="IStateWriter"/> 与 <see cref="IStateReader"/>。
    /// <para>
    /// **两个独立游标**：<see cref="Length"/> 是已写入的有效字节数（写游标），<see cref="ReadPosition"/> 是读游标。
    /// 写只追加在末尾，读只在 [0, <see cref="Length"/>) 里走，互不干扰——所以「写完直接读回来」不需要切模式，
    /// 重读同一份只要 <see cref="SeekToStart"/>。
    /// </para>
    /// <para>
    /// **复用方式**：一局游戏持有一个实例，每次采样前调 <see cref="Reset"/>，写满后拿
    /// <see cref="GetBuffer"/> 与 <see cref="Length"/> 去算哈希或落盘。容量喂饱之后稳态零堆分配。
    /// </para>
    /// <para>线程约定：只在逻辑线程使用，内部不加锁。</para>
    /// </summary>
    public sealed class StateBuffer : IStateWriter, IStateReader
    {
        /// <summary>默认初始容量（字节）。按「一个小型世界的一次完整快照」估的，不够会自动扩容。</summary>
        public const int DefaultCapacity = 4096;

        /// <summary>
        /// 所有 NaN 统一写成这个位模式：正号、指数全 1、尾数最高位为 1 的安静 NaN（0x7FC00000）。
        /// 具体挑哪一个不重要，**固定不变**才重要，所以一旦定下就别改——改了等于老录像的哈希全部失效。
        /// </summary>
        private const uint CanonicalNaNBits = 0x7FC00000u;

        /// <summary>+0.0f 的位模式。-0.0f（0x80000000）会被归一到它。</summary>
        private const uint PositiveZeroBits = 0u;

        private byte[] buffer;
        private int length;
        private int readPosition;

        /// <summary>按指定容量预分配缓冲。</summary>
        /// <param name="capacity">初始容量（字节），必须为正。按实际快照大小给足，省掉运行期扩容。</param>
        /// <exception cref="ArgumentOutOfRangeException">容量不为正。</exception>
        public StateBuffer(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "缓冲容量必须为正");
            }

            buffer = new byte[capacity];
        }

        /// <summary>当前容量（字节）。只增不减：缩回去就等于把复用的意义丢掉了。</summary>
        public int Capacity => buffer.Length;

        /// <summary>已写入的有效字节数。算哈希、落盘都只取 [0, 本值) 这一段。</summary>
        public int Length => length;

        /// <summary>读游标位置，恒在 [0, <see cref="Length"/>] 区间内。</summary>
        public int ReadPosition => readPosition;

        /// <summary>还没读到的字节数。读完一份状态后它应该恰好是 0，不为 0 就说明读写不对称。</summary>
        public int Remaining => length - readPosition;

        /// <summary>
        /// 取底层数组，配合 <see cref="Length"/> 使用（**只有前 <see cref="Length"/> 个字节有效**，
        /// 后面是上一轮的残留，不要当数据读）。给 <see cref="StateHasher"/> 和落盘用，零拷贝。
        /// <para>返回的是活的内部数组，扩容后会换成另一个对象——**别把它缓存起来跨轮使用**。</para>
        /// </summary>
        public byte[] GetBuffer()
        {
            return buffer;
        }

        /// <summary>
        /// 清空并复用：有效长度与读游标都归零，底层数组原样留着不重新分配。
        /// <para>
        /// 故意**不擦掉旧字节**——擦一遍是纯浪费，因为有效区永远是 [0, <see cref="Length"/>)，
        /// 残留字节既进不了哈希也进不了快照。真要确认没有残留，看 <see cref="Length"/>，别去翻数组。
        /// </para>
        /// </summary>
        public void Reset()
        {
            length = 0;
            readPosition = 0;
        }

        /// <summary>读游标回到起点，有效内容不变。用于把同一份字节再读一遍（比如先自检再恢复）。</summary>
        public void SeekToStart()
        {
            readPosition = 0;
        }

        /// <summary>
        /// 把现有字节数组的一段装进来供读取（从录像文件或网络收到的快照走这条路）。
        /// 装完 <see cref="Length"/> 等于 <paramref name="count"/>，读游标归零。
        /// </summary>
        /// <param name="source">来源数组。</param>
        /// <param name="offset">起始偏移。</param>
        /// <param name="count">字节数。超过当前容量会扩容一次（之后复用就不再分配）。</param>
        /// <exception cref="ArgumentNullException">source 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">偏移或长度为负，或这段超出了 source 的范围。</exception>
        public void LoadFrom(byte[] source, int offset, int count)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (offset < 0 || count < 0 || source.Length - offset < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "要装载的区间超出了来源数组的范围");
            }

            EnsureCapacity(0, count);
            Array.Copy(source, offset, buffer, 0, count);
            length = count;
            readPosition = 0;
        }

        /// <inheritdoc />
        public void WriteBool(bool value)
        {
            // 固定写 0 / 1：bool 在 CLR 里虽然就是 0/1，但从 interop 或 unsafe 转出来的 bool 可能带别的非零值，
            // 那会让同一个 true 出现两种字节、算出两个哈希——又是一次误报漂移。
            WriteByte(value ? (byte)1 : (byte)0);
        }

        /// <inheritdoc />
        public void WriteByte(byte value)
        {
            EnsureCapacity(length, 1);
            buffer[length] = value;
            length += 1;
        }

        /// <inheritdoc />
        public void WriteShort(short value)
        {
            WriteUShort(unchecked((ushort)value));
        }

        /// <inheritdoc />
        public void WriteUShort(ushort value)
        {
            EnsureCapacity(length, 2);
            buffer[length] = (byte)value;
            buffer[length + 1] = (byte)(value >> 8);
            length += 2;
        }

        /// <inheritdoc />
        public void WriteInt(int value)
        {
            WriteUInt(unchecked((uint)value));
        }

        /// <inheritdoc />
        public void WriteUInt(uint value)
        {
            // 手写移位固定小端，不跟着 BitConverter.IsLittleEndian 走：录像要能跨机器读。
            // 也不用 BitConverter.GetBytes——那个每次调用都 new 一个 byte[4]。
            EnsureCapacity(length, 4);
            buffer[length] = (byte)value;
            buffer[length + 1] = (byte)(value >> 8);
            buffer[length + 2] = (byte)(value >> 16);
            buffer[length + 3] = (byte)(value >> 24);
            length += 4;
        }

        /// <inheritdoc />
        public void WriteLong(long value)
        {
            WriteULong(unchecked((ulong)value));
        }

        /// <inheritdoc />
        public void WriteULong(ulong value)
        {
            EnsureCapacity(length, 8);
            buffer[length] = (byte)value;
            buffer[length + 1] = (byte)(value >> 8);
            buffer[length + 2] = (byte)(value >> 16);
            buffer[length + 3] = (byte)(value >> 24);
            buffer[length + 4] = (byte)(value >> 32);
            buffer[length + 5] = (byte)(value >> 40);
            buffer[length + 6] = (byte)(value >> 48);
            buffer[length + 7] = (byte)(value >> 56);
            length += 8;
        }

        /// <summary>
        /// 写一个单精度浮点数（小端，4 字节），**写之前先把位模式规范化**。
        /// <para>
        /// 要管的两种情况，都是「数值相等、字节不同」——不处理就会算出两个不同的哈希，
        /// 让回放报一个根本不存在的漂移（误报比漏报更糟：它会让人花半天查一个不存在的问题）：
        /// </para>
        /// <para>
        /// ① **负零**。<c>-0.0f</c> 的位模式是 0x80000000，<c>+0.0f</c> 是 0x00000000，
        /// 但 <c>-0.0f == +0.0f</c> 为真。速度衰减到停、位置减回原点这类算式随手就能产出负零，
        /// 两次运行一个停在 +0 一个停在 -0，世界其实一模一样。统一归到 +0.0f。
        /// </para>
        /// <para>
        /// ② **NaN**。NaN 不是一个值而是一整族位模式（指数全 1、尾数非 0 的任意组合，符号位还能取两种），
        /// <c>0f/0f</c>、<c>Sqrt(-1)</c>、<c>Infinity - Infinity</c> 产出的位模式未必相同，
        /// 而它们表达的都是同一件事「这个数算坏了」。统一归到 <see cref="CanonicalNaNBits"/>。
        /// </para>
        /// <para>
        /// 反过来说，这里**不做**任何精度截断或四舍五入：真的漂了 1 个 ulp 也必须让哈希变，
        /// 那正是这套系统要抓的东西。规范化只抹掉「同一个值的不同写法」，绝不抹掉「不同的值」。
        /// </para>
        /// <para>
        /// 实现走 <c>BitConverter.SingleToInt32Bits</c> 取位模式（Game.Core 的 asmdef 里
        /// <c>allowUnsafeCode: false</c>，指针重解释这条路走不了；Simulation/InputCommand 也是这么写的）。
        /// </para>
        /// </summary>
        public void WriteFloat(float value)
        {
            WriteUInt(NormalizeFloatBits(value));
        }

        /// <inheritdoc />
        public void WriteVector2(Vector2 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
        }

        /// <inheritdoc />
        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        /// <inheritdoc />
        public byte ReadByte()
        {
            EnsureReadable(1);
            byte value = buffer[readPosition];
            readPosition += 1;
            return value;
        }

        /// <inheritdoc />
        public short ReadShort()
        {
            return unchecked((short)ReadUShort());
        }

        /// <inheritdoc />
        public ushort ReadUShort()
        {
            EnsureReadable(2);
            ushort value = (ushort)(buffer[readPosition] | (buffer[readPosition + 1] << 8));
            readPosition += 2;
            return value;
        }

        /// <inheritdoc />
        public int ReadInt()
        {
            return unchecked((int)ReadUInt());
        }

        /// <inheritdoc />
        public uint ReadUInt()
        {
            EnsureReadable(4);
            uint value = buffer[readPosition]
                | ((uint)buffer[readPosition + 1] << 8)
                | ((uint)buffer[readPosition + 2] << 16)
                | ((uint)buffer[readPosition + 3] << 24);
            readPosition += 4;
            return value;
        }

        /// <inheritdoc />
        public long ReadLong()
        {
            return unchecked((long)ReadULong());
        }

        /// <inheritdoc />
        public ulong ReadULong()
        {
            EnsureReadable(8);
            ulong value = buffer[readPosition]
                | ((ulong)buffer[readPosition + 1] << 8)
                | ((ulong)buffer[readPosition + 2] << 16)
                | ((ulong)buffer[readPosition + 3] << 24)
                | ((ulong)buffer[readPosition + 4] << 32)
                | ((ulong)buffer[readPosition + 5] << 40)
                | ((ulong)buffer[readPosition + 6] << 48)
                | ((ulong)buffer[readPosition + 7] << 56);
            readPosition += 8;
            return value;
        }

        /// <inheritdoc />
        public float ReadFloat()
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt()));
        }

        /// <inheritdoc />
        public Vector2 ReadVector2()
        {
            float x = ReadFloat();
            float y = ReadFloat();
            return new Vector2(x, y);
        }

        /// <summary>
        /// 取一个浮点数规范化之后的位模式。写入路径唯一的浮点处理点，
        /// 两种要管的情况与理由见 <see cref="WriteFloat"/>。
        /// </summary>
        private static uint NormalizeFloatBits(float value)
        {
            if (float.IsNaN(value))
            {
                return CanonicalNaNBits;
            }

            // -0.0f == +0.0f 为真，所以这一个判断同时接住了正零和负零，两者都写成 +0.0f 的位模式。
            if (value == 0f)
            {
                return PositiveZeroBits;
            }

            return unchecked((uint)BitConverter.SingleToInt32Bits(value));
        }

        /// <summary>
        /// 确保从 <paramref name="offset"/> 起还能放下 <paramref name="count"/> 个字节，不够就扩容。
        /// <para>
        /// 扩容会分配一个新数组并拷贝旧内容——这是**唯一**的堆分配点，按倍增走，几轮之后就不再触发。
        /// 如果它在稳态还在触发，说明构造时给的容量太小，去把容量调大，而不是容忍它每轮分配。
        /// </para>
        /// </summary>
        private void EnsureCapacity(int offset, int count)
        {
            int required = offset + count;
            if (required <= buffer.Length)
            {
                return;
            }

            int grown = buffer.Length * 2;
            if (grown < required)
            {
                grown = required;
            }

            Array.Resize(ref buffer, grown);
        }

        /// <summary>
        /// 确保还有 <paramref name="count"/> 个字节可读，不够直接抛。
        /// <para>
        /// 这里**故意不返回默认值**：读不够只有两种可能——字节被截断了，或者读写顺序不对称。
        /// 两种都是必须立刻停下来修的错，补个 0 继续读只会把问题推到后面某个看不懂的地方再炸。
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">剩余字节不足。</exception>
        private void EnsureReadable(int count)
        {
            if (length - readPosition >= count)
            {
                return;
            }

            throw new InvalidOperationException(
                $"状态缓冲读越界：位置 {readPosition} 处要读 {count} 字节，但只剩 {length - readPosition} 字节（有效长度 {length}）。" +
                "多半是 Serialize 与 Deserialize 的字段顺序不对称，或者这份字节来自另一个格式版本。");
        }
    }
}
