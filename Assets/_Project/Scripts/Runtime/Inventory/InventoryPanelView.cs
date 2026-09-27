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
        [Tooltip("筛选：物品（材料 + 消耗品 + 关键物）。")]
        [SerializeField] private Button filterItemsButton;
        [Tooltip("筛选：线索。")]
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
        /// <summary>面板已被关闭（不论谁关的），在 <see cref="OnCloseAsync"/> 里触发一次。</summary>
        public event Action OnClosed;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            itemTemplate.SetActive(false);
            RemoveListeners();
            filterAllButton.onClick.AddListener(RaiseFilterAll);
            filterItemsButton.onClick.AddListener(RaiseFilterItems);
            filterCluesButton.onClick.AddListener(RaiseFilterClues);
            closeButton.onClick.AddListener(RaiseClose);
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
        }

        private void RaiseFilterAll() => OnFilterChanged?.Invoke(InventoryFilter.All);
        private void RaiseFilterItems() => OnFilterChanged?.Invoke(InventoryFilter.Items);
        private void RaiseFilterClues() => OnFilterChanged?.Invoke(InventoryFilter.Clues);
        private void RaiseClose() => OnClose?.Invoke();

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
