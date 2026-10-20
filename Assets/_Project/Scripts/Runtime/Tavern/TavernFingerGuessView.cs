// 职责：分步选出指与喊数，结算先给结论、详情按需展开；事件仍由接线方验证，不修改醉意或出拳规则。
// 复用UIView、UGUI与既有划拳会话；白盒无分步接线时保留原显示，正式美术由预制体提供。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Game.Tavern
{
    public sealed class TavernFingerGuessView : UIView
    {
        private static readonly string[] Calls = { "零", "一心敬", "哥俩好", "三星照", "四喜财", "五魁首", "六六顺", "七个巧", "八仙到", "九连环", "十全到" };
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text status;
        [SerializeField] private TMP_Text resultLabel;
        [SerializeField] private Button[] fingerButtons;
        [SerializeField] private Button[] callButtons;
        [SerializeField] private TMP_Text[] callLabels;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Sprite choiceIdle;
        [SerializeField] private Sprite choiceSelected;
        [SerializeField] private GameObject fingerRoot;
        [SerializeField] private GameObject callRoot;
        [SerializeField] private Button chooseCallButton;
        [SerializeField] private Button changeFingerButton;
        [SerializeField] private TMP_Text detailsLabel;
        [SerializeField] private Button detailsButton;
        [SerializeField] private TMP_Text detailsButtonLabel;
        [SerializeField] private CanvasGroup resultGroup;
        [SerializeField, Min(0f)] private float resultRevealSeconds = .16f;
        private readonly UnityAction[] fingerActions = new UnityAction[6];
        private readonly UnityAction[] callActions = new UnityAction[6];
        private UniTaskCompletionSource closed;
        private MotionHandle resultReveal;
        private int fingers = 1, offset = 4;
        private bool pending, choosingCall, detailsOpen;
        private string detailsHint = "";
        public event Action<int, int> OnGuess;
        public event Action OnNext;
        public override UILayer Layer => UILayer.Panel;
        public int SelectedFingers => fingers;
        public int SelectedCall => fingers + offset;
        public bool IsAwaitingGuess => pending;
        public bool IsChoosingCall => pending && choosingCall;
        public string ResultText => resultLabel == null ? "" : resultLabel.text;
        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (title == null || status == null || resultLabel == null || confirmButton == null || nextButton == null || backButton == null ||
                fingerButtons == null || fingerButtons.Length != 6 || callButtons == null || callButtons.Length != 6 || callLabels == null || callLabels.Length != 6)
                throw new InvalidOperationException("划拳预制体引用未接齐");
            if (fingerRoot != null && (callRoot == null || chooseCallButton == null || changeFingerButton == null ||
                detailsLabel == null || detailsButton == null || detailsButtonLabel == null || choiceIdle == null || choiceSelected == null || resultGroup == null))
                throw new InvalidOperationException("行酒令分步界面引用未接齐");
            closed = new UniTaskCompletionSource();
            for (int i = 0; i < 6; i++)
            {
                if (fingerButtons[i] == null || callButtons[i] == null || callLabels[i] == null) throw new InvalidOperationException("划拳按钮未接齐");
                int index = i;
                if (fingerActions[i] != null) Unhook(fingerButtons[i], fingerActions[i]);
                if (callActions[i] != null) Unhook(callButtons[i], callActions[i]);
                fingerActions[i] = () => SelectFinger(index); callActions[i] = () => SelectCall(index);
                Hook(fingerButtons[i], fingerActions[i]); Hook(callButtons[i], callActions[i]);
            }
            Hook(confirmButton, Confirm); Hook(nextButton, Next); Hook(backButton, Back);
            Hook(chooseCallButton, ChooseCall); Hook(changeFingerButton, ChangeFinger); Hook(detailsButton, ToggleDetails);
            return UniTask.CompletedTask;
        }
        public void BeginRound(string opponent, string drunkStatus)
        {
            StopResultReveal();
            title.text = "与" + opponent + "划拳"; status.text = drunkStatus;
            resultLabel.text = "各出一手，喊双方总数。只一人猜中才分胜负，输者喝一杯。";
            pending = true; choosingCall = detailsOpen = false;
            if (detailsLabel != null) detailsLabel.text = "";
            OnRefresh();
        }
        public void SelectFinger(int value) { if (!pending || value < 0 || value > 5 || value == fingers) return; fingers = value; OnRefresh(); }
        public void SelectCall(int value) { if (!pending || value < 0 || value > 5 || value == offset) return; offset = value; OnRefresh(); }
        private void ChooseCall() { if (pending) { choosingCall = true; OnRefresh(); } }
        private void ChangeFinger() { if (pending) { choosingCall = false; OnRefresh(); } }
        private void ToggleDetails() { if (!pending) { detailsOpen = !detailsOpen; OnRefresh(); } }
        public void Confirm()
        {
            if (!pending || closed == null || closed.Task.Status != UniTaskStatus.Pending) return;
            pending = false; OnRefresh(); OnGuess?.Invoke(fingers, SelectedCall);
        }
        public void ShowResult(in TavernFingerGuessRules.Result result, string opponent, string drunkStatus, string message)
        {
            string verdict = result.Value == TavernFingerGuessRules.Outcome.Tie ? "平局，都不喝"
                : result.Value == TavernFingerGuessRules.Outcome.PlayerWins ? opponent + "喝一杯" : "你喝一杯";
            string detail = $"你：{result.Fingers} 指，喊 {result.Call}（{Calls[result.Call]}）\n{opponent}：{result.OpponentFingers} 指，喊 {result.OpponentCall}（{Calls[result.OpponentCall]}）\n合计 {result.Total} 指" +
                (string.IsNullOrEmpty(message) ? "" : "\n" + message);
            status.text = drunkStatus;
            ShowOutcome(verdict, detail, string.IsNullOrEmpty(message) ? "查看出拳详情" : "新消息 · 展开");
        }
        public void Reject(string message) => ShowOutcome("暂不能出拳", message, "查看原因");
        private void ShowOutcome(string summary, string detail, string hint)
        {
            resultLabel.text = detailsLabel == null ? summary + "\n" + detail : summary;
            if (detailsLabel != null) detailsLabel.text = detail;
            detailsHint = hint; detailsOpen = false; pending = false; OnRefresh(); RevealResult();
        }
        private void Next() { if (!pending && closed != null && closed.Task.Status == UniTaskStatus.Pending) OnNext?.Invoke(); }
        private void Back() => closed?.TrySetResult();
        public UniTask WaitAsync(CancellationToken ct) => closed.Task.AttachExternalCancellation(ct);
        public override void OnRefresh()
        {
            var events = EventSystem.current;
            var current = events == null ? null : events.currentSelectedGameObject;
            for (int i = 0; i < 6; i++)
            {
                fingerButtons[i].interactable = pending; callButtons[i].interactable = pending;
                SetChoice(fingerButtons[i], i == fingers); SetChoice(callButtons[i], i == offset);
                int call = fingers + i;
                callLabels[i].text = callRoot == null ? call + "\n" + Calls[call] : call.ToString();
            }
            confirmButton.interactable = pending; nextButton.interactable = !pending;
            if (fingerRoot != null)
            {
                SetActive(fingerRoot, pending && !choosingCall); SetActive(callRoot, pending && choosingCall);
                SetActive(chooseCallButton.gameObject, pending && !choosingCall);
                SetActive(changeFingerButton.gameObject, pending && choosingCall);
                SetActive(confirmButton.gameObject, pending && choosingCall); SetActive(nextButton.gameObject, !pending);
                SetActive(detailsButton.gameObject, !pending); SetActive(detailsLabel.gameObject, !pending && detailsOpen);
                detailsButtonLabel.text = detailsOpen ? "收起详情" : detailsHint;
                if (pending) resultLabel.text = choosingCall ? "你出 " + fingers + " 指，猜双方总数" : "出几指？";
            }
            if (current != null && current.transform.IsChildOf(transform))
            {
                var selected = current.GetComponent<Button>();
                if (selected != null && (!selected.interactable || !selected.IsActive()))
                    events.SetSelectedGameObject(!pending ? nextButton.gameObject
                        : choosingCall ? callButtons[offset].gameObject : fingerButtons[fingers].gameObject);
            }
        }
        private static void SetActive(GameObject target, bool active) { if (target.activeSelf != active) target.SetActive(active); }
        private void SetChoice(Button button, bool selected)
        {
            if (choiceIdle != null && choiceSelected != null)
            {
                var sprite = selected ? choiceSelected : choiceIdle;
                if (button.image.sprite != sprite) button.image.sprite = sprite;
            }
            else button.image.color = selected ? new Color(.66f, .45f, .2f) : new Color(.2f, .25f, .26f);
        }
        private void RevealResult()
        {
            StopResultReveal();
            if (resultGroup == null || resultRevealSeconds <= 0f || !isActiveAndEnabled) return;
            resultGroup.alpha = 0f;
            resultReveal = LMotion.Create(0f, 1f, resultRevealSeconds).WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).BindToAlpha(resultGroup).AddTo(gameObject);
        }
        private void OnDisable() => StopResultReveal();
        private void OnDestroy() => StopResultReveal();
        private void StopResultReveal()
        {
            if (resultReveal.IsActive()) resultReveal.Cancel();
            if (resultGroup != null) resultGroup.alpha = 1f;
        }
        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            StopResultReveal();
            if (fingerButtons != null && callButtons != null)
                for (int i = 0; i < 6 && i < fingerButtons.Length && i < callButtons.Length; i++)
                { Unhook(fingerButtons[i], fingerActions[i]); Unhook(callButtons[i], callActions[i]); }
            Unhook(confirmButton, Confirm); Unhook(nextButton, Next); Unhook(backButton, Back);
            Unhook(chooseCallButton, ChooseCall); Unhook(changeFingerButton, ChangeFinger); Unhook(detailsButton, ToggleDetails);
            pending = false; closed?.TrySetResult(); OnGuess = null; OnNext = null;
            return UniTask.CompletedTask;
        }
    }
}
