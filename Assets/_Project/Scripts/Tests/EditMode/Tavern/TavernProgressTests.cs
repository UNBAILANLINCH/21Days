// 职责：醉酒存读一致，移动倍率不从感知参数推导。
using System.Collections.Generic;
using Game.Tavern;
using NUnit.Framework;
namespace Game.Tests.EditMode.Tavern
{
    public sealed class TavernProgressTests
    {
        [Test]
        public void Restore_PerActorValuesAndIndependentSpeed_RejectsCorruptionBeforeMutation()
        {
            var rules = new TavernDrunkRules(10, 100, 100, 20, 10, .6f, .3f);
            rules.Restore(10, 100, new Dictionary<string, int> { { "a", 20 } });
            Assert.That(rules.BossSpeedMultiplier, Is.EqualTo(.6f)); Assert.That(rules.BossAwarenessMultiplier, Is.EqualTo(.3f).Within(.0001));
            Assert.That(rules.CustomerSpeed("a"), Is.EqualTo(.6f)); Assert.That(rules.CustomerSpeed("b"), Is.EqualTo(1));
            Assert.Throws<System.ArgumentException>(() => rules.Restore(0, 0, new Dictionary<string, int> { { "a", 21 } }));
            Assert.That(rules.PlayerDrunk, Is.EqualTo(10)); Assert.That(rules.BossDrunk, Is.EqualTo(100));
            rules.Reset(); Assert.That(rules.PlayerDrunk, Is.Zero);
        }
    }
}
