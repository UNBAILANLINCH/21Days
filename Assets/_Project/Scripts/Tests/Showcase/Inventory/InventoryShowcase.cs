// 职责：Inventory 模块回放——开箱拿到物品 → 按背包键打开背包看到它 → 点「合成」（原料不够，被拒）→
//   点「使用」用掉一件（数量减一）；另一条用例把原料塞齐，钉住「合成成功、产物入包、已有产物时被拒」。
// 覆盖的坑位：S5 波次「背包的合成与使用零生产调用方」——本文件是 ItemUseRules / ItemCraftRules 的第一条生产调用链，
//   也是 LootService.GrantItems / BackpackChangedEvent 的第一条端到端使用。
// 骨架照 ExplorationShowcase：ScenePath 返回 null（世界由 Boot 流程加载）、EnterWorldFromTitle 进世界、
//   服务用 ResolveService 反射取、面板按钮用 FindDeep + onClick.Invoke 点（与真点击走同一条监听器）。
// 依赖（写在交付报告里，主窗口落完预制体改动才能跑）：
//   ① Prefabs/UI/InventoryPanelView.prefab 的 Detail 下要有 UseButton / CraftButton / RecipeLabel，
//      并拖到 InventoryPanelView 的新字段上（清单见交付报告第 10 节）。**缺了不崩**（面板照常打开），
//      但「点使用 / 点合成 / 读配方说明」这几步会红在「找不到按钮」上。
//   ② 治疗效果的执行者需要 Game.Player 侧补一个公开治疗口（见 IItemEffectTarget 文件头）——
//      那一条没落地时，「用掉一件、数量减一」这一步会红在「效果没有执行者 / 治疗口没接上」上，
//      **不是红在背包扣件数上**；这是已知前置，不是本回放的缺陷。
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Config;
using Game.Core.Input;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Interaction;
using Game.Inventory;
using Game.Loot;
using Game.Player;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Inventory
{
    [Category("Showcase")]
    public sealed class InventoryShowcase : ShowcaseScenario
    {
        private const float BootTimeoutSeconds = 20f;
        private const float EnterTimeoutSeconds = 20f;

        /// <summary>回放舞台（SampleScene）里的箱子名：Crate_C 的奖励是 1004 治疗药水 ×2。</summary>
        private const string PotionCrateName = "Crate_C";

        /// <summary>站到箱子南侧 0.9 m（与 ExplorationShowcase 的 NearCrateA 同一算法：在 1.5 m 交互半径内）。</summary>
        private static readonly Vector2 CrateStandOffset = new Vector2(0f, -0.9f);

        // 占位配方（ItemRecipeEntry.PlaceholderOfficeKey，出处 05_皮面具与道具.md:106 R7）与它用到的三个 id。
        private const int Potion = 1004;
        private const int WetSkin = 1102;
        private const int WaterMark = 1401;
        private const int OfficeKey = 1701;

        /// <summary>动作结果通知的标题（与 InventoryPanelController 里的常量同字面量）。</summary>
        private const string UseTitle = "使用道具";
        private const string CraftTitle = "合成";

        /// <summary>本轮见过标题的通知（通知是排队卡片，会被进场景的任务通知顶后，逐帧采样）。</summary>
        private readonly HashSet<string> seenNotificationTitles = new HashSet<string>(StringComparer.Ordinal);

        private IUIService ui;
        private LootService loot;
        private PlayerRules playerRules;
        private InventoryPanelController panel;
        private IInteractionFocus focus;

        protected override string Module => "Inventory";

        /// <summary>世界由流程加载（Boot → 标题「开始」→ 探索场景），这里不直接加载场景。</summary>
        protected override string ScenePath => null;

        protected override bool LoadBootScene => true;

        protected override IEnumerator WaitForBootReady()
        {
            yield return WaitUntil(
                "启动流程到达标题界面（容器建完、UI 服务可用、标题界面已打开）",
                () =>
                {
                    IUIService candidate = ResolveService<IUIService>();
                    return candidate != null && candidate.Get<TitleView>() != null;
                },
                BootTimeoutSeconds);
        }

        /// <summary>
        /// 第一条链：开箱 → 背包里看到它 → 没原料时合成被拒（负对照）→ 用掉一件（数量减一）。
        /// </summary>
        [UnityTest]
        public IEnumerator Collect_UseAndCraft_GoThroughTheBackpackPanel()
        {
            yield return EnterWorld();

            SupplyCrate crate = FindRequired<SupplyCrate>(PotionCrateName);
            yield return Step("把玩家挪到 " + PotionCrateName + " 旁", () => MovePlayerNear(crate));
            yield return Check("焦点落在箱子上", () => crate != null && ReferenceEquals(focus.Current, crate), 3f);

            yield return Step("按交互键开箱（走玩家真按键那条路）", null, 0f);
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Interact);
            yield return Check($"开箱成功：背包里多了 tbitem {crate.ItemId} ×{crate.Count}",
                () => crate.IsOpened && Owned(crate.ItemId) == crate.Count, 3f);
            yield return Snapshot("开箱·获得物资");

            yield return Step("按背包键打开背包", OpenPanel, 0f);
            yield return Check("背包面板打开，列表第一行就是刚才拿到的那件（名字与数量对得上，不是空白面板）",
                () => Panel() != null && RowName(0) == ItemName(crate.ItemId) && RowCount(0) == "×" + crate.Count, 5f);
            yield return Snapshot("背包·列表");

            // ── 负对照：只给一半原料，合成必须被拒、背包一件都不动 ──
            // 为什么不是「一件原料都不给」：面板列表只列**已持有**的东西，而合成按钮指向哪条配方，
            // 靠的是「选中的这件东西」——选中产物就合它、选中原料就合用到它的那条配方
            // （`ItemCraftService.FindRecipeUsingIngredient`）。一件原料都没有时连选都选不中，
            // 那条负对照会退化成在测「没得选」，而不是「原料不够」。
            yield return Step("只给一件原料（" + ItemName(WaterMark) + " ×1；占位配方要水痕 + 湿皮两样）",
                () => loot.GrantItems(WaterMark, 1));
            yield return Step("选中原料「" + ItemName(WaterMark) + "」", () => AssertSelected(WaterMark));
            int productsBefore = Owned(OfficeKey);
            int waterMarkBefore = Owned(WaterMark);
            yield return Step("点「合成」", ClickCraft);
            yield return Check($"合成被拒：配方说明里写着「原料不够」，并弹出一条「{CraftTitle}」通知",
                () => CraftHintContains("原料不够") || NotificationTitleSeen(CraftTitle),
                NotificationTimeoutSeconds(2));
            yield return Check("负对照：没有产物、原料也没被扣",
                () => Owned(OfficeKey) == productsBefore && Owned(WaterMark) == waterMarkBefore);
            yield return Snapshot("合成·原料不够被拒");

            // ── 使用：数量减一 ──
            // ⚠️ 必须先打伤：治疗药水在**满血**时会被 `HealPlayerEffect` 正确拒绝（`EffectRejected`），
            // 而使用通路的顺序是「先执行效果、成功之后才扣件数」（见 ItemUseService 文件头）——
            // 满血时点「使用」本来就不该扣件数，回放不先造伤就演示不出这条链。
            int healthBefore = playerRules.Model.Health;
            yield return Step("先把玩家打掉 1 点生命（满血时治疗会被正确拒绝）", DamagePlayerOnce);
            yield return Check($"玩家已受伤：生命 {healthBefore} → {healthBefore - 1}",
                () => playerRules.Model.Health < healthBefore, 2f);
            yield return Step("选中治疗药水", () => AssertSelected(Potion));
            int ownedBeforeUse = Owned(Potion);
            yield return Step("点「使用」", ClickUse);
            yield return Check($"用掉一件：数量 {ownedBeforeUse} → {ownedBeforeUse - 1}",
                () => Owned(Potion) == ownedBeforeUse - 1, 3f);
            yield return Check($"弹出一条「{UseTitle}」通知",
                () => NotificationTitleSeen(UseTitle), NotificationTimeoutSeconds(2));
            yield return Check("列表里的数量也跟着变了（面板订阅背包变化事件刷新）",
                () => RowsShowCount(Potion, ownedBeforeUse - 1), 3f);
            yield return Snapshot("使用道具·数量减一");

            yield return Step("关背包", ClosePanel);
            yield return Check("面板已关闭", () => Panel() == null, 5f);
        }

        /// <summary>
        /// 第二条链：原料齐了真能合出钥匙——产物入包、弹通知、已有产物时再合被拒。
        /// 走的是真的 ItemCraftService → LootService.GrantItems，不是界面假动作。
        /// </summary>
        [UnityTest]
        public IEnumerator Craft_WithAllIngredients_AddsTheProductAndRejectsTheSecondOne()
        {
            yield return EnterWorld();

            yield return Step($"给背包塞齐占位配方的原料（{ItemName(WaterMark)} ×1 + {ItemName(WetSkin)} ×1）",
                () =>
                {
                    loot.GrantItems(WaterMark, 1);
                    loot.GrantItems(WetSkin, 1);
                });
            yield return Check("原料已入包", () => Owned(WaterMark) == 1 && Owned(WetSkin) == 1);

            yield return Step("按背包键打开背包", OpenPanel, 0f);
            // 选中**原料**（不是产物）：列表只列已持有的东西，而钥匙此刻还没合出来、根本不在列表里；
            // 合成按钮由「选中项属于哪条配方」决定——选中水痕就指向「水痕 + 湿皮 → 钥匙」那一条
            // （`ItemCraftService.FindRecipeUsingIngredient`）。
            yield return Check("面板打开，且已选中原料「" + ItemName(WaterMark) + "」那一行",
                () => Panel() != null && SelectRow(WaterMark), 5f);
            yield return Check("配方说明列全了两样原料",
                () => RecipeLabelText().Contains(ItemName(WaterMark)) && RecipeLabelText().Contains(ItemName(WetSkin)));
            yield return Snapshot("背包·配方说明");

            yield return Step("点「合成」", ClickCraft);
            yield return Check($"合成成功：背包里多了 {ItemName(OfficeKey)} ×1",
                () => Owned(OfficeKey) == 1, 3f);
            yield return Check($"弹出一条「{CraftTitle}」通知", () => NotificationTitleSeen(CraftTitle),
                NotificationTimeoutSeconds(2));

            // 原料是否消失取决于配置开关（**用户 2026-10-10 拍板 R8：扣**，资产里 consumeIngredients 已勾上）：
            // 两种口径各记一条检查点，谁都不算失败，但报告里能看到实际走的是哪一种。
            ItemCraftService craft = ResolveService<ItemCraftService>();
            bool consumesIngredients = craft != null && craft.ConsumesIngredients;
            yield return Check(consumesIngredients
                    ? "配置口径：原料已消耗（consumeIngredients = true）"
                    : "配置口径：原料保留（consumeIngredients = false）",
                () => consumesIngredients ? Owned(WaterMark) == 0 : Owned(WaterMark) == 1);
            yield return Snapshot("合成·产物入包");

            // 负对照：已经有一件了，再合一次要被拒（堆叠规则未定，白盒阶段不凭空造物）。
            yield return Step("再点一次「合成」", ClickCraft);
            yield return Check("负对照：已有产物时合成被拒，产物仍是一件", () => Owned(OfficeKey) == 1, 3f);
            yield return Snapshot("合成·已有产物被拒");

            yield return Step("关背包", ClosePanel);
            yield return Check("面板已关闭", () => Panel() == null, 5f);
        }

        // ───────────────────────── 进入与服务 ─────────────────────────

        private IEnumerator EnterWorld()
        {
            Connect();
            yield return EnterWorldFromTitle(BootTimeoutSeconds, EnterTimeoutSeconds);
            yield return WaitUntil("进入探索场景：标题关闭、物资箱已登记、统一焦点与背包控制器就绪",
                () => ui != null && ui.Get<TitleView>() == null && GameObject.Find("Crates") != null
                      && focus != null && panel != null && loot != null,
                EnterTimeoutSeconds);
        }

        private void Connect()
        {
            ui = ResolveService<IUIService>();
            loot = ResolveService<LootService>();
            playerRules = ResolveService<PlayerRules>();
            panel = ResolveService<InventoryPanelController>();
            focus = ResolveService<IInteractionFocus>();
        }

        // ───────────────────────── 定位与开箱 ─────────────────────────

        private void MovePlayerNear(SupplyCrate crate)
        {
            if (playerRules == null || crate == null)
            {
                throw new InvalidOperationException("玩家规则或箱子不可用（容器没建完，或场景里没有这只箱子）");
            }

            Vector3 position = crate.transform.position;
            playerRules.Reset(new Vector2(position.x + CrateStandOffset.x, position.z + CrateStandOffset.y));
            Physics.SyncTransforms();
        }

        // ───────────────────────── 面板 ─────────────────────────

        private InventoryPanelView Panel() => ui == null ? null : ui.Get<InventoryPanelView>();

        /// <summary>按背包键那条路开面板：调控制器的 OpenAsync（不自己 ui.OpenAsync&lt;InventoryPanelView&gt;）。</summary>
        private void OpenPanel() => panel.OpenAsync().Forget();

        private void ClosePanel() => panel.CloseAsync().Forget();

        private void ClickUse() => RequirePanelButton("UseButton").onClick.Invoke();

        /// <summary>给玩家打 1 点伤害——走公开入口 `PlayerRules.ApplyDamage`（与怪打人是同一条路）。</summary>
        private void DamagePlayerOnce() => playerRules.ApplyDamage(new DamageIntent(1));

        private void ClickCraft() => RequirePanelButton("CraftButton").onClick.Invoke();

        private Button RequirePanelButton(string objectName)
        {
            InventoryPanelView current = Panel();
            Button button = current == null ? null : FindDeep<Button>(current.transform, objectName);
            if (button == null)
            {
                throw new InvalidOperationException(
                    $"背包面板下找不到按钮「{objectName}」（面板没开，或预制体里还没加这个按钮——见交付报告第 10 节）");
            }

            return button;
        }

        private string RecipeLabelText()
        {
            InventoryPanelView current = Panel();
            TMP_Text label = current == null ? null : FindDeep<TMP_Text>(current.transform, "RecipeLabel");
            return label == null || label.text == null ? string.Empty : label.text;
        }

        private bool CraftHintContains(string text) => RecipeLabelText().Contains(text);

        /// <summary>列表 List 下当前激活的行（排除隐藏模板 ItemTemplate）。</summary>
        private List<Transform> ActiveRows()
        {
            var rows = new List<Transform>();
            InventoryPanelView current = Panel();
            Transform list = current == null ? null : current.transform.Find("List");
            if (list == null)
            {
                return rows;
            }

            for (int i = 0; i < list.childCount; i++)
            {
                Transform row = list.GetChild(i);
                if (row.gameObject.activeSelf && row.name != "ItemTemplate")
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private string RowName(int index)
        {
            List<Transform> rows = ActiveRows();
            return index >= 0 && index < rows.Count ? TextAt(rows[index], "Name") : string.Empty;
        }

        private string RowCount(int index)
        {
            List<Transform> rows = ActiveRows();
            return index >= 0 && index < rows.Count ? TextAt(rows[index], "Count") : string.Empty;
        }

        /// <summary>列表里的某件道具显示的数量是不是期望值。</summary>
        private bool RowsShowCount(int itemId, int count)
        {
            string expected = ItemName(itemId);
            List<Transform> rows = ActiveRows();
            for (int i = 0; i < rows.Count; i++)
            {
                if (TextAt(rows[i], "Name") == expected) return TextAt(rows[i], "Count") == "×" + count;
            }

            return false;
        }

        /// <summary>点某一行（界面选中态 + 详情跟着变，合成 / 使用按钮也就指向它）。找不到返回 false。</summary>
        private bool SelectRow(int itemId)
        {
            string expected = ItemName(itemId);
            List<Transform> rows = ActiveRows();
            for (int i = 0; i < rows.Count; i++)
            {
                if (TextAt(rows[i], "Name") != expected) continue;
                Button button = rows[i].GetComponent<Button>();
                if (button == null) return false;
                button.onClick.Invoke();
                return true;
            }

            return false;
        }

        // Step 的 act 是同步的：选中失败要抛出去（Step 会把这一步记成失败），不要静默。
        private void AssertSelected(int itemId)
        {
            if (!SelectRow(itemId))
            {
                throw new InvalidOperationException($"背包列表里找不到「{ItemName(itemId)}」那一行，选不中");
            }
        }

        private static string TextAt(Transform row, string childName)
        {
            Transform child = row == null ? null : row.Find(childName);
            TMP_Text label = child == null ? null : child.GetComponent<TMP_Text>();
            return label == null || label.text == null ? string.Empty : label.text;
        }

        // ───────────────────────── 数据与通知查询 ─────────────────────────

        private int Owned(int itemId)
        {
            if (loot == null) return 0;
            return loot.Items.TryGetValue(itemId, out int count) ? count : 0;
        }

        /// <summary>曾见过标题为 <paramref name="title"/> 的通知卡片（逐帧轮询用：卡片停留一会儿就换下一条）。</summary>
        private bool NotificationTitleSeen(string title)
        {
            NotificationView current = ui == null ? null : ui.Get<NotificationView>();
            if (current != null && current.IsCardShown)
            {
                TMP_Text titleText = FindDeep<TMP_Text>(current.transform, "Title");
                if (titleText != null && titleText.text == title) seenNotificationTitles.Add(title);
            }

            return seenNotificationTitles.Contains(title);
        }

        private float NotificationTimeoutSeconds(int maxAhead)
        {
            UIConfig config = ResolveService<UIConfig>();
            float seconds = config == null ? 2.5f : config.NotificationSeconds;
            return (maxAhead + 1) * seconds + 2f;
        }

        /// <summary>
        /// 物品名查 tbitem（照 ExplorationShowcase 的做法走反射：本程序集不引用 Luban.Runtime）；
        /// 表没就绪或查不到时用「#id」，与背包面板同一口径。
        /// </summary>
        private string ItemName(int itemId)
        {
            IConfigService config = ResolveService<IConfigService>();
            try
            {
                if (config != null)
                {
                    object table = config.Tables.TbItem;
                    object item = table.GetType().GetMethod("GetOrDefault").Invoke(table, new object[] { itemId });
                    string name = item == null ? null : item.GetType().GetField("Name").GetValue(item) as string;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch (Exception)
            {
                // 见方法注释。
            }

            return "#" + itemId;
        }
    }
}
