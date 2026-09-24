// 职责：锁定对白表现策略——三连点补全、单点推进、倍速循环、自动播放计时、复位与设置校验。
// 新建原因：DialogueRulesTests 只测规则层，策略类是独立的纯 C# 类型，按「被测类 + Tests」单独成文件。
using System;
using Game.Dialogue;
using NUnit.Framework;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class DialoguePlaybackPolicyTests
    {
        private const DialogueSaveData.Phase Typing = DialogueSaveData.Phase.Typing;
        private const DialogueSaveData.Phase AwaitAdvance = DialogueSaveData.Phase.AwaitAdvance;
        private DialoguePlaybackPolicy policy;

        private static DialoguePlaybackSettings Settings() =>
            new DialoguePlaybackSettings(40f, new[] { 1f, 2f, 4f }, 3, 0.5f, 1.5f);

        [SetUp]
        public void SetUp() => policy = new DialoguePlaybackPolicy(Settings());

        [Test]
        public void RegisterTap_WhenTypingThreeTapsInWindow_RevealsOnThird()
        {
            Assert.That(policy.RegisterTap(0f, Typing), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.None));
            Assert.That(policy.RegisterTap(0.2f, Typing), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.None));
            Assert.That(policy.RegisterTap(0.4f, Typing), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.Reveal));
            Assert.That(policy.RegisterTap(0.5f, Typing), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.None));
        }

        [Test]
        public void RegisterTap_WhenTapOutsideWindow_RestartsCountFromOne()
        {
            policy.RegisterTap(0f, Typing);
            policy.RegisterTap(0.3f, Typing);
            Assert.That(policy.RegisterTap(2f, Typing), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.None));
            Assert.That(policy.RegisterTap(2.1f, Typing), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.None));
            Assert.That(policy.RegisterTap(2.2f, Typing), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.Reveal));
        }

        [Test]
        public void RegisterTap_WhenAwaitAdvance_SingleTapAdvances()
        {
            Assert.That(policy.RegisterTap(0f, AwaitAdvance), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.Advance));
        }

        [Test]
        public void RegisterTap_WhenOtherPhase_ReturnsNone()
        {
            var phases = new[] { DialogueSaveData.Phase.Closed, DialogueSaveData.Phase.Preparing,
                DialogueSaveData.Phase.AwaitChoice, DialogueSaveData.Phase.Completed };
            foreach (DialogueSaveData.Phase phase in phases)
                Assert.That(policy.RegisterTap(0f, phase), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.None));
        }

        [Test]
        public void CycleSpeed_WhenCalled_WrapsAndScalesRates()
        {
            Assert.That(policy.SpeedIndex, Is.EqualTo(0));
            Assert.That(policy.CharactersPerSecond, Is.EqualTo(40f));
            Assert.That(policy.AutoAdvanceDelay, Is.EqualTo(1.5f));
            policy.CycleSpeed();
            Assert.That(policy.SpeedIndex, Is.EqualTo(1));
            Assert.That(policy.Speed, Is.EqualTo(2f));
            Assert.That(policy.CharactersPerSecond, Is.EqualTo(80f));
            Assert.That(policy.AutoAdvanceDelay, Is.EqualTo(0.75f));
            policy.CycleSpeed();
            Assert.That(policy.SpeedIndex, Is.EqualTo(2));
            Assert.That(policy.CharactersPerSecond, Is.EqualTo(160f));
            Assert.That(policy.AutoAdvanceDelay, Is.EqualTo(0.375f));
            policy.CycleSpeed();
            Assert.That(policy.SpeedIndex, Is.EqualTo(0));
        }

        [Test]
        public void TickAuto_WhenAutoOffOrNotAwaitAdvance_NeverFires()
        {
            Assert.That(policy.TickAuto(10f, AwaitAdvance), Is.False);
            policy.ToggleAuto();
            Assert.That(policy.TickAuto(10f, Typing), Is.False);
        }

        [Test]
        public void TickAuto_WhenAutoAndAwaitAdvance_FiresAfterDelayAndPhaseChangeClears()
        {
            policy.ToggleAuto();
            Assert.That(policy.TickAuto(1f, AwaitAdvance), Is.False);
            Assert.That(policy.TickAuto(0.6f, AwaitAdvance), Is.True);
            Assert.That(policy.TickAuto(1f, AwaitAdvance), Is.False);
            Assert.That(policy.TickAuto(0f, Typing), Is.False);
            Assert.That(policy.TickAuto(1f, AwaitAdvance), Is.False);
        }

        [Test]
        public void ResetForDialogue_AfterChanges_RestoresDefaults()
        {
            policy.CycleSpeed();
            policy.ToggleAuto();
            policy.BeginSkip();
            policy.RegisterTap(0f, Typing);
            policy.RegisterTap(0.1f, Typing);
            policy.ResetForDialogue();
            Assert.That(policy.SpeedIndex, Is.EqualTo(0));
            Assert.That(policy.AutoPlay, Is.False);
            Assert.That(policy.Skipping, Is.False);
            Assert.That(policy.RegisterTap(0.2f, Typing), Is.EqualTo(DialoguePlaybackPolicy.TapOutcome.None));
        }

        [Test]
        public void Settings_WhenArgumentsInvalid_Throws()
        {
            var steps = new[] { 1f, 2f };
            Assert.Throws<ArgumentException>(() => new DialoguePlaybackSettings(0f, steps, 3, 0.5f, 1f));
            Assert.Throws<ArgumentException>(() => new DialoguePlaybackSettings(10f, null, 3, 0.5f, 1f));
            Assert.Throws<ArgumentException>(() => new DialoguePlaybackSettings(10f, new float[0], 3, 0.5f, 1f));
            Assert.Throws<ArgumentException>(() => new DialoguePlaybackSettings(10f, new[] { 1f, 0f }, 3, 0.5f, 1f));
            Assert.Throws<ArgumentException>(() => new DialoguePlaybackSettings(10f, steps, 0, 0.5f, 1f));
            Assert.Throws<ArgumentException>(() => new DialoguePlaybackSettings(10f, steps, 3, 0f, 1f));
            Assert.Throws<ArgumentException>(() => new DialoguePlaybackSettings(10f, steps, 3, 0.5f, -1f));
            Assert.DoesNotThrow(() => new DialoguePlaybackSettings(10f, steps, 1, 0.5f, 0f));
        }
    }
}
