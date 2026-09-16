// 职责：按名字取随机流的契约——把「主种子」这一个数扇出成一组互不干扰的确定性流。
// 为什么不能复用：工程内没有任何随机数抽象（详见 IRandomStream.cs 文件头：能拿到的
//   UnityEngine.Random / System.Random 都不确定、也没有状态存取，正是本任务要禁掉的）。
// 为什么不能扩展到已有文件：这是新的一层框架能力，Core/ 下没有哪个服务契约的职责能容下它；
//   而它又必须与 IRandomStream 分开——一个是「发号的窗口」，一个是「号码本身」，
//   合成一个接口会让「取流」和「取数」搅在一起，调用方无法只持有一条流。

namespace Game.Core.Simulation
{
    /// <summary>
    /// 确定性随机数服务：持有主种子，按流名派生出彼此独立的 <see cref="IRandomStream"/>。
    /// <para>
    /// 主种子一定，所有流的序列就全定了——这是重放能对上的根。
    /// 主种子由外部给（新开一局时随机生成一次并记进录像，回放时原样喂回来）。
    /// </para>
    /// </summary>
    public interface IRandomService
    {
        /// <summary>
        /// 取名为 <paramref name="name"/> 的流。种子由「主种子 + 流名哈希」派生，
        /// 所以每条流的序列互相独立：一条流多抽几个数，不会让别的流跟着错位。
        /// <para>
        /// <b>同一个 name 永远返回同一个实例</b>。这一条不是优化而是正确性：
        /// 每次都新建等于每次都把流重置回初始状态，那这条流就永远只吐第一个数了。
        /// </para>
        /// <para><b>流名约定（两类，语义完全不同，不许混用）：</b></para>
        /// <list type="bullet">
        /// <item><description>
        /// <c>logic.*</c> —— 逻辑流。影响游戏状态的一切随机：伤害浮动、暴击判定、掉落、AI 选招。
        /// 状态进快照、参与状态哈希、回放时恢复。
        /// </description></item>
        /// <item><description>
        /// <c>view.*</c> —— 表现流。只影响看得见听得见的东西：粒子朝向、音效变调、受击抖动方向。
        /// 不进快照、不参与哈希、随便用，多抽少抽都不影响逻辑。
        /// </description></item>
        /// </list>
        /// <para>
        /// <b>为什么非分不可</b>：表现层一旦和逻辑共用一条序列，"这次多播了一个特效"就会把后面
        /// 所有逻辑随机往后挪一位——暴击变没暴击，掉落变了另一件——重放必炸，而且炸得毫无头绪
        /// （表现代码看起来跟逻辑八竿子打不着）。分开之后，表现层爱抽多少抽多少，逻辑序列纹丝不动。
        /// 这也是为什么表现流可以不进快照：它本来就不该被任何人校验。
        /// </para>
        /// </summary>
        /// <param name="name">流名，区分大小写、按序数比较。约定用 <c>logic.</c> / <c>view.</c> 前缀。</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="name"/> 为 null。</exception>
        /// <exception cref="System.ArgumentException"><paramref name="name"/> 为空字符串。</exception>
        IRandomStream Stream(string name);
    }
}
