// 职责：通用通知（toast）的对外契约——谁都能往屏幕顶部塞一条「标题 + 可选正文」的短提示。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：IUIService 只管面板开关，没有「排队逐条显示、到时自动收起」的语义。
//   2. 扩展不行：塞进 IUIService 会让所有面板服务的实现（将来换 UI Toolkit）都得带上通知队列；
//      通知是建在 IUIService 之上的一层用法，契约与实现分开放（同 IUIService / UIService）。

namespace Game.Core.UI
{
    /// <summary>
    /// 通知服务。玩法模块只调 <see cref="Show"/>，不自己开 <see cref="Views.NotificationView"/>。
    /// <para>
    /// 多条通知按调用顺序排队逐条显示，每条停留到时（按不受时间缩放的真实时间计）换下一条，队列空了卡片收起。
    /// 通知挂在 <see cref="UILayer.Top"/> 层，不进栈、不挡点击。
    /// </para>
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// 追加一条通知。<paramref name="title"/> 为空串时忽略；<paramref name="body"/> 为空则卡片只显示标题。
        /// <paramref name="seconds"/> ≤ 0 时取 <see cref="UIConfig.NotificationSeconds"/>。
        /// 待显示队列里已有同标题的一条时合并成一条（用新正文与时长），不重复刷屏。
        /// </summary>
        void Show(string title, string body = null, float seconds = 0f);
    }
}
