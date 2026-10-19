// 职责：分步选择、单次提交、渐进详情与短动效收尾；纯规则穷举不覆盖这些UI行为，故补视图夹具。
// 复用原规则结果与UGUI组件，不读生产预制体或改玩法数据。
using System.Collections;
using System.Reflection;
using System.Threading;
using Game.Tavern;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Game.Tests.EditMode.Tavern
{
    public sealed class TavernFingerGuessViewTests
    {
        private GameObject root, eventsHost, fingerRoot, callRoot;
        private EventSystem previousEvents;
        private TavernFingerGuessView view;
        private Button[] fingers, calls;
        private TMP_Text[] callLabels;
        private Button confirm, next, chooseCall, changeFinger, detailsButton;
        private TMP_Text details, detailsButtonLabel;
        private CanvasGroup resultGroup;
        private Sprite idle, selected;
        private Texture2D texture;
        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Guess", typeof(RectTransform)); view = root.AddComponent<TavernFingerGuessView>();
            Set("title", Text("Title")); Set("status", Text("Status"));
            var result = Text("Result"); Set("resultLabel", result);
            resultGroup = result.gameObject.AddComponent<CanvasGroup>(); Set("resultGroup", resultGroup);
            fingerRoot = Node("Fingers"); callRoot = Node("Calls");
            Set("fingerRoot", fingerRoot); Set("callRoot", callRoot);
            fingers = new Button[6]; calls = new Button[6]; callLabels = new TMP_Text[6];
            for (int i = 0; i < 6; i++)
            {
                fingers[i] = Button("Finger" + i); fingers[i].transform.SetParent(fingerRoot.transform, false);
                calls[i] = Button("Call" + i); calls[i].transform.SetParent(callRoot.transform, false);
                callLabels[i] = Text("CallText" + i);
            }
            Set("fingerButtons", fingers); Set("callButtons", calls); Set("callLabels", callLabels);
            confirm = Button("Confirm"); next = Button("Next"); chooseCall = Button("ChooseCall"); changeFinger = Button("ChangeFinger");
            detailsButton = Button("DetailsButton"); details = Text("Details"); detailsButtonLabel = Text("DetailsButtonLabel");
            Set("confirmButton", confirm); Set("nextButton", next); Set("backButton", Button("Back"));
            Set("chooseCallButton", chooseCall); Set("changeFingerButton", changeFinger);
            Set("detailsButton", detailsButton); Set("detailsLabel", details); Set("detailsButtonLabel", detailsButtonLabel);
            texture = new Texture2D(2, 2); idle = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            selected = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            Set("choiceIdle", idle); Set("choiceSelected", selected);
            view.OnOpenAsync(null, CancellationToken.None).GetAwaiter().GetResult(); view.BeginRound("客人甲", "醉意0");
        }
        [TearDown]
        public void TearDown()
        {
            if (view != null) view.OnCloseAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (root != null) Object.DestroyImmediate(root);
            if (eventsHost != null) { Object.DestroyImmediate(eventsHost); EventSystem.current = previousEvents; }
            Object.DestroyImmediate(idle); Object.DestroyImmediate(selected); Object.DestroyImmediate(texture);
        }
        [Test]
        public void Steps_OnlyCurrentChoicesShowAndBackKeepsSelection()
        {
            Assert.That(fingerRoot.activeSelf && !callRoot.activeSelf && !confirm.gameObject.activeSelf, Is.True);
            fingers[3].onClick.Invoke(); chooseCall.onClick.Invoke();
            Assert.That(!fingerRoot.activeSelf && callRoot.activeSelf && confirm.gameObject.activeSelf, Is.True);
            calls[2].onClick.Invoke(); changeFinger.onClick.Invoke();
            Assert.That(view.SelectedFingers, Is.EqualTo(3)); Assert.That(view.SelectedCall, Is.EqualTo(5));
            Assert.That(fingerRoot.activeSelf && !callRoot.activeSelf, Is.True);
            chooseCall.onClick.Invoke(); Assert.That(view.SelectedCall, Is.EqualTo(5));
        }
        [Test]
        public void Selection_AllChoicesStayLegalAndMovePersistentHighlight()
        {
            for (int finger = 0; finger < 6; finger++)
            {
                view.SelectFinger(finger);
                for (int call = 0; call < 6; call++)
                {
                    view.SelectCall(call);
                    Assert.That(TavernFingerGuessRules.IsLegal(view.SelectedFingers, view.SelectedCall), Is.True);
                    Assert.That(view.SelectedCall, Is.EqualTo(finger + call));
                    Assert.That(callLabels[call].text, Is.EqualTo((finger + call).ToString()));
                    for (int i = 0; i < 6; i++)
                    {
                        Assert.That(fingers[i].image.sprite, Is.SameAs(i == finger ? selected : idle));
                        Assert.That(calls[i].image.sprite, Is.SameAs(i == call ? selected : idle));
                    }
                }
            }
        }
        [Test]
        public void Confirm_OnceLocksChoicesAndTransfersDisabledFocus()
        {
            previousEvents = EventSystem.current; eventsHost = new GameObject("Events", typeof(EventSystem));
            var events = eventsHost.GetComponent<EventSystem>(); EventSystem.current = events;
            events.SetSelectedGameObject(chooseCall.gameObject); chooseCall.onClick.Invoke();
            Assert.That(events.currentSelectedGameObject, Is.SameAs(calls[4].gameObject));
            int submitted = 0; view.OnGuess += (f, c) => submitted++;
            events.SetSelectedGameObject(confirm.gameObject); confirm.onClick.Invoke(); confirm.onClick.Invoke();
            Assert.That(submitted, Is.EqualTo(1));
            Assert.That(fingerRoot.activeSelf || callRoot.activeSelf || confirm.gameObject.activeSelf, Is.False);
            Assert.That(events.currentSelectedGameObject, Is.SameAs(next.gameObject));
            view.BeginRound("客人甲", "醉意10");
            Assert.That(events.currentSelectedGameObject, Is.SameAs(fingers[view.SelectedFingers].gameObject));
        }
        [Test]
        public void Result_CompactVerdictKeepsNewMessageBehindDiscoverableDetails()
        {
            var result = new TavernFingerGuessRules.Result(1, 2, 1, 3, TavernFingerGuessRules.Outcome.PlayerWins);
            view.ShowResult(in result, "客人乙", "醉意10", "一条只能领取一次的消息");
            Assert.That(view.ResultText, Is.EqualTo("客人乙喝一杯"));
            Assert.That(details.gameObject.activeSelf, Is.False);
            Assert.That(detailsButtonLabel.text, Does.Contain("新消息"));
            detailsButton.onClick.Invoke();
            Assert.That(details.gameObject.activeSelf, Is.True);
            Assert.That(details.text, Does.Contain("一条只能领取一次的消息").And.Contain("合计 2 指"));
            detailsButton.onClick.Invoke(); Assert.That(details.gameObject.activeSelf, Is.False);
            view.Reject("当前身份不能出拳"); detailsButton.onClick.Invoke();
            Assert.That(view.ResultText, Is.EqualTo("暂不能出拳"));
            Assert.That(details.text, Is.EqualTo("当前身份不能出拳"));
        }
        [UnityTest]
        public IEnumerator Result_InterruptedHiddenClosedAndDestroyed_StopsReveal()
        {
            view.Reject("当前不能划拳"); Assert.That(resultGroup.alpha, Is.Zero);
            InvokeLifecycle("OnDisable"); Assert.That(resultGroup.alpha, Is.EqualTo(1f));
            view.Reject("再次拒绝"); view.OnCloseAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(resultGroup.alpha, Is.EqualTo(1f));
            view.OnOpenAsync(null, CancellationToken.None).GetAwaiter().GetResult(); view.BeginRound("客人乙", "醉意0");
            view.Reject("本轮结束");
            // EditMode不派发未唤醒MonoBehaviour的消息，显式验证同一销毁入口；真实生命周期由Play回放覆盖。
            InvokeLifecycle("OnDestroy"); Object.DestroyImmediate(root); root = null;
            for (int i = 0; i < 4; i++) yield return null;
        }
        private void InvokeLifecycle(string name) => typeof(TavernFingerGuessView)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        private GameObject Node(string name)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(root.transform, false); return go;
        }
        private TMP_Text Text(string name) => Node(name).AddComponent<TextMeshProUGUI>();
        private Button Button(string name)
        {
            var go = Node(name); var image = go.AddComponent<Image>(); var button = go.AddComponent<Button>();
            button.targetGraphic = image; return button;
        }
        private void Set(string name, object value) => typeof(TavernFingerGuessView)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, value);
    }
}
