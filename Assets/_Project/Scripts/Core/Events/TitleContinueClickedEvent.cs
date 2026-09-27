// 职责：标题面板的「继续」被点击这个事实，由 TitleState 发布，玩法层（存档会话）订阅后决定读哪个槽、去哪个状态。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；复用 TitleStartClickedEvent 加字段区分按钮不行——
//   那会让现有订阅者（只关心「开始」）都得先判一次是哪个按钮，含义也从「开始被点了」变成「某个按钮被点了」。

namespace Game.Core.Events
{
    /// <summary>
    /// 标题界面的「继续」按钮被点了。由 <c>TitleState</c> 在收到面板回调时发布一次。
    /// 是事实不是命令：框架不知道存档是什么，继续哪个槽由订阅者决定；没人订阅时只留一条日志。
    /// </summary>
    public readonly struct TitleContinueClickedEvent
    {
    }
}
