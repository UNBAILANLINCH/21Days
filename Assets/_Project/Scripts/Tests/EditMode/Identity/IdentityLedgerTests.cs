// 职责：钉住账簿——两种计量口径、超量判定（「超过」而不是「达到」）、关闭判定的上限口径、无效 id 不记。
// 为什么新建：口径是原文没写的待定项（`02_身份暴露与怀疑.md:115` R17），两种口径都得有用例钉住，
//   否则将来改配置时会静默改变触发追逐的条件。

using Game.Identity;
using NUnit.Framework;
using static Game.Tests.EditMode.Identity.IdentityTestContent;

namespace Game.Tests.EditMode.Identity
{
    public sealed class IdentityLedgerTests
    {
        private IdentityLedger ledger;

        [SetUp]
        public void SetUp()
        {
            ledger = new IdentityLedger();
        }

        [Test]
        public void Record_SameIdentityTwice_CountsDistinctOnceButTwoUses()
        {
            Assert.That(ledger.Record(IdentityId.From(Musician)), Is.True, "第一次是新身份");
            Assert.That(ledger.Record(IdentityId.From(Musician)), Is.False, "第二次不是新身份");

            Assert.That(ledger.DistinctCount, Is.EqualTo(1));
            Assert.That(ledger.TotalUses, Is.EqualTo(2));
            Assert.That(ledger.UsedIdentities, Is.EqualTo(new[] { IdentityId.From(Musician) }));
        }

        [Test]
        public void IsOverLimit_DistinctIdentitiesMode_ComparesDistinctCount()
        {
            ledger.Record(IdentityId.From(Musician));
            ledger.Record(IdentityId.From(Musician));
            ledger.Record(IdentityId.From(Musician));

            Assert.That(ledger.IsOverLimit(LedgerCountingMode.DistinctIdentities, 2), Is.False,
                "同一身份借三次，不同身份个数仍是 1");
            Assert.That(ledger.IsOverLimit(LedgerCountingMode.DistinctIdentities, 1), Is.False,
                "恰好等于上限不算超量");
        }

        [Test]
        public void IsOverLimit_TotalUsesMode_ComparesUseCount()
        {
            ledger.Record(IdentityId.From(Musician));
            ledger.Record(IdentityId.From(Musician));
            ledger.Record(IdentityId.From(Steward));

            Assert.That(ledger.IsOverLimit(LedgerCountingMode.TotalUses, 2), Is.True);
            Assert.That(ledger.IsOverLimit(LedgerCountingMode.DistinctIdentities, 2), Is.False);
        }

        [Test]
        public void IsOverLimit_NonPositiveLimit_DisablesJudgement()
        {
            for (int i = 0; i < 20; i++)
            {
                ledger.Record(IdentityId.From(Musician));
            }

            Assert.That(ledger.IsOverLimit(LedgerCountingMode.TotalUses, 0), Is.False);
            Assert.That(ledger.IsOverLimit(LedgerCountingMode.TotalUses, -1), Is.False);
        }

        [Test]
        public void Record_BodyOrMalformedId_IsIgnored()
        {
            Assert.That(ledger.Record(IdentityId.None), Is.False, "本体不是身份，不进账簿");

            Assert.That(ledger.TotalUses, Is.Zero);
            Assert.That(ledger.DistinctCount, Is.Zero);
            Assert.That(ledger.HasUsed(IdentityId.None), Is.False);
        }

        [Test]
        public void Clear_ResetsBothCounts()
        {
            ledger.Record(IdentityId.From(Musician));
            ledger.Record(IdentityId.From(Steward));

            ledger.Clear();

            Assert.That(ledger.DistinctCount, Is.Zero);
            Assert.That(ledger.TotalUses, Is.Zero);
            Assert.That(ledger.HasUsed(IdentityId.From(Musician)), Is.False);
        }
    }
}
