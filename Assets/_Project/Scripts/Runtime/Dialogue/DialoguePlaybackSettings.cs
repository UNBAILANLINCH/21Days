// 职责：对白播放表现参数的校验后快照（打字速度、标点停顿、倍速挡位、三连点、自动播放、面板动效），供纯 C# 策略类使用。
// 新建原因：DialogueConfig 是 ScriptableObject，策略类与 EditMode 测试不应依赖 UnityEngine；DialogueRules 只管推进规则，不承载表现参数。
using System;
using System.Collections.Generic;

namespace Game.Dialogue
{
    public readonly struct DialoguePlaybackSettings
    {
        /// <param name="punctuationPauseSeconds">打出标点后停顿多少秒（x1 档；倍速下同比缩短）。0 = 不停顿。</param>
        /// <param name="punctuationChars">哪些字符算标点；null / 空串 = 不停顿。</param>
        /// <param name="motion">面板动效参数；不传用 <see cref="DialogueMotionSettings.Default"/>。</param>
        public DialoguePlaybackSettings(float charactersPerSecond, float[] speedSteps, int revealTapCount,
            float tapWindowSeconds, float autoAdvanceSeconds, float punctuationPauseSeconds = 0f,
            string punctuationChars = null, DialogueMotionSettings? motion = null)
        {
            if (!(charactersPerSecond > 0f)) throw new ArgumentException("打字速度必须大于 0", nameof(charactersPerSecond));
            if (speedSteps == null || speedSteps.Length == 0) throw new ArgumentException("倍速挡位不可为空", nameof(speedSteps));
            foreach (float step in speedSteps)
                if (!(step > 0f)) throw new ArgumentException("倍速挡位必须全部大于 0", nameof(speedSteps));
            if (revealTapCount < 1) throw new ArgumentException("补全点击次数至少为 1", nameof(revealTapCount));
            if (!(tapWindowSeconds > 0f)) throw new ArgumentException("连点窗口必须大于 0", nameof(tapWindowSeconds));
            if (!(autoAdvanceSeconds >= 0f)) throw new ArgumentException("自动推进间隔不可为负", nameof(autoAdvanceSeconds));
            if (!(punctuationPauseSeconds >= 0f))
                throw new ArgumentException("标点停顿不可为负", nameof(punctuationPauseSeconds));
            if (motion.HasValue && !motion.Value.IsValid) throw new ArgumentException("动效参数未初始化", nameof(motion));
            CharactersPerSecond = charactersPerSecond;
            SpeedSteps = (float[])speedSteps.Clone();
            RevealTapCount = revealTapCount;
            TapWindowSeconds = tapWindowSeconds;
            AutoAdvanceSeconds = autoAdvanceSeconds;
            PunctuationPauseSeconds = punctuationPauseSeconds;
            PunctuationChars = punctuationChars ?? string.Empty;
            Motion = motion ?? DialogueMotionSettings.Default;
        }

        public float CharactersPerSecond { get; }
        // 构造时复制，调用方后续改原数组不影响快照；default 实例为 null，由策略类拒收。
        public IReadOnlyList<float> SpeedSteps { get; }
        public int RevealTapCount { get; }
        public float TapWindowSeconds { get; }
        public float AutoAdvanceSeconds { get; }
        public float PunctuationPauseSeconds { get; }
        /// <summary>构造后不为 null（空串 = 不停顿）；default 实例为 null。</summary>
        public string PunctuationChars { get; }
        public DialogueMotionSettings Motion { get; }
    }
}
