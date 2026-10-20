// 职责：全屏任务面板——左侧「进行中 / 已完成」两个标签的任务列表、右侧选中任务详情（描述、目标清单、追踪按钮）；只显示与抛事件，不注入服务。
// 为什么新建：任务系统首次落地（PRP/quest-system），HUD 是常驻 Hud 层的单行摘要，面板是 Panel 层的完整列表，层级与生命周期不同。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Quest
{
    /// <summary>
    /// 任务面板。预制体 Addressables 地址须为 <c>QuestPanelView</c>。
    /// <para>
    /// 接线提示：<c>itemTemplate</c> 根上挂 Button，子物体须有 <c>Title</c>(TMP)、<c>Kind</c>(TMP)、<c>Highlight</c>；
    /// <c>objectiveTemplate</c> 子物体须有 <c>Mark</c>(TMP)、<c>Text</c>(TMP)。两个模板运行时隐藏，列表项按需复制并复用。
    /// </para>
    /// </summary>
    public sealed class QuestPanelView : UIView
    {
        private const string ItemTitleName = "Title";
        private const string ItemKindName = "Kind";
        private const string ItemStatusName = "Status";
        private const string ItemHighlightName = "Highlight";
        private const string ObjectiveMarkName = "Mark";
        private const string ObjectiveTextName = "Text";
        private const string DoneMark = "✓";
        private const string PendingMark = "○";
        private const string TabSelectedName = "Selected";

        [Tooltip("任务列表容器（通常带 VerticalLayoutGroup）。")]
        [SerializeField] private RectTransform listRoot;
        [Tooltip("列表项模板：根挂 Button，子物体 Title(TMP)、Kind(TMP)、Highlight。")]
        [SerializeField] private GameObject itemTemplate;
        [Tooltip("没有进行中任务时显示的提示。")]
        [SerializeField] private GameObject emptyLabel;
        [SerializeField] private GameObject emptyBackdrop;
        [Tooltip("右侧详情区根节点。")]
        [SerializeField] private GameObject detailRoot;
        [SerializeField] private TMP_Text detailTitle;
        [SerializeField] private TMP_Text detailKind;
        [SerializeField] private TMP_Text detailDescription;
        [Tooltip("目标清单容器。")]
        [SerializeField] private RectTransform objectiveRoot;
        [Tooltip("目标行模板：子物体 Mark(TMP)、Text(TMP)。")]
        [SerializeField] private GameObject objectiveTemplate;
        [Tooltip("追踪 / 取消追踪按钮。")]
        [SerializeField] private Button trackButton;
        [SerializeField] private TMP_Text trackLabel;
        [SerializeField] private Button closeButton;
        [Tooltip("「进行中」标签按钮；子物体 Selected 为选中态。")]
        [SerializeField] private Button tabInProgressButton;
        [Tooltip("「已完成」标签按钮；子物体 Selected 为选中态。")]
        [SerializeField] private Button tabCompletedButton;
        [Header("交付美术与阅读区域")]
        [SerializeField] private Sprite mainIdle;
        [SerializeField] private Sprite mainSelected;
        [SerializeField] private Sprite sideIdle;
        [SerializeField] private Sprite sideSelected;
        [SerializeField] private Image detailKindIcon;
        [SerializeField] private Sprite mainWordmark;
        [SerializeField] private Sprite sideWordmark;
        [SerializeField] private ScrollRect listScroll;
        [SerializeField] private ScrollRect detailScroll;
        [SerializeField] private CanvasGroup detailGroup;
        [Min(0f)] [SerializeField] private float detailRevealSeconds = 0.16f;
        [SerializeField] private Color itemTextColor = new Color(88f / 255f, 150f / 255f, 163f / 255f);

        private MotionHandle detailReveal;
        private int detailId;
        private QuestPanelTab currentTab;

        private readonly List<ItemEntry> items = new List<ItemEntry>();
        private readonly List<ObjectiveEntry> objectives = new List<ObjectiveEntry>();

        public override UILayer Layer => UILayer.Panel;

        /// <summary>列表里某个任务被点，携带任务 id。</summary>
        public event Action<int> OnQuestSelected;
        /// <summary>追踪按钮被点。</summary>
        public event Action OnTrackToggled;
        /// <summary>关闭按钮被点。</summary>
        public event Action OnClose;
        /// <summary>标签按钮被点，携带目标标签。</summary>
        public event Action<QuestPanelTab> OnTabSelected;
        /// <summary>面板已被关闭（不论谁关的），在 <see cref="OnCloseAsync"/> 里触发一次。</summary>
        public event Action OnClosed;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            itemTemplate.SetActive(false);
            objectiveTemplate.SetActive(false);
            trackButton.onClick.RemoveListener(RaiseTrackToggled);
            closeButton.onClick.RemoveListener(RaiseClose);
            tabInProgressButton.onClick.RemoveListener(RaiseTabInProgress);
            tabCompletedButton.onClick.RemoveListener(RaiseTabCompleted);
            trackButton.onClick.AddListener(RaiseTrackToggled);
            closeButton.onClick.AddListener(RaiseClose);
            tabInProgressButton.onClick.AddListener(RaiseTabInProgress);
            tabCompletedButton.onClick.AddListener(RaiseTabCompleted);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            StopDetailReveal();
            detailId = 0;
            if (trackButton != null) trackButton.onClick.RemoveListener(RaiseTrackToggled);
            if (closeButton != null) closeButton.onClick.RemoveListener(RaiseClose);
            if (tabInProgressButton != null) tabInProgressButton.onClick.RemoveListener(RaiseTabInProgress);
            if (tabCompletedButton != null) tabCompletedButton.onClick.RemoveListener(RaiseTabCompleted);
            // 面板可能被 UIService 从外部关掉（返回键走 CloseTopAsync），通知持有者收尾暂停令牌与输入图。
            Action closed = OnClosed;
            OnClosed = null;
            closed?.Invoke();
            OnQuestSelected = null;
            OnTrackToggled = null;
            OnClose = null;
            OnTabSelected = null;
            for (int i = 0; i < items.Count; i++) SetActive(items[i].Root, false);
            for (int i = 0; i < objectives.Count; i++) SetActive(objectives[i].Root, false);
            return UniTask.CompletedTask;
        }

        /// <summary>切换标签的选中态显示（只改外观，不抛事件）。</summary>
        public void SetTab(QuestPanelTab tab)
        {
            if (currentTab != tab && listScroll != null) listScroll.verticalNormalizedPosition = 1f;
            currentTab = tab;
            SetTabSelected(tabInProgressButton, tab == QuestPanelTab.InProgress);
            SetTabSelected(tabCompletedButton, tab == QuestPanelTab.Completed);
        }

        /// <summary>
        /// 刷新列表。<paramref name="selectedId"/> 对应的项高亮；列表为空时显示 <paramref name="emptyText"/>、隐藏详情。
        /// </summary>
        public void SetList(IReadOnlyList<QuestProgress> ordered, int selectedId, string mainLabel, string sideLabel, string emptyText)
        {
            int count = ordered == null ? 0 : ordered.Count;
            while (items.Count < count) items.Add(CreateItem());

            for (int i = 0; i < items.Count; i++)
            {
                ItemEntry entry = items[i];
                if (i >= count)
                {
                    SetActive(entry.Root, false);
                    continue;
                }

                QuestProgress progress = ordered[i];
                entry.Id = progress.Id;
                bool selected = progress.Id == selectedId;
                bool main = progress.Definition.Kind == QuestKind.Main;
                bool completed = progress.State == QuestState.Completed;
                SetText(entry.Title, progress.Definition.Title);
                entry.Title.maxVisibleLines = completed ? 1 : 2;
                entry.Title.enableWordWrapping = !completed;
                entry.Title.color = selected ? Color.white : itemTextColor;
                SetText(entry.Status, "（已完成）");
                entry.Status.color = entry.Title.color;
                SetActive(entry.Status.gameObject, completed);
                SetText(entry.Kind, main ? mainLabel : sideLabel);
                if (entry.Paper != null)
                {
                    entry.Paper.sprite = main ? mainIdle : sideIdle;
                    SetActive(entry.Paper.gameObject, !selected);
                    entry.Button.targetGraphic = selected ? entry.SelectedPaper : entry.Paper;
                }
                if (entry.SelectedPaper != null) entry.SelectedPaper.sprite = main ? mainSelected : sideSelected;
                SetActive(entry.Highlight, selected);
                SetActive(entry.Root, true);
            }

            TMP_Text emptyTmp = emptyLabel.GetComponent<TMP_Text>();
            if (emptyTmp != null && emptyText != null) SetText(emptyTmp, emptyText);
            SetActive(emptyLabel, count == 0);
            SetActive(emptyBackdrop, count == 0);
            if (count == 0) SetActive(detailRoot, false);
            // 池项和长标题一次批量变更后排版，供导航滚动读到实际位置；不在帧循环刷新。
            LayoutRebuilder.ForceRebuildLayoutImmediate(listRoot);
        }

        /// <summary>
        /// 刷新详情。<paramref name="progress"/> 为 null 时隐藏详情并禁用追踪按钮。
        /// 目标清单：当前目标之前打勾、当前与之后画圈；当前目标要求多次时追加「 已完成/需要」。
        /// <paramref name="completed"/> 为 true（已完成标签）：所有目标打勾、不追加计数，追踪按钮隐藏。
        /// </summary>
        public void SetDetail(QuestProgress progress, bool tracked, string kindLabel, string trackText, string untrackText, bool completed, string currentObjective = "")
        {
            if (progress == null)
            {
                StopDetailReveal();
                detailId = 0;
                SetActive(detailRoot, false);
                SetActive(trackButton.gameObject, true);
                trackButton.interactable = false;
                RefreshNavigation();
                return;
            }

            SetActive(detailRoot, true);
            QuestDefinition definition = progress.Definition;
            bool changed = detailId != progress.Id;
            detailId = progress.Id;
            SetText(detailTitle, definition.Title);
            SetText(detailKind, kindLabel);
            SetText(detailDescription, definition.Description);
            if (detailKindIcon != null) detailKindIcon.sprite = definition.Kind == QuestKind.Main ? mainWordmark : sideWordmark;

            IReadOnlyList<QuestObjectiveDefinition> defs = definition.Objectives;
            bool hasPresentation = !string.IsNullOrEmpty(currentObjective);
            int count = hasPresentation ? 1 : defs.Count;
            while (objectives.Count < count) objectives.Add(CreateObjective());
            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveEntry entry = objectives[i];
                if (i >= count)
                {
                    SetActive(entry.Root, false);
                    continue;
                }

                if (hasPresentation)
                {
                    SetText(entry.Mark, PendingMark); SetText(entry.Text, currentObjective);
                    SetActive(entry.Root, true); continue;
                }
                QuestObjectiveDefinition def = defs[i];
                bool current = !completed && i == progress.ObjectiveIndex;
                SetText(entry.Mark, completed || i < progress.ObjectiveIndex ? DoneMark : PendingMark);
                SetText(entry.Text, current && def.RequiredCount > 1
                    ? def.Text + " " + progress.Count + "/" + def.RequiredCount
                    : def.Text);
                SetActive(entry.Root, true);
            }

            SetActive(trackButton.gameObject, !completed);
            SetText(trackLabel, tracked ? untrackText : trackText);
            trackButton.interactable = true;
            if (detailScroll != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(detailScroll.content);
                if (changed) detailScroll.verticalNormalizedPosition = 1f;
            }
            RefreshNavigation();
            if (changed) RevealDetail();
        }

        private ItemEntry CreateItem()
        {
            GameObject go = Instantiate(itemTemplate, listRoot);
            var entry = new ItemEntry
            {
                Root = go,
                Button = go.GetComponent<Button>(),
                Title = FindText(go.transform, ItemTitleName),
                Kind = FindText(go.transform, ItemKindName),
                Status = FindText(go.transform, ItemStatusName),
                Highlight = go.transform.Find(ItemHighlightName).gameObject,
                Paper = go.transform.Find("Paper") == null ? null : go.transform.Find("Paper").GetComponent<Image>(),
                SelectedPaper = go.transform.Find(ItemHighlightName).GetComponent<Image>(),
            };
            // 只在建池项时绑一次；之后换内容只改 entry.Id，回调读字段。
            entry.Button.onClick.AddListener(() => OnQuestSelected?.Invoke(entry.Id));
            if (listScroll != null)
                go.AddComponent<UIScrollFocus>().Bind(listScroll);
            return entry;
        }

        private void RefreshNavigation()
        {
            Button first = null, previous = tabInProgressButton, selected = null;
            Selectable right = trackButton.gameObject.activeInHierarchy && trackButton.interactable ? trackButton : closeButton;
            if (detailScroll != null && detailRoot.activeSelf && detailScroll.verticalScrollbar != null
                && detailScroll.content.rect.height > detailScroll.viewport.rect.height)
                right = detailScroll.verticalScrollbar;
            for (int i = 0; i < items.Count; i++)
            {
                ItemEntry item = items[i];
                if (!item.Root.activeSelf) continue;
                if (first == null) first = item.Button;
                if (item.Highlight.activeSelf) selected = item.Button;
                SetNavigation(item.Button, previous, closeButton, closeButton, right);
                if (previous != tabInProgressButton)
                {
                    Navigation navigation = previous.navigation;
                    navigation.selectOnDown = item.Button;
                    previous.navigation = navigation;
                }
                previous = item.Button;
            }
            SetNavigation(tabInProgressButton, closeButton, first == null ? closeButton : first, closeButton, tabCompletedButton);
            SetNavigation(tabCompletedButton, closeButton, first == null ? closeButton : first, tabInProgressButton, right);
            SetNavigation(closeButton, null, tabInProgressButton, null, tabInProgressButton);
            SetNavigation(trackButton, detailScroll == null ? null : detailScroll.verticalScrollbar, closeButton,
                selected == null ? tabInProgressButton : selected, null);
            if (detailScroll != null && detailScroll.verticalScrollbar != null)
                SetNavigation(detailScroll.verticalScrollbar, null, null,
                    selected == null ? tabInProgressButton : selected, trackButton.gameObject.activeSelf ? trackButton : closeButton);
        }

        private static void SetNavigation(Selectable target, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            target.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up,
                selectOnDown = down, selectOnLeft = left, selectOnRight = right };
        }

        private void RevealDetail()
        {
            StopDetailReveal();
            if (detailGroup == null || detailRevealSeconds <= 0f || !isActiveAndEnabled) return;
            detailGroup.alpha = 0f;
            detailReveal = LMotion.Create(0f, 1f, detailRevealSeconds).WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).BindToAlpha(detailGroup).AddTo(gameObject);
        }

        private void OnDisable() => StopDetailReveal();

        private void StopDetailReveal()
        {
            if (detailReveal.IsActive()) detailReveal.Cancel();
            if (detailGroup != null) detailGroup.alpha = 1f;
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label.text != value) label.text = value;
        }

        private ObjectiveEntry CreateObjective()
        {
            GameObject go = Instantiate(objectiveTemplate, objectiveRoot);
            return new ObjectiveEntry
            {
                Root = go,
                Mark = FindText(go.transform, ObjectiveMarkName),
                Text = FindText(go.transform, ObjectiveTextName),
            };
        }

        // 逐个点名缺失字段与模板子物体，预制体按名字接线时一眼看出漏了哪个。
        private void Validate()
        {
            var missing = new List<string>();
            if (listRoot == null) missing.Add(nameof(listRoot));
            if (itemTemplate == null) missing.Add(nameof(itemTemplate));
            else
            {
                if (itemTemplate.GetComponent<Button>() == null) missing.Add(nameof(itemTemplate) + "(Button)");
                if (FindText(itemTemplate.transform, ItemTitleName) == null) missing.Add(nameof(itemTemplate) + "/" + ItemTitleName);
                if (FindText(itemTemplate.transform, ItemKindName) == null) missing.Add(nameof(itemTemplate) + "/" + ItemKindName);
                if (FindText(itemTemplate.transform, ItemStatusName) == null) missing.Add(nameof(itemTemplate) + "/" + ItemStatusName);
                if (itemTemplate.transform.Find(ItemHighlightName) == null) missing.Add(nameof(itemTemplate) + "/" + ItemHighlightName);
                Transform paper = itemTemplate.transform.Find("Paper");
                if (paper == null || paper.GetComponent<Image>() == null) missing.Add(nameof(itemTemplate) + "/Paper(Image)");
            }

            if (emptyLabel == null) missing.Add(nameof(emptyLabel));
            if (emptyBackdrop == null) missing.Add(nameof(emptyBackdrop));
            if (detailRoot == null) missing.Add(nameof(detailRoot));
            if (detailTitle == null) missing.Add(nameof(detailTitle));
            if (detailKind == null) missing.Add(nameof(detailKind));
            if (detailDescription == null) missing.Add(nameof(detailDescription));
            if (objectiveRoot == null) missing.Add(nameof(objectiveRoot));
            if (objectiveTemplate == null) missing.Add(nameof(objectiveTemplate));
            else
            {
                if (FindText(objectiveTemplate.transform, ObjectiveMarkName) == null) missing.Add(nameof(objectiveTemplate) + "/" + ObjectiveMarkName);
                if (FindText(objectiveTemplate.transform, ObjectiveTextName) == null) missing.Add(nameof(objectiveTemplate) + "/" + ObjectiveTextName);
            }

            if (trackButton == null) missing.Add(nameof(trackButton));
            if (trackLabel == null) missing.Add(nameof(trackLabel));
            if (closeButton == null) missing.Add(nameof(closeButton));
            if (tabInProgressButton == null) missing.Add(nameof(tabInProgressButton));
            else if (tabInProgressButton.transform.Find(TabSelectedName) == null) missing.Add(nameof(tabInProgressButton) + "/" + TabSelectedName);
            if (tabCompletedButton == null) missing.Add(nameof(tabCompletedButton));
            else if (tabCompletedButton.transform.Find(TabSelectedName) == null) missing.Add(nameof(tabCompletedButton) + "/" + TabSelectedName);
            if (mainIdle == null) missing.Add(nameof(mainIdle));
            if (mainSelected == null) missing.Add(nameof(mainSelected));
            if (sideIdle == null) missing.Add(nameof(sideIdle));
            if (sideSelected == null) missing.Add(nameof(sideSelected));
            if (detailKindIcon == null) missing.Add(nameof(detailKindIcon));
            if (mainWordmark == null) missing.Add(nameof(mainWordmark));
            if (sideWordmark == null) missing.Add(nameof(sideWordmark));
            if (listScroll == null || listScroll.viewport == null) missing.Add(nameof(listScroll));
            if (detailScroll == null || detailScroll.viewport == null) missing.Add(nameof(detailScroll));
            if (detailGroup == null) missing.Add(nameof(detailGroup));
            if (missing.Count > 0)
                throw new InvalidOperationException("QuestPanelView 引用未接线：" + string.Join("、", missing));
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

        private void RaiseTrackToggled() => OnTrackToggled?.Invoke();
        private void RaiseClose() => OnClose?.Invoke();
        private void RaiseTabInProgress() => OnTabSelected?.Invoke(QuestPanelTab.InProgress);
        private void RaiseTabCompleted() => OnTabSelected?.Invoke(QuestPanelTab.Completed);

        private static void SetTabSelected(Button tab, bool selected)
        {
            Transform marker = tab.transform.Find(TabSelectedName);
            if (marker != null) SetActive(marker.gameObject, selected);
        }

        private sealed class ItemEntry
        {
            public int Id;
            public GameObject Root;
            public Button Button;
            public TMP_Text Title;
            public TMP_Text Kind;
            public TMP_Text Status;
            public GameObject Highlight;
            public Image Paper;
            public Image SelectedPaper;
        }

        private sealed class ObjectiveEntry
        {
            public GameObject Root;
            public TMP_Text Mark;
            public TMP_Text Text;
        }
    }
}
