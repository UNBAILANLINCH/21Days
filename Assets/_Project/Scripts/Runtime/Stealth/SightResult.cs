// 职责：一次视线查询的结果类型。查询本身在 `StealthSight`，几何在 `StealthGeometry`。
//
// 为什么单独一个文件：它是 `StealthSight.TrySight` 的返回类型，会被调用方缓存与传阅；
// 与配置（`SightSettings`）分开，改结果的字段时不必碰配置文件的 diff。
//
// 设计取舍：只暴露标量与访问器，不把内部数组交出去——结果可能被缓存或被多人持有，
// 交出去就等于把数组的所有权交出去了。需要零分配的调用方走
// `StealthSight.TrySightNonAlloc`，自己传一个 `List<int>` 当缓冲区。
//
// 出处：`docs/design/features-spotlight/03_潜行与暗杀.md:129-136`（R24–R28 掩体与地形）。
namespace Game.Stealth
{
    /// <summary>
    /// 一次视线查询的结果。
    /// </summary>
    public readonly struct SightResult
    {
        private readonly int[] blockers;
        private readonly int blockerCount;

        internal SightResult(bool hasSight, int[] blockers, int blockerCount)
        {
            HasSight = hasSight;
            this.blockers = blockers;
            this.blockerCount = blockerCount < 0 ? 0 : blockerCount;
        }

        /// <summary>两点之间是否通视（true = 看得见，没有任何遮挡体拦住）。</summary>
        public bool HasSight { get; }

        /// <summary>是否被遮挡（<see cref="HasSight"/> 取反，给判定点一个正向名字用）。</summary>
        public bool IsBlocked => !HasSight;

        /// <summary>造成遮挡的遮挡体数量。</summary>
        public int BlockerCount => blockerCount;

        /// <summary>取第 <paramref name="index"/> 个造成遮挡的遮挡体 id；越界返回 -1。</summary>
        public int GetBlockerId(int index)
        {
            if (blockers == null || index < 0 || index >= blockerCount)
            {
                return -1;
            }

            return blockers[index];
        }

        /// <summary>第一个造成遮挡的遮挡体 id；没有则返回 -1。</summary>
        public int FirstBlockerId => GetBlockerId(0);
    }
}
