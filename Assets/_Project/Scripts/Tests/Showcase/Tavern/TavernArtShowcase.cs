// 职责：真实酒肆入口验证分步UI、输入、暂停、简洁结果与生命周期；原TavernShowcase保留学习/存档/战斗回归。
// 复用五店进场、取皮和公开UI；独立文件避免与并发战场回放编辑同一文件。
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Session;
using Game.ShopPlaytest;
using Game.Tavern;
using Game.Tests.Showcase.ShopPlaytest;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Game.Tests.Showcase.Tavern
{
    [Category("Showcase")]
    public sealed class TavernArtShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "Tavern";
        private TavernFingerGuessView View => ResolveService<IUIService>().Get<TavernFingerGuessView>();
        [UnityTest, Timeout(600000)]
        public IEnumerator Guess_StepsInputCompactResultCancelAndReopen()
        {
            yield return ReadyUnlockedShop(4);
            yield return Step("三店成果为夹具；取皮、入席和出拳仍走真实入口", () =>
            {
                var loot = ResolveService<Game.Loot.LootService>();
                loot.GrantItems(ShopRewardRules.CostumeId, 1); loot.GrantItems(ShopRewardRules.ClothesId, 1);
                loot.GrantItems(ShopRewardRules.ClaspId, 1); loot.GrantItems(ShopRewardRules.BeltHookId, 2); loot.GrantItems(ShopRewardRules.RingId, 3);
            }, 0);
            yield return Borrow(0); yield return OpenGuess();
            var input = ResolveService<IInputService>(); var view = View;
            yield return Check("第一步只显示出指，世界已暂停且玩法输入关闭", () => !view.IsChoosingCall &&
                view.transform.Find("FingerRoot").gameObject.activeInHierarchy && !view.transform.Find("CallRoot").gameObject.activeInHierarchy &&
                !Button("Confirm").gameObject.activeInHierarchy && ResolveService<IWorldPauseService>().IsPaused && !input.Actions.Gameplay.enabled);
            yield return Snapshot("行酒令_第一步出指");
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(Button("Finger1").gameObject));
            yield return Input.HoldStick(Vector2.right, .08f); yield return Input.Press(input.Actions.UI.Submit);
            yield return Check("手柄右移并确认选择2指", () => view.SelectedFingers == 2);
            yield return Input.HoldStick(Vector2.down, .08f); yield return Input.Press(input.Actions.UI.Submit);
            yield return Check("第二步只显示合法总数，焦点从隐藏按钮转入喊数", () => view.IsChoosingCall &&
                !view.transform.Find("FingerRoot").gameObject.activeInHierarchy && view.transform.Find("CallRoot").gameObject.activeInHierarchy &&
                EventSystem.current.currentSelectedGameObject == Button("Call4").gameObject);
            UnityEngine.InputSystem.Controls.KeyControl left = null;
            foreach (var binding in input.Actions.UI.Navigate.bindings)
                if (binding.isPartOfComposite && binding.name == "left" && binding.effectivePath.StartsWith("<Keyboard>"))
                    left = UnityEngine.InputSystem.InputControlPath.TryFindControl(UnityEngine.InputSystem.Keyboard.current, binding.effectivePath) as UnityEngine.InputSystem.Controls.KeyControl;
            Assert.That(left, Is.Not.Null);
            yield return Input.PressKey(left.keyCode); yield return Input.Press(input.Actions.UI.Submit);
            yield return Check("键盘左移确认5，重选出指不丢失选择", () => view.SelectedCall == 5);
            Click(Button("ChangeFinger")); yield return null; Assert.That(view.SelectedFingers, Is.EqualTo(2));
            Click(Button("ChooseCall")); yield return null; Assert.That(view.SelectedCall, Is.EqualTo(5));
            yield return Snapshot("行酒令_第二步猜总数");
            bool saved = true;
            yield return ResolveService<GameSession>().SaveNowAsync("tavern_ui_pending").ToCoroutine(value => saved = value);
            yield return Check("未揭晓仍通过原存档门闸拒绝保存", () => !saved);
            yield return CaptureResult(Button("Confirm"));
            yield return Check("结算只给一行结论，选项收起，暂停中淡入完成", () => !view.IsAwaitingGuess &&
                !view.ResultText.Contains("\n") && (view.ResultText.Contains("喝一杯") || view.ResultText.Contains("平局")) &&
                !view.transform.Find("FingerRoot").gameObject.activeInHierarchy && !view.transform.Find("CallRoot").gameObject.activeInHierarchy &&
                view.transform.Find("Result").GetComponent<CanvasGroup>().alpha > .99f, 3);
            int history = HistoryCount();
            yield return Check("一次真实确认只记一手", () => history == 1);
            yield return Snapshot("行酒令_简洁结果");
            Click(Button("DetailsButton")); yield return null;
            yield return Check("需要时才展开双方出拳详情，结果仍在上方", () =>
                view.transform.Find("Details").gameObject.activeInHierarchy &&
                view.transform.Find("Details").GetComponent<TMP_Text>().text.Contains("合计"));
            yield return Snapshot("行酒令_按需展开详情"); Click(Button("DetailsButton")); yield return null;
            yield return Step("仅验证视图生命周期：淡入途中隐藏，重新显示再开下一拳", () =>
            {
                view.Reject("生命周期夹具，不改已结算结果或醉意");
                view.gameObject.SetActive(false);
                Assert.That(view.transform.Find("Result").GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
                view.gameObject.SetActive(true);
            }, 0);
            Click(Button("Next")); yield return null;
            yield return Check("再来回到出指，隐藏时的旧动效已停止", () => view.IsAwaitingGuess && !view.IsChoosingCall &&
                view.transform.Find("Result").GetComponent<CanvasGroup>().alpha > .99f, 3);
            yield return Input.Press(input.Actions.UI.Cancel); yield return WaitClosed();
            yield return Check("未揭晓取消不新增历史", () => HistoryCount() == history);
            yield return OpenGuess(); yield return Snapshot("行酒令_重新入席");
            Click(Button("Back")); yield return WaitClosed();
            yield return Check("离席与重开都释放暂停并恢复输入", () => !ResolveService<IWorldPauseService>().IsPaused &&
                input.Actions.Gameplay.enabled && HistoryCount() == history);
        }
        private IEnumerator OpenGuess()
        {
            yield return Command(1, false);
            yield return WaitUntil("真实主人对白后进入可操作行酒令", () => View != null && View.IsAwaitingGuess, 5);
            Assert.That(View, Is.Not.Null);
        }
        private IEnumerator WaitClosed() => WaitUntil("离席恢复街面输入与时间", () => View == null &&
            !ResolveService<IWorldPauseService>().IsPaused && ResolveService<IInputService>().Actions.Gameplay.enabled, 5);
        private int HistoryCount()
        {
            var learning = ResolveService<ISaveService>().Get<ShopProgressSaveData>().TavernLearning;
            return learning.TryGetValue("shop_boss", out var data) ? data.History.Count : 0;
        }
        private Button Button(string name)
        {
            var button = FindDeep<Button>(View.transform, name); Assert.That(button, Is.Not.Null, name); return button;
        }
        private static void Click(Button button)
        {
            Assert.That(button.IsActive() && button.IsInteractable(), Is.True, button.name);
            Canvas.ForceUpdateCanvases(); var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform), Is.True,
                "实际射线首个命中：" + button.name + "，实际=" + (hits.Count == 0 ? "无" : hits[0].gameObject.name));
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        private IEnumerator CaptureResult(Button confirm)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Click(confirm); yield break; }
            var frames = new List<Texture2D>(); var times = new List<string>();
            var overlay = FindRequired<ShowcaseOverlay>("ShowcaseOverlay"); bool visible = overlay.enabled; overlay.enabled = false;
            try
            {
                yield return new WaitForEndOfFrame(); frames.Add(ScreenCapture.CaptureScreenshotAsTexture());
                times.Add(Time.realtimeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Click(confirm);
                for (int i = 0; i < 12; i++)
                {
                    yield return null; yield return new WaitForEndOfFrame(); frames.Add(ScreenCapture.CaptureScreenshotAsTexture());
                    times.Add(Time.realtimeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                string path = System.IO.Path.Combine(ShowcaseOptions.ReportRoot, "tavern-motion", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                System.IO.Directory.CreateDirectory(path);
                for (int i = 0; i < frames.Count; i++) System.IO.File.WriteAllBytes(System.IO.Path.Combine(path, "frame-" + i.ToString("00") + ".png"), frames[i].EncodeToPNG());
                System.IO.File.WriteAllLines(System.IO.Path.Combine(path, "times.txt"), times);
            }
            finally
            {
                foreach (var frame in frames) if (frame != null) Object.Destroy(frame);
                overlay.enabled = visible;
            }
        }
    }
}
