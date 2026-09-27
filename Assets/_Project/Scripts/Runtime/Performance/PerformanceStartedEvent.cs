// 职责：某段演出已开始播放这个事实事件。
// 为什么新建（复用 → 扩展 → 新建）：一个事件一个文件（EventConventions.cs 第 3 条）；
// 工程里没有任何「按时间轴编排的演出」概念可复用，Core 不许出现玩法名词，只能新建。
// MessagePipe 事件按 Core/Events/EventConventions.cs 的约定：readonly struct、XxxEvent 命名，
// broker 由 PerformanceInstaller.InstallEvents 注册。

namespace Game.Performance
{
    /// <summary>演出已开始播放。发布方：<see cref="PerformanceService"/>。</summary>
    public readonly struct PerformanceStartedEvent
    {
        public PerformanceStartedEvent(string id)
        {
            Id = id;
        }

        /// <summary>演出 id（Addressables 地址）。</summary>
        public string Id { get; }
    }
}
