// 职责：一条确定性随机序列的契约——取数、限定范围、取 [0,1)，以及把内部状态整个存下来 / 设回去。
// 为什么不能复用：工程内没有任何随机数抽象。能直接拿到的只有两样，而这两样正是本任务要禁掉的：
//   1) UnityEngine.Random —— 全局静态状态，读不出也写不回完整状态，跨 Unity 版本不保证同种子同序列；
//   2) System.Random —— 实现随 .NET 运行时版本变化（.NET Core 3.0 起换过算法），跨运行时/平台不保证。
//   两者都没有「存状态 / 恢复状态」这一对操作，而回放要从快照续跑，这是硬需求，扩展不出来。
// 为什么不能扩展到已有文件：Core/ 下现有契约（Timing/IClock、Telemetry/ITelemetryClock 等）都是「读外界」，
//   没有一个的职责是「产生数」；把随机数塞进时间接口，名字和职责都说不通。

namespace Game.Core.Simulation
{
    /// <summary>
    /// 一条确定性随机流：同一个初始状态跑出同一串数，任何时刻都能把状态整份存下来、之后设回去续跑。
    /// <para>
    /// 「确定性」在这里是字面意思——同一串调用序列，在 Windows 编辑器、IL2CPP 安卓包、CI 无头机上
    /// 必须产出**逐位相同**的结果，否则输入录制 + 逻辑重放的 bug 复现就是假的。
    /// 所以实现只许用整数运算（移位、异或、乘法），不许碰任何浮点数学函数（libm 各平台实现有差异）。
    /// </para>
    /// <para>线程约定：只在逻辑线程调用，实现内部不加锁。</para>
    /// <para>分配约定：所有方法都在每 tick 热路径上，实现必须零分配。</para>
    /// </summary>
    public interface IRandomStream
    {
        /// <summary>
        /// 取下一个 32 位无符号整数，均匀分布在 [0, 2^32)。这是唯一的原始出口，
        /// <see cref="Range"/> 与 <see cref="Value01"/> 都由它派生。
        /// </summary>
        uint NextUInt();

        /// <summary>
        /// 取 [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>) 上的整数，含下界不含上界。
        /// 全程整数运算，每次调用**恰好消耗一个** <see cref="NextUInt"/>。
        /// </summary>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// <paramref name="minInclusive"/> 不小于 <paramref name="maxExclusive"/> 时抛出，消息里带上收到的两个值。
        /// </exception>
        int Range(int minInclusive, int maxExclusive);

        /// <summary>
        /// 取 [0, 1) 上的浮点数，含 0 不含 1。由整数位直接构造，不经过任何浮点数学函数，
        /// 因此跨平台逐位相同。粒度是 1/2^24（float 的有效位就这么多，再细也存不下）。
        /// </summary>
        float Value01();

        /// <summary>
        /// 发生器的完整内部状态，可读可写，用于快照与恢复。
        /// <para>
        /// 契约（实现必须保证）：<c>ulong s = stream.State;</c> 取走之后不管又取了多少个数，
        /// 只要 <c>stream.State = s;</c> 设回去，后续序列就与「压根没中断过」完全相同。
        /// 这要求状态是**全部**信息——不许有第二个字段、缓存的半个数、或者打包时丢掉的位。
        /// </para>
        /// <para>
        /// 谁该存：只有 <c>logic.*</c> 流的状态进快照、进状态哈希；<c>view.*</c> 流不存也不校验
        /// （语义见 <see cref="IRandomService.Stream"/>）。
        /// </para>
        /// </summary>
        ulong State { get; set; }
    }
}
