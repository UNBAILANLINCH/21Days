// 职责：一条通知的只读数据——标题、可空正文、停留秒数。
// 为什么新建：NotificationQueue 的条目类型；项目约定一文件一类型，不能与队列写在同一文件，
//   也没有现成的「标题 + 正文 + 时长」值类型可复用。

namespace Game.Core.UI
{
    /// <summary>一条通知。由 <see cref="NotificationQueue.Enqueue"/> 构造，秒数已按下限修正。</summary>
    public readonly struct NotificationEntry
    {
        public NotificationEntry(string title, string body, float seconds)
        {
            Title = title;
            Body = body;
            Seconds = seconds;
        }

        public string Title { get; }

        /// <summary>正文，可为 null 或空串（卡片只显示标题）。</summary>
        public string Body { get; }

        public float Seconds { get; }
    }
}
