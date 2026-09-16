// 职责：一段逻辑推进的契约。所有要按固定步长跑的玩法逻辑都实现它，由 SimulationRunner 按注册顺序串行调用。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：VContainer 的 ITickable 形状很像，但它是**渲染帧**回调——每帧调一次、间隔不定、
//      没有 tick 序号也没有输入快照，实现方拿不到重放需要的任何东西。IGameService 是「启动时初始化一次」，
//      更不沾边。工程内没有第二个「每逻辑帧调一次」的接口。
//   2. 扩展不行：不能给 ITickable 加重载——那是 VContainer 包里的类型，改不了；
//      在本工程定义一个同名扩展也只会让「Tick() 到底是渲染帧还是逻辑帧」变得更含糊。

namespace Game.Core.Simulation
{
    /// <summary>
    /// 一步逻辑推进。实现方只能从 <see cref="SimulationContext"/> 里取输入、随机与时间，
    /// 不许读 <c>UnityEngine.Time</c>、<c>Input</c>、<c>UnityEngine.Random</c>——
    /// 但凡有一处读了，录下来的输入就重放不出同一份结果。
    /// <para>
    /// 调用顺序 = 注册顺序，串行，同一 tick 内不并发。上下文按 <c>in</c> 传递（只读引用，不复制不装箱），
    /// 实现里不要把它存下来：它只在这一次调用内有意义。
    /// </para>
    /// <para>每 tick 都会被调到，所以实现里不要 new、不要拼字符串、不要打日志。</para>
    /// </summary>
    public interface ISimulationStep
    {
        /// <summary>推进一个逻辑 tick。</summary>
        /// <param name="context">这一 tick 的序号、步长、输入命令与随机源。</param>
        void Step(in SimulationContext context);
    }
}
