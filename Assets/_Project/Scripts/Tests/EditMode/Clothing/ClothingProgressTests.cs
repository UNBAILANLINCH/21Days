// 职责：证词和一次领取恢复，原数组不与快照共享。
using Game.Clothing;
using NUnit.Framework;
namespace Game.Tests.EditMode.Clothing
{
    public sealed class ClothingProgressTests
    {
        [Test]
        public void Restore_InvestigationsAndReceived_RejectsMissingCollectorEvidence()
        {
            var rules = new ClothingRules(3, 1);
            rules.Restore(new[] { true, true, false }, true);
            bool[] snapshot = rules.CaptureInvestigations(); snapshot[1] = false;
            Assert.That(rules.HasInvestigated(1), Is.True); Assert.That(rules.Received, Is.True);
            Assert.Throws<System.ArgumentException>(() => rules.Restore(new[] { true, false, true }, true));
            Assert.That(rules.HasInvestigated(1), Is.True);
            rules.Reset(); Assert.That(rules.Received, Is.False);
        }
    }
}
