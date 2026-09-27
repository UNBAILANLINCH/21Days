// 职责：对白表现策略——何时把点击转成补全 / 推进、倍速挡位、自动播放计时、跳过标记；纯 C#，时间由调用方传入。
// 新建原因：DialogueRules 只管内容推进与存档语义，塞入点击节奏会让规则依赖表现时间；Controller 是 MonoBehaviour，放进去无法做 EditMode 测试。
using System;

namespace Game.Dialogue
{
    public sealed class DialoguePlaybackPolicy
    {
        public enum TapOutcome { None, Reveal, Advance }

        private readonly DialoguePlaybackSettings settings;
        private int tapCount;
        private float lastTapTime;
        private float autoElapsed;

        public DialoguePlaybackPolicy(in DialoguePlaybackSettings settings)
        {
            if (settings.SpeedSteps == null) throw new ArgumentException("播放设置未初始化", nameof(settings));
            this.settings = settings;
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
            tapCount = 0;
            autoElapsed = 0f;
        }

        // Typing：相邻两次点击间隔不超过窗口才累计，累计到 revealTapCount 次返回 Reveal；超窗从 1 重新计。
        // AwaitAdvance：单点即 Advance。其它阶段：None。
        public TapOutcome RegisterTap(float unscaledNow, DialogueSaveData.Phase phase)
        {
            if (phase == DialogueSaveData.Phase.AwaitAdvance)
            {
                tapCount = 0;
                return TapOutcome.Advance;
            }
            if (phase != DialogueSaveData.Phase.Typing)
            {
                tapCount = 0;
                return TapOutcome.None;
            }
            if (tapCount > 0 && unscaledNow - lastTapTime > settings.TapWindowSeconds) tapCount = 0;
            tapCount++;
            lastTapTime = unscaledNow;
            if (tapCount < settings.RevealTapCount) return TapOutcome.None;
            tapCount = 0;
            return TapOutcome.Reveal;
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
