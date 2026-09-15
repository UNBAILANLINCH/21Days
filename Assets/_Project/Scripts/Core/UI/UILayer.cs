// 职责：UI 分层枚举，决定面板挂在哪个 Canvas 下、盖谁、被谁盖。
// 为什么新建：architecture.md 5.6 把它定成独立类型；枚举值的先后顺序同时就是 Canvas 的 sortingOrder
//   顺序，塞进 UIView 或 IUIService 会让「层」这个概念依附于某个类，纯逻辑的 UIStack 也就没法引用它。

namespace Game.Core.UI
{
    /// <summary>
    /// UI 层。**声明顺序即层级顺序**，后面的盖前面的（UIService 按序号给各层 Canvas 排 sortingOrder）。
    /// </summary>
    public enum UILayer
    {
        /// <summary>常驻抬头显示：血条、摇杆、小地图。不进栈，不被全屏面板隐藏。</summary>
        Hud = 0,

        /// <summary>主界面面板：标题、背包、设置。单栈；打开全屏面板会隐藏它下面的面板。</summary>
        Panel = 1,

        /// <summary>弹窗：确认框、奖励飘窗。独立栈，可以叠加，不影响 Panel 层。</summary>
        Popup = 2,

        /// <summary>最顶层：加载遮罩、网络转圈、调试台。不进栈，永远压在所有东西上面。</summary>
        Top = 3,
    }
}
