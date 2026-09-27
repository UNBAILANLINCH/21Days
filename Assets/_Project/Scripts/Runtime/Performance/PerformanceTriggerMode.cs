// 职责：场景演出触发器的触发时机枚举。
// 为什么新建（复用 → 扩展 → 新建）：一个文件一个类型；PerformanceTrigger 与 PerformanceSceneBinder 都要用它，工程里没有可复用的。

namespace Game.Performance
{
    /// <summary>演出触发时机。</summary>
    public enum PerformanceTriggerMode
    {
        /// <summary>带 <see cref="PerformanceTriggerActor"/> 的物体进入触发区时。</summary>
        OnEnter,

        /// <summary>启动完成且场景加载后（由 <see cref="PerformanceSceneBinder"/> 调用）。</summary>
        OnSceneStart
    }
}
