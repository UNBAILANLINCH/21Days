// 职责：打字中「连点 N 下整句补全」的计数——相邻两次点击间隔不超过窗口才累计，满次返回 true 并清零，超窗从 1 重新计；
//   纯 C#，不引 UnityEngine，时间一律由调用方传入（unscaled 秒）。对白与演出字幕共用，手感只在这一处定义。
// 放在框架层的原因（复用 → 扩展 → 新建）：复用——对白（DialoguePlaybackPolicy）与演出（PerformanceService）都要
//   「着急时连点三下整句显示」，各写一份会让两边手感漂移（先例：TypingCadence 从 Dialogue 下沉到这里）；
//   扩展——原逻辑长在 DialoguePlaybackPolicy 里，它是对白专用、Performance 不得依赖 Dialogue；TypingCadence 只管出字节奏，
//   塞进去职责说不通；所以单独成类。
using System;

namespace Game.Core.UI
{
    /// <summary>
    /// 打字中连点补全计数。调用方只在「当前句正在逐字显示」时调 <see cref="RegisterTypingTap"/>；
    /// 换句、打完、离开打字（如开台词记录）时调 <see cref="Reset"/>。
    /// <para>
    /// 规则：相邻两次点击间隔 ≤ <see cref="TapWindowSeconds"/> 才累计，累计到 <see cref="RevealTapCount"/> 次返回 true 并清零；
    /// 间隔超过窗口时本次从 1 重新计。<see cref="RevealTapCount"/> = 1 即「点一下就补全」。
    /// </para>
    /// </summary>
    public sealed class TapRevealCounter
    {
        private readonly int revealTapCount;
        private readonly float tapWindowSeconds;
        private int tapCount;
        private float lastTapTime;

        /// <param name="revealTapCount">连点几下补全，至少 1。</param>
        /// <param name="tapWindowSeconds">相邻两次点击的最大间隔（秒），必须大于 0。</param>
        /// <exception cref="ArgumentException">次数小于 1，或窗口不大于 0（含 NaN）。</exception>
        public TapRevealCounter(int revealTapCount, float tapWindowSeconds)
        {
            if (revealTapCount < 1) throw new ArgumentException("补全点击次数至少为 1", nameof(revealTapCount));
            // NaN 与任何数比较都为 false，写成「不大于 0」一并拦下。
            if (!(tapWindowSeconds > 0f)) throw new ArgumentException("连点窗口必须大于 0", nameof(tapWindowSeconds));
            this.revealTapCount = revealTapCount;
            this.tapWindowSeconds = tapWindowSeconds;
        }

        /// <summary>连点几下补全。</summary>
        public int RevealTapCount => revealTapCount;

        /// <summary>相邻两次点击的最大间隔（秒）。</summary>
        public float TapWindowSeconds => tapWindowSeconds;

        /// <summary>当前已累计的连点次数（测试与调试用）。</summary>
        public int Count => tapCount;

        /// <summary>
        /// 打字中登记一次点击。<paramref name="unscaledNow"/> 是调用方的 unscaled 时间（秒）。
        /// 满 <see cref="RevealTapCount"/> 次返回 true（该整句补全）并清零；否则返回 false。
        /// </summary>
        public bool RegisterTypingTap(float unscaledNow)
        {
            if (tapCount > 0 && unscaledNow - lastTapTime > tapWindowSeconds) tapCount = 0;
            tapCount++;
            lastTapTime = unscaledNow;
            if (tapCount < revealTapCount) return false;
            tapCount = 0;
            return true;
        }

        /// <summary>清零：换句、打完、离开打字时调用。</summary>
        public void Reset() => tapCount = 0;
    }
}
