// 职责：固定习惯可被学会、习惯改变能适应、强度缓变与存档隔离。
using Game.Core.Simulation;
using Game.Tavern;
using NUnit.Framework;
namespace Game.Tests.EditMode.Tavern
{
    public sealed class TavernOpponentLearningTests
    {
        [Test]
        public void Choose_HistoryChanges_AdaptsWithoutCurrentAnswer()
        {
            var learning = new TavernOpponentLearning(1000);
            for (int i = 0; i < 24; i++) learning.Observe(2, 5, TavernFingerGuessRules.Outcome.Tie);
            var random = new XorShiftRandomStream(123);
            learning.Choose(random, 1, 2, 600, out int fingers, out int call);
            Assert.That(call - fingers, Is.EqualTo(2)); Assert.That(fingers, Is.Not.EqualTo(3));
            for (int i = 0; i < 24; i++) learning.Observe(5, 6, TavernFingerGuessRules.Outcome.Tie);
            learning.Choose(random, 1, 2, 600, out fingers, out call);
            Assert.That(call - fingers, Is.EqualTo(5)); Assert.That(fingers, Is.Not.EqualTo(1));
            Assert.That(TavernFingerGuessRules.IsLegal(fingers, call), Is.True);
        }
        [Test]
        public void Observe_TiesDoNotPunishAndStrengthMovesAtMostConfiguredStep()
        {
            var learning = new TavernOpponentLearning(400);
            learning.Observe(2, 5, TavernFingerGuessRules.Outcome.Tie);
            Assert.That(learning.Performance, Is.EqualTo(500));
            learning.Observe(2, 5, TavernFingerGuessRules.Outcome.PlayerWins);
            Assert.That(learning.Performance, Is.GreaterThan(500));
            learning.AdjustStrength(900, 35); Assert.That(learning.Strength, Is.EqualTo(435));
            learning.AdjustStrength(0, 35); Assert.That(learning.Strength, Is.EqualTo(400));
        }
        [Test]
        public void CaptureRestore_IsDeepCopyAndRejectsMalformedHistory()
        {
            var learning = new TavernOpponentLearning(200);
            learning.Observe(2, 5, TavernFingerGuessRules.Outcome.PlayerWins);
            var saved = learning.Capture(); var restored = new TavernOpponentLearning(650, saved);
            saved.History.Clear(); Assert.That(restored.Samples, Is.EqualTo(1)); Assert.That(learning.Samples, Is.EqualTo(1));
            saved.History.Add(36); Assert.Throws<System.ArgumentException>(() => new TavernOpponentLearning(200, saved));
        }
        [Test]
        public void Controller_ResolveOnce_UpdatesOneCupAndOneSample_AndRestoreIsolatesSlots()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<TavernConfig>();
            try
            {
                var controller = new TavernController(config, null, () => 17);
                long round = controller.BeginGuess("shop_boss");
                var intent = new TavernIntent(round, "shop_boss", true, 2, 5);
                Assert.That(controller.ResolveGuess(in intent, out var result, out _), Is.True);
                int cups = controller.Rules.BossDrunk + controller.Rules.PlayerDrunk;
                Assert.That(cups, Is.EqualTo(result.Value == TavernFingerGuessRules.Outcome.Tie ? 0 : 10));
                Assert.That(controller.ResolveGuess(in intent, out _, out _), Is.False);
                Assert.That(controller.Rules.BossDrunk + controller.Rules.PlayerDrunk, Is.EqualTo(cups));
                var saved = controller.CaptureLearning();
                Assert.That(saved["shop_boss"].History.Count, Is.EqualTo(1));
                Assert.That(saved.ContainsKey("shop_customer_a"), Is.False);
                controller.RestoreLearning(null);
                Assert.That(controller.CaptureLearning(), Is.Empty);
                controller.RestoreLearning(saved); saved["shop_boss"].History.Clear();
                Assert.That(controller.CaptureLearning()["shop_boss"].History.Count, Is.EqualTo(1));
                Assert.That(controller.Guess.IsPending, Is.False);
                Assert.That(config.DesiredStrength("shop_boss", 900), Is.GreaterThan(config.DesiredStrength("shop_boss", 100)));
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }
    }
}
