// 职责：出指、喊数和同时揭晓的表现；事件交给接线方验证，不注入服务或修改醉意。
// 现有打造视图只有圆盘操作，扩展会混入另一玩法；复用 UIView 的栈和关闭生命周期。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
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
        private readonly UnityAction[] fingerActions = new UnityAction[6];
        private readonly UnityAction[] callActions = new UnityAction[6];
        private UniTaskCompletionSource closed;
        private int fingers = 1, offset = 4;
        private bool pending;
        public event Action<int, int> OnGuess;
        public event Action OnNext;
        public override UILayer Layer => UILayer.Panel;
        public int SelectedFingers => fingers;
        public int SelectedCall => fingers + offset;
        public bool IsAwaitingGuess => pending;
        public string ResultText => resultLabel == null ? "" : resultLabel.text;
        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (title == null || status == null || resultLabel == null || confirmButton == null || nextButton == null || backButton == null ||
                fingerButtons == null || fingerButtons.Length != 6 || callButtons == null || callButtons.Length != 6 || callLabels == null || callLabels.Length != 6)
                throw new InvalidOperationException("划拳预制体引用未接齐");
            closed = new UniTaskCompletionSource();
            for (int i = 0; i < 6; i++)
            {
                if (fingerButtons[i] == null || callButtons[i] == null || callLabels[i] == null) throw new InvalidOperationException("划拳按钮未接齐");
                int index = i;
                fingerActions[i] = () => SelectFinger(index); callActions[i] = () => SelectCall(index);
                Hook(fingerButtons[i], fingerActions[i]); Hook(callButtons[i], callActions[i]);
            }
            Hook(confirmButton, Confirm); Hook(nextButton, Next); Hook(backButton, Back);
            return UniTask.CompletedTask;
        }
        public void BeginRound(string opponent, string drunkStatus)
        {
            title.text = "与" + opponent + "划拳"; status.text = drunkStatus;
            resultLabel.text = "各出一手，喊双方总数。只一人猜中才分胜负，输者喝一杯。";
            pending = true; OnRefresh();
        }
        public void SelectFinger(int value) { if (!pending || value < 0 || value > 5) return; fingers = value; OnRefresh(); }
        public void SelectCall(int value) { if (!pending || value < 0 || value > 5) return; offset = value; OnRefresh(); }
        public void Confirm()
        {
            if (!pending || closed == null || closed.Task.Status != UniTaskStatus.Pending) return;
            pending = false; OnRefresh(); OnGuess?.Invoke(fingers, SelectedCall);
        }
        public void ShowResult(in TavernFingerGuessRules.Result result, string opponent, string drunkStatus, string message)
        {
            string verdict = result.Value == TavernFingerGuessRules.Outcome.Tie ? "平局，都不喝"
                : result.Value == TavernFingerGuessRules.Outcome.PlayerWins ? opponent + "喝一杯" : "你喝一杯";
            resultLabel.text = $"你：{result.Fingers} 指，喊 {result.Call}（{Calls[result.Call]}）\n{opponent}：{result.OpponentFingers} 指，喊 {result.OpponentCall}（{Calls[result.OpponentCall]}）\n合计 {result.Total} 指 · {verdict}" + (string.IsNullOrEmpty(message) ? "" : "\n" + message);
            status.text = drunkStatus; pending = false; OnRefresh();
        }
        public void Reject(string message) { resultLabel.text = message; pending = false; OnRefresh(); }
        private void Next() { if (!pending && closed != null && closed.Task.Status == UniTaskStatus.Pending) OnNext?.Invoke(); }
        private void Back() => closed?.TrySetResult();
        public UniTask WaitAsync(CancellationToken ct) => closed.Task.AttachExternalCancellation(ct);
        public override void OnRefresh()
        {
            for (int i = 0; i < 6; i++)
            {
                fingerButtons[i].interactable = pending; callButtons[i].interactable = pending;
                fingerButtons[i].image.color = i == fingers ? new Color(.66f, .45f, .2f) : new Color(.2f, .25f, .26f);
                callButtons[i].image.color = i == offset ? new Color(.66f, .45f, .2f) : new Color(.2f, .25f, .26f);
                int call = fingers + i; callLabels[i].text = call + "\n" + Calls[call];
            }
            confirmButton.interactable = pending; nextButton.interactable = !pending;
        }
        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (fingerButtons != null && callButtons != null)
                for (int i = 0; i < 6 && i < fingerButtons.Length && i < callButtons.Length; i++)
                { Unhook(fingerButtons[i], fingerActions[i]); Unhook(callButtons[i], callActions[i]); }
            Unhook(confirmButton, Confirm); Unhook(nextButton, Next); Unhook(backButton, Back);
            pending = false; closed?.TrySetResult(); OnGuess = null; OnNext = null;
            return UniTask.CompletedTask;
        }
    }
}
