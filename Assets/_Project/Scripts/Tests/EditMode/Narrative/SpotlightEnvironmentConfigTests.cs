// 职责：校验环境配置默认档位；现有剧情规则测试不依赖 Unity，故不混入 SO 测试。
using Game.Narrative;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class SpotlightEnvironmentConfigTests
    {
        private SpotlightEnvironmentConfig config;

        [SetUp]
        public void SetUp() => config = ScriptableObject.CreateInstance<SpotlightEnvironmentConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [TestCase(SpotlightEnvironmentRules.Preset.Dawn)]
        [TestCase(SpotlightEnvironmentRules.Preset.Day)]
        [TestCase(SpotlightEnvironmentRules.Preset.MoonNight)]
        public void Get_KnownPreset_ProvidesReadableLightAndValidFog(SpotlightEnvironmentRules.Preset preset)
        {
            var settings = config.Get(preset);
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.LightIntensity, Is.GreaterThan(0f));
            Assert.That(settings.AmbientIntensity, Is.GreaterThan(0f));
            Assert.That(settings.FogStart, Is.GreaterThanOrEqualTo(0f));
            Assert.That(settings.FogEnd, Is.GreaterThan(settings.FogStart));
        }

        [TestCase(SpotlightEnvironmentRules.Preset.None)]
        [TestCase((SpotlightEnvironmentRules.Preset)99)]
        public void Get_UnsupportedPreset_ReturnsNoOverride(SpotlightEnvironmentRules.Preset preset)
            => Assert.That(config.Get(preset), Is.Null);

        [Test]
        public void Defaults_NightAndDawn_AreDimmerThanDay()
        {
            float day = config.Get(SpotlightEnvironmentRules.Preset.Day).LightIntensity;
            float dawn = config.Get(SpotlightEnvironmentRules.Preset.Dawn).LightIntensity;
            float night = config.Get(SpotlightEnvironmentRules.Preset.MoonNight).LightIntensity;
            Assert.That(night, Is.LessThan(dawn));
            Assert.That(dawn, Is.LessThan(day));
            Assert.That(config.AltarIntensity, Is.GreaterThan(config.CandleIntensity));
            Assert.That(config.CandleIntensity, Is.GreaterThan(0f));
        }
    }
}
