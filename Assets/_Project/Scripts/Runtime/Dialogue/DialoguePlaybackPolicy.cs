// 职责：对白表现策略——何时把点击转成补全 / 推进、倍速挡位、自动播放计时、跳过标记；纯 C#，时间由调用方传入。
// 新建原因：DialogueRules 只管内容推进与存档语义，塞入点击节奏会让规则依赖表现时间；Controller 是 MonoBehaviour，放进去无法做 EditMode 测试。
//   打字中连点计数本身交给 Core 通用的 TapRevealCounter（演出字幕共用同一手感），本类只按阶段决定何时计数、何时清零。
using System;
using Game.Core.UI;

namespace Game.Dialogue
{
    public sealed class DialoguePlaybackPolicy
    {
        public enum TapOutcome { None, Reveal, Advance }

        private readonly DialoguePlaybackSettings settings;
        private readonly TapRevealCounter revealTaps;
        private float autoElapsed;

        public DialoguePlaybackPolicy(in DialoguePlaybackSettings settings)
        {
            if (settings.SpeedSteps == null) throw new ArgumentException("播放设置未初始化", nameof(settings));
            this.settings = settings;
            revealTaps = new TapRevealCounter(settings.RevealTapCount, settings.TapWindowSeconds);
        }

        /// <summary>构造时传入的播放设置快照（Controller 由此取标点停顿与面板动效参数）。</summary>
        public DialoguePlaybackSettings Settings => settings;
        public int SpeedIndex { get; private set; }
        public float Speed => settings.SpeedSteps[SpeedIndex];
        public bool AutoPlay { get; private set; }
        public bool Skipping { get; private set; }
        public float CharactersPerSecond => settings.CharactersPerSecond * Speed;
        public float AutoAdvanceDelay => settings.AutoAdvanceSeconds / Speed;

        // 每段对话开始时调用：速度回 0 档、自动关、跳过关、点击计数与自动计时清零。
        public void ResetForDialogue()
        {
            SpeedIndex = 0;
            AutoPlay = false;
            Skipping = false;
            OnNodeChanged();
        }

        public void CycleSpeed() => SpeedIndex = (SpeedIndex + 1) % settings.SpeedSteps.Count;

        public void ToggleAuto()
        {
            AutoPlay = !AutoPlay;
            autoElapsed = 0f;
        }

        // 跳过一旦开始，持续到下一次 ResetForDialogue。
        public void BeginSkip() => Skipping = true;

        public void OnNodeChanged()
        {
            revealTaps.Reset();
            autoElapsed = 0f;
        }

        // Typing：相邻两次点击间隔不超过窗口才累计，累计到 revealTapCount 次返回 Reveal；超窗从 1 重新计（计数见 TapRevealCounter）。
        // AwaitAdvance：单点即 Advance。其它阶段：None。非 Typing 一律清零。
        public TapOutcome RegisterTap(float unscaledNow, DialogueSaveData.Phase phase)
        {
            if (phase == DialogueSaveData.Phase.AwaitAdvance)
            {
                revealTaps.Reset();
                return TapOutcome.Advance;
            }
            if (phase != DialogueSaveData.Phase.Typing)
            {
                revealTaps.Reset();
                return TapOutcome.None;
            }
            return revealTaps.RegisterTypingTap(unscaledNow) ? TapOutcome.Reveal : TapOutcome.None;
        }

        // AutoPlay 且 AwaitAdvance 时累计，达到 AutoAdvanceDelay 返回 true 并清零；其它情况清零返回 false。
        public bool TickAuto(float unscaledDelta, DialogueSaveData.Phase phase)
        {
            if (!AutoPlay || phase != DialogueSaveData.Phase.AwaitAdvance)
            {
                autoElapsed = 0f;
                return false;
            }
            if (unscaledDelta > 0f) autoElapsed += unscaledDelta;
            if (autoElapsed < AutoAdvanceDelay) return false;
            autoElapsed = 0f;
            return true;
        }
    }
}
