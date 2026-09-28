// 职责：PerformanceTemplateFactory 的 EditMode 测试——在临时目录里建一段演出（世界舞台壳），核对预制体、时间轴轨道、
//   舞台相机（透视 Base、不打 MainCamera 标签）、图层与校验结果；非法 / 重名 id 抛异常。
// 为什么新建（复用 → 扩展 → 新建）：模板工厂是新写的编辑器类，没有现成测试可扩展。
using System;
using System.Linq;
using Game.Editor.Performance;
using Game.Performance;
using Game.Performance.Timeline;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

namespace Game.Tests.EditMode.Editor.Performance
{
    public sealed class PerformanceTemplateFactoryTests
    {
        private const string TempRoot = "Assets/_Project/Tests_PerformanceFactoryTmp";
        private const string TestId = "perf_factory_test";

        private PerformanceTemplateOptions options;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempRoot)) AssetDatabase.CreateFolder("Assets/_Project", "Tests_PerformanceFactoryTmp");
            options = new PerformanceTemplateOptions
            {
                PrefabFolder = TempRoot + "/Prefabs",
                TimelineFolder = TempRoot + "/Timelines",
                RegisterAddressable = false,
            };
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void Create_WithValidId_BuildsPrefabWithStageAndDirector()
        {
            PerformanceTemplateResult result = PerformanceTemplateFactory.Create(TestId, options);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<PerformanceStage>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PlayableDirector>(), Is.Not.Null);
            Assert.That(result.Registered, Is.False);
        }

        [Test]
        public void Create_WithValidId_TimelineHasSubtitleAnimationAudioTracks()
        {
            PerformanceTemplateResult result = PerformanceTemplateFactory.Create(TestId, options);

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(result.TimelinePath);
            Assert.That(timeline, Is.Not.Null);
            TrackAsset[] tracks = timeline.GetOutputTracks().ToArray();
            Assert.That(tracks.OfType<SubtitleTrack>().Count(), Is.EqualTo(1));
            Assert.That(tracks.OfType<AnimationTrack>().Count(), Is.EqualTo(1));
            Assert.That(tracks.OfType<AudioTrack>().Count(), Is.EqualTo(1));
            Assert.That(tracks.Length, Is.EqualTo(3), "模板只保留字幕、动作、音效三条轨");
            Assert.That(timeline.duration, Is.EqualTo(PerformanceTemplateFactory.DefaultDurationSeconds));
        }

        [Test]
        public void Create_WithValidId_StageCameraIsPerspectiveBaseAndUntagged()
        {
            PerformanceTemplateResult result = PerformanceTemplateFactory.Create(TestId, options);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            Camera camera = prefab.GetComponent<PerformanceStage>().StageCamera;
            Assert.That(camera, Is.Not.Null, "舞台相机应已接到 PerformanceStage 上");
            Assert.That(camera.orthographic, Is.False, "舞台相机应为透视");
            Assert.That(camera.fieldOfView, Is.EqualTo(PerformanceTemplateFactory.DefaultCameraFieldOfView));
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            Assert.That(data, Is.Not.Null, "舞台相机应带 URP 附加数据");
            Assert.That(data.renderType, Is.EqualTo(CameraRenderType.Base), "舞台相机应为 URP Base");
            Assert.That(camera.CompareTag("MainCamera"), Is.False, "舞台相机不能打 MainCamera 标签");
            Assert.That(camera.GetComponent<AudioListener>(), Is.Null, "舞台相机不带 AudioListener");
            Assert.That(prefab.transform.Find(PerformanceTemplateFactory.ActorsName), Is.Not.Null, "应有演员站位根");
        }

        [Test]
        public void Create_WithValidId_AllObjectsOnPerformanceLayer()
        {
            PerformanceTemplateResult result = PerformanceTemplateFactory.Create(TestId, options);

            int layer = LayerMask.NameToLayer(PerformanceValidator.PerformanceLayerName);
            if (layer < 0) Assert.Ignore("工程里没有 Performance 图层。");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                Assert.That(t.gameObject.layer, Is.EqualTo(layer), t.name);
        }

        [Test]
        public void Create_WithValidId_PassesValidatorWithoutErrors()
        {
            PerformanceTemplateResult result = PerformanceTemplateFactory.Create(TestId, options);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            var errors = PerformanceValidator.Validate(prefab).Where(i => i.IsError).ToList();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [TestCase("")]
        [TestCase("Perf_Upper")]
        [TestCase("perf-dash")]
        [TestCase("perf space")]
        public void Create_WithInvalidId_Throws(string id)
        {
            Assert.Throws<ArgumentException>(() => PerformanceTemplateFactory.Create(id, options));
        }

        [Test]
        public void Create_WithExistingId_Throws()
        {
            PerformanceTemplateFactory.Create(TestId, options);

            Assert.Throws<ArgumentException>(() => PerformanceTemplateFactory.Create(TestId, options));
        }
    }
}
