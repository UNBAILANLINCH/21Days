// 职责：复用 UIView 展示文字、左右两槽立绘、选项与自动 / 倍速 / 跳过控件；只显示与抛事件，不注入服务，不拥有剧情进度。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
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
        private const string ChoiceIconName = "Icon";

        [SerializeField] private TMP_Text speaker;
        [SerializeField] private TMP_Text body;
        [Tooltip("长度 2：0 左、1 右")]
        [SerializeField] private Image[] portraits;
        [SerializeField] private Button tapArea;
        [SerializeField] private Button history;
        [SerializeField] private Button auto;
        [SerializeField] private Button speed;
        [SerializeField] private Button skip;
        [SerializeField] private TMP_Text autoLabel;
        [SerializeField] private TMP_Text speedLabel;
        [SerializeField] private Transform choiceRoot;
        [SerializeField] private Button choiceTemplate;
        private readonly List<Button> rows = new List<Button>();
        // 与 rows 一一对应：该行的选项 id 与图标 Image，异步加载完的图标按选项 id 回填。
        private readonly List<string> rowChoiceIds = new List<string>();
        private readonly List<Image> rowIcons = new List<Image>();
        private long generation;
        private long visit;
        private bool shownAuto;
        private float shownSpeed = -1f;
        private bool shownSkipping;
        private bool controlsShown;

        public override UILayer Layer => UILayer.Popup;
        public override bool IsFullScreen => false;
        /// <summary>选项被点：携带 Choose 意图。</summary>
        public event Action<DialogueIntent> OnIntent;
        /// <summary>对话框被点（补全 / 推进由策略判定）。</summary>
        public event Action OnTap;
        public event Action OnHistory;
        public event Action OnAuto;
        public event Action OnSpeed;
        public event Action OnSkip;

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
            choiceTemplate.gameObject.SetActive(false);
            controlsShown = false;
            return UniTask.CompletedTask;
        }

        /// <summary>设置一句台词并返回 TMP 解析后的可见字符总数（富文本标签不计）。</summary>
        public int SetLine(long session, long nodeVisit, string name, string text)
        {
            generation = session;
            visit = nodeVisit;
            speaker.text = name;
            body.text = text;
            body.maxVisibleCharacters = 0;
            body.ForceMeshUpdate();
            ClearChoices();
            return body.textInfo.characterCount;
        }

        public void SetVisible(int count) => body.maxVisibleCharacters = count;

        public void SetInput(bool enabled) => Group.interactable = enabled;

        public void SetPortrait(int slot, Sprite sprite, bool speaking)
        {
            portraits[slot].sprite = sprite;
            portraits[slot].enabled = sprite != null;
            portraits[slot].color = speaking ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);
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
            autoLabel.text = autoPlay ? AutoOnLabel : AutoOffLabel;
            speedLabel.text = "x" + speedValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            skip.interactable = !skipping;
        }

        public void ClearChoices()
        {
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
            label.text = available ? choice.Text : choice.Text + " — " + choice.UnavailableReason;
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
            OnIntent = null;
            OnTap = null;
            OnHistory = null;
            OnAuto = null;
            OnSpeed = null;
            OnSkip = null;
            return UniTask.CompletedTask;
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

        private void Tap() => OnTap?.Invoke();
        private void History() => OnHistory?.Invoke();
        private void Auto() => OnAuto?.Invoke();
        private void Speed() => OnSpeed?.Invoke();
        private void Skip() => OnSkip?.Invoke();
    }
}
