// 职责：PerformanceTemplateFactory 的 EditMode 测试——在临时目录里建一段演出，核对预制体、时间轴、轨道绑定与图层；非法 / 重名 id 抛异常。
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
                ActorSprites = new[] { new SpritePerformanceActor.ExpressionEntry("smile", null) },
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
        public void Create_WithValidId_TimelineHasFourTypedTracks()
        {
            PerformanceTemplateResult result = PerformanceTemplateFactory.Create(TestId, options);

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(result.TimelinePath);
            Assert.That(timeline, Is.Not.Null);
            TrackAsset[] tracks = timeline.GetOutputTracks().ToArray();
            Assert.That(tracks.OfType<SubtitleTrack>().Count(), Is.EqualTo(1));
            Assert.That(tracks.OfType<ExpressionTrack>().Count(), Is.EqualTo(1));
            Assert.That(tracks.OfType<AnimationTrack>().Count(), Is.EqualTo(1));
            Assert.That(tracks.OfType<AudioTrack>().Count(), Is.EqualTo(1));
            Assert.That(timeline.duration, Is.EqualTo(PerformanceTemplateFactory.DefaultDurationSeconds));
        }

        [Test]
        public void Create_WithValidId_ExpressionTrackBoundToActor()
        {
            PerformanceTemplateResult result = PerformanceTemplateFactory.Create(TestId, options);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            var director = prefab.GetComponent<PlayableDirector>();
            var timeline = (TimelineAsset)director.playableAsset;
            ExpressionTrack track = timeline.GetOutputTracks().OfType<ExpressionTrack>().Single();
            var actor = director.GetGenericBinding(track) as SpritePerformanceActor;
            Assert.That(actor, Is.Not.Null);
            Assert.That(actor.ExpressionNames, Is.EqualTo(new[] { "smile" }));
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
