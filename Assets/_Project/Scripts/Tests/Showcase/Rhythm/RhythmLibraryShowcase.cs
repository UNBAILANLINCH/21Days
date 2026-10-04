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
        public IEnumerator Pages_RenderAtRequestedAndWideAspect_WithoutOldControls()
        {
            yield return Prepare(3);
            yield return Enter();
            var view = ResolveService<IUIService>().Get<RhythmView>();
            var library = FindDeep<RectTransform>(view.transform, "SongSelection");
            Assert.That(Object.FindObjectsOfType<RhythmView>().Length, Is.EqualTo(1));
            Assert.That(view.GetComponentsInChildren<Canvas>(true).Length, Is.EqualTo(0));
            yield return Check("曲库只显示一套开始入口且不露出演奏HUD", () =>
                FindDeep<Button>(view.transform, "LibraryStart").gameObject.activeInHierarchy &&
                !FindDeep<Button>(view.transform, "StartButton").gameObject.activeInHierarchy &&
                !FindDeep<TMP_Text>(view.transform, "Score").gameObject.activeInHierarchy &&
                library.GetComponent<Image>().color.a == 1);
            RenderPage(view, 1227, 710, "library");
            RenderPage(view, 1920, 1080, "library");
            RenderPage(view, 1280, 800, "library");
            FindDeep<Button>(view.transform, "LibrarySettings").onClick.Invoke();
            yield return Check("设置默认隐藏开发工具", () => !FindDeep<Button>(view.transform, "DiagnosticExportButton").gameObject.activeInHierarchy);
            RenderPage(view, 1227, 710, "settings");
            RenderPage(view, 1920, 1080, "settings");
            FindDeep<Button>(view.transform, "SettingsBack").onClick.Invoke();
            FindDeep<Button>(view.transform, "LibraryStart").onClick.Invoke();
            yield return WaitUntil("演奏启动", () => state.IsPlaying, 5f);
            state.PauseRound();
            RenderPage(view, 1227, 710, "pause");
            state.ResumeRound();
            yield return WaitUntil("演奏完成", () => !state.IsPlaying, 5f);
            yield return Check("结算只有重试和曲库导航", () =>
                !FindDeep<Button>(view.transform, "SettingsButton").gameObject.activeInHierarchy &&
                FindDeep<Button>(view.transform, "SongMenuButton").gameObject.activeInHierarchy &&
                !FindDeep<RectTransform>(view.transform, "JudgementLine").gameObject.activeInHierarchy &&
                !FindDeep<TMP_Text>(view.transform, "KeyLabel0").gameObject.activeInHierarchy);
            RenderPage(view, 1227, 710, "results");
            FindDeep<Button>(view.transform, "StartButton").onClick.Invoke();
            yield return WaitUntil("中断页检查重开", () => state.IsPlaying, 5f);
            typeof(RhythmState).GetMethod("FocusLost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, null);
            yield return Check("失焦中止仍只提供重试与返回选曲", () => !state.IsPlaying &&
                !FindDeep<Button>(view.transform, "SettingsButton").gameObject.activeInHierarchy &&
                !FindDeep<Button>(view.transform, "BackButton").gameObject.activeInHierarchy &&
                !FindDeep<TMP_Text>(view.transform, "KeyLabel0").gameObject.activeInHierarchy &&
                FindDeep<Button>(view.transform, "StartButton").gameObject.activeInHierarchy &&
                FindDeep<Button>(view.transform, "SongMenuButton").gameObject.activeInHierarchy);
            RenderPage(view, 1227, 710, "interrupted");
            yield return Snapshot("页面显隐与实际比例渲染");
        }

        private void RenderPage(RhythmView view, int width, int height, string page)
        {
            // 只改测试中的运行时 Canvas，实际按目标像素布局并渲染；不改 GameView/EditorPrefs 或任何资产。
            var canvas = view.GetComponentInParent<Canvas>().rootCanvas;
            var scaler = canvas.GetComponent<CanvasScaler>();
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
            float oldDistance = canvas.planeDistance, oldScale = canvas.scaleFactor;
            bool scalerEnabled = scaler != null && scaler.enabled;
            var cameraObject = new GameObject("RhythmPageCaptureCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var oldTarget = RenderTexture.active;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .05f, .08f);
                camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 10;
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                if (scaler != null)
                {
                    scaler.enabled = false;
                    var reference = scaler.referenceResolution;
                    canvas.scaleFactor = Mathf.Pow(2, Mathf.Lerp(Mathf.Log(width / reference.x, 2),
                        Mathf.Log(height / reference.y, 2), scaler.matchWidthOrHeight));
                }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                string folder = Path.Combine("Logs", "verify", "rhythm-ui-20261006");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, width + "x" + height + "-" + page + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = oldTarget; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance; canvas.scaleFactor = oldScale;
                if (scaler != null) scaler.enabled = scalerEnabled;
                Object.Destroy(cameraObject); Object.Destroy(target); Object.Destroy(texture);
                Canvas.ForceUpdateCanvases();
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PauseHold_RealSourceAndInput_KeepOrReleaseSameRun()
        {
            yield return Prepare(3);
            JsonUtility.FromJsonOverwrite("{\"durationSeconds\":2.8,\"notes\":[{\"id\":\"pause-hold\",\"lane\":0,\"timeMs\":300,\"type\":1,\"durationMs\":2000}]}", catalog.Song(0).Chart);
            yield return Enter();
            var view = ResolveService<IUIService>().Get<RhythmView>();
            FindDeep<Button>(view.transform, "LibraryStart").onClick.Invoke();
            yield return WaitUntil("长按头部", () => state.IsPlaying && state.SongSeconds >= .3, 5f);
            yield return Input.Hold(state.LaneAction(0));
            yield return Check("实际键盘抓住长按", () => state.Rules.IsHolding(0), 1f);
            state.PauseRound();
            double paused = state.SongSeconds;
            int score = state.Rules.Score;
            yield return Wait(3.2f);
            yield return Check("跨原预约终点仍冻结长按和成绩", () => state.IsPaused && state.SongSeconds == paused &&
                state.Rules.IsHolding(0) && state.Rules.CompletedHolds == 0 && state.Rules.Score == score && state.Rules.Miss == 0);
            state.ResumeRound();
            for (int i = 0; i < 20; i++) { state.PauseRound(); state.ResumeRound(); yield return null; }
            yield return WaitUntil("恢复后长按尾部完成", () => state.Rules.CompletedHolds == 1, 4f);
            yield return Input.Release(state.LaneAction(0));
            yield return WaitUntil("同局结算", () => !state.IsPlaying, 3f);
            yield return Check("二十次暂停恢复保留同局无漏判", () => state.Rules.CompletedHolds == 1 && state.Rules.Miss == 0);
            yield return Snapshot("长按保持恢复后结算");
            FindDeep<Button>(view.transform, "StartButton").onClick.Invoke();
            yield return WaitUntil("重试长按头部", () => state.IsPlaying && state.SongSeconds >= .3, 5f);
            yield return Input.Hold(state.LaneAction(0));
            yield return Check("重试抓住长按", () => state.Rules.IsHolding(0), 1f);
            state.PauseRound();
            yield return Input.Release(state.LaneAction(0));
            Assert.That(state.Rules.Miss, Is.Zero, "暂停中释放不立即判 Miss");
            state.ResumeRound();
            yield return Check("暂停中松开恢复后只判一次", () => state.Rules.Miss == 1 && !state.Rules.IsHolding(0), 2f);
            yield return WaitUntil("早放本局结束", () => !state.IsPlaying, 5f);
            Assert.That(state.Rules.Miss, Is.EqualTo(1));
            yield return Snapshot("暂停松开恢复仅一次释放判定");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SelectionLoading_KeepsLibraryAndRowsAcrossClicksAndReentry()
        {
            yield return Prepare(3);
            var seed = new RhythmProgressData();
            seed.UnlockedSongs.Add("test-02"); seed.UnlockedSongs.Add("test-03");
            yield return ResolveService<ISaveService>().WriteProfileAsync("rhythm-progress", seed).ToCoroutine();
            yield return Enter();
            var view = ResolveService<IUIService>().Get<RhythmView>();
            var library = FindDeep<RectTransform>(view.transform, "SongSelection").gameObject;
            var row = FindDeep<Button>(view.transform, "Song_test-01");
            int rowId = row.GetInstanceID();
            view.PreparingSelection();
            Assert.That(library.activeSelf, Is.True, "加载入口不得隐藏曲库");
            Assert.That(state.IsPlaying, Is.False);
            view.ShowSongMenu(catalog, seed);
            for (int i = 0; i < 20; i++)
            {
                FindDeep<Button>(view.transform, "Song_test-0" + (i % 3 + 1)).onClick.Invoke();
                FindDeep<Button>(view.transform, "Song_test-0" + ((i + 1) % 3 + 1)).onClick.Invoke();
                FindDeep<Button>(view.transform, "Song_test-0" + (i % 3 + 1)).onClick.Invoke();
                Assert.That(library.activeSelf, Is.True, "点击当帧曲库必须仍然显示");
                Assert.That(state.IsPlaying, Is.False);
                yield return WaitUntil("曲目准备完成", () => !state.IsSongMenu, 5f);
                Assert.That(library.activeSelf, Is.True);
                Assert.That(FindDeep<Button>(view.transform, "Song_test-01").GetInstanceID(), Is.EqualTo(rowId));
                Assert.That(FindDeep<TMP_Text>(view.transform, "SongDetails").text,
                    Does.Contain(catalog.Find(state.SelectedSongId).Title), "被拒绝的点击不能覆盖已提交详情");
            }
            yield return Step("开始后才进入演奏", () => FindDeep<Button>(view.transform, "LibraryStart").onClick.Invoke(), 0f);
            yield return WaitUntil("实际演奏启动", () => state.IsPlaying, 5f);
            Assert.That(library.activeSelf, Is.False);
            state.ShowSongMenu();
            Assert.That(library.activeSelf, Is.True);
            yield return Leave();
            yield return Enter(false);
            view = ResolveService<IUIService>().Get<RhythmView>();
            Assert.That(view.GetComponentsInChildren<ScrollRect>(true).Length, Is.EqualTo(1));
            var subscribers = typeof(RhythmView).GetField("OnSongSelected", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view) as Delegate;
            Assert.That(subscribers?.GetInvocationList().Length, Is.EqualTo(1), "重入仅注册一个选曲订阅");
            Assert.That(FindDeep<RectTransform>(view.transform, "SongSelection").gameObject.activeSelf, Is.True);
            yield return Snapshot("选曲连续点击与重入曲库");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LibraryStart_SettingsDraftAndManualPractice_AreSeparated()
        {
            yield return Prepare(3);
            yield return Enter();
            var view = ResolveService<IUIService>().Get<RhythmView>();
            var library = FindDeep<RectTransform>(view.transform, "SongSelection").gameObject;
            yield return Check("曲库详情提供开始，测试和诊断默认隐藏", () =>
                library.activeSelf && FindDeep<Button>(view.transform, "LibraryStart").gameObject.activeInHierarchy &&
                !FindDeep<Button>(view.transform, "DiagnosticExportButton").gameObject.activeInHierarchy);
            yield return Step("从曲库详情直接开始首曲", () => FindDeep<Button>(view.transform, "LibraryStart").onClick.Invoke(), 0f);
            yield return WaitUntil("详情入口启动演奏", () => state.IsPlaying, 5f);
            yield return Check("演奏隐藏设置并提供退出", () => !library.activeSelf &&
                !FindDeep<Button>(view.transform, "SettingsButton").gameObject.activeInHierarchy &&
                FindDeep<Button>(view.transform, "PlayExit").gameObject.activeInHierarchy);
            yield return Step("打开暂停菜单并退出返回曲库", () =>
            {
                FindDeep<Button>(view.transform, "PlayExit").onClick.Invoke();
                FindDeep<Button>(view.transform, "PauseExit").onClick.Invoke();
            });
            yield return Check("返回保留曲目详情", () => library.activeSelf && !state.IsPlaying);
            yield return Step("打开统一延迟校准并调整未保存值", () =>
            {
                FindDeep<Button>(view.transform, "LibrarySettings").onClick.Invoke();
                FindDeep<Slider>(view.transform, "Offset").value = 120;
            });
            yield return Step("取消保留原值", () => FindDeep<Button>(view.transform, "SettingsBack").onClick.Invoke());
            yield return Check("取消恢复原补偿", () => FindDeep<Slider>(view.transform, "Offset").value == 0);
            yield return Step("手动校准复用教学曲试听试打", () =>
            {
                FindDeep<Button>(view.transform, "LibrarySettings").onClick.Invoke();
                FindDeep<Button>(view.transform, "ManualCalibration").onClick.Invoke();
            }, 0f);
            yield return WaitUntil("手动试听开始", () => state.IsPlaying, 5f);
            yield return WaitUntil("试听第一拍", () => state.SongSeconds >= 0.3, 3f);
            yield return Input.Press(state.LaneAction(0));
            yield return WaitUntil("试听第二拍", () => state.SongSeconds >= 0.7, 3f);
            yield return Input.Press(state.LaneAction(1));
            yield return WaitUntil("手动试听自然结束", () => !state.IsPlaying && state.LastRunResult != null, 6f);
            yield return Check("试听满分结束仍不计成绩或解锁", () => state.LastRunResult.Mode == RhythmPlayMode.Practice &&
                state.Rules.Perfect + state.Rules.Good == 2 && state.Rules.Miss == 0 &&
                state.BestScore("test-01") == 0 && !state.IsUnlocked("test-02"));
            yield return Step("取消手动校准返回曲库", () => FindDeep<Button>(view.transform, "ManualCancel").onClick.Invoke());
            yield return Snapshot("曲库与校准资格分离");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PauseContinue_FreezesRoundAndKeepsSameRun()
        {
            yield return Prepare(3);
            yield return Enter();
            var view = ResolveService<IUIService>().Get<RhythmView>();
            FindDeep<Button>(view.transform, "LibraryStart").onClick.Invoke();
            yield return WaitUntil("演奏倒数开始", () => state.IsPlaying, 5f);
            yield return Step("倒数阶段暂停", state.PauseRound, 0f);
            double seconds = state.SongSeconds;
            int miss = state.Rules.Miss;
            yield return Wait(2f);
            yield return Check("暂停期间歌曲时刻与判定冻结", () => state.IsPaused && state.SongSeconds == seconds && state.Rules.Miss == miss);
            yield return Snapshot("暂停菜单继续重试退出");
            yield return Step("继续同一局", state.ResumeRound, 0f);
            yield return Check("继续不重置本局判定", () => state.IsPlaying && !state.IsPaused && state.Rules.Miss == miss);
            yield return WaitUntil("同一局自然结束", () => !state.IsPlaying && state.LastRunResult != null, 8f);
            yield return Check("已暂停局完成但诊断明确不完整", () => state.LastRunResult.Completion == RhythmRunCompletion.Completed &&
                state.LastDiagnostic.Commands[state.LastDiagnostic.Commands.Count - 1].EndReason == RhythmDiagnosticData.Reason.AudioPaused);
            yield return Snapshot("继续后的结算");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SettingsSave_FailureCancelAndSlowDuplicate_KeepTransaction()
        {
            yield return Prepare(3);
            var saves = ResolveService<ISaveService>();
            yield return saves.WriteProfileAsync("rhythm-calibration", new RhythmCalibrationData { OffsetMs = 40, VisualOffsetMs = 20 }).ToCoroutine();
            saveFixture = new SaveFixture(saves); state = CreateState(saveFixture); yield return Enter(false);
            var view = ResolveService<IUIService>().Get<RhythmView>();
            FindDeep<Button>(view.transform, "LibrarySettings").onClick.Invoke();
            FindDeep<Slider>(view.transform, "Offset").value = 90;
            saveFixture.FailNextCalibration = true;
            ExpectErrorLogs("设置故意一次Profile写失败", message => message.Contains("rhythm/calibration_save_failed") && message.Contains("library-fixture-calibration-write"));
            yield return Step("保存失败保持旧内存与旧档", () => FindDeep<Button>(view.transform, "SettingsSave").onClick.Invoke(), 0f);
            yield return WaitUntil("设置保存失败返回", () => saveFixture.FailedCalibrationWrites == 1, 5f);
            RhythmCalibrationData disk = null;
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(value => disk = value).ToCoroutine();
            yield return Check("失败后仍为原值", () => disk.OffsetMs == 40 &&
                ((RhythmCalibrationData)typeof(RhythmState).GetField("calibration", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state)).OffsetMs == 40);
            FindDeep<Button>(view.transform, "SettingsBack").onClick.Invoke();
            yield return Check("取消恢复滑块", () => FindDeep<Slider>(view.transform, "Offset").value == 40);
            FindDeep<Button>(view.transform, "LibrarySettings").onClick.Invoke();
            FindDeep<Slider>(view.transform, "Offset").value = 110; saveFixture.GateCalibration = true;
            FindDeep<Button>(view.transform, "SettingsSave").onClick.Invoke();
            yield return WaitUntil("设置慢写已建立", () => saveFixture.CalibrationRequests == 1, 5f);
            FindDeep<Button>(view.transform, "SettingsSave").onClick.Invoke();
            FindDeep<Button>(view.transform, "SettingsBack").onClick.Invoke();
            state.StartRound(); state.ShowSongMenu(); state.SelectSong("test-03");
            yield return Check("慢写重复点不并发也不取消草稿", () => saveFixture.CalibrationRequests == 1 &&
                FindDeep<Slider>(view.transform, "Offset").value == 110 && !state.IsPlaying);
            saveFixture.GateCalibration = false; saveFixture.ReleaseCalibration(0);
            yield return WaitUntil("设置保存成功返回曲库", () => state.IsSongMenu && FindDeep<Button>(view.transform, "SettingsButton").interactable, 5f);
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(value => disk = value).ToCoroutine();
            yield return Check("保存同一快照值", () => disk.OffsetMs == 110 && disk.VisualOffsetMs == 20);
            yield return Snapshot("设置事务保存与取消");
        }

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

        [UnityTest, Timeout(90000)]
        public IEnumerator FixedChartCombat_RealInputSuccessFailureAndAbort_Work()
        {
            yield return Prepare(3);
            yield return Enter();
            var song = catalog.Song(0);
            int success = 0, failure = 0, aborted = 0;
            var permission = new PermissionFixture { Tamed = true, Controlled = true };
            var policy = new RhythmFixedChartCombatPolicy(song.Id, song.Chart.ChartId, song.Revision, song.RulesetId, song.ScoringVersion);
            var consumer = new RhythmExternalSession(permission, policy, _ => success++, _ => failure++, _ => aborted++);
            state.SelectSong(song.Id);
            yield return WaitUntil("固定谱准备完成", () => !state.IsSongMenu, 5f);
            state.ConfigureExternal(new RhythmPlayRequest("fixed-fail", "controlled-performer", song.Id, RhythmPlayMode.Combat), consumer);
            state.StartRound();
            yield return WaitUntil("固定谱低分局开始", () => state.IsPlaying, 5f);
            yield return WaitUntil("固定谱自然低分完成", () => !state.IsPlaying, 6f);
            Assert.That(failure, Is.EqualTo(1)); Assert.That(success, Is.Zero);
            state.ConfigureExternal(new RhythmPlayRequest("fixed-success", "controlled-performer", song.Id, RhythmPlayMode.Combat), consumer);
            state.StartRound();
            yield return WaitUntil("固定谱输入局开始", () => state.IsPlaying, 5f);
            for (int i = 0; i < 2; i++)
            {
                int note = i;
                yield return WaitUntil("固定谱真实按键 " + i, () => state.SongSeconds >= state.Rules.NoteTime(note), 5f);
                yield return Input.Press(state.LaneAction(state.Rules.NoteLane(i)));
            }
            yield return WaitUntil("固定谱达标完成", () => !state.IsPlaying, 6f);
            Assert.That(success, Is.EqualTo(1)); Assert.That(failure, Is.EqualTo(1));
            Assert.That(policy.Matches(state.LastRunResult), Is.True);
            Assert.That(state.LastRunResult.Score, Is.GreaterThanOrEqualTo(1200));
            consumer.Consume(state.LastRunResult); Assert.That(success, Is.EqualTo(1));
            state.ConfigureExternal(new RhythmPlayRequest("fixed-abort", "controlled-performer", song.Id, RhythmPlayMode.Combat), consumer);
            state.StartRound(); yield return WaitUntil("固定谱取消局开始", () => state.IsPlaying, 5f);
            typeof(RhythmState).GetMethod("FocusLost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, null);
            Assert.That(aborted, Is.EqualTo(1)); Assert.That(failure, Is.EqualTo(1));
            RhythmProgressData saved = null; yield return ReadSaved(value => saved = value);
            Assert.That(saved.Records.Count, Is.Zero);
            yield return Snapshot("固定谱真实输入成功失败取消不写自由纪录");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator CalibrationApply_ProfileFailureRetryKeepAndSlowExit_Work()
        {
            yield return Prepare(3);
            var saves = ResolveService<ISaveService>();
            yield return saves.WriteProfileAsync("rhythm-calibration", new RhythmCalibrationData { OffsetMs = 140, VisualOffsetMs = 125 }).ToCoroutine();
            saveFixture = new SaveFixture(saves);
            state = CreateState(saveFixture); yield return Enter(false);
            state.SelectSong("test-01"); yield return WaitUntil("采用事务曲目准备", () => !state.IsSongMenu, 5f);
            var view = ResolveService<IUIService>().Get<RhythmView>();
            // 只验证采用与真实Profile IO；候选是显式fixture，不冒充新真人/32拍采样。
            SeedCalibrationCandidate(80);
            saveFixture.FailNextCalibration = true;
            ExpectErrorLogs("明确采用故意一次Profile写失败", message => message.Contains("rhythm/calibration_save_failed") && message.Contains("library-fixture-calibration-write"));
            FindDeep<Button>(view.transform, "CalibrationApply").onClick.Invoke();
            yield return WaitUntil("Profile采用失败已返回", () => saveFixture.FailedCalibrationWrites == 1, 5f);
            Assert.That(state.CalibrationCandidate.OffsetMs, Is.EqualTo(80));
            Assert.That(FindDeep<Slider>(view.transform, "Offset").value, Is.EqualTo(140));
            var memory = (RhythmCalibrationData)typeof(RhythmState).GetField("calibration", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
            Assert.That(memory.OffsetMs, Is.EqualTo(140));
            RhythmCalibrationData disk = null;
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(value => disk = value).ToCoroutine();
            Assert.That(disk.OffsetMs, Is.EqualTo(140)); Assert.That(disk.VisualOffsetMs, Is.EqualTo(125));
            string text = FindDeep<TMP_Text>(view.transform, "CalibrationResultText").text;
            Assert.That(text, Does.Contain("采用未保存").And.Contain("原补偿保持"));
            Assert.That(text, Does.Not.Contain("已采用并保存"));
            yield return Snapshot("Profile失败保留旧内存旧档与可重试候选");
            FindDeep<Button>(view.transform, "CalibrationApply").onClick.Invoke();
            yield return WaitUntil("同一候选重试成功", () => state.CalibrationCandidate == null, 5f);
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(value => disk = value).ToCoroutine();
            Assert.That(disk.OffsetMs, Is.EqualTo(80)); Assert.That(memory.OffsetMs, Is.EqualTo(80));
            SeedCalibrationCandidate(120); saveFixture.FailNextCalibration = true;
            ExpectErrorLogs("第二次Profile故障后保留", message => message.Contains("rhythm/calibration_save_failed") && message.Contains("library-fixture-calibration-write"));
            FindDeep<Button>(view.transform, "CalibrationApply").onClick.Invoke();
            yield return WaitUntil("第二次失败可取消", () => saveFixture.FailedCalibrationWrites == 2, 5f);
            FindDeep<Button>(view.transform, "CalibrationKeep").onClick.Invoke();
            Assert.That(state.CalibrationCandidate, Is.Null); Assert.That(memory.OffsetMs, Is.EqualTo(80));
            yield return Leave(); yield return Enter(false);
            view = ResolveService<IUIService>().Get<RhythmView>();
            Assert.That(FindDeep<Slider>(view.transform, "Offset").value, Is.EqualTo(80));
            SeedCalibrationCandidate(90); saveFixture.GateCalibration = true;
            FindDeep<Button>(view.transform, "CalibrationApply").onClick.Invoke();
            yield return WaitUntil("慢写已建立", () => saveFixture.CalibrationRequests == 1, 5f);
            FindDeep<Button>(view.transform, "CalibrationApply").onClick.Invoke();
            FindDeep<Button>(view.transform, "CalibrationPreviewCandidate").onClick.Invoke();
            FindDeep<Button>(view.transform, "CalibrationKeep").onClick.Invoke();
            state.StartRound(); state.StartCalibration(); state.ShowSongMenu(); state.SelectSong("test-03");
            typeof(RhythmState).GetMethod("FocusLost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, null);
            Assert.That(state.IsPlaying, Is.False); Assert.That(state.SelectedSongId, Is.EqualTo("test-01"));
            Assert.That(saveFixture.CalibrationRequests, Is.EqualTo(1)); Assert.That(state.CalibrationCandidate.OffsetMs, Is.EqualTo(90));
            Assert.That(FindDeep<Slider>(view.transform, "Offset").interactable, Is.False);
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(value => disk = value).ToCoroutine();
            Assert.That(disk.OffsetMs, Is.EqualTo(80));
            var exiting = state.ExitAsync(CancellationToken.None).AsTask();
            yield return null; Assert.That(exiting.IsCompleted, Is.False);
            saveFixture.GateCalibration = false; saveFixture.ReleaseCalibration(0);
            yield return WaitUntil("退出等待采用落盘后完成", () => exiting.IsCompleted, 5f);
            exiting.GetAwaiter().GetResult();
            if (host != null && tick != null) host.StopCoroutine(tick); tick = null;
            yield return Enter(false);
            view = ResolveService<IUIService>().Get<RhythmView>();
            Assert.That(FindDeep<Slider>(view.transform, "Offset").value, Is.EqualTo(90));
            Assert.That(FindDeep<Slider>(view.transform, "VisualOffset").value, Is.EqualTo(125));
            yield return saves.ReadProfileAsync<RhythmCalibrationData>("rhythm-calibration").ContinueWith(value => disk = value).ToCoroutine();
            Assert.That(disk.OffsetMs, Is.EqualTo(90));
            yield return Snapshot("慢写单次采用退出后恢复同一持久值");
        }
        private void SeedCalibrationCandidate(double offset)
        {
            typeof(RhythmState).GetField("pendingCalibration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state,
                new RhythmCalibrationResult(RhythmCalibrationReason.Suggested, offset, 45, 45, 0, 32, 32, 32, 0, 0, 0, true));
            typeof(RhythmState).GetMethod("ShowCalibrationSuggestion", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, new object[] { null });
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
            internal bool FailNextCalibration { get; set; }
            internal int FailedCalibrationWrites { get; private set; }
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
                if (name == "rhythm-calibration" && FailNextCalibration)
                {
                    FailNextCalibration = false; FailedCalibrationWrites++;
                    throw new IOException("library-fixture-calibration-write");
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
