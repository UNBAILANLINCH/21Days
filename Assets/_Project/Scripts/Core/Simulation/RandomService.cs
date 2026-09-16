// 职责：IRandomService 的唯一实现——持有主种子，按「主种子 + 流名哈希」派生每条流的种子，并把流缓存成单例。
// 为什么不能复用：工程内没有任何随机数服务；能拿到的 UnityEngine.Random / System.Random 都被明令禁用
//   （理由见 IRandomStream.cs 文件头）。
// 为什么不能扩展到已有文件：这一层的职责是「派生 + 缓存」，与 XorShiftRandomStream 的「产生数」是两件事——
//   合在一起的话，换发生器要连派生策略一起改，写测试也没法只验其中一半。Core/ 下别的服务更接不住。

using System;
using System.Collections.Generic;

namespace Game.Core.Simulation
{
    /// <summary>
    /// 确定性随机数服务。一个主种子扇出成一组互不干扰的流，流的语义约定见 <see cref="IRandomService.Stream"/>。
    /// <para>
    /// <b>派生方式</b>：流种子 = 主种子 ^ 流名的 FNV-1a 64 位哈希，再由
    /// <see cref="XorShiftRandomStream.Scramble"/>（splitmix64）雪崩成初始状态。
    /// 全程只有整数运算与字符串内容，不掺进程地址、时间、平台信息——
    /// 所以"同样的主种子 + 同样的流名"在任何机器任何次运行上都得到同一条序列。
    /// </para>
    /// <para>
    /// <b>不读配置</b>：主种子从构造参数进来，本类不碰 ScriptableObject 也不碰容器。
    /// 谁来定种子、在哪注册，是接线那一层的事。
    /// </para>
    /// <para>
    /// <b>主种子只有一个正当的改法</b>：<see cref="Reseed"/>，而且只给回放载入用。实时游戏里它是定死的。
    /// </para>
    /// <para>线程约定：只在逻辑线程调用，字典没有加锁。</para>
    /// </summary>
    public sealed class RandomService : IRandomService
    {
        /// <summary>逻辑流前缀。状态进快照、参与状态哈希、回放时恢复。</summary>
        public const string LogicPrefix = "logic.";

        /// <summary>表现流前缀。不进快照、不参与哈希，表现层随便用。</summary>
        public const string ViewPrefix = "view.";

        /// <summary>FNV-1a 64 位的两个常数，写死在这里（理由见 <see cref="HashName"/>）。</summary>
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// 流名 → 流实例。一个名字只 new 一次：<see cref="Stream"/> 每 tick 都会被调，
        /// 每次新建等于每次把流重置回初始状态，那这条流就永远只吐第一个数了。
        /// <para>
        /// 按序数比较（<see cref="StringComparer.Ordinal"/>）：默认比较器会受当前区域文化影响，
        /// 土耳其语环境下 "I" 和 "i" 的折叠规则就和别处不一样——流名匹配不该随机器的语言设置变。
        /// </para>
        /// </summary>
        private readonly Dictionary<string, IRandomStream> streams =
            new Dictionary<string, IRandomStream>(StringComparer.Ordinal);

        /// <summary>
        /// 主种子。不是 readonly 的唯一理由是 <see cref="Reseed"/>——回放载入时要把它换回录像里那个值。
        /// 除了那一处，运行期谁都不该改它。
        /// </summary>
        private ulong masterSeed;

        /// <param name="masterSeed">
        /// 主种子。所有流的序列都由它唯一决定，回放时必须原样喂回同一个值。
        /// 允许传 0（派生与雪崩都处理得了），但按约定 0 表示"没指定"，
        /// 要随机开局请让调用方先取 <see cref="CreateSeedFromGuid"/> 并把结果记进录像。
        /// </param>
        public RandomService(ulong masterSeed)
        {
            this.masterSeed = masterSeed;
        }

        /// <summary>
        /// 当前生效的主种子。录像要把它写进头部，否则回放无从复现；
        /// 放录像时它会被 <see cref="Reseed"/> 换成录像头部记的那个值。
        /// </summary>
        public ulong MasterSeed => masterSeed;

        /// <summary>
        /// 取名为 <paramref name="name"/> 的流，同名永远返回同一个实例。
        /// 热路径（已建好的流）只有一次空判断加一次字典查找，零分配。
        /// </summary>
        public IRandomStream Stream(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (streams.TryGetValue(name, out IRandomStream stream))
            {
                return stream;
            }

            // 冷路径：一条流一辈子只走一次，校验和建流的开销都摊没了。
            if (name.Length == 0)
            {
                throw new ArgumentException("流名不能是空字符串，按约定用 logic.* 或 view.* 前缀。", nameof(name));
            }

            stream = new XorShiftRandomStream(DeriveSeed(name));
            streams.Add(name, stream);
            return stream;
        }

