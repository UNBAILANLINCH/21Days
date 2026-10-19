// 职责：穷举合法拳局，并检查重复、非法和过期确认，不依赖场景。
using Game.Tavern;
using NUnit.Framework;
namespace Game.Tests.EditMode.Tavern
{
    public sealed class TavernFingerGuessTests
    {
        [Test]
        public void Resolve_AllLegalChoices_OnlyOneCorrectWins()
        {
            var rules = new TavernFingerGuessRules();
            for (int player = 0; player <= 5; player++)
            for (int opponent = 0; opponent <= 5; opponent++)
            for (int call = player; call <= player + 5; call++)
            for (int otherCall = opponent; otherCall <= opponent + 5; otherCall++)
            {
                long round = rules.Begin("shop_boss", opponent, otherCall);
                var intent = new TavernIntent(round, "shop_boss", true, player, call);
                Assert.That(rules.TryResolve(in intent, out var result, out _), Is.True);
                int total = player + opponent;
                Assert.That(result.Total, Is.EqualTo(total));
                var expected = call == total && otherCall != total ? TavernFingerGuessRules.Outcome.PlayerWins
                    : otherCall == total && call != total ? TavernFingerGuessRules.Outcome.OpponentWins : TavernFingerGuessRules.Outcome.Tie;
                Assert.That(result.Value, Is.EqualTo(expected));
                Assert.That(rules.TryResolve(in intent, out _, out _), Is.False, "一局只能结算一次");
            }
        }
        [Test]
        public void Resolve_InvalidOrStale_DoesNotConsumeCurrentRound()
        {
            var rules = new TavernFingerGuessRules();
            long old = rules.Begin("shop_customer_a", 2, 5);
            long current = rules.Begin("shop_customer_b", 1, 3);
            foreach (var intent in new[] {
                new TavernIntent(old, "shop_customer_a", true, 2, 5), new TavernIntent(current, "shop_customer_b", false, 2, 5),
                new TavernIntent(current, "shop_customer_b", true, 6, 6), new TavernIntent(current, "shop_customer_b", true, 5, 4) })
                Assert.That(rules.TryResolve(in intent, out _, out _), Is.False);
            Assert.That(rules.RoundId, Is.EqualTo(current)); rules.Cancel();
            var cancelled = new TavernIntent(current, "shop_customer_b", true, 2, 5);
            Assert.That(rules.TryResolve(in cancelled, out _, out _), Is.False);
        }
    }
}
