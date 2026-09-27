// 职责：面板开关过渡的预设枚举，供 UIView 按值分派到不同的过渡动画。
// 为什么新建：roadmap.md D4 要面板过渡可选（不止淡入淡出）；UIView 原来只有一种硬编码实现，
//   枚举是最小的「可选项」载体——扩展 UIView 塞一个 bool/string 开关说不通，新建一个专属枚举更直观。

namespace Game.Core.UI
{
    /// <summary>
    /// 面板开关的过渡预设。策划 / 美术在预制体的 UIView 组件上选（艺术家手册 6.6），不需要改代码；
    /// 要加预设之外的花样，程序重写 <see cref="UIView.PlayOpenTransitionAsync"/> / PlayCloseTransitionAsync。
    /// </summary>
    public enum UITransition
    {
        /// <summary>CanvasGroup 透明度 0→1（关时 1→0），不改变位置和缩放。默认值，行为与框架原有实现一致。</summary>
        Fade,

        /// <summary>淡入的同时从下方滑入到位（关时反向滑出到下方）。常用于从屏幕底部弹出的面板。</summary>
        SlideUp,

        /// <summary>淡入的同时从上方滑入到位（关时反向滑出到上方）。常用于从顶部落下的通知类面板。</summary>
        SlideDown,

        /// <summary>淡入的同时从 0.92 倍缩放放大到 1（关时反向缩小）。常用于弹窗 / 确认框。</summary>
        Scale
    }
}
