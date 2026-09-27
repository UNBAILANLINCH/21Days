// 职责：HUD 显隐（沉浸模式）已切换这个事实，由 UIService 在 SetHudHidden 真正改变状态时发布一次。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；它的订阅者是世界空间标记（NPC 头顶标记、任务目标标记），
//   与启动 / 状态流 / 标题三个现有事件的订阅者都不同，合进任何一份会互相牵连。

namespace Game.Core.Events
{
    /// <summary>
    /// HUD 显隐已切换。<see cref="Hidden"/> 为 true 表示进入沉浸模式（HUD 与世界空间交互标记都该隐藏）。
    /// 发布方：<c>UIService.SetHudHidden</c>（实现 <c>IHudVisibility</c>），状态不变时不发布。
    /// </summary>
    public readonly struct HudVisibilityChangedEvent
    {
        public HudVisibilityChangedEvent(bool hidden)
        {
            Hidden = hidden;
        }

        /// <summary>true：进入沉浸（隐藏）；false：退出沉浸（恢复）。</summary>
        public bool Hidden { get; }
    }
}
