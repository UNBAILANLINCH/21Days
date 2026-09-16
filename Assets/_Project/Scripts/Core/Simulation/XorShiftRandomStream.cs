// 职责：IRandomStream 的唯一实现——xorshift64* 整数发生器，内部状态正好 64 位，State 就是状态本身。
// 为什么不能复用：工程内没有任何随机数实现；能拿到的 UnityEngine.Random / System.Random 都被明令禁用
//   （理由见 IRandomStream.cs 文件头：状态取不出、跨版本跨平台不保证同种子同序列）。
// 为什么不能扩展到已有文件：这是一份算法实现，与它最近的文件 IRandomStream.cs 是纯契约，
//   契约里塞实现会让「换一个发生器」变成改接口；而 Core/ 下别的服务职责都不是「产生数」。

using System;

namespace Game.Core.Simulation
{
    /// <summary>
    /// xorshift64*：Marsaglia 的三步异或移位（状态推进）+ 一次乘法混淆（输出加工）。
    /// <para>
    /// <b>为什么选它而不是 xorshift128+</b>：<see cref="State"/> 的类型是 <see cref="ulong"/>，
    /// 而 xorshift128+ 的内部状态是 128 位——128 位无损塞进 64 位在信息论上不可能，
    /// 任何"打包"都得丢一半，快照就不再无损。所以这里选了同族里**状态本来就是 64 位**的成员：
    /// 状态与 <c>State</c> 一比一对应，存取零损耗、零打包代码。
    /// 代价是周期从 2^128-1 降到 2^64-1，而一局游戏撑死也就 1e7 次调用，差着十二个数量级，够不着。
    /// </para>
    /// <para>
    /// <b>为什么跨平台一致</b>：全程只有无符号整数的移位、异或、乘法。这三样在 C# 里语义写死
    /// （<c>ulong</c> 的 <c>&gt;&gt;</c> 是逻辑移位，乘法按 2^64 取模回绕），x64 / ARM64、Mono / IL2CPP
    /// 都必须给出同一个位模式。没有浮点运算、没有 libm 调用、没有依赖运行时版本的库实现——
    /// 这三样正是 <c>System.Random</c> 与 <c>UnityEngine.Random</c> 对不上的原因。
    /// </para>
    /// <para>零分配：所有方法都只动一个 <see cref="ulong"/> 字段和栈上局部量。</para>
    /// </summary>
    public sealed class XorShiftRandomStream : IRandomStream
    {
        /// <summary>
        /// 输出混淆乘数（Vigna 在 "An experimental exploration of Marsaglia's xorshift generators,
        /// scrambled" 里给的 M_64）。裸 xorshift 的低位线性相关太明显，乘一下再取高位才够用。
        /// </summary>
        private const ulong OutputMultiplier = 2685821657736338717UL;

        /// <summary>splitmix64 的三个混合常数：递增步长（2^64/φ）与两个雪崩乘数。</summary>
        private const ulong SplitMixGamma = 0x9E3779B97F4A7C15UL;
        private const ulong SplitMixMultiplier1 = 0xBF58476D1CE4E5B9UL;
        private const ulong SplitMixMultiplier2 = 0x94D049BB133111EBUL;

        /// <summary>状态被外部设成 0 时的替身。取哪个非零值都行，这里沿用黄金分割常数。</summary>
        private const ulong NonZeroFallback = SplitMixGamma;

        /// <summary>float 的有效位是 24 位，所以 <see cref="Value01"/> 的粒度取 2^-24。</summary>
        private const int Value01ShiftBits = 8;
        private const float Value01Scale = 1.0f / 16777216.0f;

        /// <summary>发生器的全部状态。没有第二个字段——这正是 <see cref="State"/> 能无损存取的原因。</summary>
        private ulong state;

        /// <param name="seed">
        /// 种子。会先过一遍 <see cref="Scramble"/> 再当作初始状态，所以 0、1、2 这种低熵种子
        /// 也能得到彼此看不出关联的序列（裸 xorshift 用小种子开局，头几个数的高位全是 0）。
        /// </param>
        public XorShiftRandomStream(ulong seed)
        {
            state = Normalize(Scramble(seed));
        }

