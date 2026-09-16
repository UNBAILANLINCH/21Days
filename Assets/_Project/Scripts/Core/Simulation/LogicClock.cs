// 职责：ILogicClock 的唯一实现，持有 tick 序号，只由 SimulationRunner 推进。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：Core/Timing/LocalClock 实现的是 IClock，读的是 Time.time / Time.deltaTime，
//      报的是渲染帧口径的时间，跟 tick 没有任何换算关系。
//   2. 扩展不行：不能把 tick 计数塞进 LocalClock。那会让同一个对象同时报两种口径的时间，
//      注入方一不留神就在逻辑里读到了渲染帧的 DeltaTime——这正是重放对不上的头号来源。
//      分成两个类之后，「注入 IClock」和「注入 ILogicClock」在调用点就是两个不同的决定。

using System;

namespace Game.Core.Simulation
{
    /// <summary>
    /// 逻辑时钟。<see cref="Advance"/> / <see cref="Reset"/> 是**只给 <see cref="SimulationRunner"/> 用的**
    /// 推进口，故意只开在实现类上：注入方拿到的是 <see cref="ILogicClock"/>，想推也推不动。
    /// <para>
    /// <see cref="SimTime"/> 用 <c>tick ÷ tickRate</c> 现算，不做浮点累加。累加每 tick 都会引入一点误差，
    /// 跑上几万 tick 之后录制与重放的时间会缓慢错开；现算则是同一个 tick 序号永远算出同一个值。
    /// 中间用 <c>double</c> 是为了让 tick 数很大时商仍然准确，最后再落回 <c>float</c>。
    /// </para>
    /// </summary>
    public sealed class LogicClock : ILogicClock
    {
        private readonly int tickRate;
        private readonly float fixedDeltaTime;

        private long tick;

        /// <param name="tickRate">每秒多少个逻辑 tick，必须为正（默认取 <see cref="SimulationConfig.TickRate"/>）。</param>
        public LogicClock(int tickRate)
        {
            if (tickRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "tickRate 必须大于 0");
            }

            this.tickRate = tickRate;
            fixedDeltaTime = 1f / tickRate;
        }

        /// <summary>当前 tick 序号，从 0 开始。语义见 <see cref="ILogicClock.Tick"/>。</summary>
        public long Tick => tick;

        /// <summary>一个 tick 的固定时长秒数。</summary>
        public float FixedDeltaTime => fixedDeltaTime;

        /// <summary>逻辑累计秒数。</summary>
        public float SimTime => (float)(tick / (double)tickRate);

        /// <summary>每秒 tick 数。存档、录制头要记下它——步长不一样的两次运行没法互相重放。</summary>
        public int TickRate => tickRate;

        /// <summary>
        /// 走到下一个 tick。**只由 <see cref="SimulationRunner.AdvanceOneTick"/> 在这一 tick 的所有
        /// <see cref="ISimulationStep"/> 都跑完之后调用**，别的地方调就等于凭空吞掉一个 tick 的逻辑。
        /// </summary>
        public void Advance()
        {
            tick++;
        }

        /// <summary>归零，回到第 0 个 tick。开始一次新的录制或重放时调。</summary>
        public void Reset()
        {
            tick = 0;
        }

        /// <summary>
        /// 把 tick 计数直接挪到 <paramref name="target"/>，**一步逻辑都不跑**。
        /// 和 <see cref="Advance"/> / <see cref="Reset"/> 一样只开在实现类上，
        /// **只由 <see cref="SimulationRunner.SeekClockTo"/> 调用**；完整语义与那条「这不是快进」的警告写在那里。
        /// </summary>
        /// <param name="target">目标 tick 序号，不能为负。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="target"/> 为负数。</exception>
        public void SeekTo(long target)
        {
            if (target < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(target), target, "tick 序号不能为负");
            }

            tick = target;
        }
    }
}
