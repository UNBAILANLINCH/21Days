// 职责：字幕片段在图里的数据载体（说话者 + 正文），由混合器读取。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 要求每种片段有自己的 PlayableBehaviour 承载运行时数据；工程里没有可复用的。
using UnityEngine.Playables;

namespace Game.Performance.Timeline
{
    /// <summary>字幕数据载体。本身不做任何事，显示逻辑全在 <see cref="SubtitleMixerBehaviour"/>。</summary>
    public sealed class SubtitleBehaviour : PlayableBehaviour
    {
        public string Speaker { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
