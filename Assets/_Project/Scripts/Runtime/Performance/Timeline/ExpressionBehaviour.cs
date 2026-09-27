// 职责：表情片段在图里的数据载体（表情名），由混合器读取。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 要求每种片段有自己的 PlayableBehaviour 承载运行时数据；工程里没有可复用的。
using UnityEngine.Playables;

namespace Game.Performance.Timeline
{
    /// <summary>表情数据载体。切换逻辑全在 <see cref="ExpressionMixerBehaviour"/>。</summary>
    public sealed class ExpressionBehaviour : PlayableBehaviour
    {
        public string ExpressionName { get; set; } = string.Empty;
    }
}
