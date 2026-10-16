// 职责：演奏前置身份、巡逻闸和重试；音游判分复用原模块测试。
using Game.Core.Telemetry;
using Game.Theater;
using NUnit.Framework;
namespace Game.Tests.EditMode.Theater
{
    public sealed class TheaterRulesTests
    {
        [Test]
        public void Perform_RequiresOrderedRoleActions_FailureCanRetry_SuccessCloses()
        {
            var rules = new TheaterController(NullTelemetryScope.Instance);
            rules.Apply(new TheaterIntent(TheaterAction.Verify, "director")); Assert.That(rules.Entered, Is.False);
            rules.Apply(new TheaterIntent(TheaterAction.Redirect, "director")); Assert.That(rules.PatrolRedirected, Is.False);
            rules.Apply(new TheaterIntent(TheaterAction.Verify, "apprentice"));
            Assert.That(rules.CanPerform("musician"), Is.False);
            rules.Apply(new TheaterIntent(TheaterAction.Redirect, "director"));
            Assert.That(rules.CanPerform("director"), Is.False); Assert.That(rules.CanPerform("musician"), Is.True);
            rules.Apply(new TheaterIntent(TheaterAction.FinishFailure, "musician")); Assert.That(rules.CanPerform("musician"), Is.True);
            rules.Apply(new TheaterIntent(TheaterAction.FinishSuccess, "musician")); Assert.That(rules.Completed, Is.True); Assert.That(rules.CanPerform("musician"), Is.False);
            rules.Reset(); Assert.That(rules.Entered || rules.PatrolRedirected || rules.Completed, Is.False);
        }
    }
}