        /// <summary>
        /// 换主种子，并把**已经建好的每条流**按新主种子重新派生、原地重设回各自的初始状态。
        /// <para>
        /// <b>回放专用。</b>唯一的正当调用点是回放载入（录像头部记的主种子和当前这次运行不是同一个），
        /// 而且要在「用起始快照恢复世界」之前调——恢复快照会把 <c>logic.*</c> 流的状态再覆盖一遍，
        /// 顺序反了就等于把刚恢复好的流状态冲掉。
        /// <b>实时游戏过程中调它，等于把随机序列拦腰截断</b>：所有流当场跳回开头，
        /// 这一局后半段的每一次判定都和前半段属于两串不相干的数，录下来的回放也就再没法重现。
        /// </para>
        /// <para>
        /// <b>为什么是原地重设，不是换实例</b>：玩法很可能已经把 <see cref="IRandomStream"/>
        /// 引用缓存进了自己的字段。换实例的话，这些字段仍指向旧对象——一半换了、一半没换，
        /// 在回放里表现成「某些系统按新种子在抽、另一些还按旧种子」的缓慢漂移，比崩溃难查得多。
        /// （同样的理由见 <see cref="InputSourceSwitch"/>：那里换的也是指向，不是实例。）
        /// 所以这里保持对象身份不变，只改它们的 <see cref="IRandomStream.State"/>。
        /// </para>
        /// </summary>
        /// <param name="masterSeed">新的主种子，通常来自录像头部。</param>
        public void Reseed(ulong masterSeed)
        {
            this.masterSeed = masterSeed;

            foreach (KeyValuePair<string, IRandomStream> entry in streams)
            {
                // 借一条同种子的新流取初始状态，再把状态搬进老实例，而不是在这里照抄一遍雪崩。
                // 「种子 → 初始状态」这步换算只有流实现自己知道（XorShiftRandomStream 构造时过一遍 splitmix64），
                // 在本类里复制一份，换发生器的那天必然漏掉这一处，且漏了不报错、只表现为回放对不上。
                // 这点分配只发生在载入回放时（几十条流、一次），不在任何每 tick 路径上。
                entry.Value.State = new XorShiftRandomStream(DeriveSeed(entry.Key)).State;
            }
        }

        /// <summary>
        /// 生成一个一次性随机主种子，给"配置里种子填 0 = 这局随机来"的场景用。
        /// <para>
        /// 用 <see cref="Guid.NewGuid"/> 的哈希凑 64 位——工程里 Core/Telemetry/TelemetryService.cs
        /// 生成会话 id 就是这个先例。<b>不用 UnityEngine.Random 取种子</b>：那正是本服务要禁掉的东西，
        /// 而且它带全局状态，取一次种子就把别处用它的表现随机一起搅了。
        /// </para>
        /// <para>
        /// 这个方法本身不确定（每次调用结果都不同），所以只该在"开一局新的"时调一次，
        /// 把返回值记进录像；回放时必须把录像里那个数原样传进构造函数，绝不能再调它。
        /// </para>
        /// </summary>
        public static ulong CreateSeedFromGuid()
        {
            // 一个 Guid 的哈希只有 32 位，取两个拼满 64 位；再过一遍雪崩，
            // 免得 GetHashCode 的折叠在高低两半之间留下可见规律。
            ulong high = (uint)Guid.NewGuid().GetHashCode();
            ulong low = (uint)Guid.NewGuid().GetHashCode();
            ulong seed = XorShiftRandomStream.Scramble((high << 32) | low);

            // 0 按约定表示"没指定种子"，别让随机生成的结果撞上这个含义（概率 2^-64，但含义冲突是真的）。
            return seed == 0UL ? 1UL : seed;
        }

        /// <summary>
        /// 主种子 + 流名 → 该流的种子。异或就够了：真正把种子摊开的是流构造函数里的 splitmix64 雪崩，
        /// 这一步只需要保证映射确定、且不同流名几乎不撞（64 位哈希，量级几十条流，撞的概率可以忽略）。
        /// </summary>
        private ulong DeriveSeed(string name)
        {
            return masterSeed ^ HashName(name);
        }

        /// <summary>
        /// 流名 → 64 位哈希（FNV-1a，把每个 UTF-16 码元拆成两个字节依次吃进去）。
        /// <para>
        /// <b>为什么不用 string.GetHashCode()</b>：.NET 的字符串哈希带随机化种子，同一个字符串
        /// 在两次进程里就可能得到不同的值（跨平台跨运行时更别提）。拿它派生种子，等于每次重开游戏
        /// 都换一套随机流——录像回放当场对不上，而且因为"每次都不一样"，排查时还容易误判成别处的锅。
        /// FNV-1a 只有异或和乘法，常数写死在本文件里，结果永远只由字符串内容决定。
        /// </para>
        /// <para>拆成两个字节而不是直接吃 16 位码元，是为了让哈希与"字符串怎么编码"无关，换实现也对得上。</para>
        /// </summary>
        private static ulong HashName(string name)
        {
            unchecked
            {
                ulong hash = FnvOffsetBasis;
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    hash = (hash ^ (byte)c) * FnvPrime;
                    hash = (hash ^ (byte)(c >> 8)) * FnvPrime;
                }

                return hash;
            }
        }
    }
}
