// 职责：游戏状态**即将**切换（前一状态 ExitAsync 之前）发布的事实事件，带上切换前后的状态类型。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；GameStateChangedEvent 发在新状态 Enter 之后，
//   那时前一状态已经 Exit、它的场景与对象都已释放，想在离开前同步抓一次现场（比如捕获存档）的订阅者赶不上。
//   复用 Changed 不行（时机不对），扩展 Changed 加个「阶段」字段会让现有订阅者全都要判一次阶段，所以另立一个事件。

using System;

namespace Game.Core.Events
{
    /// <summary>
    /// 状态即将切换。由 GameFlow 在调用前一状态 <c>ExitAsync</c> **之前**发布（首次进入状态机时没有前一状态，
    /// 也照样发一次，<see cref="From"/> 为 null）。
    /// <para>
    /// 订阅者在回调里同步做完要做的事，回调返回之后前一状态才开始 Exit；不要在回调里 await 或切状态。
    /// 发布了 Changing 不代表切换一定成功——目标状态 Enter 失败时不会再发 <see cref="GameStateChangedEvent"/>。
    /// </para>
    /// 用类型而不是实例，理由同 <see cref="GameStateChangedEvent"/>。
    /// </summary>
    public readonly struct GameStateChangingEvent
    {
        public GameStateChangingEvent(Type from, Type to)
        {
            From = from;
            To = to;
        }

        /// <summary>切换前的状态类型；首次进入状态机时为 null。</summary>
        public Type From { get; }

        /// <summary>要切换去的状态类型。</summary>
        public Type To { get; }
    }
}
