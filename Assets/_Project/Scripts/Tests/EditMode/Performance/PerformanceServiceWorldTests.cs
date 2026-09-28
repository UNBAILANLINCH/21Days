// 职责：钉住 PerformanceService 的世界舞台与摆放——舞台相机改 Base / 深度 +1 / 遮罩含主相机遮罩、主相机遮罩演出中为 0、
//   结束（跳过 / 取消）后主相机与舞台相机原样恢复；按摆放值摆实例、不传摆放保持原位；主相机缺失走退路并告警；
//   HideHud 时 Hud / Popup 层演出中隐藏、结束按进来前的显隐恢复（对白里插播时 Hud 不被重新亮出来）；
//   「自动」开着时停顿处过秒数自动继续；台词记录（LOG）开着时导演暂停、关上恢复，演出结束时还开着的 LOG 先于演出面板关掉。
//   后三条用真实的 PerformanceView / TranscriptView 预制体（按钮点击 → 面板事件 → 服务），假 UI 照 UIService 调面板生命周期。
// 为什么新建（复用 → 扩展 → 新建）：现有 Performance 测试都是纯逻辑类（规则 / 策略 / 存档 / 触发判定），没有服务级用例可扩展；
//   DialogueServiceTests 的假服务是对白专用的私有嵌套类，拿不过来。服务要走真实的相机与舞台组件，只能新建。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Performance;
using Game.Performance.Timeline;
using MessagePipe;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Performance
{
    /// <summary>
    /// <see cref="PerformanceService"/> 世界舞台 / 摆放的 EditMode 测试。
    /// <para>
    /// 主相机经构造参数注入（不走 <c>Camera.main</c>），编辑器里打开的场景有没有主相机都不影响用例。
    /// 假资源服务直接交出预先搭好的舞台物体、归还时只记录不销毁（TearDown 统一销毁），用来核对收尾后舞台相机也被改回。
    /// 播放循环每帧 <c>await UniTask.Yield</c>，所以用 <c>[UnityTest]</c> 等编辑器帧推进，用 <see cref="PerformanceService.Skip"/> 或取消收尾。
    /// </para>
    /// </summary>
    public sealed class PerformanceServiceWorldTests
    {
        private const string Id = "perf_world_test";
        private const int MaxFrames = 600;
        private const int MainMask = (1 << 0) | (1 << 2) | (1 << 4);
        private const int AuthorStageMask = 1 << 3;
        private const string PerformanceViewPath = "Assets/_Project/Prefabs/UI/PerformanceView.prefab";
        private const string TranscriptViewPath = "Assets/_Project/Prefabs/UI/TranscriptView.prefab";

        private readonly List<Object> created = new List<Object>();
        private RecordingTelemetry telemetry;
        private PerformanceRules rules;
        private TranscriptView transcriptView;
        private PerformanceConfig config;
        private FakeAssets assets;
        private PerformanceService service;
        private Camera main;
        private UniversalAdditionalCameraData mainData;
        private PerformanceStage stage;
        private Camera stageCamera;
        private UniversalAdditionalCameraData stageData;
        private Camera providedMain;
        private FakeUI ui;

        [SetUp]
        public void SetUp()
        {
            telemetry = new RecordingTelemetry();
            config = Track(ScriptableObject.CreateInstance<PerformanceConfig>());

            var mainGo = Track(new GameObject("perf_test_main_camera"));
            main = mainGo.AddComponent<Camera>();
            main.depth = -1f;
            main.cullingMask = MainMask;
            var distances = new float[32];
            distances[0] = 40f;
            main.layerCullDistances = distances;
            main.layerCullSpherical = true;
            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = Color.red;
            mainData = mainGo.GetComponent<UniversalAdditionalCameraData>();
            if (mainData == null) mainData = mainGo.AddComponent<UniversalAdditionalCameraData>();
            mainData.renderType = CameraRenderType.Base;
            mainData.renderPostProcessing = true;
            mainData.volumeLayerMask = 1 << 6;
            providedMain = main;

            BuildStage();
            assets = new FakeAssets(stage.gameObject);
            var view = BuildView();
            ui = new FakeUI(view);
            rules = new PerformanceRules(telemetry);
            service = new PerformanceService(config, rules, assets, ui,
                new FakeInput(), new FakeWorldPause(), new FakeSave(),
                new FakePublisher<PerformanceStartedEvent>(), new FakePublisher<PerformanceEndedEvent>(),
                telemetry, () => providedMain);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
            // 服务第一次播放时懒建的挂载根（EditMode 下不进 DontDestroyOnLoad，留在当前场景里），一并清掉。
            GameObject serviceRoot = GameObject.Find("PerformanceRoot");
            if (serviceRoot != null) Object.DestroyImmediate(serviceRoot);
        }

        [UnityTest]
        public IEnumerator PlayAsync_TakesOverWithBaseCameraAndRestoresAfterSkip()
        {
            UniTask<PerformanceResult> play = service.PlayAsync(Id);

            AssertRunning(play);
            Assert.That(stageData.renderType, Is.EqualTo(CameraRenderType.Base), "舞台相机应为 Base");
            Assert.That(stageCamera.depth, Is.EqualTo(main.depth + 1f), "深度 = 主相机 + 1");
            Assert.That(stageCamera.cullingMask & MainMask, Is.EqualTo(MainMask), "舞台相机遮罩应包含主相机遮罩");
            int performanceLayer = LayerMask.NameToLayer("Performance");
            if (performanceLayer >= 0)
                Assert.That(stageCamera.cullingMask & (1 << performanceLayer), Is.Not.EqualTo(0), "舞台相机遮罩应包含 Performance 层");
            Assert.That(stageCamera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor), "清屏方式从主相机拷贝");
            Assert.That(stageCamera.backgroundColor, Is.EqualTo(Color.red), "背景色从主相机拷贝");
            CollectionAssert.AreEqual(main.layerCullDistances, stageCamera.layerCullDistances);
            Assert.That(stageCamera.layerCullSpherical, Is.True);
            Assert.That(stageData.renderPostProcessing, Is.True, "后处理开关从主相机拷贝");
            Assert.That((int)stageData.volumeLayerMask, Is.EqualTo(1 << 6), "Volume 遮罩从主相机拷贝");
            Assert.That(stageCamera.fieldOfView, Is.EqualTo(35f), "透视参数保留作者设的值");
            Assert.That(main.cullingMask, Is.EqualTo(0), "演出中主相机遮罩应清零");
            Assert.That(main.enabled, Is.True, "主相机必须保持 enabled，Camera.main 不能变空");
            Assert.That(telemetry.Events, Has.Member("world_stage_attached"));

            service.Skip();
            yield return WaitCompleted(play);
            Assert.That(Capture(play), Is.Null);

            Assert.That(main.cullingMask, Is.EqualTo(MainMask), "跳过后主相机遮罩应恢复");
            AssertStageCameraRestored();
            Assert.That(assets.Released, Is.EqualTo(1), "实例应已归还");
        }

        [UnityTest]
        public IEnumerator PlayAsync_RestoresMainCameraAfterCancel()
        {
            using var cts = new CancellationTokenSource();

            UniTask<PerformanceResult> play = service.PlayAsync(Id, cts.Token);
            AssertRunning(play);
            Assert.That(main.cullingMask, Is.EqualTo(0));

            cts.Cancel();
            yield return WaitCompleted(play);

            Assert.That(Capture(play), Is.InstanceOf<OperationCanceledException>());
            Assert.That(main.cullingMask, Is.EqualTo(MainMask), "取消后主相机遮罩应恢复");
            AssertStageCameraRestored();
        }

        [UnityTest]
        public IEnumerator PlayAsync_WithPlacement_MovesInstanceToPlacement()
        {
            var position = new Vector3(3f, 0.5f, -7f);
            Quaternion rotation = Quaternion.Euler(0f, 45f, 0f);

            UniTask<PerformanceResult> play = service.PlayAsync(Id, new PerformancePlacement(position, rotation));
            AssertRunning(play);

            Assert.That(Vector3.Distance(stage.transform.position, position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(stage.transform.rotation, rotation), Is.LessThan(0.01f));
            Assert.That(telemetry.Warnings, Is.Empty, "有主相机、有摆放时不该有任何告警");

            service.Skip();
            yield return WaitCompleted(play);
            Capture(play);
        }

        [UnityTest]
        public IEnumerator PlayAsync_WithoutPlacement_KeepsPrefabPose()
        {
            var original = new Vector3(100f, 0f, 0f);
            stage.transform.position = original;

            UniTask<PerformanceResult> play = service.PlayAsync(Id);
            AssertRunning(play);

            Assert.That(stage.transform.position, Is.EqualTo(original), "不传摆放（None）时实例保持自身位姿");

            service.Skip();
            yield return WaitCompleted(play);
            Capture(play);
        }

        [UnityTest]
        public IEnumerator PlayAsync_WithoutMainCamera_FallsBackAndWarns()
        {
            providedMain = null;

            UniTask<PerformanceResult> play = service.PlayAsync(Id);

            Assert.That(telemetry.Warnings, Has.Member("world_camera_fallback"));
            Assert.That(stageData.renderType, Is.EqualTo(CameraRenderType.Base));
            Assert.That(stageCamera.cullingMask & AuthorStageMask, Is.EqualTo(AuthorStageMask), "退路保留作者设的遮罩");
            Assert.That(stageCamera.layerCullDistances[0], Is.EqualTo(80f));
            Assert.That(stageCamera.layerCullSpherical, Is.False);

            service.Skip();
            yield return WaitCompleted(play);
            Capture(play);
            AssertStageCameraRestored();
        }

        [UnityTest]
        public IEnumerator PlayAsync_HideHud_WhenHudWasVisible_RestoresVisibleAfterSkip()
        {
            SetHideHud(true);

            UniTask<PerformanceResult> play = service.PlayAsync(Id);
            AssertRunning(play);
            Assert.That(ui.IsLayerVisible(UILayer.Hud), Is.False, "演出中 Hud 层应隐藏");
            Assert.That(ui.IsLayerVisible(UILayer.Popup), Is.False, "演出中 Popup 层应隐藏");

            service.Skip();
            yield return WaitCompleted(play);
            Capture(play);
            Assert.That(ui.IsLayerVisible(UILayer.Hud), Is.True, "进来前可见 → 结束恢复可见");
            Assert.That(ui.IsLayerVisible(UILayer.Popup), Is.True, "进来前可见 → 结束恢复可见");
        }

        [UnityTest]
        public IEnumerator PlayAsync_HideHud_WhenHudWasHidden_KeepsHudHiddenAfterSkip()
        {
            SetHideHud(true);
            ui.SetLayerVisible(UILayer.Hud, false); // 对白里插播：Hud 层已被 DialogueService 藏掉

            UniTask<PerformanceResult> play = service.PlayAsync(Id);
            AssertRunning(play);

            service.Skip();
            yield return WaitCompleted(play);
            Capture(play);
            Assert.That(ui.IsLayerVisible(UILayer.Hud), Is.False, "进来前隐藏 → 演出结束仍隐藏");
            Assert.That(ui.IsLayerVisible(UILayer.Popup), Is.True, "对白框所在的 Popup 层应恢复可见");
        }

        [UnityTest]
        public IEnumerator PlayAsync_HideHud_ReadsLayerStateBeforeOpeningPanel()
        {
            SetHideHud(true);

            UniTask<PerformanceResult> play = service.PlayAsync(Id);
            AssertRunning(play);
            int open = ui.Calls.IndexOf("Open:" + nameof(PerformanceView));
            int readHud = ui.Calls.IndexOf("IsLayerVisible:" + UILayer.Hud);
            int readPopup = ui.Calls.IndexOf("IsLayerVisible:" + UILayer.Popup);
            Assert.That(open, Is.GreaterThanOrEqualTo(0), "演出应打开 PerformanceView");
            Assert.That(readHud, Is.InRange(0, open - 1), "Hud 层进来前的状态要在开演出面板之前记");
            Assert.That(readPopup, Is.InRange(0, open - 1), "Popup 层进来前的状态要在开演出面板之前记");

            service.Skip();
            yield return WaitCompleted(play);
            Capture(play);
        }

        [UnityTest]
        public IEnumerator PlayAsync_AutoOn_ContinuesAtHoldAfterSecondsWithoutConfirm()
        {
            // 间隔取大一些（3 秒）：EditMode 下每次循环的 dt 偏大，间隔太小会在测试看到 Holding 之前就已继续。
            const float autoSeconds = 3f;
            PerformanceView view = UseRealViews(autoSeconds);
            UniTask<PerformanceResult> play = service.PlayAsync(Id);
            AssertRunning(play);

            Field<Button>(view, "autoButton").onClick.Invoke();
            yield return WaitRealtime(() => rules.AutoPlay, 5f, "点「自动」后规则应开启自动");
            Assert.That(Field<TMP_Text>(view, "autoLabel").text, Is.EqualTo("自动中"), "自动开着时标签应为「自动中」");

            // EditMode 下时间轴不会自己走到停顿标记：直接给舞台发 HoldMarker 通知（舞台暂停导演并通知服务），同时间轴走到标记。
            stage.OnNotify(Playable.Null, Track(ScriptableObject.CreateInstance<HoldMarker>()), null);
            yield return WaitRealtime(() => rules.Phase == PerformancePhase.Holding, 5f, "收到停顿标记后应进入 Holding");
            Assert.That(telemetry.Events, Has.No.Member("hold_confirmed"), "刚进停顿不应立即继续");
            Assert.That(stage.Director.state, Is.EqualTo(PlayState.Paused), "停顿中导演应暂停");

            // 「等满秒数」的精确语义由 PerformanceRulesTests 钉住；这里不量真实耗时——EditMode 下 Time.unscaledDeltaTime
            // 不是每次循环的真实间隔（实测 14 ms 墙钟内循环累计了 1 秒以上的 dt），只验接线：不调 Confirm 也会自己继续。
            yield return WaitRealtime(() => telemetry.Events.Contains("hold_confirmed"), 10f, "自动开着时停顿处过秒数应自动继续（不调 Confirm）");
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Playing));
            Assert.That(rules.AutoPlay, Is.True, "自动继续不关自动");
            Assert.That(stage.Director.state, Is.EqualTo(PlayState.Playing), "自动继续后导演应恢复播放");

            service.Skip();
            yield return WaitCompletedRealtime(play);
            Assert.That(Capture(play), Is.Null);
        }

        [UnityTest]
        public IEnumerator PlayAsync_LogOpen_PausesDirectorAndResumesOnClose()
        {
            PerformanceView view = UseRealViews(PerformancePolicy.DefaultAutoAdvanceSeconds);
            UniTask<PerformanceResult> play = service.PlayAsync(Id);
            AssertRunning(play);
            Assert.That(stage.Director.state, Is.EqualTo(PlayState.Playing), "开演后导演应在播放");
            view.ShowSubtitle("阿米娅", "博士，前面就是村口了。", null, PerformanceAvatarSide.Left);

            Field<Button>(view, "historyButton").onClick.Invoke();
            yield return WaitRealtime(() => ui.Calls.Contains("Open:" + nameof(TranscriptView)), 5f, "点「LOG」应打开台词记录");
            Assert.That(stage.Director.state, Is.EqualTo(PlayState.Paused), "LOG 开着时导演应暂停");
            Assert.That(rules.Phase, Is.EqualTo(PerformancePhase.Playing), "LOG 只停导演，不改规则阶段");
            Assert.That(Field<TMP_Text>(transcriptView, "content").text, Does.Contain("阿米娅：博士，前面就是村口了。"),
                "台词记录应含已显示过的字幕，格式同对白历史");
            yield return null;
            yield return null;
            Assert.That(stage.Director.state, Is.EqualTo(PlayState.Paused), "LOG 开着的后续帧导演仍暂停");

            Field<Button>(transcriptView, "close").onClick.Invoke();
            yield return WaitRealtime(() => ui.Calls.Contains("Close:" + nameof(TranscriptView)), 5f, "点「关闭」应关掉台词记录");
            Assert.That(stage.Director.state, Is.EqualTo(PlayState.Playing), "关掉 LOG 后导演应恢复播放");

            service.Skip();
            yield return WaitCompletedRealtime(play);
            Assert.That(Capture(play), Is.Null);
        }

        [UnityTest]
        public IEnumerator PlayAsync_SkipWhileLogOpen_ClosesLogBeforePanel()
        {
            PerformanceView view = UseRealViews(PerformancePolicy.DefaultAutoAdvanceSeconds);
            UniTask<PerformanceResult> play = service.PlayAsync(Id);
            AssertRunning(play);
            Field<Button>(view, "historyButton").onClick.Invoke();
            yield return WaitRealtime(() => ui.Calls.Contains("Open:" + nameof(TranscriptView)), 5f, "点「LOG」应打开台词记录");

            service.Skip();
            yield return WaitCompletedRealtime(play);
            Assert.That(Capture(play), Is.Null);

            int closeLog = ui.Calls.IndexOf("Close:" + nameof(TranscriptView));
            int closePanel = ui.Calls.IndexOf("Close:" + nameof(PerformanceView));
            Assert.That(closeLog, Is.GreaterThanOrEqualTo(0), "演出结束时还开着的台词记录要一并关掉");
            Assert.That(closePanel, Is.GreaterThan(closeLog), "先关 Top 层的台词记录，再关演出面板");
        }

        // 假服务同步完成，调用返回时应停在播放循环里；提前结束就把异常带进失败信息。
        private void AssertRunning(UniTask<PerformanceResult> play)
        {
            if (play.Status.IsCompleted()) Assert.Fail("演出提前结束：" + Capture(play));
            Assert.That(service.IsRunning, Is.True);
        }

        private void AssertStageCameraRestored()
        {
            Assert.That(stageCamera.cullingMask, Is.EqualTo(AuthorStageMask), "舞台相机遮罩应改回作者的值");
            Assert.That(stageCamera.depth, Is.EqualTo(5f), "舞台相机深度应改回");
            Assert.That(stageCamera.layerCullDistances[0], Is.EqualTo(80f));
            Assert.That(stageCamera.layerCullSpherical, Is.False);
            Assert.That(main.layerCullDistances[0], Is.EqualTo(40f));
            Assert.That(main.layerCullSpherical, Is.True);
            Assert.That(stageCamera.clearFlags, Is.EqualTo(CameraClearFlags.Depth), "舞台相机清屏方式应改回");
            Assert.That(stageData.renderPostProcessing, Is.False, "舞台相机后处理开关应改回");
        }

        private void BuildStage()
        {
            var root = Track(new GameObject(Id));
            var director = root.AddComponent<PlayableDirector>();
            stage = root.AddComponent<PerformanceStage>();
            var cameraGo = new GameObject("StageCamera");
            cameraGo.transform.SetParent(root.transform, false);
            stageCamera = cameraGo.AddComponent<Camera>();
            stageCamera.orthographic = false;
            stageCamera.fieldOfView = 35f;
            stageCamera.depth = 5f;
            stageCamera.cullingMask = AuthorStageMask;
            var distances = new float[32];
            distances[0] = 80f;
            stageCamera.layerCullDistances = distances;
            stageCamera.layerCullSpherical = false;
            stageCamera.clearFlags = CameraClearFlags.Depth;
            stageData = cameraGo.GetComponent<UniversalAdditionalCameraData>();
            if (stageData == null) stageData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            stageData.renderType = CameraRenderType.Base;
            stageData.renderPostProcessing = false;

            var timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 30d;
            director.playableAsset = timeline;

            using (var so = new SerializedObject(stage))
            {
                so.FindProperty("director").objectReferenceValue = director;
                so.FindProperty("stageCamera").objectReferenceValue = stageCamera;
                so.FindProperty("pauseWorld").boolValue = false;
                so.FindProperty("hideHud").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // 最小可用的演出面板：服务在播放循环里只调 SetSkipProgress / SetHoldPromptVisible，接上这两个字段即可（不走 OnOpenAsync）。
        private PerformanceView BuildView()
        {
            var go = Track(new GameObject("perf_test_view", typeof(RectTransform)));
            var view = go.AddComponent<PerformanceView>();
            var fillGo = new GameObject("SkipFill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, false);
            var fill = fillGo.AddComponent<Image>();
            fill.type = Image.Type.Filled;
            var holdGo = new GameObject("HoldPrompt", typeof(RectTransform));
            holdGo.transform.SetParent(go.transform, false);
            // 测试程序集不引用 TMP（本波不改 asmdef），按类型名加组件，经 SerializedObject 接线。
            Component hold = holdGo.AddComponent(Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true));
            using (var so = new SerializedObject(view))
            {
                so.FindProperty("skipFill").objectReferenceValue = fill;
                so.FindProperty("holdPrompt").objectReferenceValue = hold;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return view;
        }

        /// <summary>
        /// 换上真实的 PerformanceView / TranscriptView 预制体（挂在临时画布下），假 UI 打开 / 关闭时调面板的 OnOpenAsync / OnCloseAsync，
        /// 并重建服务。黑场时长置 0（不起 LitMotion 动画），「自动」间隔按参数写进配置。
        /// </summary>
        private PerformanceView UseRealViews(float autoSeconds)
        {
            using (var so = new SerializedObject(config))
            {
                so.FindProperty("fadeSeconds").floatValue = 0f;
                so.FindProperty("autoAdvanceSeconds").floatValue = autoSeconds;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var canvas = Track(new GameObject("perf_test_canvas", typeof(RectTransform), typeof(Canvas)));
            var viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PerformanceViewPath);
            var transcriptPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TranscriptViewPath);
            Assert.That(viewPrefab, Is.Not.Null, "找不到演出面板预制体：" + PerformanceViewPath);
            Assert.That(transcriptPrefab, Is.Not.Null, "找不到记录面板预制体：" + TranscriptViewPath);
            var view = Track(Object.Instantiate(viewPrefab, canvas.transform, false)).GetComponent<PerformanceView>();
            transcriptView = Track(Object.Instantiate(transcriptPrefab, canvas.transform, false)).GetComponent<TranscriptView>();
            ui = new FakeUI(view) { RunLifecycle = true };
            ui.Register(transcriptView);
            rules = new PerformanceRules(telemetry);
            service = new PerformanceService(config, rules, assets, ui,
                new FakeInput(), new FakeWorldPause(), new FakeSave(),
                new FakePublisher<PerformanceStartedEvent>(), new FakePublisher<PerformanceEndedEvent>(),
                telemetry, () => providedMain);
            return view;
        }

        /// <summary>按真实时间等条件成立（EditMode 帧率不定，不按帧数算）；超时断言失败。</summary>
        private static IEnumerator WaitRealtime(Func<bool> condition, float timeoutSeconds, string failMessage)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, failMessage);
        }

        /// <summary>按序列化字段名取面板上接好的引用（顺带验接线）。</summary>
        private static T Field<T>(Object owner, string field) where T : Object
        {
            using (var so = new SerializedObject(owner))
            {
                SerializedProperty property = so.FindProperty(field);
                Assert.That(property, Is.Not.Null, $"{owner.GetType().Name} 没有字段 {field}");
                var value = property.objectReferenceValue as T;
                Assert.That(value, Is.Not.Null, $"{owner.GetType().Name}.{field} 未接线");
                return value;
            }
        }

        private void SetHideHud(bool hide)
        {
            using (var so = new SerializedObject(stage))
            {
                so.FindProperty("hideHud").boolValue = hide;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static IEnumerator WaitCompleted(UniTask<PerformanceResult> task)
        {
            for (int i = 0; i < MaxFrames && !task.Status.IsCompleted(); i++) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, $"{MaxFrames} 帧内演出没有收尾");
        }

        /// <summary>
        /// 按真实时间等演出收尾。EditMode 下 UniTask 的循环在 <c>EditorApplication.isUpdating</c>（别的会话导入资产）时停摆，
        /// 按帧数等会在停摆期间把帧数耗光误报超时（全量跑时实测过）；新用例按墙钟等，给足 10 秒。
        /// </summary>
        private static IEnumerator WaitCompletedRealtime(UniTask<PerformanceResult> task, float timeoutSeconds = 10f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, $"{timeoutSeconds} 秒内演出没有收尾");
        }

        // 观察已完成的任务：返回它抛出的异常（成功返回 null），不留未观察异常（见 pitfalls）。
        private static Exception Capture(UniTask<PerformanceResult> task)
        {
            try
            {
                task.GetAwaiter().GetResult();
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        /// <summary>交出预先搭好的舞台；归还只计数不销毁（TearDown 统一销毁，以便核对收尾后的相机状态）。</summary>
        private sealed class FakeAssets : IAssetService
        {
            private readonly GameObject instance;

            public FakeAssets(GameObject instance)
            {
                this.instance = instance;
            }

            public int Released { get; private set; }

            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default) =>
                UniTask.FromResult(instance);

            public void ReleaseInstance(GameObject released) => Released++;

            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : Object =>
                throw new NotSupportedException("假资源服务不加载");

            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default)
                where T : Object => throw new NotSupportedException("假资源服务不加载");

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default) =>
                throw new NotSupportedException("假资源服务不加载场景");
        }

        /// <summary>
        /// 打开面板直接交出预先搭好的面板（按类型登记，可登记多个）；按层记 SetLayerVisible 设的值（初始全可见，语义同 UIService：
        /// 只反映整层开关）。<see cref="Calls"/> 按顺序记打开、关闭与读层，用来钉住「先记层状态、再开面板」与收尾顺序。
        /// <see cref="RunLifecycle"/> 为 true 时照 UIService 调面板的 OnOpenAsync / OnCloseAsync（真预制体面板用；最小面板不走生命周期）。
        /// </summary>
        private sealed class FakeUI : IUIService
        {
            private readonly Dictionary<Type, UIView> views = new Dictionary<Type, UIView>();

            public FakeUI(UIView view)
            {
                Register(view);
            }

            public List<string> Calls { get; } = new List<string>();

            public bool RunLifecycle { get; set; }

            public void Register(UIView view) => views[view.GetType()] = view;

            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                Calls.Add("Open:" + typeof(T).Name);
                if (!views.TryGetValue(typeof(T), out UIView view))
                    throw new InvalidOperationException("假 UI 没登记面板 " + typeof(T).Name);
                if (RunLifecycle) view.OnOpenAsync(arg, ct).GetAwaiter().GetResult();
                return UniTask.FromResult((T)view);
            }

            public UniTask CloseAsync(UIView closed, CancellationToken ct = default)
            {
                if (closed == null) return UniTask.CompletedTask;
                Calls.Add("Close:" + closed.GetType().Name);
                if (RunLifecycle) closed.OnCloseAsync(ct).GetAwaiter().GetResult();
                return UniTask.CompletedTask;
            }
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => null;
            private readonly HashSet<UILayer> hiddenLayers = new HashSet<UILayer>();

            public void SetLayerVisible(UILayer layer, bool visible)
            {
                if (visible) hiddenLayers.Remove(layer);
                else hiddenLayers.Add(layer);
            }

            public bool IsLayerVisible(UILayer layer)
            {
                Calls.Add("IsLayerVisible:" + layer);
                return !hiddenLayers.Contains(layer);
            }
        }

        /// <summary>未初始化的输入服务：Actions 为 null，服务跳过全部输入图操作。</summary>
        private sealed class FakeInput : IInputService
        {
            public GameInput Actions => null;
            public void EnableMap(string map) { }
            public void DisableMap(string map) { }
        }

        private sealed class FakeWorldPause : IWorldPauseService
        {
            public bool IsPaused => false;
            public IDisposable Acquire(object owner) => new Token();

            private sealed class Token : IDisposable
            {
                public void Dispose() { }
            }
        }

        /// <summary>只支持按类型取分区；其余存读操作本文件走不到。</summary>
        private sealed class FakeSave : ISaveService
        {
            private readonly Dictionary<Type, object> parts = new Dictionary<Type, object>();

            public T Get<T>() where T : class, ISaveData, new()
            {
                if (!parts.TryGetValue(typeof(T), out object part))
                {
                    part = new T();
                    parts[typeof(T)] = part;
                }
                return (T)part;
            }

            public UniTask<bool> SaveAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public UniTask<bool> LoadAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public UniTask<SaveSnapshot> ReadCandidateAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public SaveSnapshot Capture() => throw new NotSupportedException();
            public void Commit(SaveSnapshot snapshot) => throw new NotSupportedException();
            public void ResetAll() => parts.Clear();
            public UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new() =>
                throw new NotSupportedException();
            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default)
                where T : class, new() => throw new NotSupportedException();
            public UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class =>
                throw new NotSupportedException();
            public bool Exists(int slot) => false;
            public void Delete(int slot) { }
        }

        private sealed class FakePublisher<T> : IPublisher<T>
        {
            public void Publish(T message) { }
        }

        /// <summary>只记事件名：I 级进 Events，W 级进 Warnings。</summary>
        private sealed class RecordingTelemetry : ITelemetryScope
        {
            public List<string> Events { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
            public string Module => "performance";
            public bool Enabled => true;
            public void Track(string evt) => Events.Add(evt);
            public void Track(string evt, (string Key, PropValue Value) p0) => Events.Add(evt);
            public void Track(string evt, (string Key, PropValue Value) p0, (string Key, PropValue Value) p1) => Events.Add(evt);

            public void Track(string evt, (string Key, PropValue Value) p0, (string Key, PropValue Value) p1,
                (string Key, PropValue Value) p2) => Events.Add(evt);

            public void Track(string evt, (string Key, PropValue Value) p0, (string Key, PropValue Value) p1,
                (string Key, PropValue Value) p2, (string Key, PropValue Value) p3) => Events.Add(evt);

            public void TrackWarn(string evt, in TelemetryProps props = default) => Warnings.Add(evt);
            public void TrackLevel(TelemetryLevel level, string evt, in TelemetryProps props = default) => Events.Add(evt);
            public void TrackError(string evt, Exception error, in TelemetryProps props = default) => Events.Add(evt);
            public void TrackError(string evt, string message, in TelemetryProps props = default) => Events.Add(evt);
            public TelemetrySpan BeginSpan(string name) => default;
        }
    }
}
