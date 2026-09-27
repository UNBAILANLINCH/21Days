// 职责：通知的纯逻辑队列——入队、同标题合并、当前项计时、到时出队换下一条。不碰 Unity 对象，EditMode 直接测。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：UIStack 管的是面板层级栈，不是「按时长逐条过期」的 FIFO；System.Collections.Generic.Queue
//      不带当前项计时与合并语义。
//   2. 扩展不行：写进 NotificationService 里就只能在有 PlayerLoop 的环境测（它要开面板、逐帧等待），
//      拆出纯 C# 的一段才能 EditMode 覆盖顺序与超时（同 UIStack 与 UIService 的分法）。

using System;
using System.Collections.Generic;

namespace Game.Core.UI
{
    /// <summary>
    /// 通知队列。时间由调用方经 <see cref="Advance"/> 喂进来（NotificationService 喂 unscaled 帧时间），
    /// 本类不读任何时钟。
    /// <para>
    /// 状态：「当前项」（正在显示，带剩余时长）+「待显示」FIFO。入队不会直接成为当前项，
    /// 要等下一次 <see cref="Advance"/>——这样「何时换卡片」只有一个出口，调用方只看它的返回值。
    /// </para>
    /// </summary>
    public sealed class NotificationQueue
    {
        /// <summary>单条停留秒数下限：配成 0 或负数时防止一帧内把整个队列刷完。</summary>
        public const float MinSeconds = 0.1f;

        private readonly List<NotificationEntry> pending = new List<NotificationEntry>();
        private readonly bool mergeSameTitle;
        private NotificationEntry current;
        private float remaining;

        /// <param name="mergeSameTitle">为 true 时，入队的标题与某条待显示项相同则原位替换那条（不追加）。
        /// 正在显示的那条不参与合并——它的文字已经上屏，改了反而闪。</param>
        public NotificationQueue(bool mergeSameTitle)
        {
            this.mergeSameTitle = mergeSameTitle;
        }

        /// <summary>是否有正在显示的一条。</summary>
        public bool HasCurrent { get; private set; }

        /// <summary>正在显示的一条；<see cref="HasCurrent"/> 为 false 时是 default。</summary>
        public NotificationEntry Current => current;

        /// <summary>当前项剩余秒数；没有当前项时为 0。</summary>
        public float Remaining => HasCurrent ? remaining : 0f;

        /// <summary>待显示条数，不含当前项。</summary>
        public int PendingCount => pending.Count;

        /// <summary>
        /// 入队。标题为 null 或空串返回 false 不入队；秒数低于 <see cref="MinSeconds"/> 时按下限算。
        /// </summary>
        public bool Enqueue(string title, string body, float seconds)
        {
            if (string.IsNullOrEmpty(title)) return false;
            NotificationEntry entry = new NotificationEntry(title, body, seconds < MinSeconds ? MinSeconds : seconds);

            if (mergeSameTitle)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (string.Equals(pending[i].Title, title, StringComparison.Ordinal))
                    {
                        pending[i] = entry;
                        return true;
                    }
                }
            }

            pending.Add(entry);
            return true;
        }

        /// <summary>
        /// 推进时间。返回 true 表示「当前项变了」（空闲时取到了下一条、或当前项到时出队——之后可能是下一条也可能为空），
        /// 调用方据此换卡片或收起。<paramref name="deltaSeconds"/> 为负按 0 算。
        /// <para>空闲且有待显示项时，传 0 也会立刻取出下一条。一次调用最多换一条，剩余的超时量不往下一条结转。</para>
        /// </summary>
        public bool Advance(float deltaSeconds)
        {
            if (!HasCurrent) return TryPromote();

            if (deltaSeconds > 0f) remaining -= deltaSeconds;
            if (remaining > 0f) return false;

            HasCurrent = false;
            current = default;
            remaining = 0f;
            TryPromote();
            return true;
        }

        /// <summary>清空当前项与全部待显示项。</summary>
        public void Clear()
        {
            pending.Clear();
            HasCurrent = false;
            current = default;
            remaining = 0f;
        }

        private bool TryPromote()
        {
            if (pending.Count == 0) return false;
            current = pending[0];
            pending.RemoveAt(0);
            remaining = current.Seconds;
            HasCurrent = true;
            return true;
        }
    }
}
