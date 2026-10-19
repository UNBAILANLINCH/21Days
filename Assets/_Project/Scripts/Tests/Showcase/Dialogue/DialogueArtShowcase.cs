// 职责：本批气泡、通知与沉浸外观的真实预制体回放，保留旧入口失败证据。
// 旧探索回放被正式序章隐藏示例物体，不能复用；扩展它会混入剧情迁移，故独立验证公开表现入口。
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using Game.IsometricExploration;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Dialogue
{
    [Category("Showcase")]
    public sealed class DialogueArtShowcase : ShowcaseScenario
    {
        protected override string Module => "Dialogue";
        protected override string ScenePath => ShowcaseOptions.DemoScenePath;

        [UnityTest]
        public IEnumerator BatchArt_PauseQueueImmersiveAndBubbleLifecycle()
        {
            yield return WaitUntil("标题与探索 HUD 已经真实 UI 服务打开", () =>
                ResolveService<IUIService>() != null && ResolveService<IUIService>().Get<TitleView>() != null
                && ResolveService<IUIService>().Get<ExplorationHudView>() != null, 20f);
            IUIService ui = ResolveService<IUIService>();
            yield return ui.CloseAsync(ui.Get<TitleView>()).ToCoroutine();
            var hud = ui.Get<ExplorationHudView>();
            var hidden = ResolveService<IHudVisibility>();
            var notifications = ResolveService<INotificationService>();
            var npc = FindRequired<DialogueInteractable>("Npc_Villager");
            var bubble = npc.GetComponentInChildren<DialogueSpeechBubble>(true);
            Assert.That(bubble, Is.Not.Null);
            var body = FindDeep<TMP_Text>(bubble.transform, "Body");
            var overlay = FindRequired<ShowcaseOverlay>("ShowcaseOverlay");
            var button = FindDeep<Button>(hud.transform, "ImmersiveButton");
            var exitLabel = FindDeep<TMP_Text>(button.transform, "Label");
            var follow = Camera.main.GetComponent<SmoothCameraFollow>();
            Assert.That(follow, Is.Not.Null);
            follow.SetTarget(npc.transform);
            yield return WaitUntil("只将验证镜头对准气泡 NPC，不启动正式序章", () =>
                Vector3.Distance(follow.transform.position, npc.transform.position + follow.Offset) < .1f, 4f);

            using (ResolveService<IWorldPauseService>().Acquire(this))
            {
                yield return Step("暂停中展示气泡，逐字和淡入继续运行", () =>
                {
                    bubble.Show("这井水……今天格外凉。你也听见井下的声音了吗？");
                }, 0f);
                overlay.enabled = false;
                for (int frame = 0; frame < 12; frame++)
                {
                    yield return Snapshot($"UI动效{frame:00}");
                    yield return new WaitForSecondsRealtime(0.06f);
                }
                overlay.enabled = true;
                yield return Check("timeScale 为 0 时气泡仍完整逐字、文字不溢出", () => Time.timeScale == 0f
                    && bubble.IsShowing && body.maxVisibleCharacters >= body.textInfo.characterCount
                    && !body.isTextOverflowing && FitsViewport(bubble), 4f);
                overlay.enabled = false;
                yield return Snapshot("阳间气泡正式切片");
                overlay.enabled = true;

                yield return Step("重复触发气泡，随后在淡入中禁用并重开", () =>
                {
                    bubble.Show("新句重播");
                    bubble.gameObject.SetActive(false);
                }, 0f);
                yield return Check("关闭时停止并还原 alpha，不留下迟到动画", () => !bubble.IsShowing
                    && !bubble.transform.Find("Content").gameObject.activeSelf
                    && Mathf.Approximately(bubble.GetComponent<CanvasGroup>().alpha, 1f));
                bubble.gameObject.SetActive(true);
                bubble.Show("长句换行验证：这井水今天格外凉。你也听见井下的声音了吗？"
                    + "我在井边等了许久，风停了，水面依然泛着波纹。不要靠得太近。我们明天再来看看。");
                yield return Check("长中文布局随全文一次排版增高，名字与正文不裁切", () =>
                    !body.isTextOverflowing && body.preferredHeight <= body.rectTransform.rect.height + .5f
                    && body.maxVisibleCharacters >= body.textInfo.characterCount && FitsViewport(bubble), 5f);
                overlay.enabled = false;
                yield return Snapshot("长中文气泡");
                overlay.enabled = true;
                bubble.gameObject.SetActive(false);

                notifications.Show("获得物资", "治疗药水 ×2", 3f);
                yield return Check("真实通知队列显示新底框且淡入完成", () => ui.Get<NotificationView>() != null
                    && ui.Get<NotificationView>().IsCardShown
                    && ui.Get<NotificationView>().transform.Find("Card").GetComponent<CanvasGroup>().alpha > .99f, 3f);
                yield return Snapshot("物资正式切片");
                notifications.Show("获得物资", "破旧信笺 ×1", 1.2f);
                notifications.Show("获得物资", "治疗药水 ×2\n水痕 ×1\n进入废弃官署的钥匙 ×1", 3f);
                yield return Check("同标题待显示通知合并为最后内容，多项正文可换行", () =>
                {
                    NotificationView view = ui.Get<NotificationView>();
                    if (view == null || !view.IsCardShown) return false;
                    var text = FindDeep<TMP_Text>(view.transform, "Body");
                    return text != null && text.text.Contains("进入废弃官署") && !text.isTextOverflowing;
                }, 6f);
                yield return Check("通知进场完成后再记录多行布局", () =>
                    ui.Get<NotificationView>().transform.Find("Card").GetComponent<CanvasGroup>().alpha > .99f, 1f);
                yield return Snapshot("物资连发多行数量");
                notifications.Show("通知标题无正文", null, .6f);
                yield return Check("空正文隐藏正文行，队列继续退出", () => ui.Get<NotificationView>() != null
                    && FindDeep<TMP_Text>(ui.Get<NotificationView>().transform, "Title").text == "通知标题无正文"
                    && !FindDeep<TMP_Text>(ui.Get<NotificationView>().transform, "Body").gameObject.activeSelf, 6f);
            }

            yield return Step("射线命中新按钮，点击进入沉浸", () =>
            {
                bubble.gameObject.SetActive(true);
                bubble.Show("进入沉浸时收起这一句");
                var point = new PointerEventData(EventSystem.current)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null,
                        button.GetComponent<RectTransform>().TransformPoint(button.GetComponent<RectTransform>().rect.center))
                };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(point, hits);
                Assert.That(hits.Exists(hit => hit.gameObject == button.gameObject), Is.True, "子 Canvas 须保留实际命中");
                ExecuteEvents.Execute(button.gameObject, point, ExecuteEvents.pointerClickHandler);
            }, 0f);
            yield return Check("沉浸后按钮可用、退出文字显示、气泡收起", () => hidden.IsHudHidden
                && button.IsInteractable() && exitLabel.text == "退出沉浸" && !bubble.IsShowing, 3f);
            yield return Snapshot("沉浸状态与退出提示");
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            yield return Input.Press(ResolveService<IInputService>().Actions.UI.Submit);
            yield return Check("UI Submit 导航可退出沉浸并恢复 HUD", () => !hidden.IsHudHidden
                && exitLabel.text.Length == 0 && button.IsInteractable(), 3f);
            bubble.Show(string.Empty);
            yield return Check("空台词安全进入停留并在既有时长后淡出", () => !bubble.IsShowing,
                bubble.HoldSeconds + 2f);
            yield return Snapshot("退出沉浸与动画收尾");
        }

        private static bool FitsViewport(DialogueSpeechBubble bubble)
        {
            foreach (TMP_Text text in bubble.GetComponentsInChildren<TMP_Text>())
                if (Quaternion.Angle(text.transform.rotation, Camera.main.transform.rotation) > 1f)
                    return false;
            var corners = new Vector3[4];
            ((RectTransform)bubble.transform).GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 viewport = Camera.main.WorldToViewportPoint(corner);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                    return false;
            }
            return true;
        }
    }
}
