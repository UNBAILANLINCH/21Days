// 职责：经正式新游戏和 Tab 验证任务面板，再用公开视图入口验证多任务／长文本美术边界。
// 旧 QuestShowcase 依赖已冻结的 1001/2001 正式任务集，不能复用；复用现有聚光灯进场助手，不改正式任务配置。
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Quest;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Quest
{
    [Category("Showcase")]
    public sealed class QuestArtShowcase : Game.Tests.Showcase.ShopPlaytest.ShopPlaytestShowcase
    {
        protected override string Module => "Quest";
        protected override float BootShutdownSettleSeconds => .5f;

        [UnityTest, Timeout(240000)]
        public IEnumerator Tab_RealTrackingAndArtBoundaries()
        {
            yield return EnterStoryWorld();
            yield return WaitCurtainRevealed();
            yield return AdvanceFormalDialogue();
            IUIService ui = ResolveService<IUIService>();
            IInputService input = ResolveService<IInputService>();
            QuestService service = ResolveService<QuestService>();
            IWorldPauseService pause = ResolveService<IWorldPauseService>();
            QuestPanelController controller = ResolveService<QuestPanelController>();
            yield return WaitUntil("序章结束，真实任务 HUD 与 Gameplay 输入就绪", () =>
                ui.Get<QuestHudView>() != null && input.Actions.Gameplay.enabled, 8f);
            yield return Step("按任务键 Tab，从真实输入打开任务面板", null, 0f);
            yield return Input.Press(input.Actions.Gameplay.Journal);
            yield return Check("面板打开并暂停世界，展示真实900任务", () => controller.IsOpen
                && pause.IsPaused && !input.Actions.Gameplay.enabled && ui.Get<QuestPanelView>() != null
                && service.TryGet(900, out QuestProgress actual)
                && FindDeep<TMP_Text>(ui.Get<QuestPanelView>().transform.Find("ArtLayout/Detail"), "Title").text == actual.Definition.Title
                && ui.Get<QuestPanelView>().transform.Find("ArtLayout/Detail").GetComponent<CanvasGroup>().alpha > .99f, 5f);
            QuestPanelView panel = ui.Get<QuestPanelView>();
            Assert.That(panel, Is.Not.Null);
            var overlay = FindRequired<ShowcaseOverlay>("ShowcaseOverlay");
            overlay.enabled = false;
            yield return Snapshot("真实Tab入口_正式任务");
            overlay.enabled = true;
            yield return Input.HoldStick(Vector2.down, .08f);
            yield return Check("手柄向下从分类进入任务页签", () => EventSystem.current.currentSelectedGameObject != null
                && EventSystem.current.currentSelectedGameObject.GetComponent<UIScrollFocus>() != null, 2f);
            Button track = FindDeep<Button>(panel.transform, "TrackButton");
            yield return Step("点击真实追踪按钮并通过导航确认恢复追踪", () => Click(track), 0f);
            yield return Check("取消追踪立即更新真实服务和按钮文字", () => service.TrackedId == 0
                && FindDeep<TMP_Text>(panel.transform, "TrackLabel").text == "追踪", 3f);
            EventSystem.current.SetSelectedGameObject(track.gameObject);
            yield return Input.Press(input.Actions.UI.Submit);
            yield return Check("UI Submit 恢复追踪，文字变为取消追踪", () => service.TrackedId == 900
                && FindDeep<TMP_Text>(panel.transform, "TrackLabel").text == "取消追踪", 3f);
            yield return Input.Press(input.Actions.Gameplay.Journal);
            yield return Check("面板内 Tab 保持既有语义，不关闭", () => controller.IsOpen);
            yield return Step("Esc 关闭，再 Tab 重开并点返回", null, 0f);
            yield return Input.Press(input.Actions.UI.Cancel);
            yield return Check("Esc 释放暂停并恢复 Gameplay", () => !controller.IsOpen && !pause.IsPaused
                && input.Actions.Gameplay.enabled, 5f);
            yield return Input.Press(input.Actions.Gameplay.Journal);
            yield return WaitUntil("Tab 重开完成", () => controller.IsOpen, 5f);
            Click(FindDeep<Button>(ui.Get<QuestPanelView>().transform, "CloseButton"));
            yield return WaitUntil("返回按钮完成同样收尾", () => !controller.IsOpen && !pause.IsPaused, 5f);

            // 以下是明确隔离的视图夹具；不向真实服务或 SO 写入示例任务。
            using (pause.Acquire(this))
            {
                yield return ui.OpenAsync<QuestPanelView>().ToCoroutine(value => panel = value);
                track = FindDeep<Button>(panel.transform, "TrackButton");
                QuestRules rules = Fixture();
                var rows = new List<QuestProgress>();
                rules.GetOrdered(rows);
                int selected = rows[0].Id;
                System.Action refresh = () =>
                {
                    rules.GetOrdered(rows);
                    rules.TryGet(selected, out QuestProgress p);
                    panel.SetList(rows, selected, "主线", "支线", "暂无进行中的任务");
                    panel.SetDetail(p, rules.TrackedId == selected, p.Definition.Kind == QuestKind.Main ? "主线" : "支线",
                        "追踪", "取消追踪", false);
                };
                panel.OnQuestSelected += id => { selected = id; refresh(); };
                panel.OnTrackToggled += () => { if (rules.TrackedId == selected) rules.Untrack(); else rules.Track(selected); refresh(); };
                yield return Step("视图夹具：多任务叠页、长中文详情与数量目标", refresh, 0f);
                ScrollRect list = panel.transform.Find("ArtLayout/ListViewport").GetComponent<ScrollRect>();
                ScrollRect detail = FindDeep<ScrollRect>(panel.transform, "ReadingViewport");
                CanvasGroup group = panel.transform.Find("ArtLayout/Detail").GetComponent<CanvasGroup>();
                TMP_Text description = FindDeep<TMP_Text>(panel.transform, "Description");
                yield return Check("暂停中详情淡入完成，长内容可滚动且列表被遮罩裁剪", () => group.alpha > .99f
                    && list.content.rect.height > list.viewport.rect.height
                    && detail.content.rect.height > detail.viewport.rect.height
                    && !description.isTextOverflowing && description.preferredHeight <= description.rectTransform.rect.height + .5f
                    && !panel.transform.Find("ArtLayout/EmptyBackdrop").gameObject.activeSelf
                    && list.GetComponent<RectMask2D>() != null, 3f);
                overlay.enabled = false;
                yield return Snapshot("多任务夹具_长中文与半透明边缘");
                overlay.enabled = true;
                yield return CaptureSelectionMotion(list.content.GetComponentsInChildren<Button>()[1], overlay);
                yield return Check("鼠标实际命中页签并切换支线详情", () => selected == 2001 && group.alpha > .99f, 2f);

                yield return Step("将导航焦点移至最后一项，再确认选择", () =>
                {
                    Button[] buttons = list.content.GetComponentsInChildren<Button>();
                    EventSystem.current.SetSelectedGameObject(buttons[buttons.Length - 1].gameObject);
                }, 0f);
                yield return Input.Press(input.Actions.UI.Submit);
                yield return Check("焦点项自动滚入视口，确认后使用支线选中图", () => list.content.anchoredPosition.y > 100f
                    && FindDeep<Image>(panel.transform, "KindArt").sprite.name == "18-side-wordmark" && group.alpha > .99f, 3f);
                overlay.enabled = false;
                yield return Snapshot("滚动到底_支线选择");
                overlay.enabled = true;

                yield return Step("快速切换任务后中途禁用，重新启用恢复终值", () =>
                {
                    selected = 1001; refresh(); selected = 2003; refresh();
                    panel.gameObject.SetActive(false);
                }, 0f);
                yield return Check("禁用取消详情动画并还原 alpha", () => group.alpha == 1f);
                panel.gameObject.SetActive(true);
                selected = 1001; refresh();
                yield return Check("重启详情在暂停中完成", () => group.alpha > .99f, 2f);
                var readingRect = (RectTransform)detail.transform;
                var scrollEvent = new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down * 12f,
                    position = RectTransformUtility.WorldToScreenPoint(null, readingRect.TransformPoint(readingRect.rect.center)) };
                var scrollHits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(scrollEvent, scrollHits);
                Assert.That(scrollHits.Count > 0 && scrollHits[0].gameObject.transform.IsChildOf(detail.transform), Is.True,
                    "透明阅读区必须接收真实滚轮命中");
                ExecuteEvents.ExecuteHierarchy(scrollHits[0].gameObject, scrollEvent, ExecuteEvents.scrollHandler);
                yield return Check("长描述与目标可通过滚轮访问下方内容", () => detail.content.anchoredPosition.y > 20f, 2f);
                overlay.enabled = false;
                yield return Snapshot("长详情滚动_目标进度");
                overlay.enabled = true;

                yield return Step("完成夹具中的两条支线，展示完成列表，再检查空列表", () =>
                {
                    rules.Report(QuestObjectiveKind.Counter, "k2001", 5);
                    rules.Report(QuestObjectiveKind.Counter, "k2002", 5);
                    rules.GetCompleted(rows);
                    panel.SetTab(QuestPanelTab.Completed);
                    panel.SetList(rows, rows[0].Id, "主线", "支线", "还没有完成的任务");
                    panel.SetDetail(rows[0], false, "支线", "追踪", "取消追踪", true);
                }, 0f);
                yield return Check("完成附注可见，所有目标已打勾且追踪按钮隐藏", () =>
                {
                    TMP_Text[] labels = list.content.GetComponentsInChildren<TMP_Text>();
                    bool completedTitle = System.Array.Exists(labels, label => label.name == "Status" && label.text == "（已完成）");
                    TMP_Text[] goals = FindDeep<RectTransform>(panel.transform, "Objectives").GetComponentsInChildren<TMP_Text>();
                    return !track.gameObject.activeSelf && completedTitle && group.alpha > .99f
                        && System.Array.Exists(goals, label => label.name == "Mark" && label.text == "✓");
                }, 2f);
                overlay.enabled = false;
                yield return Snapshot("已完成列表_动态附注");
                overlay.enabled = true;
                panel.SetList(null, 0, "主线", "支线", "还没有完成的任务");
                panel.SetDetail(null, false, "", "追踪", "取消追踪", true);
                yield return Check("空列表显示提示，不留下旧详情和可用追踪按钮", () =>
                    FindDeep<TMP_Text>(panel.transform, "EmptyLabel").gameObject.activeSelf
                    && panel.transform.Find("ArtLayout/EmptyBackdrop").gameObject.activeSelf
                    && !panel.transform.Find("ArtLayout/Detail").gameObject.activeSelf);
                overlay.enabled = false;
                yield return Snapshot("空任务列表");
                overlay.enabled = true;
                yield return ui.CloseAsync(panel).ToCoroutine();
            }
        }

        private static void Click(Button button)
        {
            var rect = (RectTransform)button.transform;
            var point = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null,
                rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(point, hits);
            Assert.That(hits.Count > 0 && hits[0].gameObject == button.gameObject, Is.True, "实际按钮必须位于命中首位");
            ExecuteEvents.Execute(hits[0].gameObject, point, ExecuteEvents.pointerClickHandler);
        }

        private static IEnumerator CaptureSelectionMotion(Button button, ShowcaseOverlay overlay)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Click(button);
                yield break;
            }
            var frames = new List<Texture2D>();
            var times = new List<string>();
            bool visible = overlay.enabled;
            overlay.enabled = false;
            try
            {
                yield return new WaitForEndOfFrame();
                frames.Add(ScreenCapture.CaptureScreenshotAsTexture());
                times.Add(Time.realtimeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Click(button);
                for (int i = 0; i < 12; i++)
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    frames.Add(ScreenCapture.CaptureScreenshotAsTexture());
                    times.Add(Time.realtimeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                // 先采集再编码，避免 PNG 编码占去整个 160ms 过渡；真实间隔另存，不能当性能采样。
                string path = System.IO.Path.Combine(ShowcaseOptions.ReportRoot, "quest-motion", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                System.IO.Directory.CreateDirectory(path);
                for (int i = 0; i < frames.Count; i++)
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(path, "frame-" + i.ToString("00") + ".png"), frames[i].EncodeToPNG());
                System.IO.File.WriteAllLines(System.IO.Path.Combine(path, "times.txt"), times);
            }
            finally
            {
                foreach (Texture2D frame in frames) if (frame != null) Object.Destroy(frame);
                overlay.enabled = visible;
            }
        }

        private static QuestRules Fixture()
        {
            var defs = new List<QuestDefinition>();
            string longDescription = "长文验证：营地里的旅人正在等候消息。请沿河道寻找旧日的线索，核对物资与沿途见闻。";
            for (int i = 0; i < 4; i++) longDescription += longDescription;
            for (int i = 0; i < 12; i++)
            {
                int id = i == 0 ? 1001 : 2000 + i;
                var goals = new List<QuestObjectiveDefinition>();
                int count = i == 0 ? 8 : 1;
                for (int j = 0; j < count; j++) goals.Add(new QuestObjectiveDefinition("核对沿途的物资与线索，记录目标进度 " + (j + 1),
                    QuestObjectiveKind.Counter, "k" + id, 5, null));
                defs.Add(new QuestDefinition(id, i == 0 ? QuestKind.Main : QuestKind.Side,
                    i == 0 ? "前往营地核对旅人留下的长篇线索与未竟之事" : "支线查阅 " + i,
                    i == 0 ? longDescription : "视图边界夹具：这条任务用于检查列表滚动与支线样式，不写入正式剧情。",
                    new int[0], goals));
            }
            var rules = new QuestRules(new QuestContent(defs), NullTelemetryScope.Instance);
            rules.ActivateAvailable();
            rules.Report(QuestObjectiveKind.Counter, "k1001", 3);
            return rules;
        }
    }
}
