// 职责：演出播放结束时的结果分类，供服务、事件、埋点统一使用。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有任何「按时间轴编排的演出」概念可复用；
// Dialogue 是内容驱动推进机、Core 不许出现玩法名词，扩展不了，只能新建。

namespace Game.Performance
{
    /// <summary>演出播放结束时的结果分类。</summary>
    public enum PerformanceOutcome
    {
        /// <summary>自然播完。</summary>
        Completed,

        /// <summary>玩家长按跳过。</summary>
        Skipped,

        /// <summary>外部取消（ct）。</summary>
        Cancelled,

        /// <summary>加载失败 / 预制体缺舞台。</summary>
        Failed
    }
}
