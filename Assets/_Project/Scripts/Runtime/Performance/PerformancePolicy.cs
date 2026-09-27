// 职责：一段演出的播放策略快照（可否跳过、长按跳过秒数、是否时停 / 藏 HUD / 上黑边），构造时校验。
// 为什么新建（复用 → 扩展 → 新建）：策略来自「舞台预制体开关 + 全局配置默认值」两处，需要一个校验过的不可变值在
//   规则、服务、面板之间传递；DialoguePlaybackSettings 是对白专用且 Performance 不得依赖 Dialogue，只能新建。
using System;

namespace Game.Performance
{
    /// <summary>演出播放策略。只读值类型，构造即校验。</summary>
    public readonly struct PerformancePolicy
    {
        /// <param name="skippable">玩家能否长按跳过。</param>
        /// <param name="skipHoldSeconds">长按多少秒触发跳过，必须大于 0（不可跳过时也要合法，便于统一校验）。</param>
        /// <param name="pauseWorld">演出期间是否暂停世界（timeScale = 0）。</param>
        /// <param name="hideHud">演出期间是否整层隐藏 HUD。</param>
        /// <param name="letterbox">是否上下黑边。</param>
        /// <exception cref="ArgumentException"><paramref name="skipHoldSeconds"/> 不大于 0 或不是有限数。</exception>
        public PerformancePolicy(bool skippable, float skipHoldSeconds, bool pauseWorld, bool hideHud, bool letterbox)
        {
            // NaN 与任何数比较都为 false，所以写成「不大于 0」一并拦下；无穷大单独拦。
            if (!(skipHoldSeconds > 0f) || float.IsInfinity(skipHoldSeconds))
                throw new ArgumentException($"SkipHoldSeconds 必须是大于 0 的有限数，实际 {skipHoldSeconds}", nameof(skipHoldSeconds));
            Skippable = skippable;
            SkipHoldSeconds = skipHoldSeconds;
            PauseWorld = pauseWorld;
            HideHud = hideHud;
            Letterbox = letterbox;
        }

        /// <summary>玩家能否长按跳过。</summary>
        public bool Skippable { get; }

        /// <summary>长按多少秒触发跳过（大于 0）。</summary>
        public float SkipHoldSeconds { get; }

        /// <summary>演出期间是否暂停世界。</summary>
        public bool PauseWorld { get; }

        /// <summary>演出期间是否整层隐藏 HUD 层与弹窗层（对白框在弹窗层）。</summary>
        public bool HideHud { get; }

        /// <summary>是否上下黑边。</summary>
        public bool Letterbox { get; }

        /// <summary>是否由构造函数建出（default 值的 SkipHoldSeconds 为 0，视为无效）。</summary>
        public bool IsValid => SkipHoldSeconds > 0f;
    }
}
