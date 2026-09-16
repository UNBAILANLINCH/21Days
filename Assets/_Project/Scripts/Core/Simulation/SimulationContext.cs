// 职责：一个逻辑 tick 的全部输入面——tick 序号、固定步长、这一 tick 的输入命令、随机源。
//   ISimulationStep 能合法读到的东西全在这里，读不到的就是不许读的。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何「一帧的只读上下文」类型。TelemetryProps 是定长键值对容器，
//      AssetHandle / TimerHandle 是句柄，都不是这个语义。
//   2. 扩展不行：不能把这四项挂到 ILogicClock 上。时钟报的是时间，输入命令与随机源是另外两件事，
//      合在一起之后「哪些东西是这一 tick 的确定性输入」就没有一个地方能一眼看全，
//      而重放系统的正确性恰恰取决于这张清单是封闭的。
//   做成 readonly struct 而不是 class：每 tick 都要构造一个，class 就是每 tick 一次堆分配。

namespace Game.Core.Simulation
{
    /// <summary>
    /// 一个逻辑 tick 的上下文。**值类型、只读、零堆分配**，按 <c>in</c> 传给每个
    /// <see cref="ISimulationStep"/>。
    /// <para>
    /// 这是「确定性的边界」：逻辑只要严格只读这四项，同样的输入序列就一定推出同样的结果；
    /// 任何一处绕过它去读 <c>Time.deltaTime</c> / <c>Input</c> / <c>UnityEngine.Random</c>，
    /// 重放就会在某个 tick 悄悄分叉。
    /// </para>
    /// </summary>
    public readonly struct SimulationContext
    {
        /// <summary>构造一个 tick 上下文，只由 <see cref="SimulationRunner"/> 调用。</summary>
        /// <param name="tick">这一 tick 的序号，从 0 开始。</param>
        /// <param name="deltaTime">固定步长秒数，整局不变。</param>
        /// <param name="input">这一 tick 的输入命令快照。</param>
        /// <param name="random">确定性随机源。</param>
        public SimulationContext(long tick, float deltaTime, in InputCommand input, IRandomService random)
        {
            Tick = tick;
            DeltaTime = deltaTime;
            Input = input;
            Random = random;
        }

        /// <summary>这一 tick 的序号，从 0 开始。录制与重放按它逐条对齐。</summary>
        public long Tick { get; }

        /// <summary>这一 tick 的时长秒数。**固定值**，不是渲染帧的 deltaTime，整局都一样。</summary>
        public float DeltaTime { get; }

        /// <summary>这一 tick 的输入命令快照。已经是定格的值，不会在 Step 执行期间变。</summary>
        public InputCommand Input { get; }

        /// <summary>确定性随机源。逻辑里要随机数只能从这里取，取 <c>UnityEngine.Random</c> 会让重放分叉。</summary>
        public IRandomService Random { get; }
    }
}
