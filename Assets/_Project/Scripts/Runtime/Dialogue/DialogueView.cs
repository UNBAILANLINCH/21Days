// 职责：复用 UIView 展示文字、三槽立绘和选项；不注入服务，不拥有剧情进度。
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
    public sealed class DialogueView : UIView
    {
        [SerializeField] private TMP_Text speaker;
        [SerializeField] private TMP_Text body;
        [SerializeField] private Image[] portraits;
        [SerializeField] private Button advance;
        [SerializeField] private Button history;
        [SerializeField] private Toggle skip;
        [SerializeField] private Transform choiceRoot;
        [SerializeField] private Button choiceTemplate;
        private readonly List<Button> rows = new List<Button>();
        private long generation;
        private long visit;
        public override UILayer Layer => UILayer.Popup;
        public event Action<DialogueIntent> OnIntent;
        public event Action OnHistory;
        public event Action<bool> OnSkip;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (speaker == null || body == null || advance == null || history == null || skip == null ||
                choiceRoot == null || choiceTemplate == null || portraits == null || portraits.Length != 3)
                throw new InvalidOperationException("DialogueView 引用不完整");
            foreach (Image portrait in portraits)
                if (portrait == null) throw new InvalidOperationException("立绘槽未接线");
            advance.onClick.RemoveListener(Advance);
            history.onClick.RemoveListener(History);
            skip.onValueChanged.RemoveListener(Skip);
            advance.onClick.AddListener(Advance);
            history.onClick.AddListener(History);
            skip.onValueChanged.AddListener(Skip);
            choiceTemplate.gameObject.SetActive(false);
            return UniTask.CompletedTask;
        }

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
        public void SetInput(bool enabled, bool canAdvance, bool skipping)
        {
            Group.interactable = enabled;
            advance.interactable = enabled && canAdvance;
            skip.SetIsOnWithoutNotify(skipping);
        }
        public void SetPortrait(int slot, Sprite sprite, bool speaking)
        {
            portraits[slot].sprite = sprite;
            portraits[slot].enabled = sprite != null;
            portraits[slot].color = speaking ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);
        }
        public void ClearChoices()
        {
            foreach (Button row in rows) { row.onClick.RemoveAllListeners(); Destroy(row.gameObject); }
            rows.Clear();
        }
        public void AddChoice(DialogueContent.Choice choice, bool available)
        {
            if (!available && choice.HideWhenUnavailable) return;
            Button row = Instantiate(choiceTemplate, choiceRoot);
            TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
            if (label == null) { Destroy(row.gameObject); throw new InvalidOperationException("选项模板缺少 TMP 文本"); }
            label.text = available ? choice.Text : choice.Text + " — " + choice.UnavailableReason;
            row.interactable = available;
            var intent = new DialogueIntent(DialogueIntent.Action.Choose, generation, visit, choice.Id);
            row.onClick.AddListener(() => OnIntent?.Invoke(intent));
            row.gameObject.SetActive(true);
            rows.Add(row);
        }
        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            advance.onClick.RemoveListener(Advance);
            history.onClick.RemoveListener(History);
            skip.onValueChanged.RemoveListener(Skip);
            ClearChoices();
            OnIntent = null;
            OnHistory = null;
            OnSkip = null;
            return UniTask.CompletedTask;
        }
        private void Advance() => OnIntent?.Invoke(new DialogueIntent(DialogueIntent.Action.Advance, generation, visit));
        private void History() => OnHistory?.Invoke();
        private void Skip(bool value) => OnSkip?.Invoke(value);
    }
}
