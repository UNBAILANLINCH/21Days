// 职责：经正式新游戏验证小任务栏、交互提示、成功通知队列与本批确认金饰。
// 为什么新建：旧任务美术回放只测 Tab 面板；复用进场与报告框架，隔离本批提示边界，保留前批回放。
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using Game.Interaction;
using Game.Inventory;
using Game.Loot;
using Game.Quest;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Quest
{
    [Category("Showcase")]
    public sealed class QuestHudArtShowcase : Game.Tests.Showcase.ShopPlaytest.ShopPlaytestShowcase
    {
        protected override string Module => "Quest";
        protected override float BootShutdownSettleSeconds => .5f;

        [UnityTest, Timeout(240000)]
        public IEnumerator HudPromptCompletionAndConfirmedIcons()
        {
            yield return EnterStoryWorld();
            yield return WaitCurtainRevealed();
            yield return AdvanceFormalDialogue();
            var ui = ResolveService<IUIService>();
            var input = ResolveService<IInputService>();
            var service = ResolveService<QuestService>();
            var panel = ResolveService<QuestPanelController>();
            var dialogue = ResolveService<DialogueService>();
            var registry = ResolveService<IInteractionRegistry>();
            var focus = ResolveService<IInteractionFocus>();
            var hidden = ResolveService<IHudVisibility>();
            var notices = ResolveService<INotificationService>();
            var pause = ResolveService<IWorldPauseService>();
            yield return WaitUntil("正式序章结束且小任务栏就绪", () => input.Actions.Gameplay.enabled
                && ui.Get<QuestHudView>() != null && registry.Actor != null, 8f);
            QuestHudView hud = ui.Get<QuestHudView>();
            var title = FindDeep<TMP_Text>(hud.transform, "Title");
            var objective = FindDeep<TMP_Text>(hud.transform, "Objective");
            var overlay = FindRequired<ShowcaseOverlay>("ShowcaseOverlay");
            var originalBindings = input.Actions.asset.SaveBindingOverridesAsJson();
            Vector3 origin = registry.Actor.Anchor.position;
            var first = new PromptProbe(origin, "旅人", null);
            var second = new PromptProbe(origin, "核验轮回堂旧日案卷的旅人", () =>
                dialogue.PlayAsync(ProbeDialogue(), null, "ui_widget_probe").Forget());
            try
            {
                yield return Step("真实小任务栏：核对追踪文字，点击后由 Tab 面板返回", null, 0f);
                yield return Check("900 的实际标题可见，文本框容纳字高", () => service.TryGet(900, out QuestProgress p)
                    && title.text == p.Definition.Title && title.textInfo.characterCount > 0
                    && title.preferredHeight <= title.rectTransform.rect.height + .5f, 3f);
                overlay.enabled = false;
                yield return Snapshot("小任务栏_真实追踪");
                overlay.enabled = true;
                Click(hud.transform.Find("Root").GetComponent<Button>());
                yield return Check("点击小任务栏打开真实任务面板", () => panel.IsOpen && pause.IsPaused, 4f);
                yield return Input.Press(input.Actions.UI.Cancel);
                yield return WaitUntil("任务面板收尾", () => !panel.IsOpen && input.Actions.Gameplay.enabled, 4f);
                service.Untrack();
                yield return Check("取消追踪清空旧目标，保留空态底板与任务键", () => objective.text == string.Empty
                    && hud.transform.Find("Root").gameObject.activeSelf
                    && FindDeep<TMP_Text>(hud.transform, "KeyHint").gameObject.activeSelf, 2f);
                overlay.enabled = false;
                yield return Snapshot("小任务栏_未追踪");
                overlay.enabled = true;
                service.Track(900);

                yield return Step("长文夹具：小任务栏换内容、中途隐藏与恢复", () =>
                    hud.SetTracked("核验轮回堂旅人留下的所有旧日案卷与身份资料",
                        "前往轮回堂核对登记资料，沿途调查见闻并向旅人核实旧日证词与物证。"), 0f);
                yield return Check("长标题省略、目标至多两行且落在底板内", () => title.isTextTruncated
                    && objective.textInfo.lineCount <= 2
                    && objective.rectTransform.rect.height >= 65f
                    && hud.transform.Find("Root/Content").GetComponent<CanvasGroup>().alpha > .99f, 3f);
                overlay.enabled = false;
                yield return Snapshot("小任务栏_长目标");
                overlay.enabled = true;
                hud.SetTracked("淡入中途切换", "验证隐藏时立即停止动画");
                hidden.SetHudHidden(true);
                yield return Check("沉浸时小任务栏和交互提示均隐藏", () => !hud.transform.Find("Root").gameObject.activeSelf
                    && hud.GetComponent<CanvasGroup>().alpha == 0f
                    && hud.transform.Find("Root/Content").GetComponent<CanvasGroup>().alpha == 1f
                    && (ui.Get<InteractPromptHudView>() == null || !ui.Get<InteractPromptHudView>().IsShown), 2f);
                hidden.SetHudHidden(false);
                service.Untrack(); service.Track(900);
                yield return WaitUntil("退出沉浸恢复小任务栏", () => hud.transform.Find("Root").gameObject.activeSelf, 2f);

                yield return Step("交互夹具经真实登记表进入／离开范围并切目标", () => registry.Register(first), 0f);
                yield return Check("最近目标出现 E 提示与交付字图", () => ReferenceEquals(focus.Current, first)
                    && ui.Get<InteractPromptHudView>() != null && ui.Get<InteractPromptHudView>().LabelText == "对话 · 旅人", 3f);
                var prompt = ui.Get<InteractPromptHudView>();
                yield return Input.Press(input.Actions.Gameplay.Execute);
                yield return Check("键盘绑定 E 时只显示一份烘焙 E", () => prompt.KeyText == "E"
                    && prompt.transform.Find("Root/KeyBadge/EArtwork").gameObject.activeSelf
                    && !FindDeep<TMP_Text>(prompt.transform, "KeyText").enabled, 2f);
                overlay.enabled = false;
                yield return Snapshot("交互提示_E与旅人");
                overlay.enabled = true;
                first.Position = origin + Vector3.right * 20f;
                yield return Check("目标离开半径后不再保留旧提示", () => !ReferenceEquals(focus.Current, first)
                    && (prompt.LabelText != "对话 · 旅人" || !prompt.IsShown), 3f);
                registry.Unregister(first); registry.Register(second);
                yield return Check("切换到新目标时动态文案更新", () => ReferenceEquals(focus.Current, second)
                    && prompt.LabelText.Contains("核验轮回堂"), 3f);

                yield return Step("改键与设备切换：Q、手柄，再用真实交互输入开对白", null, 0f);
                int keyIndex = input.Actions.Gameplay.Interact.GetBindingIndex(new InputBinding { path = "<Keyboard>/e" });
                Assert.That(keyIndex, Is.GreaterThanOrEqualTo(0));
                input.Actions.Gameplay.Interact.ApplyBindingOverride(keyIndex, "<Keyboard>/q");
                yield return Check("改键立即显示 Q 且隐藏 E 图", () => prompt.KeyText == "Q"
                    && !prompt.transform.Find("Root/KeyBadge/EArtwork").gameObject.activeSelf, 2f);
                yield return Input.HoldStick(Vector2.right, .04f);
                yield return Check("真实手柄动作使交互与任务键提示一起换设备", () => prompt.KeyText
                    == InputBindingDisplay.Read(input.Actions.Gameplay.Interact, true)
                    && FindDeep<TMP_Text>(hud.transform, "KeyHint").text
                    == InputBindingDisplay.Read(input.Actions.Gameplay.Journal, true), 2f);
                overlay.enabled = false;
                yield return Snapshot("交互提示_手柄与任务键");
                overlay.enabled = true;
                yield return Input.Press(input.Actions.Gameplay.Interact);
                yield return Check("实际 Q 动作仅交互一次并进入正常对白会话，两条 HUD 收起", () => second.Interactions == 1
                    && dialogue.IsRunning && !prompt.IsShown && !hud.transform.Find("Root").gameObject.activeSelf, 4f);
                yield return AdvanceFormalDialogue();
                yield return Check("对白收尾后 Gameplay 与提示恢复", () => !dialogue.IsRunning
                    && input.Actions.Gameplay.enabled && prompt.IsShown && hud.transform.Find("Root").gameObject.activeSelf, 4f);
                registry.Unregister(second);
                input.Actions.asset.LoadBindingOverridesFromJson(originalBindings);

                yield return Step("目标上报夹具经真实任务服务完成900，沿现有事件与通知队列显示", () =>
                {
                    QuestProgress progress;
                    Assert.That(service.TryGet(900, out progress), Is.True);
                    int limit = progress.Definition.Objectives.Count;
                    for (int i = 0; i < limit && progress.HasCurrentObjective; i++)
                    {
                        QuestObjectiveDefinition current = progress.CurrentObjective;
                        Assert.That(service.Report(current.Kind, current.Key, current.RequiredCount), Is.GreaterThan(0));
                    }
                    Assert.That(progress.State, Is.EqualTo(QuestState.Completed));
                }, 0f);
                yield return Check("现有完成事件显示独立成功底板、标题与真实900名称", () =>
                {
                    NotificationView n = ui.Get<NotificationView>();
                    return n != null && n.IsCardShown && FindDeep<TMP_Text>(n.transform, "Title").text == "完成任务"
                        && service.TryGet(900, out QuestProgress p) && FindDeep<TMP_Text>(n.transform, "Body").text == p.Definition.Title
                        && n.transform.Find("Card").GetComponent<Image>().sprite.name == "completion-panel";
                }, 8f);
                yield return WaitUntil("完成卡片淡入结束", () => ui.Get<NotificationView>().transform.Find("Card").GetComponent<CanvasGroup>().alpha > .99f, 2f);
                overlay.enabled = false;
                yield return Snapshot("完成任务_现有事件入口");
                overlay.enabled = true;
                yield return WaitUntil("当前队列空闲", () => !ui.Get<NotificationView>().IsCardShown, 8f);

                yield return Step("暂停中连发三条成功卡片，长名称自动增高，随后恢复物资样式", null, 0f);
                using (pause.Acquire(this))
                {
                    yield return CaptureMotion(() =>
                    {
                        notices.Show("完成任务", "核对轮回堂全部登记资料、调查沿途见闻与旧日证词", 3f, true);
                        notices.Show("完成任务", "第二条独立成果", .8f, true);
                        notices.Show("完成任务", "第三条独立成果", .8f, true);
                    }, overlay);
                    yield return Check("暂停时长任务名完整换行并完成淡入", () => Time.timeScale == 0f
                        && FindDeep<TMP_Text>(ui.Get<NotificationView>().transform, "Body").text.Contains("全部登记资料")
                        && !FindDeep<TMP_Text>(ui.Get<NotificationView>().transform, "Body").isTextOverflowing
                        && ui.Get<NotificationView>().transform.Find("Card").GetComponent<CanvasGroup>().alpha > .99f, 2f);
                    overlay.enabled = false;
                    yield return Snapshot("完成任务_长名称暂停");
                    overlay.enabled = true;
                    yield return Check("第二条成功卡片未被同标题吞掉", () => FindDeep<TMP_Text>(ui.Get<NotificationView>().transform, "Body").text == "第二条独立成果", 3f);
                    yield return Check("第三条成功卡片顺序显示", () => FindDeep<TMP_Text>(ui.Get<NotificationView>().transform, "Body").text == "第三条独立成果", 3f);
                    notices.Show("获得物资", "治疗药水 ×2", .8f);
                    yield return Check("普通通知恢复前批底图和文字颜色", () => ui.Get<NotificationView>().transform.Find("Card").GetComponent<Image>().sprite.name == "ui_reward_panel"
                        && FindDeep<TMP_Text>(ui.Get<NotificationView>().transform, "Title").color == new Color32(181,255,219,255), 4f);
                }

                yield return Step("通过真实背包入口查看已确认的三件金饰", () =>
                {
                    var loot = ResolveService<LootService>();
                    loot.GrantItems(1803, 1); loot.GrantItems(1804, 1); loot.GrantItems(1805, 1);
                }, 0f);
                yield return Input.Press(input.Actions.Gameplay.Inventory);
                yield return WaitUntil("背包开场完成且控制器已填充列表", () =>
                    ResolveService<InventoryPanelController>().IsOpen && ui.Get<InventoryPanelView>() != null, 4f);
                var inventory = ui.Get<InventoryPanelView>();
                string[] names = { "金襟扣", "金带钩", "金环扣" };
                string[] sprites = { "gold-lapel-clasp", "gold-belt-hook", "gold-ring-clasp" };
                for (int i = 0; i < names.Length; i++)
                {
                    Button row = null;
                    foreach (Transform item in inventory.transform.Find("ArtLayout/ListViewport/List"))
                        if (item.gameObject.activeSelf && item.Find("Name").GetComponent<TMP_Text>().text == names[i]) row = item.GetComponent<Button>();
                    Assert.That(row, Is.Not.Null);
                    EventSystem.current.SetSelectedGameObject(row.gameObject);
                    yield return Input.Press(input.Actions.UI.Submit);
                    int index = i;
                    yield return Check(names[i] + " 显示正确图标且详情已显现", () => FindDeep<Image>(inventory.transform, "DetailIcon").sprite.name == sprites[index]
                        && inventory.transform.Find("ArtLayout/Detail").GetComponent<CanvasGroup>().alpha > .99f, 3f);
                    overlay.enabled = false;
                    yield return Snapshot("确认金饰_" + names[i]);
                    overlay.enabled = true;
                }
                yield return Input.Press(input.Actions.UI.Cancel);
                yield return Check("背包关闭且世界暂停已释放", () => !pause.IsPaused && input.Actions.Gameplay.enabled, 4f);
            }
            finally
            {
                overlay.enabled = true;
                registry.Unregister(first); registry.Unregister(second);
                hidden.SetHudHidden(false);
                input.Actions.asset.LoadBindingOverridesFromJson(originalBindings);
            }
        }

        private static DialogueContent ProbeDialogue() => new DialogueContent("ui_widget_probe", "line", new[]
        {
            new DialogueContent.Node { Id = "line", Kind = DialogueContent.NodeKind.Line, SpeakerName = "旅人", Text = "交互提示在对白进行期间收起，结束后按当前焦点恢复。", Next = "end" },
            new DialogueContent.Node { Id = "end", Kind = DialogueContent.NodeKind.End, Outcome = "Done" }
        });

        private static void Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var r = (RectTransform)button.transform;
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform), Is.True);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        private static IEnumerator CaptureMotion(Action trigger, ShowcaseOverlay overlay)
        {
            var frames = new List<Texture2D>();
            var times = new List<string>();
            overlay.enabled = false;
            try
            {
                trigger();
                for (int i = 0; i < 13; i++)
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    frames.Add(ScreenCapture.CaptureScreenshotAsTexture());
                    times.Add(Time.realtimeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                string path = System.IO.Path.Combine(ShowcaseOptions.ReportRoot, "widgets-motion", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                System.IO.Directory.CreateDirectory(path);
                for (int i = 0; i < frames.Count; i++)
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(path, "frame-" + i.ToString("00") + ".png"), frames[i].EncodeToPNG());
                System.IO.File.WriteAllLines(System.IO.Path.Combine(path, "times.txt"), times);
            }
            finally
            {
                foreach (Texture2D frame in frames) if (frame != null) UnityEngine.Object.Destroy(frame);
                overlay.enabled = true;
            }
        }

        private sealed class PromptProbe : IInteractable
        {
            private readonly string name;
            private readonly Action onInteract;
            public PromptProbe(Vector3 position, string name, Action onInteract)
            { Position = position; this.name = name; this.onInteract = onInteract; }
            public Vector3 Position { get; set; }
            public float InteractionRadius => 2f;
            public bool CanInteract => true;
            public int InteractionPriority => 0;
            public InteractionPrompt Prompt => new InteractionPrompt("对话", name);
            public int Interactions { get; private set; }
            public void Interact() { Interactions++; onInteract?.Invoke(); }
            public void OnFocusChanged(bool focused) { }
        }
    }
}
