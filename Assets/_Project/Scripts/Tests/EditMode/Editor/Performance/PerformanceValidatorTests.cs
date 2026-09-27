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
        private SpritePerformanceActor actor;
        private TimelineAsset timeline;
        private ExpressionTrack expressionTrack;
        private SubtitleTrack subtitleTrack;
        private int layer;

        [SetUp]
        public void SetUp()
        {
            layer = LayerMask.NameToLayer(PerformanceValidator.PerformanceLayerName);
            int useLayer = layer >= 0 ? layer : 0;

            root = Track(new GameObject("perf_validator_test"));
            director = root.AddComponent<PlayableDirector>();
            stage = root.AddComponent<PerformanceStage>();

            var cameraGo = new GameObject("StageCamera");
            cameraGo.transform.SetParent(root.transform, false);
            stageCamera = cameraGo.AddComponent<Camera>();
            stageCamera.cullingMask = 1 << useLayer;
            UniversalAdditionalCameraData data = cameraGo.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Overlay;

            var actorGo = new GameObject("Actor");
            actorGo.transform.SetParent(root.transform, false);
            var spriteRenderer = actorGo.AddComponent<SpriteRenderer>();
            actor = actorGo.AddComponent<SpritePerformanceActor>();
            using (var so = new SerializedObject(actor))
            {
                so.FindProperty("target").objectReferenceValue = spriteRenderer;
                SerializedProperty list = so.FindProperty("expressions");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "smile";
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = useLayer;

            timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 8d;
            subtitleTrack = Track(timeline.CreateTrack<SubtitleTrack>(null, "字幕"));
            expressionTrack = Track(timeline.CreateTrack<ExpressionTrack>(null, "表情"));
            director.playableAsset = timeline;
            director.SetGenericBinding(expressionTrack, actor);

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
        public void Validate_WellFormedStage_HasNoIssuesBesidesLayerMissing()
        {
            List<PerformanceIssue> issues = PerformanceValidator.Validate(root);

            Assert.That(issues.Where(i => i.Code != "layer_missing").Select(i => i.ToString()), Is.Empty);
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
        public void Validate_BaseCamera_WarnsNotOverlay()
        {
            stageCamera.GetComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Base;

            PerformanceIssue issue = PerformanceValidator.Validate(root).Single(i => i.Code == "camera_not_overlay");

            Assert.That(issue.Severity, Is.EqualTo(PerformanceIssueSeverity.Warning));
        }

        [Test]
        public void Validate_OverlayCamera_NoNotOverlayWarning()
        {
            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Not.Contain("camera_not_overlay"));
        }

        [Test]
        public void Validate_CameraCullsOtherLayers_WarnsCullingExtra()
        {
            if (layer < 0) Assert.Ignore("工程里没有 Performance 图层。");
            stageCamera.cullingMask = ~0;

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("camera_culling_extra"));
        }

        [Test]
        public void Validate_CameraCullsOnlyPerformance_NoCullingWarning()
        {
            if (layer < 0) Assert.Ignore("工程里没有 Performance 图层。");

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Not.Contain("camera_culling_extra"));
        }

        [Test]
        public void Validate_ChildOffLayer_WarnsLayerMismatchOrLayerMissing()
        {
            actor.gameObject.layer = layer == 0 ? 1 : 0;

            List<string> codes = Codes(PerformanceValidator.Validate(root));

            Assert.That(codes, Does.Contain(layer < 0 ? "layer_missing" : "layer_mismatch"));
        }

        [Test]
        public void Validate_AllOnLayer_NoLayerMismatch()
        {
            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Not.Contain("layer_mismatch"));
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

        [Test]
        public void Validate_UnboundExpressionTrack_ReportsUnbound()
        {
            director.ClearGenericBinding(expressionTrack);

            PerformanceIssue issue = PerformanceValidator.Validate(root).Single(i => i.Code == "expression_unbound");

            Assert.That(issue.Severity, Is.EqualTo(PerformanceIssueSeverity.Error));
        }

        [Test]
        public void Validate_UnknownExpressionName_ReportsUnknown()
        {
            AddExpression("angry");

            Assert.That(Codes(PerformanceValidator.Validate(root)), Does.Contain("expression_unknown"));
        }

        [Test]
        public void Validate_KnownExpressionName_NoExpressionIssue()
        {
            AddExpression("smile");

            List<string> codes = Codes(PerformanceValidator.Validate(root));

            Assert.That(codes, Does.Not.Contain("expression_unknown"));
            Assert.That(codes, Does.Not.Contain("expression_unbound"));
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
        public void Validate_WorldStageWithPerspectiveBaseCamera_NoCameraOrLayerIssues()
        {
            MakeWorldStage();
            stageCamera.cullingMask = ~0;
            actor.gameObject.layer = 0;

            List<string> codes = Codes(PerformanceValidator.Validate(root));

            Assert.That(codes, Does.Not.Contain("camera_not_overlay"));
            Assert.That(codes, Does.Not.Contain("camera_culling_extra"));
            Assert.That(codes, Does.Not.Contain("camera_world_not_base"));
            Assert.That(codes, Does.Not.Contain("camera_world_orthographic"));
            Assert.That(codes, Does.Not.Contain("layer_mismatch"), "世界模式的演员站在世界图层上，不要求 Performance 层");
            Assert.That(codes, Does.Not.Contain("layer_missing"));
        }

        [Test]
        public void Validate_WorldStageWithOverlayOrthographicCamera_Warns()
        {
            MakeWorldStage();
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

        private void MakeWorldStage()
        {
            stageCamera.GetComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Base;
            stageCamera.orthographic = false;
            using (var so = new SerializedObject(stage))
            {
                so.FindProperty("mode").enumValueIndex = (int)PerformanceStageMode.World;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
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

        private void AddExpression(string expressionName)
        {
            TimelineClip clip = expressionTrack.CreateClip<ExpressionClip>();
            var asset = Track((Object)clip.asset);
            using (var so = new SerializedObject(asset))
            {
                so.FindProperty("expressionName").stringValue = expressionName;
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
