// 职责：真实场景生命周期下检查环境快照恢复；纯规则测试不能验证 RenderSettings 的活动场景归属。
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class SpotlightEnvironmentViewTests
    {
        private readonly List<Scene> testScenes = new List<Scene>();
        private Scene CreateScene(string name)
        {
            var scene = SceneManager.CreateScene(name); testScenes.Add(scene); return scene;
        }
        [UnityTearDown]
        public IEnumerator RemoveTestScenes()
        {
            foreach (var scene in testScenes)
                if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            testScenes.Clear();
        }
        [UnityTest]
        public IEnumerator AdditiveView_UsesActiveOwnerAndRestoresOnDisable()
        {
            var previous = SceneManager.GetActiveScene();
            var owner = CreateScene("EnvironmentOwnerTest");
            var additive = CreateScene("EnvironmentAdditiveTest");
            SceneManager.SetActiveScene(owner);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.red;
            RenderSettings.fog = false;
            var config = ScriptableObject.CreateInstance<SpotlightEnvironmentConfig>();
            var host = new GameObject("EnvironmentUnderTest");
            SceneManager.MoveGameObjectToScene(host, additive);
            var sun = host.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 2.3f;
            sun.enabled = false;
            var view = host.AddComponent<SpotlightEnvironmentView>();
            Set(view, "config", config); Set(view, "directionalLight", sun);
            int invalidated = 0; view.OnInvalidated += () => invalidated++;
            try
            {
                view.Apply(new SpotlightEnvironmentRules.State(SpotlightEnvironmentRules.Preset.MoonNight));
                Assert.That(view.HasApplied, Is.True);
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(owner));
                Assert.That(sun.intensity, Is.EqualTo(config.Get(SpotlightEnvironmentRules.Preset.MoonNight).LightIntensity));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                view.enabled = false;
                Assert.That(view.HasApplied, Is.False);
                Assert.That(invalidated, Is.EqualTo(1));
                Assert.That(sun.intensity, Is.EqualTo(2.3f));
                Assert.That(sun.enabled, Is.False);
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
                Assert.That(RenderSettings.ambientLight, Is.EqualTo(Color.red));
                Assert.That(RenderSettings.fog, Is.False);
                view.enabled = true;
                Assert.That(invalidated, Is.EqualTo(2));
                view.Apply(new SpotlightEnvironmentRules.State(SpotlightEnvironmentRules.Preset.Day));
                Assert.That(view.HasApplied, Is.True);
                view.Apply(default);
                Assert.That(RenderSettings.ambientLight, Is.EqualTo(Color.red));
            }
            finally
            {
                view.Restore(); Object.Destroy(host); Object.Destroy(config);
                SceneManager.SetActiveScene(previous);
            }
            yield return SceneManager.UnloadSceneAsync(additive);
            yield return SceneManager.UnloadSceneAsync(owner);
        }

        [UnityTest]
        public IEnumerator OwnerChanges_DoNotOverwriteOtherSceneAndRestoreOnReturn()
        {
            var previous = SceneManager.GetActiveScene();
            var owner = CreateScene("EnvironmentRestoreOwnerTest");
            var other = CreateScene("EnvironmentRestoreOtherTest");
            SceneManager.SetActiveScene(owner);
            RenderSettings.ambientLight = Color.red;
            var config = ScriptableObject.CreateInstance<SpotlightEnvironmentConfig>();
            var host = new GameObject("EnvironmentRestoreUnderTest");
            var sun = host.AddComponent<Light>(); sun.type = LightType.Directional;
            var view = host.AddComponent<SpotlightEnvironmentView>();
            Set(view, "config", config); Set(view, "directionalLight", sun);
            try
            {
                view.Apply(new SpotlightEnvironmentRules.State(SpotlightEnvironmentRules.Preset.MoonNight));
                SceneManager.SetActiveScene(other);
                RenderSettings.ambientLight = Color.green;
                view.Restore();
                Assert.That(RenderSettings.ambientLight, Is.EqualTo(Color.green));
                SceneManager.SetActiveScene(owner);
                Assert.That(RenderSettings.ambientLight, Is.EqualTo(Color.red));
                Assert.That(view.HasApplied, Is.False);
                view.Apply(new SpotlightEnvironmentRules.State(SpotlightEnvironmentRules.Preset.Dawn));
                Assert.That(view.HasApplied, Is.True);
            }
            finally
            {
                SceneManager.SetActiveScene(owner); view.Restore();
                Object.Destroy(host); Object.Destroy(config); SceneManager.SetActiveScene(previous);
            }
            yield return SceneManager.UnloadSceneAsync(other);
            yield return SceneManager.UnloadSceneAsync(owner);
        }

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        [UnityTest]
        public IEnumerator Snow_DuringDialoguePause_StillEmitsAndRestoreClearsIt()
        {
            var host = new GameObject("EnvironmentPausedParticles");
            var config = ScriptableObject.CreateInstance<SpotlightEnvironmentConfig>();
            var sun = host.AddComponent<Light>(); sun.type = LightType.Directional;
            var snow = host.AddComponent<ParticleSystem>();
            snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = snow.main; main.playOnAwake = false;
            var emission = snow.emission; emission.rateOverTime = 60f;
            var view = host.AddComponent<SpotlightEnvironmentView>();
            Set(view, "config", config); Set(view, "directionalLight", sun); Set(view, "snow", snow);
            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f; // lint-ok: 测试复现对白暂停，finally恢复原值。
                view.Apply(new SpotlightEnvironmentRules.State(SpotlightEnvironmentRules.Preset.MoonNight, snow: true));
                Assert.That(snow.main.useUnscaledTime, Is.True, "对白暂停不能冻住剧情天气");
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(snow.particleCount, Is.GreaterThan(0));
                view.Restore();
                Assert.That(snow.particleCount, Is.Zero);
                Assert.That(snow.isPlaying, Is.False);
            }
            finally
            {
                Time.timeScale = originalTimeScale; // lint-ok: 还原测试前的全局时间状态。
                view.Restore(); Object.Destroy(host); Object.Destroy(config);
            }
        }
    }
}
