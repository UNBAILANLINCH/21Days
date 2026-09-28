// 职责：打开演出面板时传入的参数快照（策略、跳过 / 自动 / LOG 键位提示、黑场时长、停顿提示符、字幕逐字节奏）。
// 为什么新建（复用 → 扩展 → 新建）：UIView.OnOpenAsync 只收一个 object 参数，面板又不许注入服务 / 读配置，
//   需要一个由服务组装好的值类型把全部表现参数一次带进去；PerformancePolicy 只描述策略，塞文案进去职责不对。

namespace Game.Performance
{
    /// <summary>演出面板参数。由 <see cref="PerformanceService"/> 按配置与舞台策略组装。</summary>
    public readonly struct PerformanceViewArgs
    {
        /// <param name="autoHint">「自动」按钮下方的键位小字（如「A」）；null 记为空串（不显示）。</param>
        /// <param name="historyHint">「LOG」按钮下方的键位小字（如「H」）；null 记为空串（不显示）。</param>
        public PerformanceViewArgs(PerformancePolicy policy, string skipHint, float fadeSeconds,
            string holdPrompt, float charactersPerSecond, float punctuationPauseSeconds, string punctuationChars,
            string autoHint = null, string historyHint = null)
        {
            Policy = policy;
            SkipHint = skipHint ?? string.Empty;
            FadeSeconds = fadeSeconds;
            HoldPrompt = holdPrompt ?? string.Empty;
            CharactersPerSecond = charactersPerSecond > 0f ? charactersPerSecond : 0f;
            PunctuationPauseSeconds = punctuationPauseSeconds > 0f ? punctuationPauseSeconds : 0f;
            PunctuationChars = punctuationChars ?? string.Empty;
            AutoHint = autoHint ?? string.Empty;
            HistoryHint = historyHint ?? string.Empty;
        }

        /// <summary>本段演出的策略（决定是否显示跳过提示）。</summary>
        public PerformancePolicy Policy { get; }

        /// <summary>已格式化好的跳过键位提示（如「长按 Ctrl」）。</summary>
        public string SkipHint { get; }

        /// <summary>进场黑场淡变时长（秒，unscaled）。</summary>
        public float FadeSeconds { get; }

        /// <summary>停顿提示符（如「▼」）。</summary>
        public string HoldPrompt { get; }

        /// <summary>字幕逐字速度（字 / 秒，unscaled）；0 = 整句直出。</summary>
        public float CharactersPerSecond { get; }

        /// <summary>字幕打出标点后的停顿秒数；0 = 不停。</summary>
        public float PunctuationPauseSeconds { get; }

        /// <summary>算作标点的字符；空串 = 不停。</summary>
        public string PunctuationChars { get; }

        /// <summary>「自动」按钮的键位小字（Auto 动作第一条键盘绑定的显示串）；空串 = 不显示。</summary>
        public string AutoHint { get; }

        /// <summary>「LOG」按钮的键位小字（History 动作第一条键盘绑定的显示串）；空串 = 不显示。</summary>
        public string HistoryHint { get; }
    }
}
