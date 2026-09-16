// 职责：把定长基础类型按固定字节布局写进一段字节缓冲——回放系统里「世界状态 → 字节」那一半的契约。
//   模块的 IReplayState.Serialize 只认这个接口，看不到底层数组，也管不着这些字节最后是进快照还是进哈希。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何状态序列化设施。唯一沾边的是 Core/Save/ISaveService，但那是 JSON 存档，
//      每一条设计取舍都跟这里相反——它面向人读、面向跨版本迁移、一局存一两次；这里要的是定长二进制、
//      每秒一次、稳态零分配、写出来的字节能直接拿去算哈希。拿 JSON 顶这个位置会出两类硬伤：
//      每次序列化都产生字符串与中间对象（每秒一次的路径上持续制造 GC 压力），
//      且字段顺序由反射/序列化器实现决定而不由代码顺序决定——同一个世界两次序列化可能得到不同字节，
//      算出来的哈希也就不同，回放会报一个根本不存在的漂移。所以不是「懒得用存档那套」，是用不了。
//   2. 扩展不行：不能把写入能力加进 ISaveData / ISaveService。存档的兼容策略是「老档必须能读」，
//      要一路往下兼容；回放格式的策略是「布局一改老录像整份作废」，靠版本号硬拒。
//      两套相反的兼容策略塞进同一个接口，以后改任何一边都得先证明不会毁掉另一边。
//   3. Simulation/InputCommand.WriteTo 也不是可扩展的落点：那是「一条定长输入命令」的专用写法，
//      字段与偏移量写死在那个结构里；世界状态的字段数与布局由各模块自己决定，套不进固定结构。

using UnityEngine;

namespace Game.Core.Replay
{
    /// <summary>
    /// 状态写入器：把定长基础类型按小端字节序追加进缓冲。回放系统的状态哈希与完整快照都从这里出字节。
    /// <para>
    /// **方法按类型显式命名，不用重载**（<c>WriteInt</c> 而不是 <c>Write(int)</c>）。重载在隐式数值转换下
    /// 会静默选错——<c>writer.Write(someShort)</c> 可能挑中 int 重载写成 4 字节，而 <c>Deserialize</c> 那边
    /// 按 short 读 2 字节，从此后面所有字段整体错位。这类错位在哈希上表现为「莫名其妙的漂移」，
    /// 排查代价极高，而显式命名让它在编译期就写不出来。
    /// </para>
    /// <para>
    /// **写入顺序即字节布局**：<see cref="IReplayState.Deserialize"/> 必须以完全相同的顺序读回。
    /// 增删字段或调换顺序都会改变布局，老录像随即作废，**必须同步升回放格式版本号**。
    /// </para>
    /// <para>分配约定：实现必须预分配缓冲并复用，稳态零堆分配。线程约定：只在逻辑线程调用，实现不加锁。</para>
    /// </summary>
    public interface IStateWriter
    {
        /// <summary>写一个布尔值，占 1 字节，值恒为 0 或 1（不写别的非零值，免得同一个 true 出两种字节）。</summary>
        void WriteBool(bool value);

        /// <summary>写一个字节。</summary>
        void WriteByte(byte value);

        /// <summary>写一个 16 位有符号整数，小端，占 2 字节。</summary>
        void WriteShort(short value);

        /// <summary>写一个 16 位无符号整数，小端，占 2 字节。</summary>
        void WriteUShort(ushort value);

        /// <summary>写一个 32 位有符号整数，小端，占 4 字节。</summary>
        void WriteInt(int value);

        /// <summary>写一个 32 位无符号整数，小端，占 4 字节。</summary>
        void WriteUInt(uint value);

        /// <summary>写一个 64 位有符号整数，小端，占 8 字节。</summary>
        void WriteLong(long value);

        /// <summary>
        /// 写一个 64 位无符号整数，小端，占 8 字节。
        /// 确定性随机流的 <c>IRandomStream.State</c> 就是这个类型，快照必须存它才能从中途续跑。
        /// </summary>
        void WriteULong(ulong value);

        /// <summary>
        /// 写一个单精度浮点数，小端，占 4 字节。
        /// <para>
        /// **实现必须先规范化位模式再写**：<c>-0.0f</c> 归为 <c>+0.0f</c>，任何 NaN 归为同一个固定位模式。
        /// 这两种值都是「数值相等但字节不同」，不规范化就会让两个完全一样的世界算出不同哈希，
        /// 也就是**误报漂移**。误报比漏报更糟——它会让人花半天去查一个根本不存在的问题，
        /// 查完还会开始怀疑整套校验机制。规范化的细节见 <see cref="StateBuffer.WriteFloat"/>。
        /// </para>
        /// </summary>
        void WriteFloat(float value);

        /// <summary>写一个二维向量：先 x 后 y，各按 <see cref="WriteFloat"/> 的规则写，共 8 字节。</summary>
        void WriteVector2(Vector2 value);
    }
}
