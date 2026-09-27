// 职责：标题面板的「选择存档」被点击这个事实，由 TitleState 发布，玩法层（存档会话）订阅后打开选槽面板。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；不合进 TitleStartClickedEvent 的理由同 TitleContinueClickedEvent。

namespace Game.Core.Events
{
    /// <summary>
    /// 标题界面的「选择存档」按钮被点了。由 <c>TitleState</c> 在收到面板回调时发布一次。
    /// 是事实不是命令：选槽面板归玩法层，Core 不认识它；没人订阅时只留一条日志。
    /// </summary>
    public readonly struct TitleLoadClickedEvent
    {
    }
}
