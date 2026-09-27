// 职责：沉浸模式的框架契约——一键隐藏 / 恢复全部 Hud 层面板（沉浸中仍需显示的面板除外），并对外报告当前状态。
// 为什么新建：复用——IUIService.SetLayerVisible 是整层切 Canvas，做不到「沉浸按钮自己留着、其余 Hud 隐藏」；
//   扩展——塞进 IUIService 会让只关心沉浸状态的玩法（对白焦点、任务标记）也拿到开关面板的全部能力，
//   单独一个窄接口更贴合调用方；实现仍在 UIService（Hud 面板的记账只有它有）。

namespace Game.Core.UI
{
    /// <summary>
    /// HUD 显隐（沉浸模式）。由 <see cref="UIService"/> 实现，根作用域按本接口注入。
    /// <para>
    /// <see cref="SetHudHidden"/>(true) 把已打开的 Hud 层面板里 <see cref="UIView.VisibleWhenHudHidden"/> 为 false 的
    /// 全部置为透明且不吃点击（CanvasGroup alpha 0 / interactable false / blocksRaycasts false），
    /// 不关面板、不触发生命周期；沉浸中新打开的 Hud 面板同样套用。传 false 原样恢复。
    /// 状态真正变化时发布一次 <see cref="Events.HudVisibilityChangedEvent"/>，世界空间标记（NPC 头顶、任务目标）据此自行隐藏。
    /// </para>
    /// </summary>
    public interface IHudVisibility
    {
        /// <summary>当前是否处于沉浸模式（HUD 已隐藏）。</summary>
        bool IsHudHidden { get; }

        /// <summary>进入 / 退出沉浸模式。与当前状态相同时是空操作，不重复发布事件。</summary>
        void SetHudHidden(bool hidden);
    }
}
