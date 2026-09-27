// 职责：一次演出播放的结果快照（id、结果、时长），供 PlayAsync 的返回值使用。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有任何「按时间轴编排的演出」概念可复用；
// Dialogue 是内容驱动推进机、Core 不许出现玩法名词，扩展不了，只能新建。

namespace Game.Performance
{
    /// <summary>一次演出播放的结果快照。</summary>
    public readonly struct PerformanceResult
    {
        public PerformanceResult(string id, PerformanceOutcome outcome, float durationSeconds)
        {
            Id = id;
            Outcome = outcome;
            DurationSeconds = durationSeconds;
        }

        /// <summary>演出 id（Addressables 地址）。</summary>
        public string Id { get; }

        /// <summary>播放结果分类。</summary>
        public PerformanceOutcome Outcome { get; }

        /// <summary>实际播放时长（秒）。</summary>
        public float DurationSeconds { get; }
    }
}
