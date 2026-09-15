// 职责：定时器的取消凭证——一个只读值类型，Dispose 即取消。
// 为什么新建：不能把「已注册的定时器」用 int id 裸暴露出去（槽位复用后旧 id 会误杀新定时器），
// 也不能返回 TimerService 的内部条目对象（外部就能改到内部状态）。需要一个独立的值类型做凭证。

using System;

namespace Game.Core.Timing
{
    /// <summary>
    /// 定时器句柄。持有槽位 id 与代数：槽位被回收复用后代数会自增，
    /// 于是旧句柄 Dispose 时对不上代数，不会误取消复用该槽位的新定时器。
    /// 默认值（default）是一个永远无效的空句柄，Dispose 无副作用。
    /// </summary>
    public readonly struct TimerHandle : IDisposable
    {
        private readonly TimerService owner;
        private readonly int id;
        private readonly int generation;

        internal TimerHandle(TimerService owner, int id, int generation)
        {
            this.owner = owner;
            this.id = id;
            this.generation = generation;
        }

        /// <summary>定时器是否还在等待触发。已触发的 Delay、已取消的、空句柄都是 false。</summary>
        public bool IsActive => owner != null && owner.IsActive(id, generation);

        /// <summary>取消定时器。重复调用、对空句柄调用都安全。</summary>
        public void Dispose()
        {
            if (owner == null)
            {
                return;
            }

            owner.Cancel(id, generation);
        }
    }
}
