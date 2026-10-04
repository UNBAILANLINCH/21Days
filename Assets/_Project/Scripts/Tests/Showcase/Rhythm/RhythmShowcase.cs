// 职责：经真实 Input System 跑四轨音游并检查重试、结算、校准保存。既有回放没有音游；使用用户指定的独立试玩场景。
using System.Collections;
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
            var queueField = typeof(RhythmState).GetField("inputQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            var watermarkField = typeof(RhythmInputQueue).GetField("watermark", BindingFlags.Instance | BindingFlags.NonPublic);
            System.Action<InputAction.CallbackContext> observe = context =>
            {
                var clock = (AudioPlayback)playbackField.GetValue(state);
                if (clock == null) return;
                Debug.Log($"[VERIFY][Rhythm] 时间证据 event={context.time:F6}, realtime={Time.realtimeSinceStartupAsDouble:F6}, mapped={clock.PositionAtInputTime(context.time):F6}, dsp={clock.Position:F6}, watermark={(double)watermarkField.GetValue(queueField.GetValue(state)):F6}");
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
            yield return Step("完整演奏保留的 56 个单键音符", null, 0f);
            int firstFrame = Time.frameCount;
            double firstTime = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < state.Rules.Count; i++)
            {
                double target = state.Rules.NoteTime(i) - 0.02;
                double timeout = Time.realtimeSinceStartupAsDouble + 6;
                while (state.IsPlaying && state.SongSeconds < target && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
                if (!state.IsPlaying) break;
                yield return Input.Press(state.LaneAction(state.Rules.NoteLane(i)));
            }
            Debug.Log($"[VERIFY][Rhythm] 56Tap 场景平均帧率={(Time.frameCount - firstFrame) / (Time.realtimeSinceStartupAsDouble - firstTime):F1} FPS（含编辑器及回放开销）");
            yield return Check("56 枚单键均被输入命中，没有漏击", () => state.Rules.Perfect + state.Rules.Good == 56 && state.Rules.Miss == 0, 2f);
            yield return Snapshot("完整56Tap命中");
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
            RhythmCalibrationData calibrated = null;
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(data => calibrated = data).ToCoroutine();
            yield return Check("校准替换输入补偿，视觉值保持独立", () => calibrated.OffsetMs > 30 && calibrated.OffsetMs < 140 && calibrated.VisualOffsetMs == 125);
            yield return Snapshot("校准质量与独立偏移");
        }

        private IEnumerator TickScenario()
        {
            while (scenarioState != null) { scenarioState.Tick(); yield return null; }
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
