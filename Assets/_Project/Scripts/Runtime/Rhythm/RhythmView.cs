// 职责：四轨下落、操作界面和结果显示。既有面板没有轨道布局，扩展通用 UI 会让 Core 认识玩法。
using System;
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
        private TMP_Text heading;
        private TMP_Text subtitle;
        private TMP_Text instructions;
        private double roundDuration;
        private float visualOffsetMs;
        private int calibrationPhase = int.MinValue;
        private int calibrationSamples;
        private readonly float[] lightRemaining = new float[4];
        private RhythmConfig config;
        private int countdown = int.MinValue;
        private bool playing;
        public event Action OnStartClicked;
        public event Action OnBackClicked;
        public event Action<float> OnOffsetChanged;
        public event Action OnFocusLost;
        public event Action OnCalibrationClicked;
        public event Action OnPracticeClicked;
        public event Action<float> OnVisualOffsetChanged;
        public override UILayer Layer => UILayer.Panel;
        public override bool CloseOnCancel => false;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            config = arg as RhythmConfig;
            ExtraControls();
            Hook(startButton, StartClicked);
            Hook(backButton, BackClicked);
            offsetSlider.onValueChanged.AddListener(OffsetChanged);
            visualSlider.onValueChanged.AddListener(VisualChanged);
            calibrationButton.onClick.AddListener(CalibrationClicked);
            practiceButton.onClick.AddListener(PracticeClicked);
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
            startLabel.text = "虫儿飞试玩（56Tap）";
            practiceButton.GetComponentInChildren<TMP_Text>().text = "短 Tap/Hold 练习";
            statusLabel.text = message;
            feedbackLabel.text = "D / F / J / K";
            resultPanel.SetActive(false);
            if (noteImages != null) for (int i = 0; i < noteImages.Length; i++) noteImages[i].enabled = false;
        }
        public void Starting()
        {
            resultPanel.SetActive(false); calibrationPhase = int.MinValue;
            playing = true; offsetSlider.interactable = false; visualSlider.interactable = false;
            calibrationButton.interactable = false; practiceButton.interactable = false; startButton.interactable = false; statusLabel.text = "准备演奏…";
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
            for (int i = 0; i < 4; i++) lightRemaining[i] = float.NegativeInfinity;
            countdown = int.MinValue;
            offsetSlider.interactable = false;
            visualSlider.interactable = false;
            calibrationButton.interactable = false;
            practiceButton.interactable = true;
            startButton.interactable = true;
            resultPanel.SetActive(false);
            startLabel.text = practice ? "切回虫儿飞试玩" : "重开虫儿飞";
            practiceButton.GetComponentInChildren<TMP_Text>().text = practice ? "重开 Tap/Hold 练习" : "短 Tap/Hold 练习";
            heading.text = practice ? "Tap / Hold 练习" : "虫儿飞";
            subtitle.text = practice ? "独立参考拍 · 不混入歌曲谱面" : "校准试听 / 56 个单击";
            instructions.text = practice ? "D、F 单击一次\nJ、K 长条按住到尾\n\n到尾自动成功\n提前松开会失败\n\n右侧可随时重开练习" : "让音符到达横线\n按下对应的键\n\nD    F    J    K\n\n这首试玩曲只有单击\n长按请点右侧练习";
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
            Ready(practice ? "练习结束，可重开练习或切回虫儿飞" : "试听结束，可调整延迟后再试");
            resultPanel.SetActive(true);
            resultLabel.text = $"{(practice ? "练习完成" : "试听完成")}\n\n{rules.Score:000000}\n\nPerfect  {rules.Perfect}    Good  {rules.Good}\nMiss  {rules.Miss}    最高连击  {rules.MaxCombo}\n长按成功  {rules.CompletedHolds}";
        }
        public static Color LaneColor(int lane)
        {
            switch (lane) { case 0: return new Color(0.3f, 0.95f, 0.85f); case 1: return new Color(0.4f, 0.75f, 1f);
                case 2: return new Color(0.75f, 0.55f, 1f); default: return new Color(1f, 0.7f, 0.4f); }
        }
        private void StartClicked() => OnStartClicked?.Invoke();
        private void BackClicked() => OnBackClicked?.Invoke();
        private void OffsetChanged(float value) { SetOffset(value); OnOffsetChanged?.Invoke(value); }
        public void SetVisualOffset(float value)
        {
            visualOffsetMs = value; visualSlider.SetValueWithoutNotify(value);
            visualLabel.text = $"视觉延迟 {value:+0;-0;0} ms\n正值：画面稍晚到线";
        }
        private void VisualChanged(float value) { SetVisualOffset(value); OnVisualOffsetChanged?.Invoke(value); }
        private void CalibrationClicked() => OnCalibrationClicked?.Invoke();
        private void PracticeClicked() => OnPracticeClicked?.Invoke();
        public void Calibrating(double seconds, int samples, int warmup, double beatSeconds)
        {
            int phase = seconds < 0 ? -1 : seconds < warmup * beatSeconds ? 0 : 1;
            if (phase == calibrationPhase && samples == calibrationSamples) return;
            if (calibrationPhase == int.MinValue && noteImages != null)
                for (int i = 0; i < noteImages.Length; i++) noteImages[i].enabled = false;
            calibrationPhase = phase; calibrationSamples = samples;
            startButton.interactable = true; practiceButton.interactable = true; startLabel.text = "虫儿飞（取消校准）";
            heading.text = "参考拍校准"; subtitle.text = "独立节拍器 · 听声跟拍";
            instructions.text = $"先听 {warmup} 拍适应\n再自然跟拍 {config.CalibrationSampleBeats} 次\n\nD / F / J / K 任一键\n至少 {config.CalibrationMinimumSamples} 次稳定样本\n不稳定则保留旧值";
            statusLabel.text = seconds < 0 ? "参考拍倒数…" : seconds < warmup * beatSeconds ? "先听节拍，准备跟拍" : $"听声自然点击 · {samples}/{config.CalibrationSampleBeats} 次";
            feedbackLabel.text = "综合输入偏差\n谱面对齐与视觉值不变";
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
            heading = transform.Find("Heading").GetComponent<TMP_Text>();
            subtitle = transform.Find("Subtitle").GetComponent<TMP_Text>();
            instructions = transform.Find("Instructions").GetComponent<TMP_Text>();
            subtitle.text = "校准试听 / 56 个单击";
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
            instructions.text = "让音符到达横线\n按下对应的键\n\nD    F    J    K\n\n虫儿飞只有单击\n长按请点右侧练习";
            for (int i = 0; i < 4; i++)
            {
                var oldLane = transform.Find("Lane" + i); // 只在面板打开时调整旧布局，不在每帧查找。
                if (oldLane != null) oldLane.gameObject.SetActive(false);
            }
            var graphic = noteArea.gameObject.AddComponent<RhythmTrackGraphic>();
            graphic.Configure(true);
        }
        private void OnApplicationFocus(bool focused) { if (!focused && playing) OnFocusLost?.Invoke(); }
    }
}
