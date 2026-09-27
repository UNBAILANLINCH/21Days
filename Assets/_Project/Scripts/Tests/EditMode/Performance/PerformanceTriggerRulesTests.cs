// 职责：钉住 PerformanceTriggerRules 的触发判定（once / played / busy 与原因优先级）与触发者渲染器 / Canvas 的隐藏 / 恢复。
// 为什么新建：Performance 模块首次落地（PRP/performance-pipeline 波 1），一个被测类一个测试类。
using Game.Performance;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformanceTriggerRulesTests
    {
        [Test]
        public void ShouldFire_FreshAndIdle_Fires()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(true, false, false, out string reason), Is.True);
            Assert.That(reason, Is.Null);
        }

        [Test]
        public void ShouldFire_OnceAndPlayed_SkipsAsPlayed()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(true, true, false, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(PerformanceTriggerRules.ReasonPlayed));
        }

        [Test]
        public void ShouldFire_NotOnceButPlayed_Fires()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(false, true, false, out string reason), Is.True);
            Assert.That(reason, Is.Null);
        }

        [Test]
        public void ShouldFire_ServiceRunning_SkipsAsBusy()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(true, false, true, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(PerformanceTriggerRules.ReasonBusy));
        }

        [Test]
        public void ShouldFire_PlayedAndBusy_ReportsPlayedFirst()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(true, true, true, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(PerformanceTriggerRules.ReasonPlayed));
        }

        [Test]
        public void ReasonConstants_MatchTelemetryValues()
        {
            Assert.That(PerformanceTriggerRules.ReasonPlayed, Is.EqualTo("played"));
            Assert.That(PerformanceTriggerRules.ReasonBusy, Is.EqualTo("busy"));
        }

        [Test]
        public void HideRenderers_ThenRestore_KeepsOriginalEnabledStates()
        {
            var go = new GameObject("perf_rules_renderers");
            try
            {
                var on = go.AddComponent<SpriteRenderer>();
                var offGo = new GameObject("off");
                offGo.transform.SetParent(go.transform, false);
                var off = offGo.AddComponent<SpriteRenderer>();
                off.enabled = false;
                Renderer[] renderers = { on, null, off };

                bool[] states = PerformanceTriggerRules.HideRenderers(renderers);

                Assert.That(states, Is.EqualTo(new[] { true, false, false }));
                Assert.That(on.enabled, Is.False);
                PerformanceTriggerRules.RestoreRenderers(renderers, states);
                Assert.That(on.enabled, Is.True);
                Assert.That(off.enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HideRenderers_Null_ReturnsEmptyAndRestoreIgnoresNull()
        {
            Assert.That(PerformanceTriggerRules.HideRenderers(null), Is.Empty);
            Assert.DoesNotThrow(() => PerformanceTriggerRules.RestoreRenderers(null, null));
        }

        [Test]
        public void HideVisuals_DuplicateNestedAndNullRoots_HidesOnceAndRestores()
        {
            var root = new GameObject("perf_rules_visuals");
            try
            {
                var sprite = root.AddComponent<SpriteRenderer>();
                var childGo = new GameObject("NameTag");
                childGo.transform.SetParent(root.transform, false);
                var canvas = childGo.AddComponent<Canvas>();
                GameObject[] roots = { root, null, root, childGo };

                PerformanceTriggerRules.HiddenVisuals hidden = PerformanceTriggerRules.HideVisuals(roots);

                Assert.That(hidden.Renderers.Count, Is.EqualTo(1), "组件去重，只记一次");
                Assert.That(hidden.Canvases.Count, Is.EqualTo(1));
                Assert.That(sprite.enabled, Is.False);
                Assert.That(canvas.enabled, Is.False);
                PerformanceTriggerRules.RestoreVisuals(hidden);
                Assert.That(sprite.enabled, Is.True);
                Assert.That(canvas.enabled, Is.True);
                Assert.That(PerformanceTriggerRules.HideVisuals(null).Renderers, Is.Empty);
                Assert.DoesNotThrow(() => PerformanceTriggerRules.RestoreVisuals(null));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HideBehaviours_ThenRestore_KeepsOriginalEnabledStates()
        {
            var go = new GameObject("perf_rules_canvases");
            try
            {
                var on = go.AddComponent<Canvas>();
                var offGo = new GameObject("off");
                offGo.transform.SetParent(go.transform, false);
                var off = offGo.AddComponent<Canvas>();
                off.enabled = false;
                Behaviour[] behaviours = { on, null, off };

                bool[] states = PerformanceTriggerRules.HideBehaviours(behaviours);

                Assert.That(states, Is.EqualTo(new[] { true, false, false }));
                Assert.That(on.enabled, Is.False);
                PerformanceTriggerRules.RestoreBehaviours(behaviours, states);
                Assert.That(on.enabled, Is.True);
                Assert.That(off.enabled, Is.False);
                Assert.That(PerformanceTriggerRules.HideBehaviours(null), Is.Empty);
                Assert.DoesNotThrow(() => PerformanceTriggerRules.RestoreBehaviours(null, null));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
