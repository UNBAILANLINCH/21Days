// 职责：隔离短谱验证滚动曲库、真实进度档案与外部演奏结果；旧全曲回放不适合扩展到二十曲和外部权限 fixture。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Audio;
using Game.Core.Boot;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Rhythm;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests.Showcase.Rhythm
{
    [Category("Showcase")]
    public sealed class RhythmLibraryShowcase : ShowcaseScenario
    {
        private RhythmState state;
        private RhythmCatalogConfig catalog;
        private GameBootstrap host;
        private Coroutine tick;
        private SaveFixture saveFixture;
        protected override string Module => "Rhythm";
        // 用户指定的隔离入口；不进入 Boot 或改正式谱面。
        protected override string ScenePath => "Assets/_Project/Scenes/RhythmDemo.unity";
        protected override bool LoadBootScene => false;

        [UnityTest, Timeout(120000)]
        public IEnumerator TwentySongs_ScrollLockRecordsAndRestore_Work()
        {
            yield return Prepare(20);
            yield return Enter();
            var view = ResolveService<IUIService>().Get<RhythmView>();
            var scroll = FindDeep<ScrollRect>(view.transform, "SongScroll");
            Assert.That(scroll, Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            yield return Check("二十曲列表超过可见区域，可滚动到末尾", () => scroll.content.rect.height > scroll.viewport.rect.height);
            var locked = FindDeep<Button>(view.transform, "Song_test-02");
            Assert.That(locked.interactable, Is.False);
            Assert.That(locked.GetComponentInChildren<TMP_Text>().text, Does.Contain("测试曲 01"));
            state.SelectSong("test-02");
            Assert.That(state.IsSongMenu, Is.True);
            yield return Step("滚动至第二十曲并选择", () => scroll.verticalNormalizedPosition = 0);
            FindDeep<Button>(view.transform, "Song_test-20").onClick.Invoke();
            yield return WaitUntil("末尾曲准备完成", () => !state.IsSongMenu && state.SelectedSongId == "test-20", 8f);
            state.ShowSongMenu();
            yield return Check("返回保留选中详情和滚动位置", () =>
                FindDeep<TMP_Text>(view.transform, "SongDetails").text.Contains("测试曲 20") && scroll.verticalNormalizedPosition < .05f);
            yield return Snapshot("二十曲末尾与当前谱面详情");
            FindDeep<Button>(view.transform, "Song_test-20").onClick.Invoke();
            yield return WaitUntil("再次选曲完成", () => !state.IsSongMenu, 8f);
            yield return Step("短谱全部漏按，自然结束仍产生真实完整局", state.StartRound, 0f);
            yield return WaitUntil("自由局开始", () => state.IsPlaying, 5f);
            yield return WaitUntil("自由局自然结束", () => !state.IsPlaying && state.LastRunResult != null, 6f);
            var completed = state.LastRunResult;
            Assert.That(completed.Completion, Is.EqualTo(RhythmRunCompletion.Completed));
            Assert.That(completed.Miss, Is.EqualTo(2));
            RhythmProgressData saved = null;
            yield return ReadSaved(value => saved = value);
            var record = saved.Records[catalog.Find("test-20").RecordKey];
            Assert.That(record.BestScoreRun.RunId, Is.EqualTo(completed.RunId));
            Assert.That(record.BestScoreRun.Score, Is.Zero);
            state.SelectSong("test-20");
            yield return WaitUntil("重入后再次准备末尾曲", () => !state.IsSongMenu, 5f);
            state.StartRound();
            yield return WaitUntil("重试开始", () => state.IsPlaying, 5f);
            typeof(RhythmState).GetMethod("FocusLost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, null);
            Assert.That(state.LastRunResult.Completion, Is.EqualTo(RhythmRunCompletion.Aborted));
            yield return Leave();
            yield return Enter();
            yield return ReadSaved(value => saved = value);
            Assert.That(saved.Records[catalog.Find("test-20").RecordKey].LastCompletedRun.RunId, Is.EqualTo(completed.RunId));
            state.SelectSong("test-20");
            yield return WaitUntil("重入恢复纪录详情", () => !state.IsSongMenu, 8f);
            yield return Snapshot("自然完成纪录恢复而失焦不覆盖");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LegacyProfile_BackupAndReentry_PreserveOriginal()
        {
            yield return Prepare(3);
            string path = Path.Combine(PlatformServiceBase.SaveRootOverride, "profile-rhythm-progress.json");
            // fixture 身份仅含固定安全字符；Showcase 程序集不直接依赖 Newtonsoft。
            string legacyKey = catalog.Song(0).ProgressKey;
            string legacy = "{\"version\":1,\"data\":{\"BestScores\":{\"" + legacyKey + "\":1700},\"ClearedCharts\":[\"" + legacyKey + "\"]}}";
            // 只写基类已隔离的临时 Profile，绝不碰真实玩家根目录。
            File.WriteAllText(path, legacy);
            byte[] original = File.ReadAllBytes(path);
            yield return Step("载入旧成绩档案并保留原始备份", null, 0f);
            yield return Enter();
            Assert.That(state.BestScore("test-01"), Is.Zero, "未知评分版本旧最高分不冒充当前完整纪录");
            Assert.That(FindDeep<TMP_Text>(ResolveService<IUIService>().Get<RhythmView>().transform, "SongDetails").text, Does.Contain("历史最高 1700"));
            var unlocked = FindDeep<Button>(ResolveService<IUIService>().Get<RhythmView>().transform, "Song_test-02");
            Assert.That(unlocked.interactable, Is.True);
            string[] backups = Directory.GetFiles(PlatformServiceBase.SaveRootOverride, "profile-rhythm-progress.json.migration-v1-*.bak");
            Assert.That(backups.Length, Is.EqualTo(1));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(backups[0]));
            yield return Snapshot("旧通关解锁保留");
            yield return Leave();
            yield return Enter();
            RhythmProgressData saved = null;
            yield return ReadSaved(value => saved = value);
            Assert.That(saved.BestScores[catalog.Song(0).ProgressKey], Is.EqualTo(1700));
            Assert.That(saved.Records.Count, Is.Zero, "旧最高分不可伪造完整历史判定局");
            Assert.That(Directory.GetFiles(PlatformServiceBase.SaveRootOverride, "profile-rhythm-progress.json.migration-v1-*.bak").Length, Is.EqualTo(1));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(backups[0]));
            string futurePath = Path.Combine(PlatformServiceBase.SaveRootOverride, "profile-future-fixture.json");
            File.WriteAllText(futurePath, "{\"version\":99,\"data\":{}}");
            byte[] future = File.ReadAllBytes(futurePath);
            Assert.Throws<InvalidDataException>(() => RhythmProgressArchive.EnsureSupportedAndBackup(PlatformServiceBase.SaveRootOverride, "future-fixture", 2));
            CollectionAssert.AreEqual(future, File.ReadAllBytes(futurePath));
            yield return Check("重入仍显示旧成绩且不伪造判定统计", () =>
                state.BestScore("test-01") == 0 && FindDeep<TMP_Text>(ResolveService<IUIService>().Get<RhythmView>().transform, "SongDetails").text.Contains("历史最高 1700"));
            yield return Snapshot("旧档重入与纪录保留");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ExternalCombat_PermissionsCallbacksAndRecordSeparation_Work()
        {
            yield return Prepare(3);
            var permission = new PermissionFixture();
            int successes = 0;
            int aborted = 0;
            var consumer = new RhythmExternalSession(permission, new SuccessFixture(), onSuccess: result => successes++, onAborted: result => aborted++);
            var request = new RhythmPlayRequest("combat-fixture", "controlled-performer", "test-01", RhythmPlayMode.Combat);
            state = CreateState();
            Assert.Throws<InvalidOperationException>(() => state.ConfigureExternal(request, consumer));
            permission.Tamed = true;
            state.ConfigureExternal(request, consumer);
            yield return Enter(false);
            yield return Step("已驯服可进入，未控制乐师不能开始演奏", state.StartRound, 0f);
            yield return WaitUntil("权限拒绝提示", () => FindDeep<TMP_Text>(ResolveService<IUIService>().Get<RhythmView>().transform, "Status").text.Contains("外部演奏请求不可用"), 5f);
            Assert.That(state.IsPlaying, Is.False);
            permission.Controlled = true;
            yield return Step("控制乐师后由注入策略消费完成结果", state.StartRound, 0f);
            yield return WaitUntil("外部短谱开始", () => state.IsPlaying, 5f);
            yield return WaitUntil("外部短谱完成", () => !state.IsPlaying && successes == 1, 6f);
            var result = state.LastRunResult;
            Assert.That(result.Mode, Is.EqualTo(RhythmPlayMode.Combat));
            Assert.That(result.Completion, Is.EqualTo(RhythmRunCompletion.Completed));
            Assert.That(consumer.Consume(result), Is.False);
            Assert.That(consumer.TryBegin(request, out _), Is.False);
            Assert.That(successes, Is.EqualTo(1));
            RhythmProgressData saved = null;
            yield return ReadSaved(value => saved = value);
            Assert.That(saved.Records.Count, Is.Zero);
            state.StartRound();
            yield return WaitUntil("已消费请求不能再次启动", () => FindDeep<TMP_Text>(ResolveService<IUIService>().Get<RhythmView>().transform, "Status").text.Contains("外部演奏请求不可用"), 5f);
            Assert.That(state.IsPlaying, Is.False);
            state.ConfigureExternal(new RhythmPlayRequest("combat-abort-fixture", "controlled-performer", "test-01", RhythmPlayMode.Combat), consumer);
            state.StartRound();
            yield return WaitUntil("新外部请求启动", () => state.IsPlaying, 5f);
            typeof(RhythmState).GetMethod("FocusLost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, null);
            Assert.That(aborted, Is.EqualTo(1));
            Assert.That(successes, Is.EqualTo(1));
            Assert.That(state.LastRunResult.Completion, Is.EqualTo(RhythmRunCompletion.Aborted));
            yield return Snapshot("外部战斗结果与个人纪录分流");
            yield return Leave();
            state.Dispose(); state = null;
            yield return Enter();
            state.SelectSong("test-01");
            yield return WaitUntil("自由局曲目就绪", () => !state.IsSongMenu, 5f);
            state.StartRound();
            yield return WaitUntil("自由局开始", () => state.IsPlaying, 5f);
            yield return WaitUntil("自由局完成", () => !state.IsPlaying && state.LastRunResult != null, 6f);
            Assert.That(successes, Is.EqualTo(1), "普通自由局不能触发战斗回调");
            yield return ReadSaved(value => saved = value);
            Assert.That(saved.Records.Count, Is.EqualTo(1));
            yield return Snapshot("自由演奏只保存个人纪录");
        }

        private IEnumerator Prepare(int count)
        {
            yield return WaitUntil("隔离场景就绪", () => ResolveService<IGameFlow>()?.Current is RhythmState, 30f);
            Assert.That(PlatformServiceBase.SaveRootOverride, Is.Not.Null.And.Not.Empty);
            Assert.That(ResolveService<IPlatformService>().SaveRoot, Is.EqualTo(PlatformServiceBase.SaveRootOverride));
            yield return ResolveService<IGameFlow>().GoToAsync<TitleState>().ToCoroutine();
            catalog = Track(ScriptableObject.CreateInstance<RhythmCatalogConfig>());
            var songs = new RhythmSongData[count];
            for (int i = 0; i < count; i++)
            {
                var config = Track(Object.Instantiate(ResolveService<RhythmConfig>()));
                JsonUtility.FromJsonOverwrite("{\"chartId\":\"library-short\",\"schemaVersion\":2,\"noteTimes\":[],\"noteLanes\":[],\"durationSeconds\":1.5,\"countdownSeconds\":0.2,\"approachSeconds\":0.15,\"notes\":[{\"id\":\"first\",\"lane\":0,\"timeMs\":300,\"type\":0,\"durationMs\":0},{\"id\":\"second\",\"lane\":1,\"timeMs\":700,\"type\":0,\"durationMs\":0}]}", config);
                songs[i] = new RhythmSongData();
                Set(songs[i], "id", "test-" + (i + 1).ToString("00"));
                Set(songs[i], "title", "测试曲 " + (i + 1).ToString("00"));
                Set(songs[i], "difficulty", "测试正曲"); Set(songs[i], "chart", config);
                if (i == 1) Set(songs[i], "prerequisiteId", "test-01");
            }
            Set(catalog, "songs", songs); catalog.Validate();
            Input.Prime();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator DelayedStartCancellation_AndProgressWriteRecovery_Work()
        {
            yield return Prepare(3);
            saveFixture = new SaveFixture(ResolveService<ISaveService>());
            state = CreateState(saveFixture);
            yield return Enter(false);
            state.SelectSong("test-01");
            yield return WaitUntil("第一曲准备完成", () => !state.IsSongMenu, 5f);
            saveFixture.GateCalibration = true;
            yield return Step("开始第一曲并停在校准档案保存", state.StartRound, 0f);
            yield return WaitUntil("第一次保存等待已建立", () => saveFixture.CalibrationRequests == 1, 5f);
            Assert.That(state.IsPlaying, Is.False);
            state.ShowSongMenu();
            yield return WaitUntil("等待保存时仍可取消回选曲", () => state.IsSongMenu, 5f);
            state.SelectSong("test-03");
            yield return WaitUntil("取消后换曲完成", () => !state.IsSongMenu && state.SelectedSongId == "test-03", 5f);
            state.StartRound();
            yield return WaitUntil("第二曲保存等待已建立", () => saveFixture.CalibrationRequests == 2, 5f);
            yield return Step("只放行旧曲保存，不得恢复旧曲或解除新曲等待", () => saveFixture.ReleaseCalibration(0), 0f);
            yield return WaitUntil("旧曲真实保存已返回", () => saveFixture.CompletedCalibrationWrites == 1, 5f);
            Assert.That(state.IsPlaying, Is.False);
            state.StartRound();
            Assert.That(saveFixture.CalibrationRequests, Is.EqualTo(2), "旧请求 finally 不能清除新请求 starting，重复开始应被拒绝");
            Assert.That(state.SelectedSongId, Is.EqualTo("test-03"));
            int completed = 0;
            state.OnRunFinished += result => { if (result.Completion == RhythmRunCompletion.Completed) completed++; };
            saveFixture.FailNextProgress = true;
            ExpectErrorLogs("仅注入一次进度保存故障", message => message.Contains("rhythm/progress_save_failed") && message.Contains("library-fixture-progress-write"));
            yield return Step("放行新曲保存，只允许启动当前一轮", () => saveFixture.ReleaseCalibration(1), 0f);
            yield return WaitUntil("新曲播放开始", () => state.IsPlaying, 5f);
            saveFixture.GateCalibration = false;
            yield return WaitUntil("新曲自然完成并触发一次故障", () => !state.IsPlaying && completed == 1 && saveFixture.FailedProgressWrites == 1, 6f);
            var finished = state.LastRunResult;
            Assert.That(finished.SongId, Is.EqualTo("test-03"));
            Assert.That(completed, Is.EqualTo(1));
            yield return Check("成绩保存失败有通知，内存完整局仍存在", () =>
                NotificationTitle() == "成绩未保存" && state.LastRunResult.RunId == finished.RunId, 5f);
            yield return Snapshot("保存失败通知与完整局保留");
            RhythmProgressData saved = null;
            yield return ReadSaved(value => saved = value);
            Assert.That(saved.Records[catalog.Find("test-03").RecordKey].BestScoreRun.RunId, Is.EqualTo(finished.RunId));
            Assert.That(saveFixture.FailedProgressWrites, Is.EqualTo(1));
            state.SelectSong("test-03");
            yield return WaitUntil("恢复后重入显示完整纪录", () => !state.IsSongMenu, 5f);
            yield return Snapshot("退出重写成功且重入恢复纪录");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ResultCallbacks_ReentrantMenuCalibrationAndRestart_RemainCurrent()
        {
            yield return Prepare(3);
            yield return Enter();
            state.SelectSong("test-01");
            yield return WaitUntil("回调验证短谱准备完成", () => !state.IsSongMenu, 5f);
            bool completedWasStopped = false;
            bool completedWasPublished = false;
            Action<RhythmRunResult> completedHandler = result =>
            {
                if (result.Completion != RhythmRunCompletion.Completed) return;
                completedWasStopped = !state.IsPlaying;
                completedWasPublished = state.LastRunResult == result;
            };
            state.OnRunFinished += completedHandler;
            yield return Step("自然结算后通知调用方", state.StartRound, 0f);
            yield return WaitUntil("短谱开始", () => state.IsPlaying, 5f);
            yield return WaitUntil("结算回调已看到停止和发布后的同局结果", () => !state.IsPlaying && completedWasStopped && completedWasPublished, 6f);
            state.OnRunFinished -= completedHandler;
            int menuCallbacks = 0;
            Action<RhythmRunResult> menuHandler = result =>
            {
                if (result.Completion != RhythmRunCompletion.Aborted) return;
                menuCallbacks++;
                state.ShowSongMenu();
            };
            state.OnRunFinished += menuHandler;
            state.StartRound();
            yield return WaitUntil("校准前歌曲正在播放", () => state.IsPlaying, 5f);
            yield return Step("歌曲停止回调返回选曲，旧校准请求必须失效", state.StartCalibration, 0f);
            yield return Check("选曲页仍可见且没有参考拍播放", () => menuCallbacks == 1 && state.IsSongMenu && !state.IsPlaying);
            state.OnRunFinished -= menuHandler;
            yield return Snapshot("停止回调选曲阻止陈旧校准启动");
            state.SelectSong("test-03");
            yield return WaitUntil("新曲准备完成", () => !state.IsSongMenu && state.SelectedSongId == "test-03", 5f);
            state.StartRound();
            yield return WaitUntil("新曲播放开始", () => state.IsPlaying, 5f);
            int restartCallbacks = 0;
            Action<RhythmRunResult> restartHandler = result =>
            {
                if (result.Completion != RhythmRunCompletion.Aborted) return;
                restartCallbacks++;
                state.StartRound();
            };
            state.OnRunFinished += restartHandler;
            yield return Step("中断回调立即重开，新轮提示不得被旧中断覆盖", () =>
                typeof(RhythmState).GetMethod("FocusLost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, null), 0f);
            yield return WaitUntil("回调重开已排程当前曲目", () => state.IsPlaying && restartCallbacks == 1, 5f);
            Assert.That(FindDeep<TMP_Text>(ResolveService<IUIService>().Get<RhythmView>().transform, "Status").text, Does.Not.Contain("失焦"));
            state.OnRunFinished -= restartHandler;
            yield return Snapshot("中断回调重开保持新轮状态");
        }

        private string NotificationTitle()
        {
            var view = ResolveService<IUIService>().Get<NotificationView>();
            if (view == null || !view.IsCardShown) return null;
            return ((TMP_Text)typeof(NotificationView).GetField("titleLabel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view)).text;
        }

        private RhythmState CreateState(ISaveService saveOverride = null)
        {
            var value = new RhythmState(catalog.Song(0).Chart, ResolveService<IUIService>(), ResolveService<IAudioService>(),
                saveOverride ?? ResolveService<ISaveService>(), ResolveService<IGameFlow>(), ResolveService<IInputService>(),
                ResolveService<IWorldPauseService>(), ResolveService<ITelemetryService>(), ResolveService<INotificationService>());
            value.ConfigureCatalog(catalog); return value;
        }
        private IEnumerator Enter(bool create = true)
        {
            if (create && state == null) state = CreateState();
            yield return state.EnterAsync(CancellationToken.None).ToCoroutine();
            host = Object.FindObjectOfType<GameBootstrap>(); tick = host.StartCoroutine(TickState());
        }
        private IEnumerator Leave()
        {
            if (host != null && tick != null) host.StopCoroutine(tick);
            tick = null;
            if (state != null) yield return state.ExitAsync(CancellationToken.None).ToCoroutine();
        }
        private IEnumerator TickState() { while (state != null) { state.Tick(); yield return null; } }
        private IEnumerator ReadSaved(Action<RhythmProgressData> receive)
        {
            // Exit 等待串行写队列；真实档案读取不能靠固定帧数猜写盘结束。
            yield return Leave();
            yield return ResolveService<ISaveService>().ReadProfileAsync<RhythmProgressData>("rhythm-progress").ContinueWith(receive).ToCoroutine();
            yield return Enter(false);
        }
        private static void Set(object value, string name, object fieldValue) =>
            value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, fieldValue);

        private sealed class PermissionFixture : IRhythmEntryPermission
        {
            internal bool Tamed { get; set; }
            internal bool Controlled { get; set; }
            public RhythmEntryAccess Capture(string contextId) => new RhythmEntryAccess(contextId == "controlled-performer", Tamed, Controlled);
        }
        // 仅验证调用方策略接线，不宣称实际战斗成功标准或伤害量。
        private sealed class SuccessFixture : IRhythmCombatPolicy
        { public bool IsSuccess(RhythmRunResult result) => result.Completion == RhythmRunCompletion.Completed; }

        // 所有成功读写仍委托真实隔离服务；只延迟两次校准保存并抛出一个可识别的进度错误。
        private sealed class SaveFixture : ISaveService
        {
            private readonly ISaveService inner;
            private readonly List<UniTaskCompletionSource> calibrationGates = new List<UniTaskCompletionSource>();
            internal bool GateCalibration { get; set; }
            internal bool FailNextProgress { get; set; }
            internal int CalibrationRequests => calibrationGates.Count;
            internal int CompletedCalibrationWrites { get; private set; }
            internal int FailedProgressWrites { get; private set; }
            internal SaveFixture(ISaveService inner) { this.inner = inner; }
            internal void ReleaseCalibration(int index) => calibrationGates[index].TrySetResult();
            internal void ReleaseAll() { GateCalibration = false; foreach (var gate in calibrationGates) gate.TrySetResult(); }
            public async UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class
            {
                bool gated = GateCalibration && name == "rhythm-calibration";
                if (gated)
                {
                    var gate = new UniTaskCompletionSource(); calibrationGates.Add(gate);
                    await gate.Task;
                }
                if (name == "rhythm-progress" && FailNextProgress)
                {
                    FailNextProgress = false; FailedProgressWrites++;
                    throw new IOException("library-fixture-progress-write");
                }
                await inner.WriteProfileAsync(name, data, ct);
                if (gated) CompletedCalibrationWrites++;
            }
            public T Get<T>() where T : class, ISaveData, new() => inner.Get<T>();
            public UniTask<bool> SaveAsync(int slot, CancellationToken ct = default) => inner.SaveAsync(slot, ct);
            public UniTask<bool> LoadAsync(int slot, CancellationToken ct = default) => inner.LoadAsync(slot, ct);
            public UniTask<SaveSnapshot> ReadCandidateAsync(int slot, CancellationToken ct = default) => inner.ReadCandidateAsync(slot, ct);
            public SaveSnapshot Capture() => inner.Capture();
            public void Commit(SaveSnapshot snapshot) => inner.Commit(snapshot);
            public void ResetAll() => inner.ResetAll();
            public UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new() => inner.ReadProfileAsync<T>(name, ct);
            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default) where T : class, new() => inner.ReadProfileAsync(name, validate, ct);
            public bool Exists(int slot) => inner.Exists(slot);
            public void Delete(int slot) => inner.Delete(slot);
        }

        [UnityTearDown]
        public IEnumerator CleanupIsolation()
        {
            if (saveFixture != null) saveFixture.ReleaseAll();
            yield return Leave();
            if (state != null) { state.Dispose(); state = null; }
            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            saveFixture = null;
            yield return null;
        }
    }
}
