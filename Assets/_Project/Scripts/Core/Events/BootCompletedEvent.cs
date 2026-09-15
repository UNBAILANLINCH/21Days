// 职责：所有 IGameService 初始化完成、即将进入第一个可交互状态时发布的事实事件。
// 为什么新建：波 1 之前没有任何事件类型；按 EventConventions.cs 第 3 条，一个事件一个文件，
// 不能塞进别的事件文件里搭车。

namespace Game.Core.Events
{
    /// <summary>
    /// 启动完成。由 GameBootstrap 在全部服务初始化成功后发布一次。
    /// 订阅者可以据此做「启动后一次性」的事情（打点、预热、隐藏启动画面）。
    /// </summary>
    public readonly struct BootCompletedEvent
    {
        public BootCompletedEvent(int serviceCount)
        {
            ServiceCount = serviceCount;
        }

        /// <summary>本次启动初始化过的服务数量，便于日志与自检。</summary>
        public int ServiceCount { get; }
    }
}
