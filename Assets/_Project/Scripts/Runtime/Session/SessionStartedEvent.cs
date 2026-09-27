// 职责：一局会话开始（新游戏或读档）的事实事件，各存档分区的所有者据此重载自己的运行时状态。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；用事件而不是 Session 逐个调模块，
//   Session 就不用枚举「哪些模块有分区」，新增分区的模块自己订阅即可。

namespace Game.Session
{
    /// <summary>
    /// 会话开始。由 <see cref="GameSession"/> 在内存分区已就位（新游戏已重置 / 读档已载入）之后、
    /// 切进玩法状态之前发布。订阅者在回调里同步从 <c>ISaveService.Get&lt;T&gt;()</c> 重载即可。
    /// </summary>
    public readonly struct SessionStartedEvent
    {
        public SessionStartedEvent(int slot, bool isNewGame)
        {
            Slot = slot;
            IsNewGame = isNewGame;
        }

        /// <summary>本局使用的存档槽，从 1 起。</summary>
        public int Slot { get; }

        /// <summary>true = 新游戏（分区是默认值）；false = 读档。</summary>
        public bool IsNewGame { get; }
    }
}
