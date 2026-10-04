// 职责：四轨下落、操作界面和结果显示。既有面板没有轨道布局，扩展通用 UI 会让 Core 认识玩法。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using Game.Core.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Rhythm
{
    public sealed class RhythmView : UIView
    {
        [SerializeField] private RectTransform noteArea;
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private TMP_Text feedbackLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_Text offsetLabel;
        [SerializeField] private TMP_Text resultLabel;
        [SerializeField] private TMP_Text startLabel;
        [SerializeField] private Button startButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Slider offsetSlider;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Image[] laneLights;
        private RectTransform[] notes;
        private RhythmTrackGraphic[] noteImages;
        private Slider visualSlider;
        private TMP_Text visualLabel;
        private Button calibrationButton;
        private Button practiceButton;
        private Button diagnosticButton;
        private TMP_Text heading;
        private TMP_Text subtitle;
        private TMP_Text instructions;
        private double roundDuration;
        private float visualOffsetMs;
        private int calibrationPhase = int.MinValue;
        private int calibrationSamples;
        private int calibrationProgress = int.MinValue;
        private string calibrationPreviewFeedback = "等待跟拍";
        private int calibrationPreviewRemaining = int.MinValue;
        private double calibrationPreviewShownOffset = double.NaN;
        private bool calibrationPreviewDirty;
        private readonly float[] lightRemaining = new float[4];
        private RhythmConfig config;
        private int countdown = int.MinValue;
        private bool playing;
        private int songNoteCount;
        private string songSummary;
        private string songInstructions;
        private string songTitle = "虫儿飞";
        private GameObject songMenu;
        private TMP_Text songMenuHeading;
        private Button songMenuButton;
        private readonly List<Button> songButtons = new List<Button>();
        private readonly Dictionary<GameObject, bool> libraryHiddenObjects = new Dictionary<GameObject, bool>();
        private RhythmCatalogConfig songMenuCatalog;
        private ScrollRect songScroll;
        private RectTransform songContent;
        private TMP_Text songDetails;
        private string menuSelectedId;
        private GameObject calibrationResult;
        private TMP_Text calibrationResultText;
        private Button calibrationApply;
        private Button calibrationCandidate;
        private Button calibrationSupplementButton;
        private Button libraryStart;
        private GameObject settingsPanel;
        private GameObject developerPanel;
        private Button settingsButton;
        private Button playExit;
        private bool settingsFromLibrary;
        private bool editingSettings;
        private bool settingsSaving;
        private bool referenceCalibration;
        private bool manualTrial;
        private float originalOffset;
        private float originalVisual;
        private GameObject manualPanel;
        private Slider manualOffsetSlider;
        private TMP_Text manualOffsetLabel;
        private TMP_Text manualFeedback;
        private GameObject pausePanel;
        public event Action OnStartClicked;
        public event Action OnLibraryStartClicked;
        public event Action OnManualCalibrationClicked;
        public event Action<float, float> OnSettingsSaved;
        public event Action OnSettingsCancelled;
        public event Action OnPauseClicked;
        public event Action OnResumeClicked;
        public event Action<string> OnSongSelected;
        public event Action OnSongMenuClicked;
        public event Action OnBackClicked;
        public event Action<float> OnOffsetChanged;
        public event Action OnFocusLost;
        public event Action OnApplicationSuspended;
        public event Action OnDiagnosticExportClicked;
        public event Action OnCalibrationClicked;
        public event Action OnPracticeClicked;
        public event Action<float> OnVisualOffsetChanged;
        public event Action OnCalibrationApply;
        public event Action OnCalibrationKeep;
        public event Action OnCalibrationSupplement;
        public event Action<bool> OnCalibrationPreview;
        public override UILayer Layer => UILayer.Panel;
        public override bool CloseOnCancel => false;
        public bool IsApplicationPaused { get; private set; }

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            config = arg as RhythmConfig;
            ExtraControls();
            editingSettings = false; manualTrial = false; settingsSaving = false;
            ShowPause(false);
            settingsPanel.SetActive(false); manualPanel.SetActive(false); developerPanel.SetActive(false);
            ConfigureSong(config, "虫儿飞", "调试谱");
            HideSongMenu();
            HideCalibrationResult();
            if (songMenuButton != null) songMenuButton.gameObject.SetActive(false);
            Hook(startButton, StartClicked);
            Hook(backButton, BackClicked);
            offsetSlider.onValueChanged.AddListener(OffsetChanged);
            visualSlider.onValueChanged.AddListener(VisualChanged);
            calibrationButton.onClick.AddListener(CalibrationClicked);
            practiceButton.onClick.AddListener(PracticeClicked);
            diagnosticButton.onClick.AddListener(DiagnosticExportClicked);
            return UniTask.CompletedTask;
        }
        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            Unhook(startButton, StartClicked);
            Unhook(backButton, BackClicked);
            offsetSlider.onValueChanged.RemoveListener(OffsetChanged);
            visualSlider.onValueChanged.RemoveListener(VisualChanged);
            calibrationButton.onClick.RemoveListener(CalibrationClicked);
            practiceButton.onClick.RemoveListener(PracticeClicked);
            diagnosticButton.onClick.RemoveListener(DiagnosticExportClicked);
            if (songMenuButton != null) songMenuButton.onClick.RemoveListener(SongMenuClicked);
            return UniTask.CompletedTask;
        }
        public void SetOffset(float value)
        {
            offsetSlider.SetValueWithoutNotify(value);
            offsetLabel.text = $"延迟补偿 {value:+0;-0;0} ms\n正值：补偿晚按　负值：补偿早按";
        }
        public void Ready(string message)
        {
            playing = false;
            offsetSlider.interactable = true;
            visualSlider.interactable = true;
            calibrationButton.interactable = true;
            practiceButton.interactable = true;
            startButton.interactable = true;
            startLabel.text = "开始演奏";
            heading.text = songTitle; subtitle.text = songSummary; instructions.text = songInstructions;
            practiceButton.GetComponentInChildren<TMP_Text>().text = "短 Tap/Hold 练习";
            statusLabel.text = message;
            feedbackLabel.text = "D / F / J / K";
            resultPanel.SetActive(false);
            SetPlayControls(false);
            if (manualTrial) ShowManualCalibration();
            if (noteImages != null) for (int i = 0; i < noteImages.Length; i++) noteImages[i].enabled = false;
        }
        public void Starting()
        {
            HideSongMenu();
            settingsPanel.SetActive(false);
            developerPanel.SetActive(false);
            SetPlayControls(true);
            resultPanel.SetActive(false); calibrationPhase = int.MinValue;
            playing = true; offsetSlider.interactable = false; visualSlider.interactable = false;
            diagnosticButton.interactable = false;
            calibrationButton.interactable = false; practiceButton.interactable = false; startButton.interactable = false; statusLabel.text = "准备演奏…";
        }
        public void PreparingSelection()
        {
            // 曲目准备仍属于曲库；不能用演奏启动路径隐藏当前页面。
            libraryStart.interactable = false;
            foreach (var button in songButtons) button.interactable = false;
            songDetails.text = "正在准备所选曲目…";
        }
        public void Begin(RhythmRules rules, double duration = -1, bool practice = false)
        {
            roundDuration = duration > 0 ? duration : config.DurationSeconds;
            if (notes == null || notes.Length != rules.Count)
            {
                if (notes != null) for (int i = 0; i < notes.Length; i++) Destroy(notes[i].gameObject);
                notes = new RectTransform[rules.Count];
                noteImages = new RhythmTrackGraphic[rules.Count];
                for (int i = 0; i < rules.Count; i++)
                {
                    var item = new GameObject("Note" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(RhythmTrackGraphic)).GetComponent<RhythmTrackGraphic>();
                    item.transform.SetParent(noteArea, false);
                    item.rectTransform.anchorMin = Vector2.zero; item.rectTransform.anchorMax = Vector2.one;
                    item.rectTransform.offsetMin = item.rectTransform.offsetMax = Vector2.zero;
                    item.rectTransform.pivot = new Vector2(0.5f, 0);
                    item.Configure(false);
                    item.color = LaneColor(rules.NoteLane(i));
                    noteImages[i] = item;
                    notes[i] = item.rectTransform;
                }
            }
            playing = true;
            SetPlayControls(true);
            for (int i = 0; i < 4; i++) lightRemaining[i] = float.NegativeInfinity;
            countdown = int.MinValue;
            offsetSlider.interactable = false;
            visualSlider.interactable = false;
            calibrationButton.interactable = false;
            practiceButton.interactable = true;
            startButton.interactable = true;
            resultPanel.SetActive(false);
            startLabel.text = practice ? $"切回{songTitle}" : $"重开{songTitle}";
            practiceButton.GetComponentInChildren<TMP_Text>().text = practice ? "重开 Tap/Hold 练习" : "短 Tap/Hold 练习";
            heading.text = practice ? "Tap / Hold 练习" : songTitle;
            subtitle.text = practice ? "独立参考拍 · 不混入歌曲谱面" : songSummary;
            instructions.text = practice ? "D、F 单击一次\nJ、K 长条按住到尾\n\n到尾自动成功\n提前松开会失败\n\n右侧可随时重开练习" : songInstructions;
            feedbackLabel.text = "准备";
            Score(rules);
        }
        public void Frame(double seconds, RhythmRules rules)
        {
            double displaySeconds = seconds - visualOffsetMs / 1000;
            for (int i = 0; i < notes.Length; i++)
            {
                double remaining = rules.NoteTime(i) - displaySeconds;
                double tail = rules.NoteEnd(i) - displaySeconds;
                bool holding = rules.IsHolding(i);
                bool visible = !rules.IsResolved(i) && remaining <= config.ApproachSeconds && (tail >= -0.5 || holding);
                if (noteImages[i].enabled != visible) noteImages[i].enabled = visible;
                if (visible) noteImages[i].Show(rules.NoteLane(i), holding ? 0 : RhythmTrackGraphic.PositionAt(rules.NoteTime(i), seconds, visualOffsetMs, config.ApproachSeconds),
                    RhythmTrackGraphic.PositionAt(rules.NoteEnd(i), seconds, visualOffsetMs, config.ApproachSeconds), rules.NoteType(i) == RhythmNoteType.Hold, LaneColor(rules.NoteLane(i)));
            }
            int next = seconds < 0 ? (int)GameMath.Ceil((float)-seconds) : 0;
            if (next != countdown)
            {
                countdown = next;
                statusLabel.text = next > 0 ? $"倒数 {next}" : "音符到达横线时按键";
            }
            progressSlider.SetValueWithoutNotify(GameMath.Clamp01((float)(seconds / roundDuration)));
            for (int i = 0; i < 4; i++)
            {
                // 视觉衰减只依赖歌曲时刻差值，不参与判定。
                laneLights[i].color = new Color(LaneColor(i).r, LaneColor(i).g, LaneColor(i).b,
                    seconds < lightRemaining[i] ? 0.9f : 0.14f);
            }
        }
        public void Flash(int lane, double seconds) { lightRemaining[lane] = (float)seconds + 0.12f; }
        public void Hit(RhythmHitResult result, RhythmRules rules)
        {
            if (result.Grade == RhythmGrade.None) return;
            if (manualTrial && result.Grade != RhythmGrade.Miss && !result.HoldStarted)
            {
                double error = result.RawErrorMs - offsetSlider.value;
                manualFeedback.text = $"本次{(error < 0 ? "早" : "晚")} {GameMath.Abs(error):0} ms\n当前试用 {offsetSlider.value:+0;-0;0} ms\n只试听试打，不计成绩";
            }
            if (result.HoldStarted) { feedbackLabel.color = LaneColor(0); feedbackLabel.text = $"头部 {result.Grade}\n继续按住至尾部"; return; }
            if (result.Grade == RhythmGrade.Miss)
            {
                Missed(rules);
                if (result.Note >= 0 && rules.NoteType(result.Note) == RhythmNoteType.Hold) feedbackLabel.text = "提前松开 · MISS\n整条长按失败";
                return;
            }
            feedbackLabel.color = result.Grade == RhythmGrade.Perfect ? LaneColor(0) : LaneColor(3);
            string timing = result.ErrorMs < -0.5 ? "早" : result.ErrorMs > 0.5 ? "晚" : "正中";
            feedbackLabel.text = $"{result.Grade}　{timing} {GameMath.Abs(result.ErrorMs):0} ms\n原始 {result.RawErrorMs:+0;-0;0} ms";
            Score(rules);
        }
        public void Missed(RhythmRules rules)
        {
            feedbackLabel.color = new Color(1f, 0.45f, 0.5f);
            feedbackLabel.text = "MISS";
            Score(rules);
        }
        public void Score(RhythmRules rules) => scoreLabel.text = $"{rules.Score:000000}\n连击 {rules.Combo}　最高 {rules.MaxCombo}";
        public void HoldCompleted(RhythmRules rules)
        {
            feedbackLabel.color = LaneColor(0); feedbackLabel.text = "长按完成 ✓\n现在可以松开"; Score(rules);
        }
        public void Finish(RhythmRules rules, bool practice = false)
        {
            Ready(practice ? $"练习结束，可重开练习或切回{songTitle}" : "歌曲结束，可重试或返回选曲");
            resultPanel.SetActive(true);
            startLabel.text = "重试本曲";
            SetPostRoundControls();
            resultLabel.text = $"{(practice ? "练习完成" : "演奏完成")}\n\n{rules.Score:000000}\n\nPerfect  {rules.Perfect}    Good  {rules.Good}\nMiss  {rules.Miss}    最高连击  {rules.MaxCombo}\n长按成功  {rules.CompletedHolds}";
        }
        public void Interrupted(string message)
        {
            Ready(message);
            startLabel.text = "重试本曲";
            SetPostRoundControls();
        }
        private void SetPostRoundControls()
        {
            settingsButton.gameObject.SetActive(false);
            instructions.gameObject.SetActive(false);
            scoreLabel.gameObject.SetActive(false);
            feedbackLabel.gameObject.SetActive(false);
            progressSlider.gameObject.SetActive(false);
            noteArea.gameObject.SetActive(false);
            foreach (var light in laneLights) light.gameObject.SetActive(false);
            SetLaneGuides(false);
            if (songMenuButton != null)
            {
                backButton.gameObject.SetActive(false);
                Place(songMenuButton.GetComponent<RectTransform>(), new Vector2(0.71f, 0.19f), new Vector2(0.91f, 0.27f));
            }
        }
        public void ConfigureSong(RhythmConfig chart, string title, string difficulty)
        {
            config = chart; songTitle = title;
            var songRules = chart.CreateRules(0, null);
            songNoteCount = songRules.Count;
            int holds = 0;
            for (int i = 0; i < songRules.Count; i++) if (songRules.NoteType(i) == RhythmNoteType.Hold) holds++;
            songSummary = $"{difficulty} / {songNoteCount - holds} 个单击 · {holds} 个长按";
            songInstructions = holds > 0 ? "让音符到达横线\n按下对应的键\n\nD    F    J    K\n单击轻点一次\n长条按住至尾\n提前松开会失败" : "让音符到达横线\n按下对应的键\n\nD    F    J    K\n\n单击轻点一次\n长按可用右侧练习";
            Score(songRules);
            progressSlider.SetValueWithoutNotify(0);
            if (noteImages != null) for (int i = 0; i < noteImages.Length; i++) noteImages[i].enabled = false;
            for (int i = 0; i < laneLights.Length; i++) laneLights[i].color = new Color(LaneColor(i).r, LaneColor(i).g, LaneColor(i).b, 0.14f);
        }
        public void SetSongResult(bool passed, int required, int best)
        {
            statusLabel.text = passed ? "通关达标！可返回选曲查看解锁" : $"尚未达标，需要 {required} 分；可重试";
            resultLabel.text += $"\n{(passed ? "通关" : "未达标")} · 目标 {required} · 最佳 {best}";
        }
        public void ConfigureCatalog(RhythmCatalogConfig catalog)
        {
            if (songMenu != null)
            {
                songMenuButton.gameObject.SetActive(true);
                songMenuButton.onClick.RemoveListener(SongMenuClicked);
                songMenuButton.onClick.AddListener(SongMenuClicked);
                if (songMenuCatalog != catalog) BuildSongRows(catalog);
                return;
            }
            songMenuButton = Instantiate(startButton, startButton.transform.parent);
            songMenuButton.gameObject.name = "SongMenuButton";
            songMenuButton.onClick = new Button.ButtonClickedEvent();
            songMenuButton.GetComponentInChildren<TMP_Text>().text = "返回选曲";
            songMenuButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(startButton.GetComponent<RectTransform>().anchoredPosition.x, -390);
            songMenuButton.onClick.AddListener(SongMenuClicked);
            songMenu = new GameObject("SongSelection", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            songMenu.transform.SetParent(transform, false);
            var panel = songMenu.GetComponent<RectTransform>();
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.offsetMin = panel.offsetMax = Vector2.zero;
            songMenu.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 1);
            songMenuHeading = Instantiate(heading, songMenu.transform);
            songMenuHeading.name = "SongSelectionHeading";
            songMenuHeading.rectTransform.anchorMin = songMenuHeading.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            songMenuHeading.rectTransform.anchoredPosition = new Vector2(0, 440);
            songMenuHeading.rectTransform.sizeDelta = new Vector2(1400, 100);
            songMenuHeading.text = "曲库 · 选择歌曲";
            var scrollObject = new GameObject("SongScroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(songMenu.transform, false);
            var scrollRect = scrollObject.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.05f, 0.17f); scrollRect.anchorMax = new Vector2(0.55f, 0.82f);
            scrollRect.offsetMin = scrollRect.offsetMax = Vector2.zero;
            scrollObject.GetComponent<Image>().color = new Color(0.09f, 0.11f, 0.17f, 1);
            var viewportObject = new GameObject("SongViewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            var contentObject = new GameObject("SongContent", typeof(RectTransform)); contentObject.transform.SetParent(viewport, false);
            songContent = contentObject.GetComponent<RectTransform>();
            songContent.anchorMin = new Vector2(0, 1); songContent.anchorMax = Vector2.one; songContent.pivot = new Vector2(0.5f, 1);
            songScroll = scrollObject.GetComponent<ScrollRect>(); songScroll.viewport = viewport; songScroll.content = songContent;
            songScroll.horizontal = false; songScroll.vertical = true; songScroll.movementType = ScrollRect.MovementType.Clamped;
            songDetails = Instantiate(statusLabel, songMenu.transform); songDetails.name = "SongDetails";
            songDetails.rectTransform.anchorMin = new Vector2(0.59f, 0.3f); songDetails.rectTransform.anchorMax = new Vector2(0.95f, 0.82f);
            songDetails.rectTransform.offsetMin = songDetails.rectTransform.offsetMax = Vector2.zero;
            songDetails.enableAutoSizing = true; songDetails.fontSizeMin = 20; songDetails.fontSizeMax = 30;
            songDetails.alignment = TextAlignmentOptions.TopLeft;
            libraryStart = PageButton(songMenu.transform, "LibraryStart", "开始演奏", new Vector2(0.59f, 0.17f), new Vector2(0.95f, 0.26f),
                () => OnLibraryStartClicked?.Invoke());
            PageButton(songMenu.transform, "LibrarySettings", "设置 · 延迟校准", new Vector2(0.59f, 0.07f), new Vector2(0.95f, 0.14f), OpenSettings);
            BuildSongRows(catalog);
            var exit = Instantiate(backButton, songMenu.transform);
            exit.name = "SongSelectionBack"; exit.onClick = new Button.ButtonClickedEvent();
            Place(exit.GetComponent<RectTransform>(), new Vector2(0.05f, 0.07f), new Vector2(0.3f, 0.14f));
            exit.GetComponentInChildren<TMP_Text>().text = "返回标题";
            exit.onClick.AddListener(BackClicked);
            songMenu.SetActive(false);
        }
        public void ShowSongMenu(RhythmCatalogConfig catalog, RhythmProgressData progress)
        {
            playing = false;
            for (int i = 0; i < catalog.Count; i++)
            {
                var song = catalog.Song(i);
                bool unlocked = catalog.IsUnlocked(song, progress);
                var record = RhythmProgressRules.ReadCurrentRecord(progress, song);
                bool cleared = progress.ClearedCharts.Contains(song.ProgressKey) || record?.Cleared == true;
                int best = RhythmProgressRules.GetBestScore(progress, song);
                int count = song.Chart.CreateRules(0, null).Count;
                int required = RhythmProgressRules.RequiredScore(count, song.PassScoreRatio);
                var prerequisite = catalog.Find(song.PrerequisiteId);
                string state = unlocked ? cleared ? "已通关" : "已解锁" : "锁定：先通过「" + (prerequisite == null ? song.PrerequisiteId : prerequisite.Title) + "」";
                songButtons[i].interactable = unlocked;
                songButtons[i].GetComponentInChildren<TMP_Text>().text =
                    $"{song.Title} · {song.Difficulty} · {state}\n最佳 {best:000000} · 通关目标 {required} 分（{song.PassScoreRatio:P0}）";
            }
            var selected = catalog.Find(menuSelectedId) ?? catalog.Song(0);
            menuSelectedId = selected.Id;
            libraryStart.interactable = catalog.IsUnlocked(selected, progress);
            foreach (var button in songButtons)
                button.GetComponent<Image>().color = button.name == "Song_" + selected.Id ? new Color(0.18f, 0.38f, 0.42f, 1) : new Color(0.12f, 0.15f, 0.22f, 1);
            songDetails.text = RecordText(selected, progress);
            // 曲库是独占页面，旧演奏内容既不绘制也不接收点击。
            foreach (Transform child in transform)
            {
                var item = child.gameObject;
                if (item == songMenu || item == settingsPanel || item == developerPanel || item == manualPanel ||
                    item == pausePanel || item == calibrationResult) continue;
                if (!libraryHiddenObjects.ContainsKey(item)) libraryHiddenObjects.Add(item, item.activeSelf);
                item.SetActive(false);
            }
            songMenu.transform.SetAsLastSibling();
            songMenu.SetActive(true);
        }
        public void HideSongMenu()
        {
            if (songMenu != null) songMenu.SetActive(false);
            foreach (var item in libraryHiddenObjects) if (item.Key != null) item.Key.SetActive(item.Value);
            libraryHiddenObjects.Clear();
        }
        private void BuildSongRows(RhythmCatalogConfig catalog)
        {
            foreach (var button in songButtons) { button.gameObject.SetActive(false); Destroy(button.gameObject); }
            songButtons.Clear(); songMenuCatalog = catalog;
            songContent.sizeDelta = new Vector2(0, catalog.Count * 142 + 16); songContent.anchoredPosition = Vector2.zero;
            for (int i = 0; i < catalog.Count; i++)
            {
                var song = catalog.Song(i);
                var button = Instantiate(startButton, songContent); button.name = "Song_" + song.Id; button.onClick = new Button.ButtonClickedEvent();
                var rect = button.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1);
                rect.anchoredPosition = new Vector2(0, -8 - i * 142); rect.sizeDelta = new Vector2(-24, 126);
                var label = button.GetComponentInChildren<TMP_Text>(); label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(16, 8); label.rectTransform.offsetMax = new Vector2(-16, -8);
                label.enableAutoSizing = true; label.fontSizeMin = 18; label.fontSizeMax = 26;
                string id = song.Id;
                // 选中高亮由成功准备后的 ShowCurrentRecord 提交，拒绝的重复点击不改详情。
                button.onClick.AddListener(() => OnSongSelected?.Invoke(id)); songButtons.Add(button);
            }
            songScroll.verticalNormalizedPosition = 1;
        }
        public void ShowCurrentRecord(RhythmSongData song, RhythmProgressData progress)
        {
            menuSelectedId = song.Id;
            if (songDetails != null) songDetails.text = RecordText(song, progress);
            resultLabel.text = $"当前谱面最高 {RhythmProgressRules.GetBestScore(progress, song)} 分\n完整判定与历史成绩可在曲库详情查看";
        }
        private static string RecordText(RhythmSongData song, RhythmProgressData progress)
        {
            var rules = song.Chart.CreateRules(0, null);
            var record = RhythmProgressRules.ReadCurrentRecord(progress, song);
            string text = $"{song.Title}\n{song.Difficulty} · {rules.Count} 枚 · {song.Chart.DurationSeconds:0.#} 秒\n通关目标 {RhythmProgressRules.RequiredScore(rules.Count, song.PassScoreRatio)} 分\n";
            if (record?.BestScoreRun != null)
            {
                var run = record.BestScoreRun;
                text += $"\n当前谱面最佳 {run.Score}\nPerfect {run.Perfect} · Good {run.Good} · Miss {run.Miss}\n最高连击 {run.MaxCombo} · 完成长按 {run.CompletedHolds}";
            }
            else text += "\n当前谱面暂无完整单局纪录";
            if (progress.BestScores.TryGetValue(song.ProgressKey, out int legacy)) text += $"\n历史最高 {legacy}（无判定摘要）";
            int oldBest = -1;
            foreach (var pair in progress.BestScores)
                if (pair.Key != song.ProgressKey && RhythmProgressRules.SongIdFromProgressKey(pair.Key) == song.Id && pair.Value > oldBest) oldBest = pair.Value;
            if (oldBest >= 0) text += $"\n旧谱面历史最高 {oldBest}（无判定摘要）";
            if (record?.LastCompletedRun != null) text += $"\n最近完成 {record.LastCompletedRun.Score} 分";
            return text;
        }
        private void SongMenuClicked() => OnSongMenuClicked?.Invoke();
        public static Color LaneColor(int lane)
        {
            switch (lane) { case 0: return new Color(0.3f, 0.95f, 0.85f); case 1: return new Color(0.4f, 0.75f, 1f);
                case 2: return new Color(0.75f, 0.55f, 1f); default: return new Color(1f, 0.7f, 0.4f); }
        }
        private void StartClicked() => OnStartClicked?.Invoke();
        private void BackClicked() => OnBackClicked?.Invoke();
        private void OffsetChanged(float value)
        {
            SetOffset(value);
            if (manualOffsetSlider != null) manualOffsetSlider.SetValueWithoutNotify(value);
            if (manualOffsetLabel != null) manualOffsetLabel.text = $"试用补偿 {value:+0;-0;0} ms\n未保存";
            if (!editingSettings) OnOffsetChanged?.Invoke(value);
        }
        public void SetVisualOffset(float value)
        {
            visualOffsetMs = value; visualSlider.SetValueWithoutNotify(value);
            visualLabel.text = $"视觉延迟 {value:+0;-0;0} ms\n正值：画面稍晚到线";
        }
        private void VisualChanged(float value) { SetVisualOffset(value); if (!editingSettings) OnVisualOffsetChanged?.Invoke(value); }
        private void CalibrationClicked() { if (settingsSaving) return; CancelSettingsDraft(); settingsPanel.SetActive(false); OnCalibrationClicked?.Invoke(); }
        private void PracticeClicked()
        {
            if (settingsSaving) return;
            CancelSettingsDraft(); settingsPanel.SetActive(false); developerPanel.SetActive(false);
            OnPracticeClicked?.Invoke();
        }
        private void DiagnosticExportClicked() => OnDiagnosticExportClicked?.Invoke();
        public void SetDiagnosticAvailable(bool available) => diagnosticButton.interactable = available;
        public void Calibrating(double seconds, int samples, int warmup, double beatSeconds, int targetCount = -1)
        {
            referenceCalibration = true;
            playExit.gameObject.SetActive(true);
            playExit.GetComponentInChildren<TMP_Text>().text = "取消校准";
            int total = targetCount > 0 ? targetCount : config.CalibrationSampleBeats;
            int phase = seconds < 0 ? -1 : seconds < warmup * beatSeconds ? 0 : 1;
            int progress = GameMath.Clamp((int)(seconds / beatSeconds) - warmup, 0, total);
            if (phase == calibrationPhase && samples == calibrationSamples && progress == calibrationProgress) return;
            if (calibrationPhase == int.MinValue && noteImages != null)
                for (int i = 0; i < noteImages.Length; i++) noteImages[i].enabled = false;
            calibrationPhase = phase; calibrationSamples = samples; calibrationProgress = progress;
            startButton.interactable = true; practiceButton.interactable = true; startLabel.text = $"{songTitle}（取消校准）";
            heading.text = "参考拍校准"; subtitle.text = "独立节拍器 · 听声跟拍";
            instructions.gameObject.SetActive(true);
            instructions.text = $"先听 {warmup} 拍适应\n固定采样 {total} 拍，不自动延长\n\nD / F / J / K 任一键\n轮后可试听、保留或手调\n建议须明确确认才保存";
            statusLabel.text = seconds < 0 ? "参考拍倒数…" : seconds < warmup * beatSeconds ? $"适应 {GameMath.Clamp((int)(seconds / beatSeconds), 0, warmup)}/{warmup} 拍" : $"采样 {progress}/{total} 拍 · 已匹配 {samples} 次";
            feedbackLabel.text = "综合输入偏差\n谱面对齐与视觉值不变";
        }
        public void ShowCalibrationResult(RhythmCalibrationResult result, double original, bool canSupplement, string notice = null)
        {
            EnsureCalibrationResult();
            canSupplement = canSupplement && result.CanSupplement;
            string reason;
            switch (result.Reason)
            {
                case RhythmCalibrationReason.NoInput: reason = "采样阶段未观察到按键；可检查焦点/键位，或保留继续"; break;
                case RhythmCalibrationReason.Unmatched: reason = "观察到输入但未匹配采样拍；没有生成补偿建议"; break;
                case RhythmCalibrationReason.Insufficient: reason = "有效样本或时间覆盖不足；没有可确认的建议"; break;
                case RhythmCalibrationReason.Unstable: reason = "波动较大，暂不能提供可靠建议；可保留或重新测量"; break;
                case RhythmCalibrationReason.Drift: reason = "低可信：有明显时段变化；不能靠8拍补测修复，可试听估计或保留原值"; break;
                case RhythmCalibrationReason.Suggested: reason = "低可信：波动或估计精度不足；可比较试听，自行确认后采用"; break;
                default: reason = "可靠估计：本轮采样一致且区间较窄；试听确认后才保存"; break;
            }
            calibrationPreviewRemaining = int.MinValue;
            calibrationResultText.text = (notice == null ? "" : notice + "\n") + reason + $"\n原补偿 {original:+0;-0;0} ms" + (result.HasCandidate ? $" · 估计 {result.OffsetMs:+0;-0;0} ms（未保存）" : " · 原值保持") +
                $"\n输入 {result.Observed} · 匹配 {result.Matched} · 剔除后有效 {result.Accepted}\n重复 {result.Duplicate} · 窗外 {result.Outside} · 无效 {result.Invalid} · MAD {result.RawMadMs:0} ms" +
                (RhythmRules.Finite(result.MedianLowerMs) && RhythmRules.Finite(result.MedianUpperMs) ?
                    $"\n中位数95%区间 [{result.MedianLowerMs:+0;-0;0}, {result.MedianUpperMs:+0;-0;0}] ms（独立样本假设）" : "\n样本不足以给出有限95%区间") +
                (RhythmRules.Finite(result.BlockSpreadMs) ? $"\n分段差 {result.BlockSpreadMs:0.0} ms（20 ms参考，非单独否决）" : "\n分段覆盖不足") +
                (canSupplement ? "\n可主动补测一次固定8拍；也可直接保留继续" : "\n本轮不提供补测；可完整重测或保留原值继续");
            calibrationApply.interactable = calibrationCandidate.interactable = result.HasCandidate;
            calibrationApply.GetComponentInChildren<TMP_Text>().text = result.Confidence == RhythmCalibrationConfidence.Low ? "自行采用估计并保存" : "采用估计并保存";
            calibrationCandidate.GetComponentInChildren<TMP_Text>().text = "试听估计值";
            calibrationSupplementButton.interactable = canSupplement;
            calibrationResult.transform.SetAsLastSibling(); calibrationResult.SetActive(true);
        }
        public void HideCalibrationResult() { if (calibrationResult != null) calibrationResult.SetActive(false); }
        public void SetCalibrationSaving(bool saving)
        {
            settingsSaving = saving;
            settingsButton.interactable = !saving;
            if (settingsPanel != null)
                foreach (var button in settingsPanel.GetComponentsInChildren<Button>(true)) button.interactable = !saving;
            if (manualPanel != null)
                foreach (var button in manualPanel.GetComponentsInChildren<Button>(true)) button.interactable = !saving;
            if (manualOffsetSlider != null) manualOffsetSlider.interactable = !saving;
            if (libraryStart != null) libraryStart.interactable = !saving;
            offsetSlider.interactable = visualSlider.interactable = !saving;
            startButton.interactable = practiceButton.interactable = calibrationButton.interactable = !saving;
            if (calibrationResult != null)
                foreach (var button in calibrationResult.GetComponentsInChildren<Button>(true)) button.interactable = !saving;
        }
        public void CalibrationPreviewFrame(double seconds, double duration, double offset)
        {
            if (calibrationResultText == null) return;
            if (seconds < 0) calibrationPreviewFeedback = "等待跟拍";
            int remaining = (int)(GameMath.Max(0, (float)(duration - seconds)) * 10);
            if (remaining == calibrationPreviewRemaining && offset == calibrationPreviewShownOffset && !calibrationPreviewDirty) return;
            calibrationPreviewRemaining = remaining; calibrationPreviewShownOffset = offset; calibrationPreviewDirty = false;
            calibrationResultText.text = $"试听补偿 {offset:+0;-0;0} ms（尚未保存）\n听参考拍，用 D/F/J/K 跟拍，观察校正后早晚误差\n固定8拍，可切换原值/估计或保留退出\n剩余 {GameMath.Max(0, (float)(duration - seconds)):0.0} 秒\n{calibrationPreviewFeedback}\n采用前请自行确认手感；这不是物理延迟测量";
        }
        public void CalibrationPreviewHit(double correctedError)
        {
            feedbackLabel.text = $"试听误差 {correctedError:+0;-0;0} ms";
            calibrationPreviewFeedback = $"本次校正后：{(correctedError < 0 ? "早" : "晚")} {GameMath.Abs(correctedError):0} ms";
            calibrationPreviewDirty = true;
        }
        private void EnsureCalibrationResult()
        {
            if (calibrationResult != null) return;
            calibrationResult = new GameObject("CalibrationResult", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            calibrationResult.transform.SetParent(transform, false);
            var panel = calibrationResult.GetComponent<RectTransform>();
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.offsetMin = panel.offsetMax = Vector2.zero;
            calibrationResult.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 0.98f);
            calibrationResultText = Instantiate(statusLabel, calibrationResult.transform);
            calibrationResultText.name = "CalibrationResultText";
            calibrationResultText.rectTransform.anchorMin = calibrationResultText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            calibrationResultText.rectTransform.anchoredPosition = new Vector2(0, 240);
            calibrationResultText.rectTransform.sizeDelta = new Vector2(1450, 340);
            calibrationResultText.enableAutoSizing = true; calibrationResultText.fontSizeMin = 22; calibrationResultText.fontSizeMax = 32;
            calibrationApply = CalibrationChoice("CalibrationApply", "应用建议并保存", new Vector2(-420, -30), () => OnCalibrationApply?.Invoke());
            calibrationCandidate = CalibrationChoice("CalibrationPreviewCandidate", "试听建议值", new Vector2(0, -30), () => OnCalibrationPreview?.Invoke(true));
            CalibrationChoice("CalibrationPreviewOriginal", "试听原值", new Vector2(420, -30), () => OnCalibrationPreview?.Invoke(false));
            calibrationSupplementButton = CalibrationChoice("CalibrationSupplement", "一次8拍补测", new Vector2(-420, -170), () => OnCalibrationSupplement?.Invoke());
            CalibrationChoice("CalibrationRetry", "重新测量整轮", new Vector2(0, -170), () => OnCalibrationClicked?.Invoke());
            CalibrationChoice("CalibrationKeep", "保留原值继续", new Vector2(420, -170), () => OnCalibrationKeep?.Invoke());
        }
        private Button CalibrationChoice(string name, string text, Vector2 position, UnityEngine.Events.UnityAction click)
        {
            var button = Instantiate(startButton, calibrationResult.transform);
            button.name = name; button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(click);
            var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = new Vector2(380, 90);
            var label = button.GetComponentInChildren<TMP_Text>(); label.text = text;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(12, 8); label.rectTransform.offsetMax = new Vector2(-12, -8);
            label.enableAutoSizing = true; label.fontSizeMin = 20; label.fontSizeMax = 28;
            return button;
        }
        private void ExtraControls()
        {
            if (calibrationButton != null) return;
            calibrationButton = Instantiate(startButton, startButton.transform.parent);
            calibrationButton.gameObject.name = "CalibrationButton";
            calibrationButton.onClick = new Button.ButtonClickedEvent();
            calibrationButton.GetComponentInChildren<TMP_Text>().text = "参考拍自动校准";
            practiceButton = Instantiate(startButton, startButton.transform.parent);
            practiceButton.gameObject.name = "PracticeButton"; practiceButton.onClick = new Button.ButtonClickedEvent();
            practiceButton.GetComponentInChildren<TMP_Text>().text = "短 Tap/Hold 练习";
            practiceButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(startButton.GetComponent<RectTransform>().anchoredPosition.x, -80);
            diagnosticButton = Instantiate(startButton, startButton.transform.parent);
            diagnosticButton.gameObject.name = "DiagnosticExportButton";
            diagnosticButton.onClick = new Button.ButtonClickedEvent();
            diagnosticButton.GetComponentInChildren<TMP_Text>().text = "保存本轮诊断";
            diagnosticButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(offsetSlider.GetComponent<RectTransform>().anchoredPosition.x, -500);
            diagnosticButton.interactable = false;
            heading = transform.Find("Heading").GetComponent<TMP_Text>();
            subtitle = transform.Find("Subtitle").GetComponent<TMP_Text>();
            heading.enableAutoSizing = true; heading.fontSizeMin = 24; heading.fontSizeMax = 64;
            heading.enableWordWrapping = false;
            subtitle.enableAutoSizing = true; subtitle.fontSizeMin = 18; subtitle.fontSizeMax = 28;
            startLabel.enableAutoSizing = true; startLabel.fontSizeMin = 18; startLabel.fontSizeMax = 28;
            instructions = transform.Find("Instructions").GetComponent<TMP_Text>();
            subtitle.text = songSummary;
            calibrationButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(offsetSlider.GetComponent<RectTransform>().anchoredPosition.x, -105);
            offsetLabel.rectTransform.anchoredPosition = new Vector2(offsetLabel.rectTransform.anchoredPosition.x, -195);
            offsetSlider.GetComponent<RectTransform>().anchoredPosition = new Vector2(offsetLabel.rectTransform.anchoredPosition.x, -250);
            visualSlider = Instantiate(offsetSlider, offsetSlider.transform.parent);
            visualSlider.gameObject.name = "VisualOffset"; visualSlider.onValueChanged = new Slider.SliderEvent();
            visualSlider.GetComponent<RectTransform>().anchoredPosition = new Vector2(offsetLabel.rectTransform.anchoredPosition.x, -370);
            visualLabel = Instantiate(offsetLabel, offsetLabel.transform.parent);
            visualLabel.name = "VisualOffsetLabel";
            visualLabel.rectTransform.anchoredPosition = new Vector2(offsetLabel.rectTransform.anchoredPosition.x, -310);
            var hint = transform.Find("OffsetHint");
            if (hint != null) hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(offsetLabel.rectTransform.anchoredPosition.x, -430);
            instructions.text = songInstructions;
            for (int i = 0; i < 4; i++)
            {
                var oldLane = transform.Find("Lane" + i); // 只在面板打开时调整旧布局，不在每帧查找。
                if (oldLane != null) oldLane.gameObject.SetActive(false);
            }
            var graphic = noteArea.gameObject.AddComponent<RhythmTrackGraphic>();
            graphic.Configure(true);
            BuildSettings();
        }
        // 页面只整理现有入口，规则与保存仍交由 State。
        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private Button PageButton(Transform parent, string name, string text, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction click)
        {
            var button = Instantiate(startButton, parent);
            button.name = name; button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(click);
            button.gameObject.SetActive(true);
            Place(button.GetComponent<RectTransform>(), min, max);
            var label = button.GetComponentInChildren<TMP_Text>(); label.text = text;
            Place(label.rectTransform, Vector2.zero, Vector2.one);
            label.enableAutoSizing = true; label.fontSizeMin = 18; label.fontSizeMax = 28;
            return button;
        }
        private GameObject Page(string name)
        {
            var page = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            page.transform.SetParent(transform, false);
            Place(page.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            page.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 1);
            return page;
        }
        private void BuildSettings()
        {
            settingsPanel = Page("CalibrationSettings");
            var title = Instantiate(heading, settingsPanel.transform);
            title.text = "设置 · 延迟校准";
            Place(title.rectTransform, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.93f));
            var description = Instantiate(statusLabel, settingsPanel.transform);
            description.text = "自动：听独立参考拍估算，试听原值 / 候选后明确保存。\n输入补偿与视觉延迟分别调整；教学通关和自动校准互相独立。";
            Place(description.rectTransform, new Vector2(0.08f, 0.65f), new Vector2(0.92f, 0.8f));
            calibrationButton.transform.SetParent(settingsPanel.transform, false);
            Place(calibrationButton.GetComponent<RectTransform>(), new Vector2(0.08f, 0.51f), new Vector2(0.47f, 0.61f));
            offsetLabel.transform.SetParent(settingsPanel.transform, false);
            offsetSlider.transform.SetParent(settingsPanel.transform, false);
            visualLabel.transform.SetParent(settingsPanel.transform, false);
            visualSlider.transform.SetParent(settingsPanel.transform, false);
            Place(offsetLabel.rectTransform, new Vector2(0.08f, 0.35f), new Vector2(0.47f, 0.47f));
            Place(offsetSlider.GetComponent<RectTransform>(), new Vector2(0.08f, 0.29f), new Vector2(0.47f, 0.33f));
            Place(visualLabel.rectTransform, new Vector2(0.53f, 0.35f), new Vector2(0.92f, 0.47f));
            Place(visualSlider.GetComponent<RectTransform>(), new Vector2(0.53f, 0.29f), new Vector2(0.92f, 0.33f));
            PageButton(settingsPanel.transform, "ManualCalibration", "手动 · 虫儿飞试听试打", new Vector2(0.08f, 0.19f), new Vector2(0.47f, 0.26f),
                () => { if (settingsSaving) return; settingsPanel.SetActive(false); OnManualCalibrationClicked?.Invoke(); });
            PageButton(settingsPanel.transform, "SettingsSave", "保存并返回", new Vector2(0.53f, 0.08f), new Vector2(0.92f, 0.18f), SaveSettings);
            PageButton(settingsPanel.transform, "SettingsBack", "取消 · 保留原值", new Vector2(0.08f, 0.08f), new Vector2(0.47f, 0.18f), CloseSettings);
            settingsButton = PageButton(transform, "SettingsButton", "设置 · 延迟校准", new Vector2(0.76f, 0.08f), new Vector2(0.96f, 0.17f), OpenSettings);
            playExit = PageButton(transform, "PlayExit", "暂停", new Vector2(0.8f, 0.86f), new Vector2(0.96f, 0.94f),
                () => { if (referenceCalibration) OnCalibrationKeep?.Invoke(); else OnPauseClicked?.Invoke(); });
            playExit.gameObject.SetActive(false);
            developerPanel = Page("DeveloperTools");
            practiceButton.transform.SetParent(developerPanel.transform, false);
            diagnosticButton.transform.SetParent(developerPanel.transform, false);
            Place(practiceButton.GetComponent<RectTransform>(), new Vector2(0.3f, 0.55f), new Vector2(0.7f, 0.65f));
            Place(diagnosticButton.GetComponent<RectTransform>(), new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.5f));
            PageButton(developerPanel.transform, "DeveloperBack", "收起工具", new Vector2(0.3f, 0.2f), new Vector2(0.7f, 0.3f), () => developerPanel.SetActive(false));
            PageButton(settingsPanel.transform, "DeveloperToggle", "展开测试 / 诊断", new Vector2(0.53f, 0.19f), new Vector2(0.92f, 0.26f),
                () => { developerPanel.transform.SetAsLastSibling(); developerPanel.SetActive(true); });
            var hint = transform.Find("OffsetHint");
            if (hint != null) hint.gameObject.SetActive(false);
            settingsPanel.SetActive(false); developerPanel.SetActive(false);
            manualPanel = Page("ManualCalibrationTrial");
            Place(manualPanel.GetComponent<RectTransform>(), new Vector2(0.76f, 0), Vector2.one);
            manualOffsetLabel = Instantiate(offsetLabel, manualPanel.transform);
            Place(manualOffsetLabel.rectTransform, new Vector2(0.05f, 0.64f), new Vector2(0.95f, 0.77f));
            manualOffsetSlider = Instantiate(offsetSlider, manualPanel.transform);
            manualOffsetSlider.onValueChanged = new Slider.SliderEvent(); manualOffsetSlider.onValueChanged.AddListener(OffsetChanged);
            Place(manualOffsetSlider.GetComponent<RectTransform>(), new Vector2(0.08f, 0.58f), new Vector2(0.92f, 0.62f));
            manualFeedback = Instantiate(statusLabel, manualPanel.transform);
            Place(manualFeedback.rectTransform, new Vector2(0.05f, 0.34f), new Vector2(0.95f, 0.56f));
            PageButton(manualPanel.transform, "ManualSave", "保存并返回", new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.28f), SaveSettings);
            PageButton(manualPanel.transform, "ManualCancel", "取消 · 保留原值", new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.15f), CloseSettings);
            manualPanel.SetActive(false);
            pausePanel = Page("RhythmPause");
            var pauseTitle = Instantiate(heading, pausePanel.transform); pauseTitle.text = "演奏已暂停";
            Place(pauseTitle.rectTransform, new Vector2(0.2f, 0.7f), new Vector2(0.8f, 0.85f));
            PageButton(pausePanel.transform, "PauseContinue", "继续演奏", new Vector2(0.3f, 0.52f), new Vector2(0.7f, 0.63f), () => OnResumeClicked?.Invoke());
            PageButton(pausePanel.transform, "PauseRetry", "重试本曲", new Vector2(0.3f, 0.36f), new Vector2(0.7f, 0.47f), StartClicked);
            PageButton(pausePanel.transform, "PauseExit", "退出 · 返回曲库", new Vector2(0.3f, 0.2f), new Vector2(0.7f, 0.31f),
                () => { if (songMenu != null) SongMenuClicked(); else BackClicked(); });
            pausePanel.SetActive(false);
        }
        public void ShowPause(bool paused)
        {
            if (pausePanel == null) return;
            if (paused) pausePanel.transform.SetAsLastSibling();
            pausePanel.SetActive(paused);
        }
        private void OpenSettings()
        {
            if (settingsSaving) return;
            originalOffset = offsetSlider.value; originalVisual = visualOffsetMs; editingSettings = true;
            settingsFromLibrary = songMenu != null && songMenu.activeSelf;
            settingsPanel.transform.SetAsLastSibling(); settingsPanel.SetActive(true);
        }
        private void CloseSettings()
        {
            if (settingsSaving) return;
            CancelSettingsDraft();
            OnSettingsCancelled?.Invoke();
            settingsPanel.SetActive(false); developerPanel.SetActive(false);
            if (settingsFromLibrary && songMenu != null) songMenu.SetActive(true);
        }
        private void CancelSettingsDraft()
        {
            if (!editingSettings) return;
            SetOffset(originalOffset); SetVisualOffset(originalVisual);
            editingSettings = false; manualTrial = false; manualPanel.SetActive(false);
        }
        public void CancelSettingsPreview() => CancelSettingsDraft();
        private void SaveSettings() { if (!settingsSaving) OnSettingsSaved?.Invoke(offsetSlider.value, visualOffsetMs); }
        public void SettingsSaved()
        {
            editingSettings = false; manualTrial = false;
            settingsPanel.SetActive(false); manualPanel.SetActive(false);
        }
        public void ShowManualCalibration()
        {
            manualTrial = true;
            startButton.gameObject.SetActive(false); settingsButton.gameObject.SetActive(false);
            backButton.gameObject.SetActive(false); playExit.gameObject.SetActive(false);
            if (songMenuButton != null) songMenuButton.gameObject.SetActive(false);
            manualOffsetSlider.SetValueWithoutNotify(offsetSlider.value);
            manualOffsetLabel.text = $"试用补偿 {offsetSlider.value:+0;-0;0} ms\n未保存";
            manualFeedback.text = "虫儿飞 · 试听试打\nD / F / J / K\n拖动补偿后看早晚反馈\n不计成绩、不解锁";
            manualOffsetSlider.interactable = true;
            manualPanel.transform.SetAsLastSibling(); manualPanel.SetActive(true);
            scoreLabel.gameObject.SetActive(false);
        }
        private void SetPlayControls(bool active)
        {
            referenceCalibration = false;
            playExit.GetComponentInChildren<TMP_Text>().text = "暂停";
            noteArea.gameObject.SetActive(true);
            progressSlider.gameObject.SetActive(true);
            feedbackLabel.gameObject.SetActive(true);
            foreach (var light in laneLights) light.gameObject.SetActive(true);
            SetLaneGuides(true);
            scoreLabel.gameObject.SetActive(true);
            playExit.gameObject.SetActive(active);
            settingsButton.gameObject.SetActive(!active);
            startButton.gameObject.SetActive(!active);
            backButton.gameObject.SetActive(!active);
            if (songMenuButton != null) songMenuButton.gameObject.SetActive(!active);
            instructions.gameObject.SetActive(!active);
        }
        private void SetLaneGuides(bool visible)
        {
            var line = transform.Find("JudgementLine");
            if (line != null) line.gameObject.SetActive(visible);
            for (int i = 0; i < 4; i++)
            {
                var label = transform.Find("KeyLabel" + i);
                if (label != null) label.gameObject.SetActive(visible);
            }
        }
        private void OnApplicationFocus(bool focused) { if (!focused && playing) OnFocusLost?.Invoke(); }
        private void OnApplicationPause(bool paused)
        {
            IsApplicationPaused = paused;
            if (paused && playing) OnApplicationSuspended?.Invoke();
        }
    }
}
