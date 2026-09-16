// 职责：把 IStateWriter 写出的字节按同样的布局读回来——回放系统里「字节 → 世界状态」那一半的契约。
//   模块的 IReplayState.Deserialize 只认这个接口，恢复快照和漂移后续跑都走它。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何状态反序列化设施。Core/Save 那套是 JSON 存档，理由见 IStateWriter 文件头
//      （面向人读与版本迁移 vs 定长二进制、高频、零分配、可哈希，两组要求逐条相反）。
//   2. 扩展不行：读写是一对必须逐字对称的契约，但**刻意拆成两个接口**而不是合成一个 IStateSerializer：
//      IReplayState.Serialize 只该拿到写的能力、Deserialize 只该拿到读的能力。合成一个接口的话，
//      Serialize 里能写出「读一下刚写的值」这种代码，Deserialize 里能写出「顺手补写一个字段」——
//      两边一旦不对称，字节布局就毁了，而这种毁法编译器一声不吭。
//      （实现类 StateBuffer 同时实现两个接口，这是实现细节，见那个文件的文件头。）
//   3. Simulation/InputCommand.ReadFrom 是「一条定长输入命令」的专用读法，字段与偏移量写死，不通用。

using UnityEngine;

namespace Game.Core.Replay
{
    /// <summary>
    /// 状态读取器：按小端字节序把缓冲里的定长基础类型逐个读回。
    /// <para>
    /// **读的顺序必须和写的顺序逐字对应**。这里没有字段名、没有长度前缀、没有类型标记——
    /// 定长二进制的全部信息就是「第几个字节是什么」，一旦读写顺序错开一个字段，
    /// 后面所有字段跟着错位，而且大概率不会抛异常，只会读出一堆看起来合理的垃圾值。
    /// 所以 <see cref="IReplayState.Serialize"/> 与 <see cref="IReplayState.Deserialize"/> 必须写成
    /// 逐行镜像的两段代码，改一边就得改另一边。
    /// </para>
    /// <para>越界读取由实现抛异常（不是返回默认值）：录像被截断或格式版本不匹配时，要立刻炸，不要静默续跑。</para>
    /// <para>分配约定：实现复用缓冲，稳态零堆分配。线程约定：只在逻辑线程调用，实现不加锁。</para>
    /// </summary>
    public interface IStateReader
    {
        /// <summary>读一个布尔值（1 字节，非 0 即 true）。</summary>
        bool ReadBool();

        /// <summary>读一个字节。</summary>
        byte ReadByte();

        /// <summary>读一个 16 位有符号整数（小端，2 字节）。</summary>
        short ReadShort();

        /// <summary>读一个 16 位无符号整数（小端，2 字节）。</summary>
        ushort ReadUShort();

        /// <summary>读一个 32 位有符号整数（小端，4 字节）。</summary>
        int ReadInt();

        /// <summary>读一个 32 位无符号整数（小端，4 字节）。</summary>
        uint ReadUInt();

        /// <summary>读一个 64 位有符号整数（小端，8 字节）。</summary>
        long ReadLong();

        /// <summary>读一个 64 位无符号整数（小端，8 字节）。</summary>
        ulong ReadULong();

        /// <summary>
        /// 读一个单精度浮点数（小端，4 字节）。
        /// 读回来的是**规范化之后**的值：写进去的 <c>-0.0f</c> 读出来是 <c>+0.0f</c>，
        /// 写进去的任何 NaN 读出来都是同一个 NaN。这是刻意的，理由见 <see cref="IStateWriter.WriteFloat"/>。
        /// </summary>
        float ReadFloat();

        /// <summary>读一个二维向量（先 x 后 y，共 8 字节）。</summary>
        Vector2 ReadVector2();
    }
}
