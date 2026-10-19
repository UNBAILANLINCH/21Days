// 职责：从正式新游戏和背包动作验证网格、图标、使用／合成与输入收尾，再隔离验证长文和动画边界。
// 复用现有聚光灯进场与回放报告；旧 InventoryShowcase 依赖旧箱子舞台，不能覆盖当前正式入口及美术边界。
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Inventory;
using Game.Loot;
using Game.Player;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Inventory
{
    [Category("Showcase")]
    public sealed class InventoryArtShowcase : Game.Tests.Showcase.ShopPlaytest.ShopPlaytestShowcase
    {
        protected override string Module => "Inventory";
        protected override float BootShutdownSettleSeconds => .5f;

        [UnityTest, Timeout(240000)]
        public IEnumerator Backpack_RealActionsAndArtBoundaries()
        {
            yield return EnterStoryWorld();
            yield return WaitCurtainRevealed();
            yield return AdvanceFormalDialogue();
            var ui = ResolveService<IUIService>();
            var input = ResolveService<IInputService>();
            var loot = ResolveService<LootService>();
            var player = ResolveService<PlayerRules>();
            var controller = ResolveService<InventoryPanelController>();
            var pause = ResolveService<IWorldPauseService>();
            yield return WaitUntil("序章结束且 Gameplay 输入就绪", () => input.Actions.Gameplay.enabled, 8f);
            yield return Step("按真实背包动作（键盘 B／手柄 RB）打开空背包", null, 0f);
            yield return Input.Press(input.Actions.Gameplay.Inventory);
            yield return Check("真实入口打开、暂停世界、空列表隐藏详情", () => controller.IsOpen && pause.IsPaused
                && !input.Actions.Gameplay.enabled && ui.Get<InventoryPanelView>() != null
                && !ui.Get<InventoryPanelView>().transform.Find("ArtLayout/Detail").gameObject.activeSelf, 5f);
            var panel = ui.Get<InventoryPanelView>();
            var overlay = FindRequired<ShowcaseOverlay>("ShowcaseOverlay");
            overlay.enabled = false;
            yield return Snapshot("真实背包入口_空背包");
            overlay.enabled = true;
            yield return Input.Press(input.Actions.UI.Cancel);
            yield return Check("Esc 关闭并恢复输入与暂停令牌", () => !controller.IsOpen && !pause.IsPaused
                && input.Actions.Gameplay.enabled, 5f);

            yield return Step("回放夹具通过 Loot 公开口发放药水、配方原料与名簿", () =>
            {
                Assert.That(loot.GrantItems(1004, 2), Is.True);
                Assert.That(loot.GrantItems(1401, 1), Is.True);
                Assert.That(loot.GrantItems(1601, 3), Is.True);
            }, 0f);
            yield return Input.Press(input.Actions.Gameplay.Inventory);
            yield return WaitUntil("背包重新打开", () => controller.IsOpen, 5f);
            panel = ui.Get<InventoryPanelView>();
            yield return Input.HoldStick(Vector2.right, .08f);
            yield return Check("手柄从分类进入网格", () => EventSystem.current.currentSelectedGameObject != null
                && EventSystem.current.currentSelectedGameObject.GetComponent<UIScrollFocus>() != null, 2f);
            yield return CaptureSelectionMotion(Row(panel, "名簿"), overlay);
            yield return Check("真实1601使用名簿图标、数量正确、两个动作禁用", () =>
                FindDeep<Image>(panel.transform, "DetailIcon").sprite.name == "name-register"
                && Row(panel, "名簿").transform.Find("Count").GetComponent<TMP_Text>().text == "×3"
                && !FindDeep<Button>(panel.transform, "UseButton").interactable
                && !FindDeep<Button>(panel.transform, "CraftButton").interactable
                && panel.transform.Find("ArtLayout/Detail").GetComponent<CanvasGroup>().alpha > .99f, 3f);
            overlay.enabled = false;
            yield return Snapshot("名簿1601_真实图标与禁用按钮");
            overlay.enabled = true;

            yield return Step("选中水痕，实际点击合成验证缺料拒绝", () =>
            {
                Select(panel, "水痕");
                Click(FindDeep<Button>(panel.transform, "CraftButton"));
            }, 0f);
            yield return Check("缺料不扣原料、不产生钥匙", () => Owned(loot, 1401) == 1 && Owned(loot, 1701) == 0);
            yield return Step("补齐湿皮后合成", () =>
            {
                loot.GrantItems(1102, 1);
                Select(panel, "水痕");
                Click(FindDeep<Button>(panel.transform, "CraftButton"));
            }, 0f);
            yield return Check("真实合成产物入包，按配置扣两样原料", () => Owned(loot, 1701) == 1
                && Owned(loot, 1401) == 0 && Owned(loot, 1102) == 0, 3f);
            yield return Step("已有钥匙时再合，产物仍为一件", () =>
            {
                Select(panel, "进入废弃官署的钥匙");
                Click(FindDeep<Button>(panel.transform, "CraftButton"));
            }, 0f);
            yield return Check("重复合成被拒且不额外入包", () => Owned(loot, 1701) == 1);
            yield return Step("受伤后实际点击使用药水", () =>
            {
                player.ApplyDamage(new DamageIntent(1));
                Select(panel, "治疗药水");
                Click(FindDeep<Button>(panel.transform, "UseButton"));
            }, 0f);
            yield return Check("真实使用扣一件且列表同步", () => Owned(loot, 1004) == 1
                && Row(panel, "治疗药水").transform.Find("Count").GetComponent<TMP_Text>().text == "×1");

            yield return Step("填充多页回放数据并保留真实分类与大数量", () =>
            {
                int[] ids = { 1001,1002,1003,1005,1006,1101,1102,1103,1201,1202,1301,1401,1501,
                    1801,1802,1803,1804,1805,1901,1902,1903,1904,1905,1906,1907,1908,1909,
                    2001,2002,2003,2004,2005,2006,2007,2008,2009,2010 };
                foreach (int id in ids) loot.GrantItems(id, 1);
                loot.GrantItems(99999, int.MaxValue);
            }, 0f);
            var list = panel.transform.Find("ArtLayout/ListViewport").GetComponent<ScrollRect>();
            yield return Check("网格超过一屏，未知道具没有误用名簿图标", () => list.content.rect.height > list.viewport.rect.height
                && !Row(panel, "#99999").transform.Find("Icon").gameObject.activeSelf);
            EventSystem.current.SetSelectedGameObject(Row(panel, "#99999").gameObject);
            yield return Input.Press(input.Actions.UI.Submit);
            yield return Check("导航自动滚到末项，十位数量完整可见", () => list.content.anchoredPosition.y > 100f
                && !Row(panel, "#99999").transform.Find("Count").GetComponent<TMP_Text>().isTextOverflowing, 3f);
            yield return WaitUntil("末项详情淡入完成再截图", () => panel.transform.Find("ArtLayout/Detail").GetComponent<CanvasGroup>().alpha > .99f, 2f);
            overlay.enabled = false;
            yield return Snapshot("多页网格_末项与最大数量");
            overlay.enabled = true;
            Click(panel.transform.Find("ArtLayout/Filters/FilterClues").GetComponent<Button>());
            yield return Check("线索筛选保留名簿与信笺，不混入药水", () => HasRow(panel, "名簿")
                && HasRow(panel, "破旧信笺") && !HasRow(panel, "治疗药水"));
            Select(panel, "名簿");
            yield return WaitUntil("分类后的详情淡入完成再截图", () => panel.transform.Find("ArtLayout/Detail").GetComponent<CanvasGroup>().alpha > .99f, 2f);
            overlay.enabled = false;
            yield return Snapshot("真实线索分类");
            overlay.enabled = true;
            yield return Input.Press(input.Actions.UI.Cancel);
            yield return WaitUntil("关闭后恢复 Gameplay", () => !controller.IsOpen && input.Actions.Gameplay.enabled, 5f);
            yield return Input.Press(input.Actions.Gameplay.Inventory);
            yield return WaitUntil("重开背包", () => controller.IsOpen, 5f);
            panel = ui.Get<InventoryPanelView>();
            Click(FindDeep<Button>(panel.transform, "CloseButton"));
            yield return Check("返回与 Esc 同样释放暂停", () => !controller.IsOpen && !pause.IsPaused, 5f);

            using (pause.Acquire(this))
            {
                yield return ui.OpenAsync<InventoryPanelView>().ToCoroutine(value => panel = value);
                var rows = new List<InventoryEntry>();
                string desc = "长文验证：纸页受潮，竖排的字迹一行行往下淌。请核对轮回堂名簿记载，沿途调查证词与物证。";
                for (int i = 0; i < 4; i++) desc += desc;
                rows.Add(new InventoryEntry(1601, "长名称边界：轮回堂用于核验旧日名册与身份的潮湿文书", int.MaxValue,
                    global::cfg.EItemQuality.Common, global::cfg.EItemCategory.Document, desc));
                rows.Add(new InventoryEntry(99999, "无图标的道具", 12, global::cfg.EItemQuality.Common,
                    global::cfg.EItemCategory.Key, "该夹具不写入正式道具表。"));
                panel.SetFilter(InventoryFilter.All);
                panel.SetList(rows, 1601);
                panel.SetDetail(true, rows[0]);
                panel.SetActions(false, false, "", "这件东西没有配方");
                var detail = FindDeep<ScrollRect>(panel.transform, "ReadingViewport");
                var group = panel.transform.Find("ArtLayout/Detail").GetComponent<CanvasGroup>();
                yield return Check("长名称与长文参与阅读滚动，暂停中动画结束", () => group.alpha > .99f
                    && detail.content.rect.height > detail.viewport.rect.height
                    && !FindDeep<TMP_Text>(detail.content, "Description").isTextOverflowing, 3f);
                overlay.enabled = false;
                yield return Snapshot("长名称与长文阅读夹具");
                overlay.enabled = true;
                var r = (RectTransform)detail.transform;
                var wheel = new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down * 12,
                    position = RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)) };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(wheel, hits);
                Assert.That(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(detail.transform), Is.True);
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, wheel, ExecuteEvents.scrollHandler);
                yield return Check("阅读区域真实接收滚轮", () => detail.content.anchoredPosition.y > 20f);
                panel.SetDetail(true, rows[1]);
                panel.gameObject.SetActive(false);
                yield return Check("动画中途禁用立即恢复 alpha", () => group.alpha == 1f);
                panel.gameObject.SetActive(true);
                panel.SetDetail(true, rows[0]);
                panel.SetActions(true, true, "", "状态预览夹具");
                yield return Check("重启后动画完成且可用按钮恢复", () => group.alpha > .99f
                    && FindDeep<Button>(panel.transform, "UseButton").interactable, 2f);
                yield return ui.CloseAsync(panel).ToCoroutine();
            }
        }

        private static int Owned(LootService loot, int id) => loot.Items.TryGetValue(id, out int count) ? count : 0;

        private static bool HasRow(InventoryPanelView panel, string name) => Row(panel, name, false) != null;

        private static Button Row(InventoryPanelView panel, string name, bool required = true)
        {
            Transform list = panel.transform.Find("ArtLayout/ListViewport/List");
            foreach (Transform row in list)
                if (row.gameObject.activeSelf && row.Find("Name").GetComponent<TMP_Text>().text == name)
                    return row.GetComponent<Button>();
            if (required) Assert.Fail("找不到背包项：" + name);
            return null;
        }

        private static void Select(InventoryPanelView panel, string name)
        {
            Button row = Row(panel, name);
            EventSystem.current.SetSelectedGameObject(row.gameObject);
            Click(row);
        }

        private static void Click(Button button)
        {
            Assert.That(button.IsInteractable(), Is.True);
            Canvas.ForceUpdateCanvases();
            var r = (RectTransform)button.transform;
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform), Is.True,
                "按钮或其选中底图必须位于实际射线命中首位：" + button.name);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerClickHandler);
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
                // 采集后再编码；真实间隔保留用于预览，截图读回不能当性能采样。
                string path = System.IO.Path.Combine(ShowcaseOptions.ReportRoot, "inventory-motion", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
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
    }
}
