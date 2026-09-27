// 职责：逐字揭示节奏——把「本帧可揭示的字符预算」换算成新的显示字数，打出标点后先停顿再继续；纯 C#，不引 UnityEngine。
//   对白与演出字幕共用：调用方每句 Reset、每帧 Advance，自己决定预算（速度 × 倍速 × Δt）与何时整句补全。
// 放在框架层的原因：复用——节奏与具体玩法无关，多个模块都要逐字显示文字，各抄一份会让节奏手感漂移；
//   扩展——塞进某个面板类做不了 EditMode 测试，塞进规则类又违反「表现参数不进规则」，所以单独成类。
using System;

namespace Game.Core.UI
{
    /// <summary>
    /// 逐字节奏。每句开始时 <see cref="Reset"/>，每帧 <see cref="Advance"/> 一次。
    /// <para>
    /// 停顿以「字符预算」计：停顿 <c>pauseSeconds</c> 等价于 <c>pauseSeconds × 基础打字速度</c> 个字符的预算。
    /// 调用方给的预算已乘倍速（<c>基础速度 × 倍速 × Δt</c>），所以倍速下停顿的实际秒数同比缩短，
    /// 与「按倍速缩短的等待」语义一致。
    /// </para>
    /// <para>
    /// 连续标点（「……」「！？」）只在最后一个之后停一次；句末标点不停（已经打完，后面的等待交给调用方）。
    /// </para>
    /// </summary>
    public sealed class TypingCadence
    {
        private readonly float pauseCharacters;
        private readonly string punctuation;
        // 上一帧没花完的零头预算（不足一个字）。
        private float carry;
        // 还要耗掉多少预算的停顿才能继续出字。
        private float pauseLeft;

        /// <param name="charactersPerSecond">x1 档打字速度（字 / 秒），用来把停顿秒数换成字符预算。</param>
        /// <param name="pauseSeconds">打出标点后停顿多少秒（x1 档）；0 = 不停。</param>
        /// <param name="punctuationChars">哪些字符算标点；null / 空串 = 不停。</param>
        public TypingCadence(float charactersPerSecond, float pauseSeconds, string punctuationChars)
        {
            if (!(charactersPerSecond > 0f)) throw new ArgumentException("打字速度必须大于 0", nameof(charactersPerSecond));
            if (!(pauseSeconds >= 0f)) throw new ArgumentException("标点停顿不可为负", nameof(pauseSeconds));
            pauseCharacters = pauseSeconds * charactersPerSecond;
            punctuation = punctuationChars ?? string.Empty;
        }

        /// <summary>当前是否处在标点停顿中（测试与调试用）。</summary>
        public bool Pausing => pauseLeft > 0f;

        /// <summary>换句时调用：清零头预算与停顿。</summary>
        public void Reset()
        {
            carry = 0f;
            pauseLeft = 0f;
        }

        /// <summary>
        /// 推进一帧。<paramref name="text"/> 是 TMP 解析后的可见字符序列（富文本标签已去掉，下标与可见字数一一对应），
        /// <paramref name="visible"/> 是当前已显示字数，<paramref name="budget"/> 是本帧预算（基础速度 × 倍速 × Δt，字符数）。
        /// 返回新的显示字数（不小于 <paramref name="visible"/>，不超过正文长度）。
        /// </summary>
        public int Advance(string text, int visible, float budget)
        {
            int length = text == null ? 0 : text.Length;
            if (visible < 0) visible = 0;
            if (visible >= length)
            {
                Reset();
                return Math.Max(visible, length); // lint-ok: 打字节奏是纯表现，不参与判定与快照
            }
            float available = carry + (budget > 0f ? budget : 0f);
            while (visible < length)
            {
                if (pauseLeft > 0f)
                {
                    float used = Math.Min(pauseLeft, available); // lint-ok: 打字节奏是纯表现，不参与判定与快照
                    pauseLeft -= used;
                    available -= used;
                    if (pauseLeft > 0f) break;
                }
                if (available < 1f) break;
                available -= 1f;
                visible++;
                if (visible < length && pauseCharacters > 0f && IsPunctuation(text[visible - 1]) &&
                    !IsPunctuation(text[visible]))
                    pauseLeft = pauseCharacters;
            }
            carry = visible >= length ? 0f : available;
            return visible;
        }

        private bool IsPunctuation(char c) => punctuation.IndexOf(c) >= 0;
    }
}
