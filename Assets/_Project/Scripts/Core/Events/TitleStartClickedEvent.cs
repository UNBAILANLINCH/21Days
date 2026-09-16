// 职责：标题面板的「开始」被点击这个事实，由 TitleState 发布，玩法层订阅后决定去哪个状态。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；它是框架层唯一一个
//   「Core 发出、玩法接住」的事件，和 BootCompletedEvent / GameStateChangedEvent 的订阅者完全不同，
//   合进它们任何一份文件都会让三者互相牵连。
//   不做成 Core 直接调玩法的接口：Game.Core 不许引用 Game.Runtime（asmdef 依赖方向）。

namespace Game.Core.Events
{
    /// <summary>
    /// 标题界面的「开始」按钮被点了。由 <c>TitleState</c> 在收到面板回调时发布一次。
    /// <para>
    /// 它是**事实**不是命令：框架只负责报告「玩家点了开始」，去哪个玩法状态由玩法层自己决定
    /// （典型做法是在根作用域注册一个入口点订阅它，然后 <c>IGameFlow.GoToAsync&lt;你的状态&gt;()</c>）。
    /// 没人订阅时点击只会留下一条日志，不是错误——框架本身跑得起来就该跑得起来。
    /// </para>
    /// <para>结构体里没有字段：这个事实不带任何数据，加字段前先想清楚谁真的需要它。</para>
    /// </summary>
    public readonly struct TitleStartClickedEvent
    {
    }
}
