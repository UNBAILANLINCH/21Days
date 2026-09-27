// 职责：打开演出面板时传入的参数快照（策略、跳过提示文案、黑边高度、黑场时长、停顿提示符、字幕逐字节奏）。
// 为什么新建（复用 → 扩展 → 新建）：UIView.OnOpenAsync 只收一个 object 参数，面板又不许注入服务 / 读配置，
//   需要一个由服务组装好的值类型把全部表现参数一次带进去；PerformancePolicy 只描述策略，塞文案进去职责不对。

namespace Game.Performance
{
    /// <summary>演出面板参数。由 <see cref="PerformanceService"/> 按配置与舞台策略组装。</summary>
    public readonly struct PerformanceViewArgs
    {
        public PerformanceViewArgs(PerformancePolicy policy, string skipHint, float letterboxHeight, float fadeSeconds,
            string holdPrompt, float charactersPerSecond, float punctuationPauseSeconds, string punctuationChars)
        {
            Policy = policy;
            SkipHint = skipHint ?? string.Empty;
            LetterboxHeight = letterboxHeight;
            FadeSeconds = fadeSeconds;
            HoldPrompt = holdPrompt ?? string.Empty;
            CharactersPerSecond = charactersPerSecond > 0f ? charactersPerSecond : 0f;
            PunctuationPauseSeconds = punctuationPauseSeconds > 0f ? punctuationPauseSeconds : 0f;
            PunctuationChars = punctuationChars ?? string.Empty;
        }

        /// <summary>本段演出的策略（决定是否显示跳过提示、是否上黑边）。</summary>
        public PerformancePolicy Policy { get; }

        /// <summary>已格式化好的跳过键位提示（如「长按 Ctrl」）。</summary>
        public string SkipHint { get; }

        /// <summary>黑边目标高度（参考分辨率像素）。</summary>
        public float LetterboxHeight { get; }

        /// <summary>黑场淡变 / 黑边推入时长（秒，unscaled）。</summary>
        public float FadeSeconds { get; }

        /// <summary>停顿提示符（如「▼」）。</summary>
        public string HoldPrompt { get; }

        /// <summary>字幕逐字速度（字 / 秒，unscaled）；0 = 整句直出。</summary>
        public float CharactersPerSecond { get; }

        /// <summary>字幕打出标点后的停顿秒数；0 = 不停。</summary>
        public float PunctuationPauseSeconds { get; }

        /// <summary>算作标点的字符；空串 = 不停。</summary>
        public string PunctuationChars { get; }
    }
}
