// 职责：PerformanceValidator 的 EditMode 测试——用内存物体与不落盘的时间轴，逐类覆盖校验规则的正反例。
// 为什么新建（复用 → 扩展 → 新建）：校验器是新写的编辑器类，没有现成测试可扩展。
using System.Collections.Generic;
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
    public sealed class PerformanceValidatorTests
    {
        private readonly List<Object> created = new List<Object>();

        private GameObject root;
        private PerformanceStage stage;
        private PlayableDirector director;
        private Camera stageCamera;
        private GameObject actor;
        private TimelineAsset timeline;
        private SubtitleTrack subtitleTrack;

        [SetUp]
        public void SetUp()
        {
            root = Track(new GameObject("perf_validator_test"));
            director = root.AddComponent<PlayableDirector>();
            stage = root.AddComponent<PerformanceStage>();

            // 合格的舞台相机：透视、URP Base、不打 MainCamera 标签（新建 GameObject 默认 Untagged）。
            var cameraGo = new GameObject("StageCamera");
            cameraGo.transform.SetParent(root.transform, false);
            stageCamera = cameraGo.AddComponent<Camera>();
            stageCamera.orthographic = false;
            UniversalAdditionalCameraData data = cameraGo.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base;

            var actorGo = new GameObject("Actor");
            actorGo.transform.SetParent(root.transform, false);
            actor = actorGo;

            timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 8d;
            subtitleTrack = Track(timeline.CreateTrack<SubtitleTrack>(null, "字幕"));
            director.playableAsset = timeline;

            SetStage(director, stageCamera);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void Validate_WellFormedStage_HasNoIssues()
        {
            List<PerformanceIssue> issues = PerformanceValidator.Validate(root);

            Assert.That(issues.Select(i => i.ToString()), Is.Empty);
        }

        [Test]
        public void Validate_NullRoot_ReportsError()
        {
            Assert.That(Codes(PerformanceValidator.Validate(null)), Does.Contain("stage_root_missing"));
        }

        [Test]
        public void Validate_WithoutStage_ReportsStageMissing()
        {
            Object.DestroyImmediate(stage);

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("stage_missing"));
        }

        [Test]
        public void Validate_WithoutDirector_ReportsDirectorMissing()
        {
            SetStage(null, stageCamera);
            Object.DestroyImmediate(director);

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("director_missing"));
        }

        [Test]
        public void Validate_WithoutTimeline_ReportsTimelineMissing()
        {
            director.playableAsset = null;

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("timeline_missing"));
        }

        [Test]
        public void Validate_ZeroDuration_ReportsDurationZero()
        {
            timeline.durationMode = TimelineAsset.DurationMode.BasedOnClips;

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("duration_zero"));
        }

        [Test]
        public void Validate_PositiveDuration_NoDurationZero()
        {
            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Not.Contain("duration_zero"));
        }

        [Test]
        public void Validate_WithoutCamera_ReportsCameraMissing()
        {
            SetStage(director, null);

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("camera_missing"));
        }

        [Test]
        public void Validate_EmptySubtitle_WarnsSubtitleEmpty()
        {
            AddSubtitle(string.Empty);

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("subtitle_empty"));
        }

        [Test]
        public void Validate_FilledSubtitle_NoSubtitleWarning()
        {
            AddSubtitle("你好。");

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Not.Contain("subtitle_empty"));
        }

        [TestCase(0d)]
        [TestCase(8d)]
        [TestCase(12d)]
        public void Validate_HoldMarkerOutOfRange_WarnsHold(double time)
        {
            AddHold(time);

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("hold_out_of_range"));
        }

        [Test]
        public void Validate_HoldMarkerInside_NoHoldWarning()
        {
            AddHold(4d);

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Not.Contain("hold_out_of_range"));
        }

        [Test]
        public void Validate_ExpectedAddressNotRegistered_ReportsAddressMissing()
        {
            Assert.That(Codes(PerformanceValidator.Validate(root, "perf_not_registered")), Does.Contain("address_missing"));
        }

        [Test]
        public void Validate_NoExpectedAddress_SkipsAddressCheck()
        {
            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Not.Contain("address_missing"));
        }

        [Test]
        public void Validate_PerspectiveBaseCameraAnyLayer_NoCameraIssues()
        {
            stageCamera.cullingMask = ~0;
            actor.layer = 0;

            List<string> codes = Codes(PerformanceValidator.Validate(root));

            Assert.That(codes, Does.Not.Contain("camera_world_not_base"));
            Assert.That(codes, Does.Not.Contain("camera_world_orthographic"));
            Assert.That(codes, Does.Not.Contain("camera_tagged_main"));
            Assert.That(codes, Is.Empty, "演员站在世界图层上、遮罩运行时从主相机拷贝，都不查");
        }

        [Test]
        public void Validate_NonBaseOrthographicCamera_Warns()
        {
            stageCamera.GetComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Overlay;
            stageCamera.orthographic = true;

            List<PerformanceIssue> issues = PerformanceValidator.Validate(root);

            Assert.That(issues.Single(i => i.Code == "camera_world_not_base").Severity, Is.EqualTo(PerformanceIssueSeverity.Warning));
            Assert.That(Codes(issues), Does.Contain("camera_world_orthographic"));
        }

        [Test]
        public void Validate_CastDuplicateOrMissingAvatar_Warns()
        {
            Sprite sprite = MakeSprite();
            SetCast(("阿米娅", sprite), ("阿米娅", sprite), ("陈", null));

            List<PerformanceIssue> issues = PerformanceValidator.Validate(root);

            Assert.That(issues.Single(i => i.Code == "cast_speaker_duplicate").Severity, Is.EqualTo(PerformanceIssueSeverity.Warning));
            Assert.That(issues.Single(i => i.Code == "cast_avatar_missing").Severity, Is.EqualTo(PerformanceIssueSeverity.Warning));
        }

        [Test]
        public void Validate_SubtitleSpeakerNotInCast_ReportsInfoExceptNarration()
        {
            SetCast(("阿米娅", MakeSprite()));
            AddSubtitle("在名单里。", "阿米娅");
            AddSubtitle("不在名单里。", "陈");
            AddSubtitle("旁白。", string.Empty);

            List<PerformanceIssue> infos = PerformanceValidator.Validate(root)
                .Where(i => i.Code == "subtitle_speaker_not_in_cast").ToList();

            Assert.That(infos.Count, Is.EqualTo(1), "只有「陈」该提示，旁白与已登记的不提示");
            Assert.That(infos[0].Severity, Is.EqualTo(PerformanceIssueSeverity.Info));
            Assert.That(infos[0].IsError, Is.False);
        }

        private void SetCast(params (string speaker, Sprite avatar)[] entries)
        {
            using (var so = new SerializedObject(stage))
            {
                SerializedProperty list = so.FindProperty("cast");
                list.arraySize = entries.Length;
                for (int i = 0; i < entries.Length; i++)
                {
                    SerializedProperty element = list.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("speaker").stringValue = entries[i].speaker;
                    element.FindPropertyRelative("avatar").objectReferenceValue = entries[i].avatar;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private Sprite MakeSprite()
        {
            var texture = Track(new Texture2D(4, 4));
            return Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)));
        }

        private void AddSubtitle(string text, string speaker)
        {
            TimelineClip clip = subtitleTrack.CreateClip<SubtitleClip>();
            var asset = Track((Object)clip.asset);
            using (var so = new SerializedObject(asset))
            {
                so.FindProperty("text").stringValue = text;
                so.FindProperty("speaker").stringValue = speaker;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private void SetStage(PlayableDirector directorValue, Camera cameraValue)
        {
            using (var so = new SerializedObject(stage))
            {
                so.FindProperty("director").objectReferenceValue = directorValue;
                so.FindProperty("stageCamera").objectReferenceValue = cameraValue;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private void AddSubtitle(string text)
        {
            TimelineClip clip = subtitleTrack.CreateClip<SubtitleClip>();
            var asset = Track((Object)clip.asset);
            using (var so = new SerializedObject(asset))
            {
                so.FindProperty("text").stringValue = text;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private void AddHold(double time)
        {
            if (timeline.markerTrack == null) timeline.CreateMarkerTrack();
            Track(timeline.markerTrack);
            Track(timeline.markerTrack.CreateMarker<HoldMarker>(time));
        }

        private T Track<T>(T obj) where T : Object
        {
            if (obj != null && !created.Contains(obj)) created.Add(obj);
            return obj;
        }

        private static List<string> Codes(List<PerformanceIssue> issues) => issues.Select(i => i.Code).ToList();

    }
}
