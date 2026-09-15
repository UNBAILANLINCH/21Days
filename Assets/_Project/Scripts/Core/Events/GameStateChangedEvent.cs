// 职责：游戏状态切换完成后发布的事实事件，带上切换前后的状态类型。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；与 BootCompletedEvent
// 的生命周期和订阅者都不同，合并成一个文件只会让两边互相牵连。

using System;

namespace Game.Core.Events
{
    /// <summary>
    /// 状态切换完成。由 GameFlow 在目标状态 EnterAsync 返回之后发布。
    /// 用类型而不是实例，避免订阅者顺手抓着状态对象不放导致生命周期外泄。
    /// </summary>
    public readonly struct GameStateChangedEvent
    {
        public GameStateChangedEvent(Type from, Type to)
        {
            From = from;
            To = to;
        }

        /// <summary>切换前的状态类型；首次进入状态机时为 null。</summary>
        public Type From { get; }

        /// <summary>切换后的状态类型。</summary>
        public Type To { get; }
    }
}
