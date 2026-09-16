// 职责：回放文件里一条记录的**内存表示**——类型、所属 tick，以及指向 payload 那一段字节的视图。
//   它只是「一条记录现在是什么样」，不管这段字节从哪来（文件 / 内存 / 将来的网络流）。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内最像的是 Simulation/InputCommand，但它是**一条输入**，
//      没有类型标记、没有 tick、长度定死 31 字节。回放文件里输入只是四种记录中的一种
//      （还有状态哈希、完整快照、QA 打点，将来还会加），把 type/tick/变长 payload 塞进 InputCommand
//      等于让确定性内核的输入类型反过来背上文件格式的包袱——而依赖方向只许 Replay → Simulation。
//   2. 扩展不行：不能把这三个字段挂到 StateBuffer 上。StateBuffer 是「一段字节怎么摆」的缓冲，
//      它同时给状态哈希、快照、网络快照用，那些场合根本没有 chunk 的概念；
//      给它加上 type/tick，等于每个用它的地方都要面对两个用不上的字段。
//   3. 做成 readonly struct 而不是 class：ReplayReader 每读一条就交出一个，一局录像几万条，
//      class 会在遍历回放时每条 new 一次（这正是「读回放时编辑器一卡一卡」的典型来源）。

using System;
using System.Text;
using Game.Core.Simulation;

namespace Game.Core.Replay
{
    /// <summary>
    /// 回放文件里的一条记录。文件里的样子是 <c>type(u8) | tick(u32) | length(u16) | payload</c>，
    /// 布局与各类型 payload 的含义见 <see cref="ReplayFormat"/>。
    /// <para>
    /// <b>payload 是一段借来的视图，不是副本</b>：<see cref="GetPayloadBuffer"/> 返回的是
    /// <see cref="ReplayReader"/> 复用的那块缓冲，**下一次 <c>ReadNext</c> 就会把它覆盖掉**。
    /// 要留着用就自己拷走（<c>Array.Copy</c> 或 <see cref="LoadPayloadInto"/>）。
    /// 这么设计是为了让遍历几万条记录时一次堆分配都没有。
    /// </para>
    /// <para>
    /// <b>不认识的类型也能装进来</b>：<see cref="RawType"/> 保留文件里那一个字节的原值，
    /// <see cref="IsKnownType"/> 说明当前版本认不认得它。<see cref="ReplayReader"/> 默认按 length
    /// 跳过不认识的类型，所以正常遍历拿到的都是认得的；保留原值是为了让将来的
    /// 「读进来原样写出去」类工具（剪辑、合并）不丢掉它看不懂的记录。
    /// </para>
    /// </summary>
    public readonly struct ReplayChunk
    {
        private readonly byte[] payload;

        /// <summary>
        /// 构造一条记录。<paramref name="payload"/> 只是被引用，不拷贝。
        /// </summary>
        /// <param name="rawType">文件里那一个字节的原值（可能是当前版本不认识的类型）。</param>
        /// <param name="tick">这条记录属于哪个逻辑 tick。</param>
        /// <param name="payload">payload 所在的数组，可为 null（此时长度必须为 0）。</param>
        /// <param name="payloadOffset">payload 在数组里的起始偏移。</param>
        /// <param name="payloadLength">payload 字节数。</param>
        /// <exception cref="ArgumentOutOfRangeException">偏移或长度为负，或这段超出了数组范围。</exception>
        public ReplayChunk(byte rawType, uint tick, byte[] payload, int payloadOffset, int payloadLength)
        {
            if (payloadOffset < 0 || payloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payloadLength), payloadLength, "payload 的偏移与长度都不能为负");
            }

