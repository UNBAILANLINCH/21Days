// 职责：一次落盘结束（成功或失败）的事实事件，回放与界面据此等待 / 提示。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；与 SessionStartedEvent 的时机和订阅者都不同。

namespace Game.Session
{
    /// <summary>落盘结束。由 <see cref="GameSession.SaveNowAsync"/> 在 <c>ISaveService.SaveAsync</c> 返回后发布。</summary>
    public readonly struct SaveCompletedEvent
    {
        public SaveCompletedEvent(int slot, string reason, bool success)
        {
            Slot = slot;
            Reason = reason;
            Success = success;
        }

        public int Slot { get; }

        /// <summary>触发原因（quest / crate / dialogue / scene / leave / quit ……）。</summary>
        public string Reason { get; }

        public bool Success { get; }
    }
}
