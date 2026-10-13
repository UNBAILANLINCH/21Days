// 职责：全屏背包面板——左上标题与三个筛选按钮（全部 / 物品 / 线索）、左侧条目列表、右侧选中条目详情、右上返回；只显示与抛事件，不注入服务。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestPanelView 的行与详情绑的是 QuestProgress（目标清单、追踪按钮），字段对不上背包条目。
//   2. 扩展不行：往 QuestPanelView 里加背包会让 Quest 认识物品，名实不符；列表行池化的写法照它抄。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Inventory
{
    /// <summary>
    /// 背包面板。预制体 Addressables 地址须为 <c>InventoryPanelView</c>。
    /// <para>
    /// 接线提示：<c>itemTemplate</c> 根上挂 Button，子物体须有 <c>Highlight</c>、<c>Icon</c>(Image)、<c>Name</c>(TMP)、
    /// <c>Count</c>(TMP)、<c>Tag</c>(TMP)；三个筛选按钮各有子物体 <c>Highlight</c>（当前档位显示）。模板运行时隐藏，行按需复制并复用。
    /// </para>
    /// <para>
    /// 动作按钮：<c>Detail</c> 下挂 <c>UseButton</c>(Button) / <c>CraftButton</c>(Button) 与 <c>RecipeLabel</c>(TMP)。
    /// **这三个是可选字段**：没接时面板照常打开，只在控制台记一条 Warn（见 <see cref="ValidateActionButtons"/>）。
    /// 键鼠 UI 导航不用额外接线：按钮是标准 <c>Button</c>，EventSystem 的上下左右选中与 Enter 提交天然可用
    /// （所以本波次**没有**新增输入动作）。
    /// </para>
    /// </summary>
    public sealed class InventoryPanelView : UIView
    {
        private const string HighlightName = "Highlight";
        private const string IconName = "Icon";
        private const string NameName = "Name";
        private const string CountName = "Count";
        private const string TagName = "Tag";
        private const string CountPrefix = "×";

        [Tooltip("筛选：全部。子物体 Highlight 在当前档位时显示。")]
        [SerializeField] private Button filterAllButton;
        [Tooltip("筛选：物品（材料 + 消耗品 + 关键物 + 皮 + 面具 + 钥匙）。")]
        [SerializeField] private Button filterItemsButton;
        [Tooltip("筛选：线索（线索 + 文书）。")]
        [SerializeField] private Button filterCluesButton;
        [Tooltip("条目列表容器（通常带 VerticalLayoutGroup）。")]
        [SerializeField] private RectTransform listRoot;
        [Tooltip("行模板：根挂 Button，子物体 Highlight、Icon(Image)、Name(TMP)、Count(TMP)、Tag(TMP)。")]
        [SerializeField] private GameObject itemTemplate;
        [Tooltip("当前档位下没有任何条目时显示的提示（「还没有拾取到任何东西」）。")]
        [SerializeField] private GameObject emptyLabel;
        [Tooltip("右侧详情区根节点。")]
        [SerializeField] private GameObject detailRoot;
        [SerializeField] private TMP_Text detailName;
        [SerializeField] private TMP_Text detailCategory;
        [SerializeField] private TMP_Text detailDescription;
        [Tooltip("右上「< 返回」。")]
        [SerializeField] private Button closeButton;

        [Tooltip("详情区「使用」按钮（子物体 Label）。**预制体字段**：缺了只在打开时记一条 Warn，面板照常可用。")]
        [SerializeField] private Button useButton;

        [Tooltip("详情区「合成」按钮（子物体 Label）。**预制体字段**：缺了只在打开时记一条 Warn，面板照常可用。")]
        [SerializeField] private Button craftButton;

        [Tooltip("详情区合成说明（原料清单 / 拒绝原因）。**预制体字段**：缺了只是不显示这行字。")]
        [SerializeField] private TMP_Text recipeLabel;

        [Header("品质色（白盒占位图标的颜色）")]
        [SerializeField] private Color commonColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        [SerializeField] private Color rareColor = new Color(0.30f, 0.55f, 0.95f, 1f);
        [SerializeField] private Color epicColor = new Color(0.65f, 0.35f, 0.90f, 1f);
        [Tooltip("表里查不到品质（未知 id）时的颜色。")]
        [SerializeField] private Color unknownColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        private readonly List<ItemEntry> items = new List<ItemEntry>();

        public override UILayer Layer => UILayer.Panel;

        public override bool IsFullScreen => true;

        public override bool CloseOnCancel => true;

        /// <summary>筛选按钮被点，携带档位。</summary>
        public event Action<InventoryFilter> OnFilterChanged;
        /// <summary>列表里某一行被点，携带物品 id。</summary>
        public event Action<int> OnEntrySelected;
        /// <summary>返回按钮被点。</summary>
        public event Action OnClose;
        /// <summary>「使用」按钮被点（用当前选中的那件道具）。</summary>
        public event Action OnUseRequested;
        /// <summary>「合成」按钮被点（合当前选中的那件产物）。</summary>
        public event Action OnCraftRequested;
        /// <summary>面板已被关闭（不论谁关的），在 <see cref="OnCloseAsync"/> 里触发一次。</summary>
        public event Action OnClosed;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            ValidateActionButtons();
            itemTemplate.SetActive(false);
            RemoveListeners();
            filterAllButton.onClick.AddListener(RaiseFilterAll);
            filterItemsButton.onClick.AddListener(RaiseFilterItems);
            filterCluesButton.onClick.AddListener(RaiseFilterClues);
            closeButton.onClick.AddListener(RaiseClose);
            if (useButton != null) useButton.onClick.AddListener(RaiseUse);
            if (craftButton != null) craftButton.onClick.AddListener(RaiseCraft);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            RemoveListeners();
            // 面板可能被 UIService 从外部关掉（Esc 走 CloseTopAsync），通知持有者收尾暂停令牌与输入图。
            Action closed = OnClosed;
            OnClosed = null;
            closed?.Invoke();
            OnFilterChanged = null;
            OnEntrySelected = null;
            OnClose = null;
            OnUseRequested = null;
            OnCraftRequested = null;
            for (int i = 0; i < items.Count; i++) SetActive(items[i].Root, false);
            return UniTask.CompletedTask;
        }

        /// <summary>标出当前筛选档位（对应按钮的 Highlight 显示）。</summary>
        public void SetFilter(InventoryFilter filter)
        {
            SetHighlight(filterAllButton, filter == InventoryFilter.All);
            SetHighlight(filterItemsButton, filter == InventoryFilter.Items);
            SetHighlight(filterCluesButton, filter == InventoryFilter.Clues);
        }

        /// <summary>刷新列表。<paramref name="selectedId"/> 对应的行高亮；列表为空时显示空提示、隐藏详情。</summary>
        public void SetList(IReadOnlyList<InventoryEntry> entries, int selectedId)
        {
            int count = entries == null ? 0 : entries.Count;
            while (items.Count < count) items.Add(CreateItem());

            for (int i = 0; i < items.Count; i++)
            {
                ItemEntry row = items[i];
                if (i >= count)
                {
                    SetActive(row.Root, false);
                    continue;
                }

                InventoryEntry entry = entries[i];
                row.Id = entry.Id;
                row.Name.text = entry.Name;
                row.Count.text = CountPrefix + entry.Count;
                row.Tag.text = InventoryRules.CategoryLabel(entry.Category);
                row.Icon.color = QualityColor(entry.Quality);
                SetActive(row.Highlight, entry.Id == selectedId);
                SetActive(row.Root, true);
            }

            SetActive(emptyLabel, count == 0);
            if (count == 0) SetActive(detailRoot, false);
        }

        /// <summary>刷新详情。<paramref name="hasEntry"/> 为 false 时隐藏详情区。</summary>
        public void SetDetail(bool hasEntry, InventoryEntry entry)
        {
            SetActive(detailRoot, hasEntry);
            if (!hasEntry) return;
            detailName.text = entry.Name;
            detailCategory.text = InventoryRules.CategoryLabel(entry.Category);
            detailDescription.text = entry.Desc;
        }

        /// <summary>
        /// 刷新两个动作按钮与合成说明。<paramref name="useHint"/> / <paramref name="craftHint"/> 是要显示给玩家的
        /// 中文短句（空串 = 隐藏那一行的说明）；按钮的**可点性只由这两个 bool 决定**——禁用不是错误。
        /// <para>
        /// 预制体没接这两个按钮时（<see cref="ValidateActionButtons"/> 已经 Warn 过一次）这里静默跳过：
        /// 面板其余部分照常可用，不为了兼容再写一套 UI。
        /// </para>
        /// </summary>
        public void SetActions(bool canUse, bool canCraft, string useHint, string craftHint)
        {
            if (useButton != null) useButton.interactable = canUse;
            if (craftButton != null) craftButton.interactable = canCraft;
            if (recipeLabel == null) return;

            string use = useHint ?? string.Empty;
            string craft = craftHint ?? string.Empty;
            if (use.Length == 0 && craft.Length == 0)
            {
                recipeLabel.text = string.Empty;
                SetActive(recipeLabel.gameObject, false);
                return;
            }

            recipeLabel.text = use.Length == 0 ? craft : (craft.Length == 0 ? use : use + "\n" + craft);
            SetActive(recipeLabel.gameObject, true);
        }

        private Color QualityColor(global::cfg.EItemQuality quality)
        {
            switch (quality)
            {
                case global::cfg.EItemQuality.Common: return commonColor;
                case global::cfg.EItemQuality.Rare: return rareColor;
                case global::cfg.EItemQuality.Epic: return epicColor;
                default: return unknownColor;
            }
        }

        private ItemEntry CreateItem()
        {
            GameObject go = Instantiate(itemTemplate, listRoot);
            Transform t = go.transform;
            var row = new ItemEntry
            {
                Root = go,
                Button = go.GetComponent<Button>(),
                Highlight = t.Find(HighlightName).gameObject,
                Icon = t.Find(IconName).GetComponent<Image>(),
                Name = FindText(t, NameName),
                Count = FindText(t, CountName),
                Tag = FindText(t, TagName),
            };
            // 只在建池项时绑一次；之后换内容只改 row.Id，回调读字段。
            row.Button.onClick.AddListener(() => OnEntrySelected?.Invoke(row.Id));
            return row;
        }

        // 逐个点名缺失字段与模板子物体，预制体按名字接线时一眼看出漏了哪个。
        private void Validate()
        {
            var missing = new List<string>();
            CheckFilterButton(filterAllButton, nameof(filterAllButton), missing);
            CheckFilterButton(filterItemsButton, nameof(filterItemsButton), missing);
            CheckFilterButton(filterCluesButton, nameof(filterCluesButton), missing);
            if (listRoot == null) missing.Add(nameof(listRoot));
            if (itemTemplate == null) missing.Add(nameof(itemTemplate));
            else
            {
                Transform t = itemTemplate.transform;
                if (itemTemplate.GetComponent<Button>() == null) missing.Add(nameof(itemTemplate) + "(Button)");
                if (t.Find(HighlightName) == null) missing.Add(nameof(itemTemplate) + "/" + HighlightName);
                Transform icon = t.Find(IconName);
                if (icon == null || icon.GetComponent<Image>() == null) missing.Add(nameof(itemTemplate) + "/" + IconName);
                if (FindText(t, NameName) == null) missing.Add(nameof(itemTemplate) + "/" + NameName);
                if (FindText(t, CountName) == null) missing.Add(nameof(itemTemplate) + "/" + CountName);
                if (FindText(t, TagName) == null) missing.Add(nameof(itemTemplate) + "/" + TagName);
            }

            if (emptyLabel == null) missing.Add(nameof(emptyLabel));
            if (detailRoot == null) missing.Add(nameof(detailRoot));
            if (detailName == null) missing.Add(nameof(detailName));
            if (detailCategory == null) missing.Add(nameof(detailCategory));
            if (detailDescription == null) missing.Add(nameof(detailDescription));
            if (closeButton == null) missing.Add(nameof(closeButton));
            if (missing.Count > 0)
                throw new InvalidOperationException("InventoryPanelView 引用未接线：" + string.Join("、", missing));
        }

        /// <summary>
        /// 两个动作按钮与合成说明是**新增字段**（预制体外由主窗口用 MCP 落）：它们没接时**不抛**，
        /// 只记一条 Warn 点名缺了哪个字段。理由：抛的话整块背包面板打不开（连筛选与详情都用不了），
        /// 而缺两个按钮只是少两个动作——降级要按影响面分级。
        /// </summary>
        private void ValidateActionButtons()
        {
            var missing = new List<string>();
            if (useButton == null) missing.Add(nameof(useButton));
            if (craftButton == null) missing.Add(nameof(craftButton));
            if (recipeLabel == null) missing.Add(nameof(recipeLabel));
            if (missing.Count == 0)
            {
                return;
            }

            Debug.LogWarning("InventoryPanelView 的动作按钮未接线：" + string.Join("、", missing)
                             + "。背包面板照常打开，但「使用」「合成」不可点。"
                             + "把 Prefabs/UI/InventoryPanelView.prefab 里 Detail 下的按钮拖到这些字段上。", this);
        }

        private static void CheckFilterButton(Button button, string fieldName, List<string> missing)
        {
            if (button == null) missing.Add(fieldName);
            else if (button.transform.Find(HighlightName) == null) missing.Add(fieldName + "/" + HighlightName);
        }

        private static void SetHighlight(Button button, bool value)
        {
            Transform highlight = button.transform.Find(HighlightName);
            if (highlight != null) SetActive(highlight.gameObject, value);
        }

        private static TMP_Text FindText(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            return child == null ? null : child.GetComponent<TMP_Text>();
        }

        private static void SetActive(GameObject go, bool value)
        {
            if (go.activeSelf != value) go.SetActive(value);
        }

        private void RemoveListeners()
        {
            if (filterAllButton != null) filterAllButton.onClick.RemoveListener(RaiseFilterAll);
            if (filterItemsButton != null) filterItemsButton.onClick.RemoveListener(RaiseFilterItems);
            if (filterCluesButton != null) filterCluesButton.onClick.RemoveListener(RaiseFilterClues);
            if (closeButton != null) closeButton.onClick.RemoveListener(RaiseClose);
            if (useButton != null) useButton.onClick.RemoveListener(RaiseUse);
            if (craftButton != null) craftButton.onClick.RemoveListener(RaiseCraft);
        }

        private void RaiseFilterAll() => OnFilterChanged?.Invoke(InventoryFilter.All);
        private void RaiseFilterItems() => OnFilterChanged?.Invoke(InventoryFilter.Items);
        private void RaiseFilterClues() => OnFilterChanged?.Invoke(InventoryFilter.Clues);
        private void RaiseClose() => OnClose?.Invoke();
        private void RaiseUse() => OnUseRequested?.Invoke();
        private void RaiseCraft() => OnCraftRequested?.Invoke();

        private sealed class ItemEntry
        {
            public int Id;
            public GameObject Root;
            public Button Button;
            public GameObject Highlight;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text Count;
            public TMP_Text Tag;
        }
    }
}
