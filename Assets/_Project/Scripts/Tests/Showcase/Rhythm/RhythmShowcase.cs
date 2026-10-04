// 职责：经真实 Input System 跑四轨音游并检查重试、结算、校准保存。既有回放没有音游；使用用户指定的独立试玩场景。
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Game.Core.Audio;
using Game.Core.Boot;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.Platform;
using Game.Core.Timing;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Rhythm;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Game.Tests.Showcase.Rhythm
{
    [Category("Showcase")]
    public sealed class RhythmShowcase : ShowcaseScenario
    {
        private RhythmState scenarioState;
        private Coroutine scenarioTick;
        private GameBootstrap scenarioHost;
        protected override string Module => "Rhythm";
        // 用户明确要求新场景；它自带根作用域，所以不能再叠加一份 Boot。
        protected override string ScenePath => "Assets/_Project/Scenes/RhythmDemo.unity";
        protected override bool LoadBootScene => false;

        [UnityTest]
        public IEnumerator FourLanes_RetryResultsAndCalibration_Work()
        {
            yield return WaitUntil("音游准备界面", () => ResolveService<IGameFlow>() != null && ResolveService<IGameFlow>().Current is RhythmState, 30f);
            var state = ResolveService<RhythmState>();
            var ui = ResolveService<IUIService>();
            var view = ui.Get<RhythmView>();
            if (state.IsSongMenu)
            {
                state.SelectSong("intro");
                yield return WaitUntil("从选曲页进入虫儿飞准备", () => !state.IsSongMenu, 8f);
            }
            Assert.That(ResolveService<IPlatformService>().SaveRoot, Is.EqualTo(PlatformServiceBase.SaveRootOverride));
            Assert.That(PlatformServiceBase.SaveRootOverride, Is.Not.Null.And.Not.Empty);
            Input.Prime();
            yield return Step("延迟补偿设为零并开始演奏", () =>
            {
                FindDeep<Slider>(view.transform, "Offset").value = 0;
                FindDeep<Button>(view.transform, "StartButton").onClick.Invoke();
            }, 0f);
            yield return Check("倒数与音乐时间轴已开始", () => state.IsPlaying, 5f);
            var playbackField = typeof(RhythmState).GetField("playback", BindingFlags.Instance | BindingFlags.NonPublic);
            var boundaryField = typeof(RhythmState).GetField("completedInputBoundary", BindingFlags.Instance | BindingFlags.NonPublic);
            System.Action<InputAction.CallbackContext> observe = context =>
            {
                var clock = (AudioPlayback)playbackField.GetValue(state);
                if (clock == null) return;
                Debug.Log($"[VERIFY][Rhythm] 时间证据 event={context.time:F6}, realtime={Time.realtimeSinceStartupAsDouble:F6}, mapped={clock.PositionAtInputTime(context.time):F6}, dsp={clock.Position:F6}, completedBatch={(double)boundaryField.GetValue(state):F6}");
            };
            for (int lane = 0; lane < 4; lane++) { state.LaneAction(lane).performed += observe; state.LaneAction(lane).canceled += observe; }
            for (int i = 0; i < 4; i++)
            {
                int note = i;
                yield return WaitUntil("第 " + (i + 1) + " 个音符到达判定线", () => state.SongSeconds >= state.Rules.NoteTime(note) - 0.02, 6f);
                yield return Input.Press(state.LaneAction(state.Rules.NoteLane(note)));
            }
            yield return Check("四条轨道各命中一次，连击为四", () => state.Rules.Combo == 4 && state.Rules.Perfect + state.Rules.Good == 4);
            for (int lane = 0; lane < 4; lane++) { state.LaneAction(lane).performed -= observe; state.LaneAction(lane).canceled -= observe; }
            yield return Snapshot("四轨命中反馈");
            yield return Step("点击重新开始", () => FindDeep<Button>(view.transform, "StartButton").onClick.Invoke(), 0f);
            yield return Check("分数清零并重新倒数", () => state.IsPlaying && state.SongSeconds < 0 && state.Rules.Score == 0, 5f);
            int noteCount = state.Rules.Count;
            int expectedHolds = 0;
            var edges = new List<ChartEdge>();
            for (int i = 0; i < noteCount; i++)
            {
                bool hold = state.Rules.NoteType(i) == RhythmNoteType.Hold;
                if (hold) expectedHolds++;
                edges.Add(new ChartEdge(i, state.Rules.NoteTime(i) - 0.02, true, edges.Count));
                edges.Add(new ChartEdge(i, hold ? state.Rules.NoteEnd(i) + 0.04 : state.Rules.NoteTime(i) + 0.05, false, edges.Count));
            }
            edges.Sort((left, right) => { int order = left.Seconds.CompareTo(right.Seconds); return order != 0 ? order : left.Sequence.CompareTo(right.Sequence); });
            yield return Check("当前调试谱显示 56 单击与 2 长按，共 58 枚", () => noteCount == 58 && expectedHolds == 2 && FindDeep<TMPro.TMP_Text>(view.transform, "Subtitle").text.Contains("56 个单击 · 2 个长按"));
            yield return Step("完整演奏当前调试谱，交错处理单击和长按头尾", null, 0f);
            int firstFrame = Time.frameCount;
            double firstTime = Time.realtimeSinceStartupAsDouble;
            foreach (var edge in edges)
            {
                double timeout = Time.realtimeSinceStartupAsDouble + 6;
                while (state.IsPlaying && state.SongSeconds < edge.Seconds && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
                if (!state.IsPlaying) break;
                var action = state.LaneAction(state.Rules.NoteLane(edge.Note));
                if (edge.Press) yield return Input.Hold(action);
                else yield return Input.Release(action);
            }
            Debug.Log($"[VERIFY][Rhythm] {noteCount} 音符场景平均帧率={(Time.frameCount - firstFrame) / (Time.realtimeSinceStartupAsDouble - firstTime):F1} FPS（含编辑器及回放开销）");
            yield return Check("58 枚音符全部完成，两枚长按成功，没有漏击", () => state.Rules.Perfect + state.Rules.Good == noteCount && state.Rules.CompletedHolds == expectedHolds && state.Rules.Miss == 0, 2f);
            yield return Snapshot("完整调试谱TapHold命中");
            yield return Step("再次重新开始，检查全部漏按", () => FindDeep<Button>(view.transform, "StartButton").onClick.Invoke(), 0f);
            yield return Check("新一轮清空所有成绩", () => state.IsPlaying && state.SongSeconds < 0 && state.Rules.Score == 0, 5f);
            yield return Step("不按键，等待完整片段结算", null, 0f);
            yield return Check("所有漏按结算为 Miss，歌曲播放停止", () => !state.IsPlaying && state.Rules.Miss == state.Rules.Count, 48f);
            yield return Snapshot("漏按结算");
            yield return Step("将延迟补偿调到 +100ms 并返回标题", () =>
            {
                FindDeep<Slider>(view.transform, "Offset").value = 100;
                FindDeep<Button>(view.transform, "BackButton").onClick.Invoke();
            }, 0f);
            yield return Check("标题恢复且音游面板关闭", () => ResolveService<IGameFlow>().Current is TitleState && ui.Get<RhythmView>() == null, 8f);
            RhythmCalibrationData saved = null;
            yield return ResolveService<ISaveService>().ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration")
                .ContinueWith(data => saved = data).ToCoroutine();
            yield return Check("补偿档案已保存 +100ms", () => saved != null && saved.OffsetMs == 100);
            yield return Snapshot("返回标题");
            yield return Step("标题开始按钮重新进入音游", () => FindDeep<Button>(ui.Get<TitleView>().transform, "StartButton").onClick.Invoke(), 0f);
            yield return Check("重新进入并读取已保存补偿", () => ResolveService<IGameFlow>().Current is RhythmState && ui.Get<RhythmView>() != null && state.Rules.OffsetMs == 100, 8f);
        }

        [UnityTest]
        public IEnumerator HoldAndCalibration_RealInputAndSessionLifecycle_Work()
        {
            yield return WaitUntil("音游准备界面", () => ResolveService<IGameFlow>() != null && ResolveService<IGameFlow>().Current is RhythmState, 30f);
            var flow = ResolveService<IGameFlow>();
            yield return flow.GoToAsync<TitleState>().ToCoroutine();
            Input.Prime();
            var config = Track(Object.Instantiate(ResolveService<RhythmConfig>()));
            // 仅修改运行时副本，不改项目 SO。短谱走同一 State/UI/音频/Input System，便于验证边界。
            JsonUtility.FromJsonOverwrite("{\"schemaVersion\":2,\"noteTimes\":[],\"noteLanes\":[],\"durationSeconds\":4,\"countdownSeconds\":0.2,\"approachSeconds\":0.15,\"notes\":[{\"id\":\"zero-hold\",\"lane\":0,\"timeMs\":0,\"type\":1,\"durationMs\":1000},{\"id\":\"later-hold\",\"lane\":1,\"timeMs\":2000,\"type\":1,\"durationMs\":500}],\"calibrationWarmupBeats\":0,\"calibrationSampleBeats\":4,\"calibrationMinimumSamples\":3}", config);
            var saves = ResolveService<ISaveService>();
            Assert.That(ResolveService<IPlatformService>().SaveRoot, Is.EqualTo(PlatformServiceBase.SaveRootOverride));
            Assert.That(PlatformServiceBase.SaveRootOverride, Is.Not.Null.And.Not.Empty);
            scenarioState = new RhythmState(config, ResolveService<IUIService>(), ResolveService<IAudioService>(), saves,
                flow, ResolveService<IInputService>(), ResolveService<IWorldPauseService>(), ResolveService<ITelemetryService>(), ResolveService<INotificationService>());
            yield return scenarioState.EnterAsync(System.Threading.CancellationToken.None).ToCoroutine();
            scenarioHost = Object.FindObjectOfType<GameBootstrap>();
            scenarioTick = scenarioHost.StartCoroutine(TickScenario());
            var view = ResolveService<IUIService>().Get<RhythmView>();
            foreach (int offset in new[] { -100, 100 })
            {
                yield return Step($"点击玩家练习入口，补偿 {offset}ms", () => { FindDeep<Slider>(view.transform, "Offset").value = offset; FindDeep<Button>(view.transform, "PracticeButton").onClick.Invoke(); }, 0f);
                yield return Check("负时间预滚已开始", () => scenarioState.IsPlaying && scenarioState.SongSeconds < 0, 5f);
                for (int i = 0; i < scenarioState.Rules.Count; i++)
                {
                    int lane = scenarioState.Rules.NoteLane(i);
                    double head = scenarioState.Rules.NoteTime(i) + offset / 1000d;
                    yield return WaitUntil("练习音符头部", () => scenarioState.SongSeconds >= head, 4f);
                    if (scenarioState.Rules.NoteType(i) == RhythmNoteType.Tap)
                    { yield return Input.Press(scenarioState.LaneAction(lane)); continue; }
                    yield return Input.Hold(scenarioState.LaneAction(lane));
                    if (offset == 100 && lane == 2) yield return Snapshot("玩家练习中的长条");
                    int expected = lane - 1;
                    yield return WaitUntil("Hold 尾部自动结算", () => scenarioState.Rules.CompletedHolds >= expected, 3f);
                    yield return Input.Release(scenarioState.LaneAction(lane));
                }
                yield return Check("单击两枚和长按两枚完成，尾部反馈清楚", () => scenarioState.Rules.Perfect + scenarioState.Rules.Good == 4 && scenarioState.Rules.CompletedHolds == 2 && scenarioState.Rules.Miss == 0 && FindDeep<TMPro.TMP_Text>(view.transform, "Feedback").text.Contains("长按完成"));
            }
            yield return Snapshot("Hold自动尾部");
            yield return Step("再次点击玩家练习入口，提前松开长按", () => FindDeep<Button>(view.transform, "PracticeButton").onClick.Invoke(), 0f);
            yield return Check("重开清零", () => scenarioState.IsPlaying && scenarioState.Rules.Score == 0 && scenarioState.SongSeconds < 0, 5f);
            yield return WaitUntil("第一枚 Hold 头部", () => scenarioState.SongSeconds >= 3.1, 4f);
            yield return Input.Hold(scenarioState.LaneAction(2));
            int oldMiss = scenarioState.Rules.Miss;
            yield return WaitUntil("早放时刻", () => scenarioState.SongSeconds >= 3.6, 2f);
            yield return Input.Release(scenarioState.LaneAction(2));
            yield return Check("早放只记一次 Miss 并显示原因", () => scenarioState.Rules.Miss == oldMiss + 1 && scenarioState.Rules.Score == 0 && FindDeep<TMPro.TMP_Text>(view.transform, "Feedback").text.Contains("提前松开"), 2f);
            yield return Step("点击玩家校准入口，不输入并保留补偿", () => FindDeep<Button>(view.transform, "CalibrationButton").onClick.Invoke(), 0f);
            yield return Check("无样本校准结束", () => !scenarioState.IsPlaying, 5f);
            RhythmCalibrationData retained = null;
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(data => retained = data).ToCoroutine();
            yield return Check("无样本没有覆盖原补偿", () => retained.OffsetMs == 100);
            yield return Step("点击玩家校准入口，稳定跟拍并保留视觉值", () => { FindDeep<Slider>(view.transform, "VisualOffset").value = 125; FindDeep<Button>(view.transform, "CalibrationButton").onClick.Invoke(); }, 0f);
            for (int i = 0; i < 4; i++)
            {
                double beat = (i + 1) * 0.5 + 0.08;
                yield return WaitUntil("参考拍输入", () => scenarioState.SongSeconds >= beat, 3f);
                yield return Input.Press(scenarioState.LaneAction(0));
            }
            yield return Check("稳定样本校准完成", () => !scenarioState.IsPlaying, 5f);
            Assert.That(scenarioState.CalibrationCandidate.HasCandidate, Is.True);
            FindDeep<Button>(view.transform, "CalibrationApply").onClick.Invoke();
            yield return Step("明确应用建议后等待档案写入", null, 0.15f);
            RhythmCalibrationData calibrated = null;
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(data => calibrated = data).ToCoroutine();
            yield return Check("校准替换输入补偿，视觉值保持独立", () => calibrated.OffsetMs > 30 && calibrated.OffsetMs < 140 && calibrated.VisualOffsetMs == 125);
            yield return Snapshot("校准质量与独立偏移");
        }

        [UnityTest]
        public IEnumerator ScheduledStop_InterruptionsAndDiagnosticReplay_Work()
        {
            yield return WaitUntil("音游准备界面", () => ResolveService<IGameFlow>() != null && ResolveService<IGameFlow>().Current is RhythmState, 30f);
            var flow = ResolveService<IGameFlow>();
            yield return flow.GoToAsync<TitleState>().ToCoroutine();
            var config = Track(Object.Instantiate(ResolveService<RhythmConfig>()));
            JsonUtility.FromJsonOverwrite("{\"schemaVersion\":2,\"noteTimes\":[],\"noteLanes\":[],\"durationSeconds\":0.5,\"countdownSeconds\":0.2,\"approachSeconds\":0.15,\"notes\":[{\"id\":\"short-tap\",\"lane\":0,\"timeMs\":100,\"type\":0,\"durationMs\":0}]}", config);
            scenarioState = new RhythmState(config, ResolveService<IUIService>(), ResolveService<IAudioService>(), ResolveService<ISaveService>(),
                flow, ResolveService<IInputService>(), ResolveService<IWorldPauseService>(), ResolveService<ITelemetryService>(), ResolveService<INotificationService>());
            yield return scenarioState.EnterAsync(System.Threading.CancellationToken.None).ToCoroutine();
            scenarioHost = Object.FindObjectOfType<GameBootstrap>();
            var view = ResolveService<IUIService>().Get<RhythmView>();
            bool originalPause = AudioListener.pause;
            var originalMode = InputSystem.settings.updateMode;
            try
            {
                yield return Step("启动短片段并阻塞主线程跨过排程终点", scenarioState.StartRound, 0f);
                yield return Check("音频句柄已建立", () => scenarioState.IsPlaying, 5f);
                yield return WaitUntil("排程终点之前", () => scenarioState.SongSeconds >= 0.35, 3f);
                var playback = (AudioPlayback)typeof(RhythmState).GetField("playback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scenarioState);
                var source = (AudioSource)typeof(AudioPlayback).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(playback);
                System.Threading.Thread.Sleep(350);
                Assert.That(source.isPlaying, Is.False, "主线程未 Tick，音频仍须按 DSP 排程结束");
                Assert.That(scenarioState.IsPlaying, Is.True, "句柄保留到输入批次结算");
                typeof(RhythmState).GetMethod("FocusLost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scenarioState, null);
                Assert.That(scenarioState.IsPlaying, Is.False);
                var recorded = scenarioState.LastDiagnostic;
                Assert.That(recorded, Is.Not.Null);
                Assert.That(recorded.Commands[recorded.Commands.Count - 1].EndReason, Is.EqualTo(RhythmDiagnosticData.Reason.FocusLost));
                using (var stream = new System.IO.MemoryStream())
                {
                    RhythmDiagnosticCodec.Write(stream, recorded);
                    stream.Position = 0;
                    Assert.That(RhythmDiagnosticReplay.Run(RhythmDiagnosticCodec.Read(stream)).Matches, Is.True);
                }
                Assert.That(FindDeep<Button>(view.transform, "DiagnosticExportButton").interactable, Is.True);
                yield return Step("连续重试二十次，每次使用新会话", null, 0f);
                int previousSession = recorded.SessionHeader.Session;
                for (int retry = 0; retry < 20; retry++)
                {
                    scenarioState.StartRound();
                    yield return Check("重试已建立", () => scenarioState.IsPlaying, 5f);
                    Assert.That(scenarioState.Rules.Score, Is.Zero);
                    AudioListener.pause = true;
                    scenarioState.Tick();
                    AudioListener.pause = false;
                    Assert.That(scenarioState.IsPlaying, Is.False);
                    Assert.That(scenarioState.LastDiagnostic.Commands[scenarioState.LastDiagnostic.Commands.Count - 1].EndReason, Is.EqualTo(RhythmDiagnosticData.Reason.AudioPaused));
                    Assert.That(scenarioState.LastDiagnostic.SessionHeader.Session, Is.GreaterThan(previousSession));
                    previousSession = scenarioState.LastDiagnostic.SessionHeader.Session;
                }
                foreach (var mode in new[] { InputSettings.UpdateMode.ProcessEventsInFixedUpdate, InputSettings.UpdateMode.ProcessEventsManually })
                {
                    InputSystem.settings.updateMode = mode;
                    scenarioState.StartRound();
                    Assert.That(scenarioState.IsPlaying, Is.False, "不支持的输入模式必须在排程之前拒绝");
                }
                InputSystem.settings.updateMode = originalMode;
                scenarioState.StartRound();
                yield return Check("应用暂停测试已开始", () => scenarioState.IsPlaying, 5f);
                var applicationPause = typeof(RhythmView).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic);
                applicationPause.Invoke(view, new object[] { true });
                Assert.That(scenarioState.IsPlaying, Is.False);
                Assert.That(scenarioState.LastDiagnostic.Commands[scenarioState.LastDiagnostic.Commands.Count - 1].EndReason, Is.EqualTo(RhythmDiagnosticData.Reason.ApplicationPaused));
                scenarioState.StartRound();
                Assert.That(scenarioState.IsPlaying, Is.False, "应用仍暂停时不能重开");
                applicationPause.Invoke(view, new object[] { false });
                scenarioState.StartRound();
                yield return Check("恢复后建立新会话", () => scenarioState.IsPlaying, 5f);
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
                scenarioState.Tick();
                Assert.That(scenarioState.IsPlaying, Is.False, "演奏中模式切换必须终止");
                Assert.That(scenarioState.LastDiagnostic.Commands[scenarioState.LastDiagnostic.Commands.Count - 1].EndReason, Is.EqualTo(RhythmDiagnosticData.Reason.InputModeChanged));
            }
            finally
            {
                AudioListener.pause = originalPause;
                InputSystem.settings.updateMode = originalMode;
                typeof(RhythmView).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { false });
            }
        }

        [UnityTest]
        public IEnumerator DiagnosticExportButton_WritesUniqueFilesAndReportsFailure()
        {
            yield return WaitUntil("音游准备界面", () => ResolveService<IGameFlow>() != null && ResolveService<IGameFlow>().Current is RhythmState, 30f);
            var flow = ResolveService<IGameFlow>();
            yield return flow.GoToAsync<TitleState>().ToCoroutine();
            var config = Track(Object.Instantiate(ResolveService<RhythmConfig>()));
            JsonUtility.FromJsonOverwrite("{\"schemaVersion\":2,\"noteTimes\":[],\"noteLanes\":[],\"durationSeconds\":0.5,\"countdownSeconds\":0.2,\"approachSeconds\":0.15,\"notes\":[{\"id\":\"export-tap\",\"lane\":0,\"timeMs\":100,\"type\":0,\"durationMs\":0}]}", config);
            scenarioState = new RhythmState(config, ResolveService<IUIService>(), ResolveService<IAudioService>(), ResolveService<ISaveService>(),
                flow, ResolveService<IInputService>(), ResolveService<IWorldPauseService>(), ResolveService<ITelemetryService>(), ResolveService<INotificationService>());
            yield return scenarioState.EnterAsync(System.Threading.CancellationToken.None).ToCoroutine();
            var ui = ResolveService<IUIService>();
            var view = ui.Get<RhythmView>();
            var export = FindDeep<Button>(view.transform, "DiagnosticExportButton");
            string isolatedRoot = PlatformServiceBase.SaveRootOverride;
            Assert.That(isolatedRoot, Is.Not.Null.And.Not.Empty);
            string folder = System.IO.Path.Combine(isolatedRoot, "rhythm-diagnostics");
            System.IO.Directory.CreateDirectory(folder);
            string sentinel = System.IO.Path.Combine(folder, "do-not-overwrite.rhd");
            byte[] sentinelBytes = { 21, 4, 99 };
            System.IO.File.WriteAllBytes(sentinel, sentinelBytes);
            bool originalPause = AudioListener.pause;
            try
            {
                Assert.That(export.interactable, Is.False);
                yield return Step("从真实面板开始，再失焦结束生成记录", () => FindDeep<Button>(view.transform, "StartButton").onClick.Invoke(), 0f);
                yield return Check("导出测试会话已建立", () => scenarioState.IsPlaying, 5f);
                scenarioState.Tick();
                typeof(RhythmState).GetMethod("FocusLost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scenarioState, null);
                Assert.That(export.interactable, Is.True);
                yield return Step("自动化触发面板导出按钮两次", () => { export.onClick.Invoke(); export.onClick.Invoke(); }, 0f);
                string[] firstFiles = System.IO.Directory.GetFiles(folder, "session-*.rhd");
                yield return Check("两次按钮点击写成不同文件", () => firstFiles.Length == 2);
                byte[][] originalBytes = { System.IO.File.ReadAllBytes(firstFiles[0]), System.IO.File.ReadAllBytes(firstFiles[1]) };
                foreach (string file in firstFiles) AssertExport(file, scenarioState.LastDiagnostic.SessionHeader.Session, RhythmDiagnosticData.Reason.FocusLost);
                yield return Check("实际通知面板显示保存成功", () => NotificationTitle(ui) == "本轮诊断已保存", 8f);
                yield return Snapshot("诊断按钮保存成功");
                yield return Step("重新开始时禁止导出，音频暂停后保存新会话", () => FindDeep<Button>(view.transform, "StartButton").onClick.Invoke(), 0f);
                yield return Check("新会话已建立", () => scenarioState.IsPlaying, 5f);
                Assert.That(export.interactable, Is.False);
                export.onClick.Invoke(); // 直接触发事件也须被 State 防重入守卫挡住。
                Assert.That(System.IO.Directory.GetFiles(folder, "session-*.rhd").Length, Is.EqualTo(2));
                AudioListener.pause = true;
                scenarioState.Tick();
                AudioListener.pause = originalPause;
                export.onClick.Invoke();
                string[] allFiles = System.IO.Directory.GetFiles(folder, "session-*.rhd");
                yield return Check("新会话新增文件，旧文件与哨兵未覆盖", () => allFiles.Length == 3 &&
                    System.Linq.Enumerable.SequenceEqual(System.IO.File.ReadAllBytes(firstFiles[0]), originalBytes[0]) &&
                    System.Linq.Enumerable.SequenceEqual(System.IO.File.ReadAllBytes(firstFiles[1]), originalBytes[1]) &&
                    System.Linq.Enumerable.SequenceEqual(System.IO.File.ReadAllBytes(sentinel), sentinelBytes));
                foreach (string file in allFiles)
                    if (file != firstFiles[0] && file != firstFiles[1]) AssertExport(file, scenarioState.LastDiagnostic.SessionHeader.Session, RhythmDiagnosticData.Reason.AudioPaused);
                var retained = scenarioState.LastDiagnostic;
                string blockedRoot = System.IO.Path.Combine(isolatedRoot, "blocked-export-root");
                System.IO.File.WriteAllBytes(blockedRoot, sentinelBytes);
                ExpectErrorLogs("导出目录故意被文件占用", message => message.Contains("rhythm/diagnostic_save_failed"));
                PlatformServiceBase.SaveRootOverride = blockedRoot;
                yield return Step("导出按钮遇到不可写目录", () => export.onClick.Invoke(), 0f);
                PlatformServiceBase.SaveRootOverride = isolatedRoot;
                yield return Check("失败通知显示且内存记录和已有文件仍保留", () => NotificationTitle(ui) == "诊断未保存" &&
                    ReferenceEquals(scenarioState.LastDiagnostic, retained) && export.interactable &&
                    System.IO.Directory.GetFiles(folder, "session-*.rhd").Length == 3, 12f);
                yield return Snapshot("诊断导出失败反馈");
                yield return Step("恢复目录后从同一按钮重试导出", () => export.onClick.Invoke(), 0f);
                yield return Check("恢复后第四份文件可回读", () => System.IO.Directory.GetFiles(folder, "session-*.rhd").Length == 4);
                foreach (string file in System.IO.Directory.GetFiles(folder, "session-*.rhd"))
                {
                    using (var stream = System.IO.File.OpenRead(file)) Assert.That(RhythmDiagnosticReplay.Run(RhythmDiagnosticCodec.Read(stream)).Matches, Is.True);
                }
            }
            finally
            {
                PlatformServiceBase.SaveRootOverride = isolatedRoot;
                AudioListener.pause = originalPause;
            }
        }

        [UnityTest]
        [Timeout(360000)]
        public IEnumerator SongCatalog_IntroUnlocksAdvancedChartsAndRestoresProgress()
        {
            yield return WaitUntil("音游选曲界面", () => ResolveService<IGameFlow>() != null && ResolveService<IGameFlow>().Current is RhythmState, 30f);
            var state = ResolveService<RhythmState>();
            var ui = ResolveService<IUIService>();
            var view = ui.Get<RhythmView>();
            Assert.That(ResolveService<IPlatformService>().SaveRoot, Is.EqualTo(PlatformServiceBase.SaveRootOverride));
            Assert.That(PlatformServiceBase.SaveRootOverride, Is.Not.Null.And.Not.Empty);
            Input.Prime();
            yield return Step("查看新档并尝试点击锁定曲目", () =>
            {
                FindDeep<Button>(view.transform, "Song_hybeboy-guitar").onClick.Invoke();
                FindDeep<Button>(view.transform, "Song_attention").onClick.Invoke();
            }, 0f);
            yield return Check("入门开放，两首进阶锁定且无法开始", () => state.IsSongMenu && state.IsUnlocked("intro") &&
                !state.IsUnlocked("hybeboy-guitar") && !state.IsUnlocked("attention") && !state.IsPlaying &&
                !FindDeep<Button>(view.transform, "Song_attention").interactable);
            yield return Snapshot("新档选曲与前置锁定");
            yield return Step("选择虫儿飞并完整漏按一轮", () => FindDeep<Button>(view.transform, "Song_intro").onClick.Invoke(), 0f);
            yield return WaitUntil("入门曲准备", () => !state.IsSongMenu, 8f);
            FindDeep<Slider>(view.transform, "Offset").value = 0;
            FindDeep<Slider>(view.transform, "VisualOffset").value = 0;
            FindDeep<Button>(view.transform, "StartButton").onClick.Invoke();
            yield return WaitUntil("未达标结算", () => !state.IsPlaying && state.Rules.Miss == 58, 48f);
            yield return Check("零分不通关，进阶仍锁定", () => !state.IsCleared("intro") && !state.IsUnlocked("attention"));
            yield return Snapshot("入门未达标");
            yield return Step("真实输入完成虫儿飞并查看解锁", () => FindDeep<Button>(view.transform, "StartButton").onClick.Invoke(), 0f);
            yield return WaitUntil("入门重试已开始", () => state.IsPlaying, 8f);
            yield return PerformCurrentChart(state);
            yield return WaitUntil("入门通关结算", () => !state.IsPlaying, 8f);
            FindDeep<Button>(view.transform, "SongMenuButton").onClick.Invoke();
            yield return Check("入门达标，双进阶均解锁且显示最佳", () => state.IsSongMenu && state.IsCleared("intro") &&
                state.IsUnlocked("hybeboy-guitar") && state.IsUnlocked("attention") && state.BestScore("intro") >= 34800);
            yield return Snapshot("入门达标解锁两曲");
            foreach (string songId in new[] { "hybeboy-guitar", "attention" })
            {
                yield return Step("选择并完整演奏进阶曲 " + songId, () => FindDeep<Button>(view.transform, "Song_" + songId).onClick.Invoke(), 0f);
                yield return WaitUntil("进阶曲准备且成绩清零", () => !state.IsSongMenu && state.SelectedSongId == songId && state.Rules.Score == 0, 10f);
                FindDeep<Button>(view.transform, "StartButton").onClick.Invoke();
                yield return WaitUntil("进阶曲已开始", () => state.IsPlaying, 8f);
                int count = state.Rules.Count;
                int holds = 0;
                for (int i = 0; i < count; i++) if (state.Rules.NoteType(i) == RhythmNoteType.Hold) holds++;
                yield return PerformCurrentChart(state);
                yield return WaitUntil("进阶完整结算", () => !state.IsPlaying, 8f);
                yield return Check("本曲全部完成且保存本曲成绩", () => state.Rules.Perfect + state.Rules.Good == count &&
                    state.Rules.Miss == 0 && state.Rules.CompletedHolds == holds && state.IsCleared(songId) && state.BestScore(songId) >= count * 600);
                yield return Snapshot(songId + "完整结算");
                yield return Step("重试后持键返回选曲并验证中断", () => FindDeep<Button>(view.transform, "StartButton").onClick.Invoke(), 0f);
                yield return WaitUntil("进阶重试清零", () => state.IsPlaying && state.SongSeconds < 0 && state.Rules.Score == 0, 8f);
                yield return Input.Hold(state.LaneAction(0));
                FindDeep<Button>(view.transform, "SongMenuButton").onClick.Invoke();
                yield return Input.Release(state.LaneAction(0));
                yield return Check("返回选曲立即停止计时，不撤销最佳和通关", () => state.IsSongMenu && !state.IsPlaying && state.BestScore(songId) >= count * 600 && state.IsCleared(songId));
            }
            yield return Step("返回标题后重新进入，重读隔离档案", () => FindDeep<Button>(view.transform, "SongSelectionBack").onClick.Invoke(), 0f);
            yield return WaitUntil("标题恢复", () => ResolveService<IGameFlow>().Current is TitleState && ui.Get<RhythmView>() == null, 12f);
            FindDeep<Button>(ui.Get<TitleView>().transform, "StartButton").onClick.Invoke();
            yield return WaitUntil("选曲与进度恢复", () => ResolveService<IGameFlow>().Current is RhythmState && ui.Get<RhythmView>() != null && state.IsSongMenu, 12f);
            yield return Check("重开后双曲已通关且最佳分保留", () => state.IsCleared("intro") && state.IsCleared("hybeboy-guitar") &&
                state.IsCleared("attention") && state.BestScore("hybeboy-guitar") > 0 && state.BestScore("attention") > 0);
            yield return Snapshot("本机档案重读与通关状态");
        }
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Attention_CompleteRetrySwitchAndRestore_WithPriorClearFixture()
        {
            yield return WaitUntil("音游选曲就绪", () => ResolveService<IGameFlow>()?.Current is RhythmState, 30f);
            var state = ResolveService<RhythmState>();
            var flow = ResolveService<IGameFlow>();
            var ui = ResolveService<IUIService>();
            var saves = ResolveService<ISaveService>();
            Assert.That(PlatformServiceBase.SaveRootOverride, Is.Not.Null.And.Not.Empty);
            Assert.That(ResolveService<IPlatformService>().SaveRoot, Is.EqualTo(PlatformServiceBase.SaveRootOverride));
            var catalog = (RhythmCatalogConfig)typeof(RhythmState).GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
            int guitarCount = catalog.Find("hybeboy-guitar").Chart.CreateRules(0, null).Count;
            var attentionSong = catalog.Find("attention");
            var attentionRules = attentionSong.Chart.CreateRules(0, null);
            int attentionHolds = 0;
            for (int i = 0; i < attentionRules.Count; i++)
                if (attentionRules.NoteType(i) == RhythmNoteType.Hold) attentionHolds++;
            yield return flow.GoToAsync<TitleState>().ToCoroutine();
            // 前轮已验证入门解锁及吉他全曲；此隔离 fixture 仅省去重复演奏，随后仍走真实存档读写与 Attention 输入。
            var prior = new RhythmProgressData();
            foreach (string id in new[] { "intro", "hybeboy-guitar" })
            {
                var song = catalog.Find(id);
                var rules = song.Chart.CreateRules(0, null);
                int count = rules.Count, holds = 0;
                for (int i = 0; i < count; i++) if (rules.NoteType(i) == RhythmNoteType.Hold) holds++;
                Assert.That(RhythmProgressRules.RecordRun(prior, song,
                    new RhythmRunResult("prior-" + song.Id, "", RhythmPlayMode.FreePlay, RhythmRunCompletion.Completed,
                        "completed", song.Id, song.Chart.ChartId, song.Revision, song.RulesetId, song.ScoringVersion,
                        count, count, 0, 0, holds, count * 1000, count)), Is.True);
            }
            yield return saves.WriteProfileAsync("rhythm-progress", prior).ToCoroutine();
            FindDeep<Button>(ui.Get<TitleView>().transform, "StartButton").onClick.Invoke();
            yield return WaitUntil("从真实档案恢复前置通关", () => state.IsSongMenu && state.IsUnlocked("attention") && state.BestScore("hybeboy-guitar") == guitarCount * 1000, 10f);
            var view = ui.Get<RhythmView>();
            foreach (string id in new[] { "intro", "hybeboy-guitar", "attention" })
            {
                var label = FindDeep<Button>(view.transform, "Song_" + id).GetComponentInChildren<TMPro.TMP_Text>();
                label.ForceMeshUpdate();
                Assert.That(label.isTextOverflowing, Is.False, "选曲文字必须完整落在本行");
                Assert.That(label.rectTransform.rect.width, Is.GreaterThan(0));
                Assert.That(label.rectTransform.rect.width, Is.LessThanOrEqualTo(label.transform.parent.GetComponent<RectTransform>().rect.width));
            }
            yield return Snapshot("选曲文字完整且恢复前置成绩");
            Input.Prime();
            foreach (string id in new[] { "hybeboy-guitar", "attention" })
            {
                FindDeep<Button>(view.transform, "Song_" + id).onClick.Invoke();
                yield return WaitUntil("跨曲准备与本轮成绩清零", () => !state.IsSongMenu && state.SelectedSongId == id && state.Rules.Score == 0, 10f);
                FindDeep<TMPro.TMP_Text>(view.transform, "Heading").ForceMeshUpdate();
                Assert.That(FindDeep<TMPro.TMP_Text>(view.transform, "Heading").isTextOverflowing, Is.False);
                if (id == "hybeboy-guitar") FindDeep<Button>(view.transform, "SongMenuButton").onClick.Invoke();
            }
            FindDeep<Slider>(view.transform, "Offset").value = 0;
            FindDeep<Slider>(view.transform, "VisualOffset").value = 0;
            FindDeep<Button>(view.transform, "StartButton").onClick.Invoke();
            yield return WaitUntil("Attention 演奏开始", () => state.IsPlaying, 8f);
            yield return PerformCurrentChart(state);
            yield return WaitUntil("Attention 完整结算", () => !state.IsPlaying, 8f);
            Assert.That(state.LastRunResult.Completion, Is.EqualTo(RhythmRunCompletion.Completed), state.LastRunResult.EndReason);
            foreach (var judgement in state.LastDiagnostic.Judgements)
                if (judgement.Grade == RhythmGrade.Miss)
                {
                    foreach (var command in state.LastDiagnostic.Commands)
                        if (command.Type == RhythmDiagnosticData.Kind.Input && command.Sequence == judgement.InputSequence)
                            Debug.Log($"[VERIFY][Rhythm] Miss note={judgement.NoteId}, cause={judgement.Cause}, head={judgement.TargetHeadSeconds}, tail={judgement.TargetTailSeconds}, input={judgement.SongSeconds:F6}, sequence={command.Sequence}, event={command.RawEventRealtime:F6}, captured={command.CapturedRealtime:F6}, dsp={command.Dsp:F6}");
                }
            yield return Check("当前谱面音符及长按全部成功，歌曲进度独立", () => state.Rules.Count == attentionRules.Count && state.Rules.Perfect + state.Rules.Good == attentionRules.Count &&
                state.Rules.Miss == 0 && state.Rules.CompletedHolds == attentionHolds && state.IsCleared("attention") &&
                state.BestScore("attention") >= attentionRules.Count * 1000 * attentionSong.PassScoreRatio && state.BestScore("hybeboy-guitar") == guitarCount * 1000);
            yield return Snapshot("Attention完整结算与长曲名布局");
            int best = state.BestScore("attention");
            FindDeep<Button>(view.transform, "StartButton").onClick.Invoke();
            yield return WaitUntil("重试清零并倒数", () => state.IsPlaying && state.SongSeconds < 0 && state.Rules.Score == 0, 8f);
            yield return Input.Hold(state.LaneAction(0));
            FindDeep<Button>(view.transform, "SongMenuButton").onClick.Invoke();
            yield return Input.Release(state.LaneAction(0));
            Assert.That(state.IsPlaying, Is.False);
            Assert.That(state.BestScore("attention"), Is.EqualTo(best));
            FindDeep<Button>(view.transform, "Song_hybeboy-guitar").onClick.Invoke();
            yield return WaitUntil("中断后换曲没有残留成绩", () => !state.IsSongMenu && state.SelectedSongId == "hybeboy-guitar" && state.Rules.Score == 0 && state.Rules.Count == guitarCount, 10f);
            FindDeep<Button>(view.transform, "BackButton").onClick.Invoke();
            yield return WaitUntil("返回标题并等待保存释放", () => flow.Current is TitleState && ui.Get<RhythmView>() == null, 12f);
            RhythmProgressData saved = null;
            yield return saves.ReadProfileAsync<RhythmProgressData>("rhythm-progress").ContinueWith(data => saved = data).ToCoroutine();
            var savedRecord = RhythmProgressRules.ReadCurrentRecord(saved, attentionSong);
            Assert.That(savedRecord, Is.Not.Null);
            Assert.That(savedRecord.BestScoreRun.Score, Is.EqualTo(best));
            Assert.That(savedRecord.BestScoreRun.Resolved, Is.EqualTo(attentionRules.Count));
            Assert.That(savedRecord.BestScoreRun.CompletedHolds, Is.EqualTo(attentionHolds));
            Assert.That(savedRecord.Cleared, Is.True);
            Assert.That(saved.ClearedCharts.Contains(catalog.Find("attention").ProgressKey), Is.True);
            FindDeep<Button>(ui.Get<TitleView>().transform, "StartButton").onClick.Invoke();
            yield return WaitUntil("重新进入读取三曲通关和最高分", () => state.IsSongMenu && state.IsCleared("intro") && state.IsCleared("hybeboy-guitar") && state.IsCleared("attention") && state.BestScore("attention") == best, 12f);
            yield return Snapshot("真实档案重读三曲通关");
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Calibration_ReasonConfirmPreviewSupplementAndRestore_Work()
        {
            yield return WaitUntil("校准流程就绪", () => ResolveService<IGameFlow>()?.Current is RhythmState, 30f);
            var flow = ResolveService<IGameFlow>(); var ui = ResolveService<IUIService>(); var saves = ResolveService<ISaveService>();
            Assert.That(PlatformServiceBase.SaveRootOverride, Is.Not.Null.And.Not.Empty);
            Assert.That(ResolveService<IPlatformService>().SaveRoot, Is.EqualTo(PlatformServiceBase.SaveRootOverride));
            yield return flow.GoToAsync<TitleState>().ToCoroutine();
            yield return saves.WriteProfileAsync("rhythm-calibration", new RhythmCalibrationData {OffsetMs=-40,VisualOffsetMs=125}).ToCoroutine();
            var config = Track(Object.Instantiate(ResolveService<RhythmConfig>()));
            JsonUtility.FromJsonOverwrite("{\"schemaVersion\":2,\"noteTimes\":[],\"noteLanes\":[],\"durationSeconds\":4,\"notes\":[{\"id\":\"calibration-flow-tap\",\"lane\":0,\"timeMs\":100,\"type\":0,\"durationMs\":0}]}",config);
            scenarioState = new RhythmState(config,ui,ResolveService<IAudioService>(),saves,flow,ResolveService<IInputService>(),
                ResolveService<IWorldPauseService>(),ResolveService<ITelemetryService>(),ResolveService<INotificationService>());
            yield return scenarioState.EnterAsync(System.Threading.CancellationToken.None).ToCoroutine();
            scenarioHost=Object.FindObjectOfType<GameBootstrap>(); scenarioTick=scenarioHost.StartCoroutine(TickScenario()); Input.Prime();
            var view=ui.Get<RhythmView>();
            yield return Step("固定8+32拍，不输入也按时结束",scenarioState.StartCalibration,0f);
            yield return WaitUntil("观察独立固定进度",()=>scenarioState.SongSeconds>=6,12f);
            Assert.That(FindDeep<TMPro.TMP_Text>(view.transform,"Status").text,Does.Contain("采样"));
            yield return WaitUntil("无输入主轮结束",()=>!scenarioState.IsPlaying,20f);
            Assert.That(scenarioState.CalibrationCandidate.Reason,Is.EqualTo(RhythmCalibrationReason.NoInput));
            Assert.That(FindDeep<Button>(view.transform,"CalibrationApply").interactable,Is.False);
            yield return Snapshot("固定轮次无输入原因与保留出口");
            Assert.That(FindDeep<Button>(view.transform,"CalibrationSupplement").interactable,Is.False);
            FindDeep<Button>(view.transform,"CalibrationSupplement").onClick.Invoke(); Assert.That(scenarioState.IsPlaying,Is.False);
            FindDeep<Button>(view.transform,"CalibrationKeep").onClick.Invoke();
            yield return AssertSavedCalibration(-40,125);
            yield return Step("32拍有波动但分段稳定，先给建议",scenarioState.StartCalibration,0f);
            yield return PerformCalibrationInputs(config,i=>80+new[]{-70d,-55,-35,-15,15,35,55,70}[i%8]);
            yield return WaitUntil("建议主轮结束",()=>!scenarioState.IsPlaying,6f);
            Assert.That(scenarioState.CalibrationCandidate.Reason,Is.EqualTo(RhythmCalibrationReason.Suggested));
            double candidate=scenarioState.CalibrationCandidate.OffsetMs;
            Assert.That(candidate,Is.InRange(60,100)); yield return AssertSavedCalibration(-40,125);
            yield return Snapshot("稳定但波动建议不自动保存");
            Assert.That(FindDeep<Button>(view.transform,"CalibrationSupplement").interactable,Is.True);
            FindDeep<Button>(view.transform,"CalibrationSupplement").onClick.Invoke();
            Assert.That(scenarioState.IsPlaying,Is.True);
            JsonUtility.FromJsonOverwrite("{\"calibrationWarmupBeats\":0,\"calibrationSampleBeats\":8,\"calibrationMinimumSamples\":6}",config);
            yield return PerformCalibrationInputs(config,i=>80);
            JsonUtility.FromJsonOverwrite("{\"calibrationWarmupBeats\":8,\"calibrationSampleBeats\":32,\"calibrationMinimumSamples\":24}",config);
            yield return WaitUntil("时序一致主轮允许一次独立8拍验证",()=>!scenarioState.IsPlaying,6f);
            Assert.That(scenarioState.CalibrationCandidate.Reason,Is.EqualTo(RhythmCalibrationReason.Suggested));
            Assert.That(FindDeep<Button>(view.transform,"CalibrationSupplement").interactable,Is.False);
            FindDeep<Button>(view.transform,"CalibrationSupplement").onClick.Invoke(); Assert.That(scenarioState.IsPlaying,Is.False);
            yield return AssertSavedCalibration(-40,125);
            FindDeep<Button>(view.transform,"CalibrationPreviewOriginal").onClick.Invoke();
            yield return WaitUntil("试听原值开始",()=>scenarioState.IsPlaying,5f);
            Assert.That(FindDeep<Slider>(view.transform,"Offset").value,Is.EqualTo(-40));
            FindDeep<Button>(view.transform,"CalibrationPreviewCandidate").onClick.Invoke();
            yield return WaitUntil("切换建议试听替换旧预约",()=>scenarioState.IsPlaying&&scenarioState.SongSeconds<0,5f);
            yield return WaitUntil("试听第一拍",()=>scenarioState.SongSeconds>=.58,5f);
            yield return Input.Press(scenarioState.LaneAction(0));
            yield return Snapshot("试听候选校正误差但档案不变");
            typeof(RhythmState).GetMethod("FocusLost",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scenarioState,null);
            Assert.That(scenarioState.IsPlaying,Is.False);
            Assert.That(scenarioState.CalibrationCandidate,Is.Not.Null);
            Assert.That(FindDeep<TMPro.TMP_Text>(view.transform,"CalibrationResultText").text,Does.Contain("失焦"));
            FindDeep<Button>(view.transform,"CalibrationKeep").onClick.Invoke(); Assert.That(scenarioState.IsPlaying,Is.False);
            Assert.That(scenarioState.CalibrationCandidate,Is.Null); yield return AssertSavedCalibration(-40,125);
            // 已验证完整主轮；缩短仅应用/取消接线的测试副本，不改变正式8+32配置。
            JsonUtility.FromJsonOverwrite("{\"calibrationWarmupBeats\":0,\"calibrationSampleBeats\":8,\"calibrationMinimumSamples\":6}",config);
            scenarioState.StartCalibration(); yield return PerformCalibrationInputs(config,i=>80);
            yield return WaitUntil("短副本完成明确确认验证",()=>!scenarioState.IsPlaying,6f);
            Assert.That(scenarioState.CalibrationCandidate.HasCandidate,Is.True); candidate=scenarioState.CalibrationCandidate.OffsetMs;
            FindDeep<Button>(view.transform,"CalibrationApply").onClick.Invoke();
            yield return AssertSavedCalibration(candidate,125);
            yield return Snapshot("明确应用才保存输入补偿");
            JsonUtility.FromJsonOverwrite("{\"calibrationWarmupBeats\":8,\"calibrationSampleBeats\":32,\"calibrationMinimumSamples\":24}",config);
            scenarioState.StartCalibration(); yield return PerformCalibrationInputs(config,i=>80+i*70d/31);
            yield return WaitUntil("漂移主轮结束",()=>!scenarioState.IsPlaying,6f);
            Assert.That(scenarioState.CalibrationCandidate.Reason,Is.EqualTo(RhythmCalibrationReason.Drift));
            Assert.That(scenarioState.CalibrationCandidate.Confidence,Is.EqualTo(RhythmCalibrationConfidence.Low));
            Assert.That(FindDeep<Button>(view.transform,"CalibrationApply").interactable,Is.True);
            Assert.That(FindDeep<Button>(view.transform,"CalibrationSupplement").interactable,Is.False);
            var driftText=FindDeep<TMPro.TMP_Text>(view.transform,"CalibrationResultText").text;
            Assert.That(driftText,Does.Contain("不能靠8拍补测").And.Contain("未保存").And.Contain("95%").And.Contain("本机校准诊断已记录"));
            FindDeep<Button>(view.transform,"CalibrationSupplement").onClick.Invoke();
            Assert.That(scenarioState.IsPlaying,Is.False);
            Assert.That(scenarioState.CalibrationCandidate.Reason,Is.EqualTo(RhythmCalibrationReason.Drift));
            var recorded=ReadCalibrationDiagnostic(scenarioState.LastCalibrationDiagnosticPath);
            Assert.That(recorded.result.reason,Is.EqualTo("Drift")); Assert.That(recorded.header.source,Is.EqualTo("isolated-test"));
            Assert.That(recorded.header.originalOffsetMs,Is.EqualTo(candidate).Within(1e-6));
            Assert.That(recorded.result.Matched,Is.EqualTo(32)); Assert.That(recorded.samples.Length,Is.EqualTo(32));
            Assert.That(recorded.targets.Length,Is.EqualTo(32)); Assert.That(recorded.blocks.Length,Is.EqualTo(4));
            for(int i=0;i<32;i++) {
                Assert.That(recorded.samples[i].BeatIndex,Is.EqualTo(i));
                Assert.That(recorded.samples[i].EventTime-recorded.header.inputStart,Is.EqualTo(recorded.samples[i].SongTime).Within(1e-6));
                Assert.That(recorded.samples[i].ErrorMs,Is.EqualTo(80+i*70d/31).Within(1e-6));
            }
            Assert.That(recorded.result.blockSpreadMs,Is.EqualTo(scenarioState.CalibrationCandidate.BlockSpreadMs).Within(1e-6));
            yield return Snapshot("时段漂移区别于普通波动");
            FindDeep<Button>(view.transform,"CalibrationPreviewCandidate").onClick.Invoke();
            Assert.That(scenarioState.IsPlaying,Is.True);
            yield return AssertSavedCalibration(candidate,125);
            typeof(RhythmState).GetMethod("FocusLost",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scenarioState,null);
            Assert.That(scenarioState.IsPlaying,Is.False);
            Assert.That(scenarioState.CalibrationCandidate.Confidence,Is.EqualTo(RhythmCalibrationConfidence.Low));
            FindDeep<Button>(view.transform,"CalibrationKeep").onClick.Invoke(); yield return AssertSavedCalibration(candidate,125);
            JsonUtility.FromJsonOverwrite("{\"calibrationWarmupBeats\":0,\"calibrationSampleBeats\":8,\"calibrationMinimumSamples\":6}",config);
            scenarioState.StartCalibration(); yield return PerformCalibrationInputs(config,i=>80+i*70d/7);
            yield return WaitUntil("低可信短轮结束",()=>!scenarioState.IsPlaying,6f);
            Assert.That(scenarioState.CalibrationCandidate.Confidence,Is.EqualTo(RhythmCalibrationConfidence.Low));
            yield return AssertSavedCalibration(candidate,125);
            candidate=scenarioState.CalibrationCandidate.OffsetMs;
            FindDeep<Button>(view.transform,"CalibrationApply").onClick.Invoke();
            yield return AssertSavedCalibration(candidate,125);
            yield return Snapshot("低可信估计仅明确采用后保存");
            scenarioState.StartCalibration();
            typeof(RhythmState).GetMethod("FocusLost",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scenarioState,null);
            Assert.That(scenarioState.IsPlaying,Is.False); Assert.That(scenarioState.CalibrationCandidate,Is.Null);
            yield return AssertSavedCalibration(candidate,125);
            yield return scenarioState.ExitAsync(System.Threading.CancellationToken.None).ToCoroutine();
            yield return scenarioState.EnterAsync(System.Threading.CancellationToken.None).ToCoroutine();
            Assert.That(FindDeep<Slider>(ui.Get<RhythmView>().transform,"Offset").value,Is.EqualTo(Mathf.Round((float)candidate)));
            Assert.That(FindDeep<Slider>(ui.Get<RhythmView>().transform,"VisualOffset").value,Is.EqualTo(125));
            yield return Snapshot("重新进入恢复明确确认值与视觉独立值");
        }
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator Calibration_DiagnosticFailureAndCancel_PreserveOriginalOffset()
        {
            yield return WaitUntil("本地校准诊断就绪",()=>ResolveService<IGameFlow>()?.Current is RhythmState,30f);
            var flow=ResolveService<IGameFlow>(); var ui=ResolveService<IUIService>(); var saves=ResolveService<ISaveService>();
            Assert.That(PlatformServiceBase.SaveRootOverride,Is.Not.Null.And.Not.Empty);
            Assert.That(ResolveService<IPlatformService>().SaveRoot,Is.EqualTo(PlatformServiceBase.SaveRootOverride));
            yield return flow.GoToAsync<TitleState>().ToCoroutine();
            yield return saves.WriteProfileAsync("rhythm-calibration",new RhythmCalibrationData {OffsetMs=100,VisualOffsetMs=125}).ToCoroutine();
            var config=Track(Object.Instantiate(ResolveService<RhythmConfig>()));
            JsonUtility.FromJsonOverwrite("{\"calibrationWarmupBeats\":0,\"calibrationSampleBeats\":8,\"calibrationMinimumSamples\":6}",config);
            scenarioState=new RhythmState(config,ui,ResolveService<IAudioService>(),saves,flow,ResolveService<IInputService>(),
                ResolveService<IWorldPauseService>(),ResolveService<ITelemetryService>(),ResolveService<INotificationService>());
            yield return scenarioState.EnterAsync(System.Threading.CancellationToken.None).ToCoroutine();
            scenarioHost=Object.FindObjectOfType<GameBootstrap>(); scenarioTick=scenarioHost.StartCoroutine(TickScenario()); Input.Prime();
            var view=ui.Get<RhythmView>();
            string directory=System.IO.Path.Combine(PlatformServiceBase.SaveRootOverride,"rhythm-diagnostics");
            Assert.That(System.IO.Directory.Exists(directory),Is.False);
            // 仅占用框架隔离根下的诊断目录名，不触玩家目录/存档或其他RHD。
            System.IO.File.WriteAllText(directory,"blocked-test-directory");
            try
            {
                yield return Step("诊断目录故意不可写，候选不能自动改补偿",scenarioState.StartCalibration,0f);
                yield return PerformCalibrationInputs(config,i=>80);
                yield return WaitUntil("保存失败仍结束校准并清理预约",()=>!scenarioState.IsPlaying,6f);
                yield return WaitUntil("结果页明确显示校准诊断未保存",()=>HasCalibrationSaveFailure(ui),5f);
                Assert.That(scenarioState.LastCalibrationDiagnosticPath,Is.Null);
                Assert.That(scenarioState.CalibrationCandidate.HasCandidate,Is.True);
                Assert.That(FindDeep<Slider>(view.transform,"Offset").value,Is.EqualTo(100));
                Assert.That(FindDeep<Slider>(view.transform,"VisualOffset").value,Is.EqualTo(125));
                yield return AssertSavedCalibration(100,125);
                yield return Snapshot("诊断保存失败仍保留原补偿与确认选择");
                FindDeep<Button>(view.transform,"CalibrationKeep").onClick.Invoke();
                Assert.That(scenarioState.CalibrationCandidate,Is.Null);
                Assert.That(scenarioState.IsPlaying,Is.False);
            }
            finally { System.IO.File.Delete(directory); }
            yield return Step("恢复目录后开始校准，中途取消",scenarioState.StartCalibration,0f);
            yield return WaitUntil("取消前收到真实采样输入",()=>scenarioState.IsPlaying&&scenarioState.SongSeconds>=.58,5f);
            yield return Input.Press(scenarioState.LaneAction(0));
            typeof(RhythmState).GetMethod("FocusLost",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scenarioState,null);
            Assert.That(scenarioState.IsPlaying,Is.False); Assert.That(scenarioState.CalibrationCandidate,Is.Null);
            var cancelled=ReadCalibrationDiagnostic(scenarioState.LastCalibrationDiagnosticPath);
            Assert.That(cancelled.endReason,Is.EqualTo("FocusLost"));
            Assert.That(cancelled.header.originalOffsetMs,Is.EqualTo(100));
            Assert.That(cancelled.header.originalVisualOffsetMs,Is.EqualTo(125));
            Assert.That(cancelled.samples.Length,Is.GreaterThan(0)); Assert.That(cancelled.complete,Is.True);
            Assert.That(cancelled.header.source,Is.EqualTo("isolated-test"));
            Assert.That(FindDeep<Slider>(view.transform,"Offset").value,Is.EqualTo(100));
            yield return AssertSavedCalibration(100,125);
            yield return Snapshot("取消校准仍记录本机证据且原值保持");
        }
        private static bool HasCalibrationSaveFailure(IUIService ui)
        {
            var view=ui.Get<RhythmView>(); if(view==null) return false;
            var label=FindDeep<TMPro.TMP_Text>(view.transform,"CalibrationResultText");
            return label!=null&&label.text.Contains("校准诊断未保存")&&label.text.Contains("原补偿不变");
        }
        private static CalibrationReportSnapshot ReadCalibrationDiagnostic(string path)
        {
            Assert.That(path,Is.Not.Null.And.Not.Empty);
            Assert.That(System.IO.File.Exists(path),Is.True);
            Assert.That(new System.IO.FileInfo(path).Length,Is.LessThanOrEqualTo(RhythmCalibrationDiagnostic.MaximumBytes));
            var report=JsonUtility.FromJson<CalibrationReportSnapshot>(System.IO.File.ReadAllText(path));
            Assert.That(report.format,Is.EqualTo("rhythm-calibration-v1"));
            return report;
        }
        [System.Serializable] private sealed class CalibrationReportSnapshot
        {
            public string format;
            public string endReason;
            public bool complete;
            public CalibrationHeader header;
            public CalibrationSample[] samples;
            public CalibrationBlock[] blocks;
            public double[] targets;
            public CalibrationSummary result;
        }
        [System.Serializable] private sealed class CalibrationHeader
        {
            public string source;
            public double originalOffsetMs;
            public float originalVisualOffsetMs;
            public double inputStart;
        }
        [System.Serializable] private sealed class CalibrationSample
        {
            public double EventTime;
            public double SongTime;
            public int BeatIndex;
            public double ErrorMs;
        }
        [System.Serializable] private sealed class CalibrationBlock
        {
            public int kept;
            public double medianMs;
        }
        [System.Serializable] private sealed class CalibrationSummary
        {
            public string reason;
            public int Matched;
            public double blockSpreadMs;
        }
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Calibration_KeyboardTimestampBacklog_ReproducesAndFixesDrift()
        {
            yield return WaitUntil("校准复现就绪", () => ResolveService<IGameFlow>()?.Current is RhythmState, 30f);
            // 独立设备/动作隔离时钟守卫和参考音；只证明 InputSystem 整机水位是否丢弃旧 fixture。
            // 完整 Runtime/保存/试听仍由确认式校准用例验证，不能用本用例替代。
            var keyboard = InputSystem.AddDevice<Keyboard>("CalibrationTimestampKeyboard");
            var map = new InputActionMap("CalibrationTimestampFixture");
            var keys = new[] { keyboard.dKey, keyboard.fKey, keyboard.jKey, keyboard.kKey };
            var received = new List<double>();
            double origin = 0;
            RhythmCalibrationEstimator estimator = null;
            try
            {
                for (int lane = 0; lane < 4; lane++)
                {
                    var action = map.AddAction("Lane" + lane, InputActionType.Button, keys[lane].path);
                    action.performed += context => { received.Add(context.time); estimator.Add(context.time - origin); };
                }
                map.Enable();
                foreach (bool legacy in new[] { true, false })
                {
                    var targets = new double[32]; for (int i = 0; i < targets.Length; i++) targets[i] = (i + 1) * .5;
                    estimator = new RhythmCalibrationEstimator(targets, 24, 250, 30);
                    received.Clear(); origin = Time.realtimeSinceStartupAsDouble + .2;
                    var trace = new List<string>();
                    yield return Step(legacy ? "旧 fixture 混合时刻在两次卡顿后丢按下" : "相同卡顿，松开与按下同用预定单调时刻", null, 0f);
                    for (int i = 0; i < targets.Length; i++)
                    {
                        double press = origin + targets[i] + (80 + new[] { -70d, -55, -35, -15, 15, 35, 55, 70 }[i % 8]) / 1000;
                        yield return WaitUntil("整机时间戳拍 " + (i + 1), () => Time.realtimeSinceStartupAsDouble >= press, 8f);
                        int before = received.Count;
                        QueueCalibrationEdge(keys[i % 4], 1f, press);
                        yield return null; yield return null;
                        if (i == 7 || i == 15) System.Threading.Thread.Sleep(i == 7 ? 1300 : 1900);
                        double release = legacy ? Time.realtimeSinceStartupAsDouble : press + .001;
                        QueueCalibrationEdge(keys[i % 4], 0f, release);
                        yield return null;
                        trace.Add($"press={i + 1},expected={press:R},received={(received.Count > before ? received[before] : double.NaN):R},release={release:R}");
                    }
                    var result = estimator.Analyze();
                    yield return Step($"整机水位证据：legacy={legacy},press={received.Count},matched={result.Matched},invalid={result.Invalid},offset={result.OffsetMs:R},MAD={result.RawMadMs:R},spread={result.BlockSpreadMs:R},reason={result.Reason}; " + string.Join("; ", trace), null, 0f);
                    if (legacy)
                    {
                        Assert.That(received.Count, Is.LessThan(32));
                        Assert.That(result.Confidence, Is.EqualTo(RhythmCalibrationConfidence.Low));
                        Assert.That(result.OffsetMs, Is.Not.EqualTo(80).Within(1e-6));
                    }
                    else
                    {
                        Assert.That(received.Count, Is.EqualTo(32)); Assert.That(result.Matched, Is.EqualTo(32));
                        Assert.That(result.Reason, Is.EqualTo(RhythmCalibrationReason.Suggested));
                        Assert.That(result.OffsetMs, Is.EqualTo(80).Within(1e-6));
                        Assert.That(result.RawMadMs, Is.EqualTo(45).Within(1e-6));
                        Assert.That(result.BlockSpreadMs, Is.Zero.Within(1e-6));
                    }
                }
                yield return Snapshot("相同卡顿下32拍建议保留");
            }
            finally { map.Dispose(); if (keyboard.added) InputSystem.RemoveDevice(keyboard); }
        }

        [UnityTest]
        public IEnumerator ClockGuard_InvalidSample_StopsAudioAndAllowsRetry()
        {
            yield return WaitUntil("时钟守卫就绪", () => ResolveService<IGameFlow>()?.Current is RhythmState, 30f);
            var state = (RhythmState)ResolveService<IGameFlow>().Current;
            if (state.IsSongMenu)
            {
                state.SelectSong("intro");
                yield return WaitUntil("时钟测试曲目就绪", () => !state.IsSongMenu, 8f);
            }
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var observe = typeof(RhythmState).GetMethod("ObserveClockSample", flags);
            foreach (bool negativeSpan in new[] { false, true })
            {
                yield return Step("开始练习后注入非法时钟采样", state.StartPractice, 0f);
                yield return WaitUntil("练习已预约", () => state.IsPlaying, 5f);
                var playback = (AudioPlayback)typeof(RhythmState).GetField("playback", flags).GetValue(state);
                double now = Time.realtimeSinceStartupAsDouble;
                object[] sample = negativeSpan ? new object[] { now + .01, AudioSettings.dspTime, now } : new object[] { now, double.NaN, now };
                Assert.That(observe.Invoke(state, sample), Is.False);
                Assert.That(state.IsPlaying, Is.False);
                Assert.That(typeof(RhythmState).GetField("playback", flags).GetValue(state), Is.Null);
                Assert.That(typeof(AudioPlayback).GetField("disposed", flags).GetValue(playback), Is.True);
                var actions = (InputActionAsset)typeof(RhythmState).GetField("actions", flags).GetValue(state);
                Assert.That(actions.FindActionMap("Rhythm").enabled, Is.False);
                Assert.That(state.LastDiagnostic.Commands[state.LastDiagnostic.Commands.Count - 1].EndReason, Is.EqualTo(RhythmDiagnosticData.Reason.InvalidClock));
            }
            yield return Step("非法值停局后正常重试", state.StartPractice, 0f);
            yield return WaitUntil("重试播放并收到正常桥接样本", () => state.IsPlaying && state.SongSeconds > .1, 5f);
            Assert.That(state.IsPlaying, Is.True);
            yield return Snapshot("非法时钟停止后正常重试");
        }

        private IEnumerator PerformCalibrationInputs(RhythmConfig config, System.Func<int,double> error)
        {
            var received = new List<double>();
            var releases = new List<double>();
            var trace = new List<string>();
            var clock = (AudioPlayback)typeof(RhythmState).GetField("playback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scenarioState);
            var estimator = (RhythmCalibrationEstimator)typeof(RhythmState).GetField("estimator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scenarioState);
            System.Action<InputAction.CallbackContext> onPress = context => received.Add(context.time);
            System.Action<InputAction.CallbackContext> onRelease = context => releases.Add(context.time);
            for (int lane = 0; lane < 4; lane++)
            {
                scenarioState.LaneAction(lane).performed += onPress;
                scenarioState.LaneAction(lane).canceled += onRelease;
            }
            double previousEdge = double.NegativeInfinity;
            try
            {
                for (int i = 0; i < config.CalibrationSampleBeats; i++)
                {
                    double target = (config.CalibrationWarmupBeats + i + 1) * 60d / config.CalibrationBpm + error(i) / 1000;
                    yield return WaitUntil("固定采样拍 " + (i + 1), () => !scenarioState.IsPlaying || scenarioState.SongSeconds >= target, 8f);
                    Assert.That(scenarioState.IsPlaying, Is.True, "校准会话提前结束，停止 fixture，不能继续发陈旧事件");
                    var action = scenarioState.LaneAction(i % 4);
                    UnityEngine.InputSystem.Controls.ButtonControl button = null;
                    foreach (var control in action.controls)
                        if (control.device.name == "ShowcaseKeyboard" && control is UnityEngine.InputSystem.Controls.ButtonControl key) { button = key; break; }
                    Assert.That(button, Is.Not.Null);
                    double press = clock.InputStart + target;
                    Assert.That(press, Is.GreaterThan(previousEdge), "整台键盘事件时间必须单调，不能按轨分别判断");
                    int before = received.Count;
                    QueueCalibrationEdge(button, 1f, press);
                    yield return null; yield return null;
                    // 松开也使用预定事件时间；卡顿不能把设备水位推到下一枚预定按下之后。
                    double release = press + 0.001;
                    QueueCalibrationEdge(button, 0f, release);
                    previousEdge = release;
                    yield return null;
                    double actual = received.Count > before ? received[before] : double.NaN;
                    var beats = (List<int>)typeof(RhythmCalibrationEstimator).GetField("beatIndices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(estimator);
                    int matchedBeat = beats.Count > 0 ? beats[beats.Count - 1] : -1;
                    trace.Add($"press={i + 1},expected={press:R},received={actual:R},song={actual - clock.InputStart:R},beat={matchedBeat},matched={estimator.Count},invalid={estimator.Invalid}");
                }
            }
            finally
            {
                for (int lane = 0; lane < 4; lane++)
                {
                    scenarioState.LaneAction(lane).performed -= onPress;
                    scenarioState.LaneAction(lane).canceled -= onRelease;
                }
                Debug.Log("[VERIFY][Rhythm] calibration-input-evidence " + string.Join("; ", trace));
            }
            var result = estimator.Analyze();
            yield return Step($"真实校准接收：press={received.Count},release={releases.Count},matched={result.Matched},invalid={result.Invalid},outside={result.Outside},duplicate={result.Duplicate},offset={result.OffsetMs:R},MAD={result.RawMadMs:R},spread={result.BlockSpreadMs:R},reason={result.Reason}; " + string.Join("; ", trace), null, 0f);
            Assert.That(received.Count, Is.EqualTo(config.CalibrationSampleBeats));
            Assert.That(releases.Count, Is.EqualTo(config.CalibrationSampleBeats));
            Assert.That(result.Matched, Is.EqualTo(config.CalibrationSampleBeats));
            Assert.That(result.Invalid + result.Outside + result.Duplicate, Is.Zero);
        }
        private static void QueueCalibrationEdge(UnityEngine.InputSystem.Controls.ButtonControl button, float value, double timestamp)
        {
            using (UnityEngine.InputSystem.LowLevel.StateEvent.From(button.device, out var eventPtr))
            {
                eventPtr.time = timestamp;
                button.WriteValueIntoEvent(value, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }
        private IEnumerator AssertSavedCalibration(double offset,float visual)
        {
            RhythmCalibrationData saved=null; double deadline=Time.realtimeSinceStartupAsDouble+5;
            do
            {
                yield return ResolveService<ISaveService>().ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(data=>saved=data).ToCoroutine();
                if(saved!=null&&System.Math.Abs(saved.OffsetMs-offset)<.001&&saved.VisualOffsetMs==visual) yield break;
                yield return null;
            } while(Time.realtimeSinceStartupAsDouble<deadline);
            Assert.Fail("真实隔离档案未恢复期望补偿/视觉值");
        }
        [UnityTest]
        public IEnumerator CrossLaneChordsAndMultipleHolds_RealInput_Work()
        {
            yield return WaitUntil("组合回放就绪",()=>ResolveService<IGameFlow>()?.Current is RhythmState,30f);
            var flow=ResolveService<IGameFlow>(); yield return flow.GoToAsync<TitleState>().ToCoroutine();
            var config=Track(Object.Instantiate(ResolveService<RhythmConfig>()));
            JsonUtility.FromJsonOverwrite("{\"schemaVersion\":2,\"noteTimes\":[],\"noteLanes\":[],\"clipStartSeconds\":16,\"durationSeconds\":3,\"countdownSeconds\":0.2,\"approachSeconds\":0.15,\"notes\":[{\"id\":\"chord-a\",\"lane\":0,\"timeMs\":500,\"type\":0,\"durationMs\":0},{\"id\":\"chord-b\",\"lane\":1,\"timeMs\":500,\"type\":0,\"durationMs\":0},{\"id\":\"hold-a\",\"lane\":2,\"timeMs\":1000,\"type\":1,\"durationMs\":1000},{\"id\":\"hold-b\",\"lane\":3,\"timeMs\":1000,\"type\":1,\"durationMs\":1000},{\"id\":\"tap-with-hold\",\"lane\":0,\"timeMs\":1000,\"type\":0,\"durationMs\":0},{\"id\":\"tap-during-hold\",\"lane\":1,\"timeMs\":1500,\"type\":0,\"durationMs\":0}]}",config);
            scenarioState=new RhythmState(config,ResolveService<IUIService>(),ResolveService<IAudioService>(),ResolveService<ISaveService>(),flow,
                ResolveService<IInputService>(),ResolveService<IWorldPauseService>(),ResolveService<ITelemetryService>(),ResolveService<INotificationService>());
            yield return scenarioState.EnterAsync(System.Threading.CancellationToken.None).ToCoroutine();
            scenarioHost=Object.FindObjectOfType<GameBootstrap>(); scenarioTick=scenarioHost.StartCoroutine(TickScenario()); Input.Prime();
            scenarioState.StartRound(); yield return WaitUntil("组合开始",()=>scenarioState.IsPlaying,5f);
            yield return PerformCurrentChart(scenarioState); yield return WaitUntil("组合结算",()=>!scenarioState.IsPlaying,5f);
            yield return Check("双押、双长按与长按中其他轨单击各结算一次",()=>scenarioState.Rules.Perfect==6&&scenarioState.Rules.Score==6000&&scenarioState.Rules.CompletedHolds==2&&scenarioState.Rules.Combo==6&&scenarioState.Rules.Miss==0);
            yield return Snapshot("跨轨组合复用TapHold规则");
        }

        private IEnumerator PerformCurrentChart(RhythmState state)
        {
            var clock = (AudioPlayback)typeof(RhythmState).GetField("playback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
            var edges = new List<ChartEdge>();
            for (int i = 0; i < state.Rules.Count; i++)
            {
                edges.Add(new ChartEdge(i, state.Rules.NoteTime(i) - 0.02, true, edges.Count));
                edges.Add(new ChartEdge(i, state.Rules.NoteType(i) == RhythmNoteType.Hold ? state.Rules.NoteEnd(i) + 0.02 :
                    state.Rules.NoteTime(i) + 0.05, false, edges.Count));
            }
            edges.Sort((a, b) => { int order = a.Seconds.CompareTo(b.Seconds); return order != 0 ? order : a.Sequence.CompareTo(b.Sequence); });
            foreach (var edge in edges)
            {
                double timeout = Time.realtimeSinceStartupAsDouble + 6;
                // 真实 InputSystem 事件使用单调输入域；不能用分块更新的 DSP 显示位置提前释放 Hold。
                while (state.IsPlaying && clock.PositionAtInputTime(Time.realtimeSinceStartupAsDouble) < edge.Seconds &&
                    Time.realtimeSinceStartupAsDouble < timeout) yield return null;
                if (!state.IsPlaying) yield break;
                if (!edge.Press && state.Rules.NoteType(edge.Note) == RhythmNoteType.Hold)
                    Debug.Log($"[VERIFY][Rhythm] Hold release note={state.Rules.NoteId(edge.Note)}, target={edge.Seconds:F6}, input={clock.PositionAtInputTime(Time.realtimeSinceStartupAsDouble):F6}, dsp={state.SongSeconds:F6}, frame={Time.frameCount}");
                var action = state.LaneAction(state.Rules.NoteLane(edge.Note));
                if (edge.Press) yield return Input.Hold(action); else yield return Input.Release(action);
            }
        }

        private static void AssertExport(string file, int session, RhythmDiagnosticData.Reason reason)
        {
            using (var stream = System.IO.File.OpenRead(file))
            {
                var restored = RhythmDiagnosticCodec.Read(stream);
                Assert.That(restored.SessionHeader.Session, Is.EqualTo(session));
                Assert.That(restored.Commands[restored.Commands.Count - 1].EndReason, Is.EqualTo(reason));
                Assert.That(RhythmDiagnosticReplay.Run(restored).Matches, Is.True);
            }
        }

        private static string NotificationTitle(IUIService ui)
        {
            var notification = ui.Get<NotificationView>();
            if (notification == null || !notification.IsCardShown) return null;
            return ((TMPro.TMP_Text)typeof(NotificationView).GetField("titleLabel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(notification)).text;
        }

        private IEnumerator TickScenario()
        {
            while (scenarioState != null) { scenarioState.Tick(); yield return null; }
        }

        private sealed class ChartEdge
        {
            public int Note { get; }
            public double Seconds { get; }
            public bool Press { get; }
            public int Sequence { get; }
            public ChartEdge(int note, double seconds, bool press, int sequence)
            { Note = note; Seconds = seconds; Press = press; Sequence = sequence; }
        }

        [UnityTearDown]
        public IEnumerator ShutdownStandaloneBoot()
        {
            if (scenarioHost != null && scenarioTick != null) scenarioHost.StopCoroutine(scenarioTick);
            scenarioTick = null;
            if (scenarioState != null)
            {
                yield return scenarioState.ExitAsync(System.Threading.CancellationToken.None).ToCoroutine();
                scenarioState.Dispose(); scenarioState = null;
            }
            // LoadBootScene 为 false，基类不会销毁独立场景自带的根作用域。
            var scope = Object.FindObjectOfType<GameBootstrap>();
            if (scope != null) Object.Destroy(scope.gameObject);
            yield return null;
        }
    }
}
