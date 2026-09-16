// 职责：对「模块序列化出来的那一段字节」算 FNV-1a 64 位哈希。回放时每隔一段时间算一次，
//   拿它和录像里记下的那个值比，不等就说明重放已经和当初分叉了（漂移）。
//
// 为什么哈希必须由框架统一算、模块绝不自己实现（这条是本文件存在的全部理由，改代码前先读完）：
//   手写哈希是漂移排查里最阴的错误源。模块自己写 GetStateHash() 时，漏掉一个字段是常态——
//   新加了个字段忘了加进哈希、某个分支下少混了一个值，代码照样编译、照样跑，
//   哈希一直相等，重放**看起来**没漂移，于是你继续信任这套机制，直到某天发现录像根本复现不出 bug，
//   才回头发现校验早就是假的。漏报比不做更糟：它同时骗走了你的时间和你对工具的信任。
//   所以这里定死一条：**模块只负责往 IStateWriter 里写字节，框架对同一份字节算哈希**。
//   序列化和哈希共用同一段字节、同一条代码路径，就不可能出现「进了快照但没进哈希」的字段——
//   一个字段要么两边都在，要么两边都不在，而「两边都不在」在恢复快照时会立刻暴露成明显的行为错误。
//
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何字节哈希设施。能直接拿到的 object.GetHashCode / string.GetHashCode
//      恰恰是最不能用的——.NET 的字符串哈希按进程随机化种子，同一份数据在两次运行里值就不同；
//      它也只有 32 位，还不保证跨运行时版本稳定。拿它做漂移校验会得到满屏误报。
//   2. 扩展不行：不能把哈希塞进 StateBuffer「边写边算」。那样哈希就和某一个写入器实例绑死了，
//      没法对「从文件读回来的那段字节」重算一遍来验证录像自身完整性，也没法只对某个片段算。
//      拆开之后两边职责干净：缓冲只管字节怎么摆，哈希只管一段字节折成一个数。
//   3. Simulation/InputCommand 里没有哈希逻辑（它的 GetHashCode 是给字典用的，同样随实现变，不能外用）。
//
// 为什么选 FNV-1a 64：算法只有异或和乘法两步，没有查表、没有分支、不依赖平台字长与浮点，
//   在编辑器、IL2CPP 安卓包、CI 无头机上逐位相同；64 位的碰撞概率对「每秒比一次」这个量级足够低。
//   它不是密码学哈希，也不需要是——这里防的是程序 bug，不是有人伪造录像。

using System;

namespace Game.Core.Replay
{
    /// <summary>
    /// 状态字节的 FNV-1a 64 位哈希。回放系统用它把一整份世界状态折成一个 <see cref="ulong"/>，
    /// 逐周期和录像里记的值比对，不等即判定漂移。
    /// <para>
    /// 典型用法：<c>buffer.Reset(); provider.SerializeAll(buffer); ulong h = StateHasher.Compute(buffer);</c>
    /// ——注意哈希的输入就是快照的内容本身，两者永远同源。
    /// </para>
    /// <para>分配约定：纯整数运算，不分配。</para>
    /// </summary>
    public static class StateHasher
    {
        /// <summary>
        /// FNV-1a 64 位的初始值（offset basis）。公开出来是为了能分段累加：
        /// 从这个值起手，连着调几次 <see cref="Accumulate"/>，结果和一次性算整段完全相同。
        /// </summary>
        public const ulong OffsetBasis = 14695981039346656037ul;

        /// <summary>FNV-1a 64 位的乘数（FNV prime）。这两个常量是算法定义的一部分，改了就不是 FNV-1a 了。</summary>
        private const ulong Prime = 1099511628211ul;

        /// <summary>
        /// 对缓冲里**已写入的那一段**（前 <see cref="StateBuffer.Length"/> 个字节）算哈希。
        /// 容量里多出来的那些残留字节不参与——否则上一轮写得更长时留下的尾巴会混进来，
        /// 让两份内容相同的状态算出不同的值。
        /// </summary>
        /// <param name="buffer">刚写完状态的缓冲，不可为空。</param>
        /// <exception cref="ArgumentNullException">buffer 为 null。</exception>
        public static ulong Compute(StateBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            return Compute(buffer.GetBuffer(), 0, buffer.Length);
        }

        /// <summary>对 <paramref name="data"/> 的 [offset, offset + count) 这一段算哈希。</summary>
        /// <exception cref="ArgumentNullException">data 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">这一段超出了数组范围。</exception>
        public static ulong Compute(byte[] data, int offset, int count)
        {
            return Accumulate(OffsetBasis, data, offset, count);
        }

        /// <summary>
        /// 从 <paramref name="hash"/> 接着往下累加一段字节，返回新的哈希值。
        /// 用于状态分散在多段缓冲里的场合（先起手 <see cref="OffsetBasis"/>，再逐段喂进来）。
        /// <para>顺序敏感：同样几段字节换个顺序喂，结果不同——这正是我们要的，字段顺序本来就是状态的一部分。</para>
        /// </summary>
        /// <exception cref="ArgumentNullException">data 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">这一段超出了数组范围。</exception>
        public static ulong Accumulate(ulong hash, byte[] data, int offset, int count)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (offset < 0 || count < 0 || data.Length - offset < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "要计算哈希的区间超出了数组范围");
            }

            // unchecked：FNV 靠的就是 64 位乘法自然溢出回绕，这是算法的一部分，不是意外。
            // 工程默认就是 unchecked，这里写出来是给读代码的人看的——别顺手给项目开 /checked+ 把它改掉。
            unchecked
            {
                int end = offset + count;
                for (int i = offset; i < end; i++)
                {
                    // FNV-1a 的顺序是「先异或、后乘」（FNV-1 是反过来的，雪崩效应更差，别写混）。
                    hash ^= data[i];
                    hash *= Prime;
                }
            }

            return hash;
        }
    }
}
