// 职责：全屏任务面板——左侧进行中任务列表、右侧选中任务详情（描述、目标清单、追踪按钮）；只显示与抛事件，不注入服务。
// 为什么新建：任务系统首次落地（PRP/quest-system），HUD 是常驻 Hud 层的单行摘要，面板是 Panel 层的完整列表，层级与生命周期不同。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
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
        private const string ItemHighlightName = "Highlight";
        private const string ObjectiveMarkName = "Mark";
        private const string ObjectiveTextName = "Text";
        private const string DoneMark = "✓";
        private const string PendingMark = "○";

        [Tooltip("任务列表容器（通常带 VerticalLayoutGroup）。")]
        [SerializeField] private RectTransform listRoot;
        [Tooltip("列表项模板：根挂 Button，子物体 Title(TMP)、Kind(TMP)、Highlight。")]
        [SerializeField] private GameObject itemTemplate;
        [Tooltip("没有进行中任务时显示的提示。")]
        [SerializeField] private GameObject emptyLabel;
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

        private readonly List<ItemEntry> items = new List<ItemEntry>();
        private readonly List<ObjectiveEntry> objectives = new List<ObjectiveEntry>();

        public override UILayer Layer => UILayer.Panel;

        /// <summary>列表里某个任务被点，携带任务 id。</summary>
        public event Action<int> OnQuestSelected;
        /// <summary>追踪按钮被点。</summary>
        public event Action OnTrackToggled;
        /// <summary>关闭按钮被点。</summary>
        public event Action OnClose;
        /// <summary>面板已被关闭（不论谁关的），在 <see cref="OnCloseAsync"/> 里触发一次。</summary>
        public event Action OnClosed;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            itemTemplate.SetActive(false);
            objectiveTemplate.SetActive(false);
            trackButton.onClick.RemoveListener(RaiseTrackToggled);
            closeButton.onClick.RemoveListener(RaiseClose);
            trackButton.onClick.AddListener(RaiseTrackToggled);
            closeButton.onClick.AddListener(RaiseClose);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (trackButton != null) trackButton.onClick.RemoveListener(RaiseTrackToggled);
            if (closeButton != null) closeButton.onClick.RemoveListener(RaiseClose);
            // 面板可能被 UIService 从外部关掉（返回键走 CloseTopAsync），通知持有者收尾暂停令牌与输入图。
            Action closed = OnClosed;
            OnClosed = null;
            closed?.Invoke();
            OnQuestSelected = null;
            OnTrackToggled = null;
            OnClose = null;
            for (int i = 0; i < items.Count; i++) SetActive(items[i].Root, false);
            for (int i = 0; i < objectives.Count; i++) SetActive(objectives[i].Root, false);
            return UniTask.CompletedTask;
        }

        /// <summary>刷新列表。<paramref name="selectedId"/> 对应的项高亮；列表为空时显示空提示、隐藏详情。</summary>
        public void SetList(IReadOnlyList<QuestProgress> ordered, int selectedId, string mainLabel, string sideLabel)
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
                entry.Title.text = progress.Definition.Title;
                entry.Kind.text = progress.Definition.Kind == QuestKind.Main ? mainLabel : sideLabel;
                SetActive(entry.Highlight, progress.Id == selectedId);
                SetActive(entry.Root, true);
            }

            SetActive(emptyLabel, count == 0);
            if (count == 0) SetActive(detailRoot, false);
        }

        /// <summary>
        /// 刷新详情。<paramref name="progress"/> 为 null 时隐藏详情并禁用追踪按钮。
        /// 目标清单：当前目标之前打勾、当前与之后画圈；当前目标要求多次时追加「 已完成/需要」。
        /// </summary>
        public void SetDetail(QuestProgress progress, bool tracked, string kindLabel, string trackText, string untrackText)
        {
            if (progress == null)
            {
                SetActive(detailRoot, false);
                trackButton.interactable = false;
                return;
            }

            SetActive(detailRoot, true);
            QuestDefinition definition = progress.Definition;
            detailTitle.text = definition.Title;
            detailKind.text = kindLabel;
            detailDescription.text = definition.Description;

            IReadOnlyList<QuestObjectiveDefinition> defs = definition.Objectives;
            while (objectives.Count < defs.Count) objectives.Add(CreateObjective());
            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveEntry entry = objectives[i];
                if (i >= defs.Count)
                {
                    SetActive(entry.Root, false);
                    continue;
                }

                QuestObjectiveDefinition def = defs[i];
                bool current = i == progress.ObjectiveIndex;
                entry.Mark.text = i < progress.ObjectiveIndex ? DoneMark : PendingMark;
                entry.Text.text = current && def.RequiredCount > 1
                    ? def.Text + " " + progress.Count + "/" + def.RequiredCount
                    : def.Text;
                SetActive(entry.Root, true);
            }

            trackLabel.text = tracked ? untrackText : trackText;
            trackButton.interactable = true;
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
                Highlight = go.transform.Find(ItemHighlightName).gameObject,
            };
            // 只在建池项时绑一次；之后换内容只改 entry.Id，回调读字段。
            entry.Button.onClick.AddListener(() => OnQuestSelected?.Invoke(entry.Id));
            return entry;
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
                if (itemTemplate.transform.Find(ItemHighlightName) == null) missing.Add(nameof(itemTemplate) + "/" + ItemHighlightName);
            }

            if (emptyLabel == null) missing.Add(nameof(emptyLabel));
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

        private sealed class ItemEntry
        {
            public int Id;
            public GameObject Root;
            public Button Button;
            public TMP_Text Title;
            public TMP_Text Kind;
            public GameObject Highlight;
        }

        private sealed class ObjectiveEntry
        {
            public GameObject Root;
            public TMP_Text Mark;
            public TMP_Text Text;
        }
    }
}