            if (payload == null)
            {
                if (payloadLength != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(payloadLength), payloadLength, "payload 数组是 null，长度就必须为 0");
                }
            }
            else if (payload.Length - payloadOffset < payloadLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payloadLength), payloadLength, "payload 区间超出了所给数组的范围");
            }

            RawType = rawType;
            Tick = tick;
            this.payload = payload;
            PayloadOffset = payload == null ? 0 : payloadOffset;
            PayloadLength = payloadLength;
        }

        /// <summary>文件里那一个类型字节的原值。不认识的类型也保留原值，别把它当成枚举直接信。</summary>
        public byte RawType { get; }

        /// <summary>
        /// 类型枚举。**先看 <see cref="IsKnownType"/>**：认不得的类型转出来是一个没有定义的枚举值，
        /// 拿它去 switch 会落进 default。
        /// </summary>
        public ReplayFormat.ChunkType Type => (ReplayFormat.ChunkType)RawType;

        /// <summary>当前版本认不认得这个类型。</summary>
        public bool IsKnownType => ReplayFormat.IsKnownChunkType(RawType);

        /// <summary>这条记录属于哪个逻辑 tick。</summary>
        public uint Tick { get; }

        /// <summary>payload 在 <see cref="GetPayloadBuffer"/> 返回的数组里的起始偏移。</summary>
        public int PayloadOffset { get; }

        /// <summary>payload 字节数，可能为 0（比如一条没写说明的 QA 打点）。</summary>
        public int PayloadLength { get; }

        /// <summary>
        /// 取 payload 所在的数组，配合 <see cref="PayloadOffset"/> 与 <see cref="PayloadLength"/> 使用。
        /// payload 为空时返回 null。
        /// <para><b>这是借来的缓冲，下一次 <c>ReadNext</c> 会覆盖它</b>，别缓存、别改写。</para>
        /// </summary>
        public byte[] GetPayloadBuffer()
        {
            return payload;
        }

        /// <summary>
        /// 把 payload 当作一条输入命令读出来（<see cref="ReplayFormat.ChunkType.Input"/>）。
        /// 解码直接走 <see cref="InputCommand.ReadFrom"/>——输入的字节布局只有那一份定义，
        /// 这里再抄一遍就会出现「录的时候按 A 布局、放的时候按 B 布局」。
        /// </summary>
        /// <exception cref="InvalidOperationException">类型不是 Input，或 payload 短于一条命令。</exception>
        public InputCommand ReadInputCommand()
        {
            if (RawType != (byte)ReplayFormat.ChunkType.Input)
            {
                throw new InvalidOperationException(
                    $"这条 chunk 的类型是 {DescribeType()}，不是输入命令，不能按输入解读");
            }

            if (PayloadLength < InputCommand.SerializedSize)
            {
                throw new InvalidOperationException(
                    $"输入 chunk（tick {Tick}）的 payload 只有 {PayloadLength} 字节，"
                    + $"一条输入命令需要 {InputCommand.SerializedSize} 字节");
            }

            return InputCommand.ReadFrom(payload, PayloadOffset);
        }

        /// <summary>
        /// 把 payload 原样装进 <paramref name="buffer"/> 供按字段读出，装完读游标在起点。
        /// <para>
        /// 状态哈希、完整快照、QA 打点都走这条路，**不在本类里另写一套小端解码**：
        /// 小端拼装的规则读写必须严格互逆，只要它在工程里有第二份实现，就会有改一份漏一份的那天
        /// （理由见 <see cref="StateBuffer"/> 文件注释）。
        /// 典型用法：<c>chunk.LoadPayloadInto(buffer); ulong hash = buffer.ReadULong();</c>
        /// </para>
        /// </summary>
        /// <param name="buffer">接收用的缓冲，不可为空；原有内容会被覆盖。</param>
        /// <exception cref="ArgumentNullException">buffer 为 null。</exception>
        public void LoadPayloadInto(StateBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (PayloadLength == 0)
            {
                buffer.Reset();
                return;
            }

            buffer.LoadFrom(payload, PayloadOffset, PayloadLength);
        }

        /// <summary>
        /// 把 payload 当作 QA 打点的说明文本读出来（<see cref="ReplayFormat.ChunkType.QaMarker"/>）。
        /// <para><b>会分配一个字符串</b>——QA 打点是人按热键产生的，一局也就几条，不在高频路径上。</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">类型不是 QaMarker。</exception>
        public string ReadQaMarkerLabel()
        {
            if (RawType != (byte)ReplayFormat.ChunkType.QaMarker)
            {
                throw new InvalidOperationException(
                    $"这条 chunk 的类型是 {DescribeType()}，不是 QA 打点，不能按打点说明解读");
            }

            if (PayloadLength == 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(payload, PayloadOffset, PayloadLength);
        }

        /// <summary>一行摘要，给日志和回放检视器用。</summary>
        public override string ToString()
        {
            return $"chunk {DescribeType()} @tick {Tick}，payload {PayloadLength} 字节";
        }

        /// <summary>类型的可读写法：认得的写枚举名，认不得的写「未知(N)」。</summary>
        private string DescribeType()
        {
            return IsKnownType ? Type.ToString() : $"未知({RawType})";
        }
    }
}