        /// <summary>
        /// 内部状态，直接就是发生器的全部状态（64 位，无任何打包、无任何丢弃）。
        /// <para>
        /// <b>怎么保证"存下来再设回去，后续序列与没中断时完全相同"</b>：<see cref="NextUInt"/> 是
        /// <see cref="state"/> 的纯函数——先由旧状态算出新状态并写回，再由新状态算出返回值，
        /// 全过程不读也不写别的字段，没有缓存的半个数、没有"下次再用"的余量。
        /// 于是 get 拿到的那个 <see cref="ulong"/> 就是全部信息，set 回去等于把发生器倒回那一刻，
        /// 之后每一步都是同一个纯函数在同一个输入上重跑，序列必然逐位相同。
        /// </para>
        /// <para>
        /// set 收到 0 会被换成一个固定非零值：xorshift 的全零状态是吸收态（0 进 0 出，之后永远吐 0）。
        /// 正常运行永远到不了 0——三步异或移位在 GF(2)^64 上都是可逆线性变换，复合仍是双射，
        /// 而 0 已经被 0 占了位，非零值不可能再映射到 0。所以这条兜底只会在"外部塞了个没初始化的 0"
        /// 时触发，快照往返（get 到的必然非零）一步都不受影响。
        /// </para>
        /// </summary>
        public ulong State
        {
            get => state;
            set => state = Normalize(value);
        }

        public uint NextUInt()
        {
            // Marsaglia 三元组 (12, 25, 27)：这组移位量能让状态遍历全部 2^64-1 个非零值。
            ulong x = state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            state = x;

            unchecked
            {
                // 取乘积的高 32 位：xorshift* 的高位质量最好，低位还残留着线性结构。
                // unchecked 是给乘法回绕上的保险——真开了 /checked 编译，这里会抛 OverflowException。
                return (uint)((x * OutputMultiplier) >> 32);
            }
        }

        /// <summary>
        /// 乘法缩放：把 [0, 2^32) 的整数等比例压进 [0, width)，全程整数，**恰好消耗一个** <see cref="NextUInt"/>。
        /// <para>
        /// 为什么不用拒绝采样（抽到落在余数区就重抽）换取绝对均匀：那样每次调用消耗的随机数个数
        /// 会随抽到的值变化。回放对不上时的第一件事是对比两次运行"第几次抽数开始分叉"，
        /// 消耗个数固定，分叉点就直接指向出错的那行逻辑；消耗个数浮动，差异会顺着序列一路放大，
        /// 排查时分不清哪些是原因、哪些是后果。代价是偏差至多 width/2^32（width=100 万时约万分之 0.02），
        /// 对游戏里的判定完全不可见。
        /// </para>
        /// </summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive),
                    maxExclusive,
                    $"Range 要求 minInclusive < maxExclusive，收到 minInclusive={minInclusive}、maxExclusive={maxExclusive}。");
            }

            // 用 long 算宽度：min=int.MinValue、max=int.MaxValue 时宽度是 2^32-1，int 装不下会溢出成负数。
            uint width = (uint)((long)maxExclusive - minInclusive);

            ulong scaled = (ulong)NextUInt() * width;

            // scaled >> 32 必定落在 [0, width)，加上下界后仍在 int 范围内；中间用 long 走一趟防溢出。
            return (int)(minInclusive + (long)(scaled >> 32));
        }

        /// <summary>
        /// 由整数位直接构造 [0, 1)：取高 24 位当分子，乘上 2^-24。
        /// <para>
        /// 这与"拼出 [1,2) 的位模式再减 1"得到的是**同一个数**（都是 k/2^24，k∈[0,2^24)），
        /// 但不需要位重解释——本工程 asmdef 里 <c>allowUnsafeCode=false</c>，而 <c>BitConverter</c> 的
        /// 单精度位转换 API 在不同 API 兼容级别下未必都在。
        /// 这两步运算在 IEEE-754 下都是**精确**的：k &lt; 2^24 能被 float 精确表示，转换无舍入；
        /// 乘 2 的幂只改指数位，也无舍入。没有舍入就没有"谁先舍入谁后舍入"的平台差异，
        /// 更没碰任何 libm 函数（<c>pow</c>、<c>ldexp</c> 这些才是漂移的来源）。
        /// </para>
        /// <para>取高位而不是低位，理由同 <see cref="NextUInt"/>：xorshift* 的高位质量更好。</para>
        /// </summary>
        public float Value01()
        {
            uint bits = NextUInt() >> Value01ShiftBits;
            return bits * Value01Scale;
        }

        /// <summary>
        /// splitmix64 雪崩函数：把低熵输入（0、1、2 这种，或者"主种子 ^ 流名哈希"这种只差几位的值）
        /// 摊成看不出关联的 64 位。它是双射，所以不同输入永远得到不同结果，不会把两条流撞成一条。
        /// <para>公开是给 <see cref="RandomService"/> 复用的：种子加工只该有一份实现。</para>
        /// </summary>
        public static ulong Scramble(ulong seed)
        {
            unchecked
            {
                ulong z = seed + SplitMixGamma;
                z = (z ^ (z >> 30)) * SplitMixMultiplier1;
                z = (z ^ (z >> 27)) * SplitMixMultiplier2;
                return z ^ (z >> 31);
            }
        }

        /// <summary>把全零状态换成非零替身，理由见 <see cref="State"/>。</summary>
        private static ulong Normalize(ulong candidate)
        {
            return candidate == 0UL ? NonZeroFallback : candidate;
        }
    }
}
