// 职责：复用 UIView 展示文字、说话者头像、选项与自动 / 倍速 / 跳过控件；只显示与抛事件，不注入服务，不拥有剧情进度。
//   视觉与演出对白面板（PerformanceView.prefab）同一套：白色圆角面板 + 左右各一个头像框 + 深灰字 + 右下 ▼ 继续提示 + 右上白字投影控件；
//   样式全在预制体，代码只填文字与显隐（一致性由 EditMode TalkPanelConsistencyTests 钉住）。
//   头像：内容按左右两槽下发，槽 0 在面板左侧头像位、从左侧进出，槽 1 在右侧头像位、从右侧进出；
//   只显示说话者那一槽，非说话者与旁白收起，不压暗不缩放；每侧头像白框跟随该槽头像的实际显隐（由槽管，退场滑完才收）。
//   键盘 / 手柄：键位提示写进各控件的 Hint 小字（未接 Hint 时后缀在主标签上），选项前缀序号、选项出现后默认选中第一个可用项（UI Submit 可直接选）。
//   动效（roadmap D1 / D2）：头像入场 / 退场 / 交叉淡化交给每槽一个 DialoguePortraitSlot；说话者名字变化时名牌 punch。
//   参数由 Controller 经 SetMotion 传入（View 拿不到 DialogueConfig）；全部 unscaled，对白期间时停也照常播。
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Dialogue
{
    /// <summary>
    /// 对白面板。预制体 Addressables 地址须为 <c>DialogueView</c>。
    /// <para>
    /// 接线提示：<c>tapArea</c> 是覆盖对话框的透明 Button，层级要在 <c>choiceRoot</c> 与各控件按钮之下，
    /// 否则会吞掉选项点击；<c>choiceTemplate</c> 下须有一个 TMP 文本作选项文字、一个名为 <c>Icon</c> 的 Image 作选项图标。
    /// </para>
    /// </summary>
    public sealed class DialogueView : UIView
    {
        private const string AutoOffLabel = "自动";
        private const string AutoOnLabel = "自动中";
        private const string SkipBaseLabel = "跳过";
        private const string HistoryBaseLabel = "LOG";
        private const string ChoiceIconName = "Icon";

        [SerializeField] private TMP_Text speaker;
        [SerializeField] private TMP_Text body;
        [Tooltip("长度 2：0 左、1 右（内容槽位）。槽 0 在面板左侧头像位，槽 1 在右侧头像位；只显示说话者那一槽。")]
        [SerializeField] private Image[] portraits;
        [Tooltip("可选：左侧头像白框，跟随槽 0（左）头像的实际显隐（退场动画播完才隐藏）。")]
        [SerializeField] private GameObject avatarFrame;
        [Tooltip("可选：右侧头像白框，跟随槽 1（右）头像的实际显隐（退场动画播完才隐藏）。")]
        [SerializeField] private GameObject avatarFrameRight;
        [Tooltip("可选：右下「▼ 点击或按空格继续」提示（含子 HoldHint），等待推进时显示。")]
        [SerializeField] private GameObject holdPrompt;
        [SerializeField] private Button tapArea;
        [SerializeField] private Button history;
        [SerializeField] private Button auto;
        [SerializeField] private Button speed;
        [SerializeField] private Button skip;
        [SerializeField] private TMP_Text autoLabel;
        [SerializeField] private TMP_Text speedLabel;
        [Tooltip("跳过按钮的文字；接了 skipHintLabel 时保留预制体固定文案，否则运行时写「跳过 + 键位」。")]
        [SerializeField] private TMP_Text skipLabel;
        [Tooltip("历史按钮的文字；接了 historyHintLabel 时保留预制体固定文案，否则运行时写「LOG + 键位」。")]
        [SerializeField] private TMP_Text historyLabel;
        [Tooltip("可选：自动按钮的键位小字；接了则键位写这里、主标签只写文案。")]
        [SerializeField] private TMP_Text autoHintLabel;
        [Tooltip("可选：倍速按钮的键位小字；接了则键位写这里、主标签只写文案。")]
        [SerializeField] private TMP_Text speedHintLabel;
        [Tooltip("可选：跳过按钮的键位小字；接了则键位写这里、主标签保留预制体固定文案。")]
        [SerializeField] private TMP_Text skipHintLabel;
        [Tooltip("可选：LOG 按钮的键位小字；接了则键位写这里、主标签保留预制体固定文案。")]
        [SerializeField] private TMP_Text historyHintLabel;
        [Tooltip("可选：自动按钮主标签的投影文字，镜像主标签文案。")]
        [SerializeField] private TMP_Text autoLabelShadow;
        [Tooltip("可选：倍速按钮主标签的投影文字，镜像主标签文案。")]
        [SerializeField] private TMP_Text speedLabelShadow;
        [SerializeField] private Transform choiceRoot;
        [SerializeField] private Button choiceTemplate;
        private readonly List<Button> rows = new List<Button>();
        // 与 rows 一一对应：该行的选项 id 与图标 Image，异步加载完的图标按选项 id 回填。
        private readonly List<string> rowChoiceIds = new List<string>();
        private readonly List<Image> rowIcons = new List<Image>();
        // 每槽一个立绘动效状态机，首次打开时按 portraits 建（取预制体原位），之后复用。
        private DialoguePortraitSlot[] slots;
        private DialogueMotionSettings motion = DialogueMotionSettings.Default;
        private MotionHandle namePunch;
        // 上一句显示的说话者名：变化才 punch，同名连续两句不动；打开面板时清空，第一句必 punch。
        private string shownSpeaker;
        private readonly StringBuilder visibleBuilder = new StringBuilder(128);
        private long generation;
        private long visit;
        private bool shownAuto;
        private float shownSpeed = -1f;
        private bool shownSkipping;
        private bool controlsShown;
        private bool advancePromptShown;
        // 键位提示（空串 = 不显示），由 Controller 经 SetKeyHints 传入；View 不读输入。
        private string autoHint = string.Empty;
        private string speedHint = string.Empty;
        private string skipHint = string.Empty;
        private string historyHint = string.Empty;
        // 选项默认选中：延后到下一帧再设，避免同一帧的 Enter（刚补全出选项的那次按键）被 UI Submit 当成点中第一个选项。
        private int selectChoiceFrame = -1;
        // 选项行因资格变化重建时，保留玩家当前选中的那个（按选项 id）。
        private string selectedChoiceId;

        public override UILayer Layer => UILayer.Popup;
        public override bool IsFullScreen => false;
        /// <summary>Esc / 手柄 B 不能关对白：关了会让 Controller 的展示循环失去面板、流程卡住。</summary>
        public override bool CloseOnCancel => false;
        /// <summary>选项被点：携带 Choose 意图。</summary>
        public event Action<DialogueIntent> OnIntent;
        /// <summary>对话框被点（补全 / 推进由策略判定）。</summary>
        public event Action OnTap;
        public event Action OnHistory;
        public event Action OnAuto;
        public event Action OnSpeed;
        public event Action OnSkip;

        /// <summary>当前台词经 TMP 解析后的可见字符序列（富文本标签已去掉，下标与可见字数一一对应），供打字节奏判标点。</summary>
        public string VisibleText { get; private set; } = string.Empty;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            tapArea.onClick.RemoveListener(Tap);
            history.onClick.RemoveListener(History);
            auto.onClick.RemoveListener(Auto);
            speed.onClick.RemoveListener(Speed);
            skip.onClick.RemoveListener(Skip);
            tapArea.onClick.AddListener(Tap);
            history.onClick.AddListener(History);
            auto.onClick.AddListener(Auto);
            speed.onClick.AddListener(Speed);
            skip.onClick.AddListener(Skip);
            // 全屏透明点击区不参与手柄 / 方向键导航，否则方向键会把选中移到一个看不见的按钮上。
            Navigation none = tapArea.navigation;
            none.mode = Navigation.Mode.None;
            tapArea.navigation = none;
            choiceTemplate.gameObject.SetActive(false);
            controlsShown = false;
            selectChoiceFrame = -1;
            selectedChoiceId = null;
            ApplyStaticLabels();
            EnsureSlots();
            // 面板可能被复用：上一段对白的立绘与名牌动画直接清到终态，第一句从空槽入场、名牌必 punch。
            foreach (DialoguePortraitSlot slot in slots) slot.Clear();
            // 强制写一次：预制体默认值不可信，打开时一律先藏起继续提示。
            advancePromptShown = true;
            SetAdvancePrompt(false);
            FinishNamePunch();
            shownSpeaker = null;
            VisibleText = string.Empty;
            return UniTask.CompletedTask;
        }

        /// <summary>设置动效参数（Controller 打开面板后调一次）；未设置时用 <see cref="DialogueMotionSettings.Default"/>。</summary>
        public void SetMotion(in DialogueMotionSettings settings)
        {
            motion = settings.IsValid ? settings : DialogueMotionSettings.Default;
            EnsureSlots();
            foreach (DialoguePortraitSlot slot in slots) slot.Configure(motion);
        }

        /// <summary>
        /// 设置按钮上的键位提示（如「A」「S」「Ctrl」「H」），空串表示不显示。只在键位变化时调，标签随即重写一次。
        /// </summary>
        public void SetKeyHints(string autoKey, string speedKey, string skipKey, string historyKey)
        {
            autoHint = autoKey ?? string.Empty;
            speedHint = speedKey ?? string.Empty;
            skipHint = skipKey ?? string.Empty;
            historyHint = historyKey ?? string.Empty;
            ApplyStaticLabels();
            controlsShown = false;
        }

        /// <summary>请求在下一帧把 EventSystem 选中项设到选项上（优先保留原选中项，否则第一个可用项）。</summary>
        public void SelectChoice()
        {
            if (rows.Count == 0) return;
            selectChoiceFrame = Time.frameCount + 1;
        }

        /// <summary>设置一句台词并返回 TMP 解析后的可见字符总数（富文本标签不计）。</summary>
        public int SetLine(long session, long nodeVisit, string name, string text)
        {
            generation = session;
            visit = nodeVisit;
            speaker.text = name;
            if (!string.Equals(shownSpeaker, name, StringComparison.Ordinal))
            {
                shownSpeaker = name;
                PunchName();
            }
            body.text = text;
            body.maxVisibleCharacters = 0;
            body.ForceMeshUpdate();
            ClearChoices();
            // 换节点：上一组选项的选中记忆作废（选项 id 可能跨节点重名）。
            selectedChoiceId = null;
            selectChoiceFrame = -1;
            TMP_TextInfo info = body.textInfo;
            visibleBuilder.Clear();
            for (int i = 0; i < info.characterCount; i++) visibleBuilder.Append(info.characterInfo[i].character);
            VisibleText = visibleBuilder.ToString();
            return info.characterCount;
        }

        public void SetVisible(int count) => body.maxVisibleCharacters = count;

        public void SetInput(bool enabled) => Group.interactable = enabled;

        /// <summary>
        /// 设置一槽头像。只显示说话者：<paramref name="speaking"/> 为 false（非说话者、旁白）时该槽收起（同传 null）。
        /// 空 → 有从本槽那一侧（槽 0 左、槽 1 右）滑入淡入，有 → 空滑出淡出，同槽换图交叉淡化；白框由槽跟随头像实际显隐一起开关。
        /// <paramref name="instant"/> = true 时直接置终态不播动画（存档恢复路径）。
        /// </summary>
        public void SetPortrait(int slot, Sprite sprite, bool speaking, bool instant = false)
        {
            EnsureSlots();
            slots[slot].Set(speaking ? sprite : null, instant);
        }

        /// <summary>切换右下「▼ 点击或按空格继续」提示（等待推进时显示）；<c>holdPrompt</c> 没接则不做事，值没变不重写。</summary>
        public void SetAdvancePrompt(bool visible)
        {
            if (holdPrompt == null || advancePromptShown == visible) return;
            advancePromptShown = visible;
            holdPrompt.SetActive(visible);
        }

        /// <summary>刷新控件：自动标签「自动」/「自动中」，倍速标签 x1 / x2 / x4，跳过中禁用跳过按钮。值没变不重写。</summary>
        public void SetControls(bool autoPlay, float speedValue, bool skipping)
        {
            // 倍速值直接取自同一份挡位数组，精确比较即可。
            if (controlsShown && shownAuto == autoPlay && shownSpeed == speedValue &&
                shownSkipping == skipping) return;
            controlsShown = true;
            shownAuto = autoPlay;
            shownSpeed = speedValue;
            shownSkipping = skipping;
            SetLabel(autoLabel, autoLabelShadow, autoHintLabel, autoPlay ? AutoOnLabel : AutoOffLabel, autoHint);
            SetLabel(speedLabel, speedLabelShadow, speedHintLabel, FormatSpeed(speedValue), speedHint);
            skip.interactable = !skipping;
        }

        public void ClearChoices()
        {
            // 记下玩家当前选中的选项 id：资格刷新重建行后按 id 选回去，不把选中跳回第一项。
            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem == null ? null : eventSystem.currentSelectedGameObject;
            for (int i = 0; i < rows.Count; i++)
                if (selected != null && rows[i].gameObject == selected) selectedChoiceId = rowChoiceIds[i];
            foreach (Button row in rows) { row.onClick.RemoveAllListeners(); Destroy(row.gameObject); }
            rows.Clear();
            rowChoiceIds.Clear();
            rowIcons.Clear();
        }

        /// <summary>加一行选项；<paramref name="icon"/> 为 null 时隐藏图标位（图标可稍后用 <see cref="SetChoiceIcon"/> 回填）。</summary>
        public void AddChoice(DialogueContent.Choice choice, bool available, Sprite icon)
        {
            if (!available && choice.HideWhenUnavailable) return;
            Button row = Instantiate(choiceTemplate, choiceRoot);
            TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
            if (label == null) { Destroy(row.gameObject); throw new InvalidOperationException("选项模板缺少 TMP 文本"); }
            Image iconImage = FindIcon(row.transform);
            if (iconImage == null) { Destroy(row.gameObject); throw new InvalidOperationException("选项模板缺少名为 Icon 的 Image"); }
            ApplyIcon(iconImage, icon);
            // 序号与数字键 1–4 对应（显示行序，从 1 起）；只在建行时拼一次。
            string text = available ? choice.Text : choice.Text + " — " + choice.UnavailableReason;
            label.text = (rows.Count + 1) + ". " + text;
            row.interactable = available;
            var intent = new DialogueIntent(DialogueIntent.Action.Choose, generation, visit, choice.Id);
            row.onClick.AddListener(() => OnIntent?.Invoke(intent));
            row.gameObject.SetActive(true);
            rows.Add(row);
            rowChoiceIds.Add(choice.Id);
            rowIcons.Add(iconImage);
        }

        /// <summary>给已显示的某个选项回填图标（异步加载完成后用）；该选项不在当前行里则忽略。</summary>
        public void SetChoiceIcon(string choiceId, Sprite icon)
        {
            for (int i = 0; i < rowChoiceIds.Count; i++)
                if (string.Equals(rowChoiceIds[i], choiceId, StringComparison.Ordinal)) ApplyIcon(rowIcons[i], icon);
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            tapArea.onClick.RemoveListener(Tap);
            history.onClick.RemoveListener(History);
            auto.onClick.RemoveListener(Auto);
            speed.onClick.RemoveListener(Speed);
            skip.onClick.RemoveListener(Skip);
            ClearChoices();
            selectChoiceFrame = -1;
            selectedChoiceId = null;
            OnIntent = null;
            OnTap = null;
            OnHistory = null;
            OnAuto = null;
            OnSpeed = null;
            OnSkip = null;
            return UniTask.CompletedTask;
        }

        // 面板停用 / 销毁（含被瞬间关闭）时掐断全部动效并直接摆到终态，避免残影或半透明立绘留在下次打开的画面里。
        private void OnDisable()
        {
            if (slots != null)
                foreach (DialoguePortraitSlot slot in slots) slot.Finish();
            FinishNamePunch();
        }

        private void EnsureSlots()
        {
            if (slots != null || portraits == null || portraits.Length != DialogueContent.SlotCount) return;
            slots = new DialoguePortraitSlot[portraits.Length];
            // 槽 0 在面板左侧头像位、从左侧进出，槽 1 在右侧头像位、从右侧进出；同一时刻只有说话者那一槽有图。
            for (int i = 0; i < portraits.Length; i++)
            {
                slots[i] = new DialoguePortraitSlot(portraits[i], i > 0, i == 0 ? avatarFrame : avatarFrameRight);
                slots[i].Configure(motion);
            }
        }

        // 名牌 punch：从放大 + 透明回落到原样。说话者名 TMP 本身就是名牌（预制体 Speaker），不另找底板。
        private void PunchName()
        {
            if (namePunch.IsActive()) namePunch.Cancel();
            float seconds = motion.NameTagPunchSeconds;
            if (seconds <= 0f || !isActiveAndEnabled)
            {
                ApplyNamePunch(1f);
                return;
            }
            ApplyNamePunch(0f);
            namePunch = LMotion.Create(0f, 1f, seconds)
                .WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, (t, view) => view.ApplyNamePunch(t))
                .AddTo(this);
        }

        private void ApplyNamePunch(float t)
        {
            float scale = motion.NameTagPunchScale + (1f - motion.NameTagPunchScale) * t;
            speaker.rectTransform.localScale = new Vector3(scale, scale, 1f);
            speaker.alpha = t;
        }

        private void FinishNamePunch()
        {
            if (namePunch.IsActive()) namePunch.Cancel();
            if (speaker != null) ApplyNamePunch(1f);
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。
        private void Validate()
        {
            var missing = new List<string>();
            if (speaker == null) missing.Add(nameof(speaker));
            if (body == null) missing.Add(nameof(body));
            if (portraits == null || portraits.Length != DialogueContent.SlotCount)
                missing.Add(nameof(portraits) + $"（长度须为 {DialogueContent.SlotCount}）");
            else
                for (int i = 0; i < portraits.Length; i++)
                    if (portraits[i] == null) missing.Add(nameof(portraits) + "[" + i + "]");
            if (tapArea == null) missing.Add(nameof(tapArea));
            if (history == null) missing.Add(nameof(history));
            if (auto == null) missing.Add(nameof(auto));
            if (speed == null) missing.Add(nameof(speed));
            if (skip == null) missing.Add(nameof(skip));
            if (autoLabel == null) missing.Add(nameof(autoLabel));
            if (speedLabel == null) missing.Add(nameof(speedLabel));
            if (skipLabel == null) missing.Add(nameof(skipLabel));
            if (historyLabel == null) missing.Add(nameof(historyLabel));
            if (choiceRoot == null) missing.Add(nameof(choiceRoot));
            if (choiceTemplate == null) missing.Add(nameof(choiceTemplate));
            else if (FindIcon(choiceTemplate.transform) == null) missing.Add(nameof(choiceTemplate) + "/" + ChoiceIconName);
            if (missing.Count > 0)
                throw new InvalidOperationException("DialogueView 引用未接线：" + string.Join("、", missing));
        }

        private static Image FindIcon(Transform row)
        {
            Transform child = row.Find(ChoiceIconName);
            return child == null ? null : child.GetComponent<Image>();
        }

        private static void ApplyIcon(Image image, Sprite icon)
        {
            image.sprite = icon;
            image.gameObject.SetActive(icon != null);
        }

        // 选项默认选中的延后执行：只在 SelectChoice 请求后的下一帧做一次，其余帧只比一个整数。
        private void Update()
        {
            if (selectChoiceFrame < 0 || Time.frameCount < selectChoiceFrame) return;
            selectChoiceFrame = -1;
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return;
            Button target = null;
            for (int i = 0; i < rows.Count && target == null; i++)
                if (rows[i].interactable && string.Equals(rowChoiceIds[i], selectedChoiceId, StringComparison.Ordinal))
                    target = rows[i];
            for (int i = 0; i < rows.Count && target == null; i++)
                if (rows[i].interactable) target = rows[i];
            if (target != null) eventSystem.SetSelectedGameObject(target.gameObject);
        }

        // 跳过 / LOG：接了 Hint 小字就只写键位、主标签保留预制体固定文案（如「跳过 ▶」）；没接则主标签写「文案 + 键位」。
        private void ApplyStaticLabels()
        {
            if (skipHintLabel != null) skipHintLabel.text = skipHint;
            else if (skipLabel != null) skipLabel.text = WithHint(SkipBaseLabel, skipHint);
            if (historyHintLabel != null) historyHintLabel.text = historyHint;
            else if (historyLabel != null) historyLabel.text = WithHint(HistoryBaseLabel, historyHint);
        }

        // 自动 / 倍速：接了 Hint 小字则主标签只写文案、键位写小字；否则主标签拼键位。投影接了就镜像主标签。
        private static void SetLabel(TMP_Text label, TMP_Text shadow, TMP_Text hintLabel, string text, string hint)
        {
            string shown = hintLabel != null ? text : WithHint(text, hint);
            label.text = shown;
            if (shadow != null) shadow.text = shown;
            if (hintLabel != null) hintLabel.text = hint;
        }

        /// <summary>倍速档的显示文字（x1 / x2 / x4）；回放与测试拼期望值用同一个方法。</summary>
        public static string FormatSpeed(float speedValue) =>
            "x" + speedValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>按钮文字 = 文案 + 空格 + 键位；键位为空只显示文案。回放与测试拼期望值用同一个方法。</summary>
        public static string WithHint(string label, string hint) =>
            string.IsNullOrEmpty(hint) ? label : label + " " + hint;

        // 鼠标点过的控件按钮会留在 EventSystem 选中态，之后按 Enter / 手柄 A 会被 UI Submit 再点一次（同时还触发推进），
        // 所以点完即取消选中；键盘 / 手柄用专属键位操作这些按钮。
        private static void ReleaseSelection(Button button)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.currentSelectedGameObject == button.gameObject)
                eventSystem.SetSelectedGameObject(null);
        }

        private void Tap() { ReleaseSelection(tapArea); OnTap?.Invoke(); }
        private void History() { ReleaseSelection(history); OnHistory?.Invoke(); }
        private void Auto() { ReleaseSelection(auto); OnAuto?.Invoke(); }
        private void Speed() { ReleaseSelection(speed); OnSpeed?.Invoke(); }
        private void Skip() { ReleaseSelection(skip); OnSkip?.Invoke(); }
    }
}
