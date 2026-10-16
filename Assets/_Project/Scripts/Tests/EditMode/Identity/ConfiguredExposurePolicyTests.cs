// 职责：钉住「按配置」的露馅后果策略（PRP/combat-iteration D1）——三种口径各一条、代码默认等于旧行为、
//   每次现读数值块（改配置立刻生效）、不认识的值不猜后果。
// 为什么新建：被测类 ConfiguredExposurePolicy 是本波新建的；IdentityRulesTests 用的是测试内的 FixedPolicy，
//   测的是「规则层把后果交给策略」，不测「策略按配置答什么」。

using Game.Identity;
using NUnit.Framework;
using static Game.Tests.EditMode.Identity.IdentityTestContent;

namespace Game.Tests.EditMode.Identity
{
    public sealed class ConfiguredExposurePolicyTests
    {
        private IdentitySettings settings;
        private ConfiguredExposurePolicy policy;

        [SetUp]
        public void SetUp()
        {
            settings = NewSettings();
            policy = new ConfiguredExposurePolicy(settings);
        }

        /// <summary>代码默认 None = 旧行为（此前不注入策略，后果恒为 None）；新口径只写资产。</summary>
        [Test]
        public void Decide_WithCodeDefaults_ReturnsNone()
        {
            Assert.That(settings.ExposureOutcome, Is.EqualTo(ExposureOutcome.None), "代码默认值必须等于旧行为");
            Assert.That(policy.Decide(ExposureCause.Alertness), Is.EqualTo(ExposureOutcome.None));
        }

        [Test]
        public void Decide_WhenConfiguredChase_ReturnsChase()
        {
            settings.ExposureOutcome = ExposureOutcome.Chase;

            Assert.That(policy.Decide(ExposureCause.Alertness), Is.EqualTo(ExposureOutcome.Chase));
            Assert.That(policy.Decide(ExposureCause.Ledger | ExposureCause.Reveal), Is.EqualTo(ExposureOutcome.Chase),
                "口径不分原因：命中哪几条都按同一口径");
        }

        [Test]
        public void Decide_WhenConfiguredDeath_ReturnsDeath()
        {
            settings.ExposureOutcome = ExposureOutcome.Death;

            Assert.That(policy.Decide(ExposureCause.Checkpoint), Is.EqualTo(ExposureOutcome.Death));
        }

        /// <summary>每次现读：策略建好之后再改数值块，下一次判定就按新口径（策划改资产不用重建策略）。</summary>
        [Test]
        public void Decide_AfterSettingsChange_ReadsTheNewValue()
        {
            Assert.That(policy.Decide(ExposureCause.Suspicion), Is.EqualTo(ExposureOutcome.None));

            settings.ExposureOutcome = ExposureOutcome.Chase;

            Assert.That(policy.Decide(ExposureCause.Suspicion), Is.EqualTo(ExposureOutcome.Chase));
        }

        /// <summary>负对照：没命中任何原因（契约上不会发生）不给后果；资产里填了不认识的值也不猜一种后果。</summary>
        [Test]
        public void Decide_NoCauseOrUnknownConfiguredValue_ReturnsNone()
        {
            settings.ExposureOutcome = ExposureOutcome.Chase;
            Assert.That(policy.Decide(ExposureCause.None), Is.EqualTo(ExposureOutcome.None));

            settings.ExposureOutcome = (ExposureOutcome)99;
            Assert.That(policy.Decide(ExposureCause.Alertness), Is.EqualTo(ExposureOutcome.None));
        }

        /// <summary>接进规则层：ResolveExposure 的后果就是策略按配置答的那一个。</summary>
        [Test]
        public void ResolveExposure_WithConfiguredPolicy_UsesConfiguredOutcome()
        {
            settings.ExposureOutcome = ExposureOutcome.Chase;
            var rules = new IdentityRules(settings, NewCatalog(), policy);
            var state = new IdentityState();
            var ledger = new IdentityLedger();
            rules.TryEnter(state, ledger, StewardId);

            ExposureResolution result = rules.ResolveExposure(state, ledger, SuspicionState.FromSettings(settings),
                ExposureSignals.Alert(true, false));

            Assert.That(result.Outcome, Is.EqualTo(ExposureOutcome.Chase));
        }

        [Test]
        public void Constructor_NullSettings_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ConfiguredExposurePolicy(null));
        }
    }
}
