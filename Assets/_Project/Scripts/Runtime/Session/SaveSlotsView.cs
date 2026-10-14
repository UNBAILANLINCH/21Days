// 职责：全屏选槽面板——标题（选择存档 / 新游戏）、每槽一行（槽号、进度描述或状态、保存时间与游玩时长、删除）、底部返回；
//   只显示与抛事件，不注入服务。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestPanelView / InventoryPanelView 的行绑的是任务进度 / 背包条目，字段与三态语义对不上存档槽。
//   2. 扩展不行：把槽位塞进它们会让 Quest / Inventory 认识存档，名实不符；列表行池化的写法照 QuestPanelView 抄。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Session
{
    /// <summary>
    /// 选槽面板。预制体 <c>Assets/_Project/Prefabs/UI/SaveSlotsView.prefab</c>，Addressables 地址须为 <c>SaveSlotsView</c>。
    /// <para>
    /// 接线提示：<c>rowTemplate</c> 根上挂 Button，子物体须有 <c>Slot</c>(TMP)、<c>Primary</c>(TMP)、<c>Secondary</c>(TMP)、
    /// <c>Delete</c>(Button)。模板运行时隐藏，行按需复制并复用。基类的 Default Selected 拖「返回」。
    /// </para>
    /// </summary>
    public sealed class SaveSlotsView : UIView
    {
        private const string RowSlotName = "Slot";
        private const string RowPrimaryName = "Primary";
        private const string RowSecondaryName = "Secondary";
        private const string RowDeleteName = "Delete";

        private const string LoadTitle = "选择存档";
        private const string NewGameTitle = "新游戏";
        private const string SlotLabelPrefix = "槽 ";
        private const string EmptyText = "新游戏";
        private const string UnavailableText = "不可用";
        private const string UnknownProgressText = "进度未知";
        private const string SecondarySeparator = "\n游玩时长：";
        private const string SavedAtFormat = "yyyy-MM-dd";

        [Tooltip("面板标题（按模式显示「选择存档」/「新游戏」）。")]
        [SerializeField] private TMP_Text titleLabel;
        [Tooltip("槽位行容器（通常带 VerticalLayoutGroup）。")]
        [SerializeField] private RectTransform listRoot;
        [Tooltip("行模板：根挂 Button，子物体 Slot(TMP)、Primary(TMP)、Secondary(TMP)、Delete(Button)。")]
        [SerializeField] private GameObject rowTemplate;
        [Tooltip("底部「返回」；同时应拖到基类的 Default Selected 上。")]
        [SerializeField] private Button backButton;

        [Header("悬挂卷轴（按槽号排序）")]
        [SerializeField] private Sprite[] normalSprites;
        [SerializeField] private Sprite[] selectedSprites;
        [SerializeField] private Sprite[] unavailableSprites;
        [SerializeField] private Sprite emptySprite;
        [SerializeField] private Vector2 firstSlotPosition;
        [SerializeField] private float slotSpacing;
        [SerializeField] private float unavailableTextOffset;
        [SerializeField] private Color selectedTextColor = new Color32(39, 160, 191, 255);

        private readonly List<RowEntry> rows = new List<RowEntry>();

        public override UILayer Layer => UILayer.Panel;

        public override bool IsFullScreen => true;

        public override bool CloseOnCancel => true;

        /// <summary>某一行被点，携带槽号。</summary>
        public event Action<int> OnSlotClicked;
        /// <summary>某一行的「删除」被点，携带槽号。</summary>
        public event Action<int> OnDeleteClicked;
        /// <summary>「返回」被点。</summary>
        public event Action OnBack;
        /// <summary>面板已被关闭（不论谁关的），在 <see cref="OnCloseAsync"/> 里触发一次。</summary>
        public event Action OnClosed;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            rowTemplate.SetActive(false);
            Hook(backButton, RaiseBack);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            Unhook(backButton, RaiseBack);
            // 面板可能被 UIService 从外部关掉（返回键走 CloseTopAsync、离开标题状态），通知持有者收尾。
            Action closed = OnClosed;
            OnClosed = null;
            closed?.Invoke();
            OnSlotClicked = null;
            OnDeleteClicked = null;
            OnBack = null;
            for (int i = 0; i < rows.Count; i++) SetActive(rows[i].Root, false);
            return UniTask.CompletedTask;
        }

        /// <summary>按槽摘要重画全部行与标题。行数 = <paramref name="infos"/> 条数，多出的池化行隐藏。</summary>
        public void Bind(IReadOnlyList<SlotInfo> infos, SlotsMode mode)
        {
            if (titleLabel != null) titleLabel.text = FormatTitle(mode);

            int count = infos == null ? 0 : infos.Count;
            while (rows.Count < count) rows.Add(CreateRow());

            for (int i = 0; i < rows.Count; i++)
            {
                RowEntry row = rows[i];
                if (i >= count)
                {
                    SetActive(row.Root, false);
                    continue;
                }

                SlotInfo info = infos[i];
                row.Slot = info.Slot;
                ApplyAppearance(row, info);
                row.SlotLabel.text = FormatSlotLabel(info.Slot);
                row.Primary.text = FormatPrimary(info);
                SetActive(row.Primary.gameObject, info.State != SlotState.Empty || normalSprites == null || normalSprites.Length == 0);
                row.Secondary.text = FormatSecondary(info);
                SetActive(row.Secondary.gameObject, info.State == SlotState.Available);
                row.Button.interactable = IsRowSelectable(info.State, mode);
                SetActive(row.Delete.gameObject, IsDeleteVisible(info.State));
                SetActive(row.Root, true);
            }
        }

        /// <summary>面板标题：读档「选择存档」，新游戏「新游戏」。</summary>
        public static string FormatTitle(SlotsMode mode) => mode == SlotsMode.NewGame ? NewGameTitle : LoadTitle;

        /// <summary>行左侧的槽号文字「槽 N」。</summary>
        public static string FormatSlotLabel(int slot) => SlotLabelPrefix + slot.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// 行第一行文字（三态）：可读槽显示进度描述（空则「进度未知」），空槽「新游戏」，坏槽「不可用」。
        /// </summary>
        public static string FormatPrimary(SlotInfo info)
        {
            switch (info.State)
            {
                case SlotState.Available:
                    return string.IsNullOrEmpty(info.ProgressText) ? UnknownProgressText : info.ProgressText;
                case SlotState.Unavailable:
                    return UnavailableText;
                default:
                    return EmptyText;
            }
        }

        /// <summary>可读槽分两行显示本地保存日期和游玩时长，其它两态为空串。</summary>
        public static string FormatSecondary(SlotInfo info)
        {
            if (info.State != SlotState.Available) return string.Empty;
            string savedAt = info.SavedAtUtc.ToLocalTime().ToString(SavedAtFormat, CultureInfo.InvariantCulture);
            return savedAt + SecondarySeparator + FormatPlaytime(info.PlaytimeSeconds);
        }

        /// <summary>
        /// 游玩时长「mm:ss」：分钟不封顶（超过一小时显示 75:03 这样），秒向下取整；负数 / NaN / 无穷按 0 处理。
        /// </summary>
        public static string FormatPlaytime(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return "00:00";
            // 上面已排除 ≤ 0，正数强转即向下取整。
            long total = (long)seconds;
            long minutes = total / 60;
            long secs = total % 60;
            return minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + secs.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>行能不能点：读档模式只有可读槽能点；新游戏模式三态都能点（非空槽由控制器先确认覆盖）。</summary>
        public static bool IsRowSelectable(SlotState state, SlotsMode mode) =>
            mode == SlotsMode.NewGame || state == SlotState.Available;

        /// <summary>「删除」只在可读槽上显示；空槽没东西可删，坏槽走新游戏覆盖。</summary>
        public static bool IsDeleteVisible(SlotState state) => state == SlotState.Available;

        private RowEntry CreateRow()
        {
            GameObject go = Instantiate(rowTemplate, listRoot);
            Transform t = go.transform;
            var row = new RowEntry
            {
                Root = go,
                Button = go.GetComponent<Button>(),
                SlotLabel = FindText(t, RowSlotName),
                Primary = FindText(t, RowPrimaryName),
                Secondary = FindText(t, RowSecondaryName),
                Delete = t.Find(RowDeleteName).GetComponent<Button>(),
            };
            // 只在建池行时绑一次；之后换内容只改 row.Slot，回调读字段。
            row.Button.onClick.AddListener(() => OnSlotClicked?.Invoke(row.Slot));
            row.Delete.onClick.AddListener(() => OnDeleteClicked?.Invoke(row.Slot));
            row.Image = go.GetComponent<Image>();
            row.PrimaryPosition = row.Primary.rectTransform.anchoredPosition;
            if (row.Image != null)
                row.Image.RegisterDirtyVerticesCallback(() => RefreshTextColor(row));
            return row;
        }

        private void ApplyAppearance(RowEntry row, SlotInfo info)
        {
            int index = info.Slot - 1;
            if (normalSprites == null || index < 0 || index >= normalSprites.Length) return;
            Image image = row.Root.GetComponent<Image>();
            if (image == null) return;
            Sprite normal = info.State == SlotState.Empty ? emptySprite :
                info.State == SlotState.Unavailable ? unavailableSprites[index] : normalSprites[index];
            image.sprite = normal;
            image.overrideSprite = null;
            image.SetNativeSize();
            var rect = (RectTransform)row.Root.transform;
            rect.anchoredPosition = firstSlotPosition + new Vector2(index * slotSpacing, 0f);
            Sprite selected = info.State == SlotState.Available ? selectedSprites[index] : normal;
            row.SelectedSprite = info.State == SlotState.Available ? selected : null;
            row.Button.transition = Selectable.Transition.SpriteSwap;
            row.Button.spriteState = new SpriteState
            {
                highlightedSprite = selected, pressedSprite = selected,
                selectedSprite = selected, disabledSprite = normal,
            };
            row.Primary.rectTransform.anchoredPosition = row.PrimaryPosition +
                new Vector2(0f, info.State == SlotState.Unavailable ? -unavailableTextOffset : 0f);
            RefreshTextColor(row);
        }

        private void RefreshTextColor(RowEntry row)
        {
            bool selected = row.SelectedSprite != null && row.Image != null && row.Image.overrideSprite == row.SelectedSprite;
            Color color = selected ? selectedTextColor : Color.white;
            row.Primary.color = color;
            row.Secondary.color = color;
        }

        // 逐个点名缺失字段与模板子物体，预制体按名字接线时一眼看出漏了哪个。
        private void Validate()
        {
            var missing = new List<string>();
            if (listRoot == null) missing.Add(nameof(listRoot));
            if (rowTemplate == null) missing.Add(nameof(rowTemplate));
            else
            {
                Transform t = rowTemplate.transform;
                if (rowTemplate.GetComponent<Button>() == null) missing.Add(nameof(rowTemplate) + "(Button)");
                if (FindText(t, RowSlotName) == null) missing.Add(nameof(rowTemplate) + "/" + RowSlotName);
                if (FindText(t, RowPrimaryName) == null) missing.Add(nameof(rowTemplate) + "/" + RowPrimaryName);
                if (FindText(t, RowSecondaryName) == null) missing.Add(nameof(rowTemplate) + "/" + RowSecondaryName);
                Transform delete = t.Find(RowDeleteName);
                if (delete == null || delete.GetComponent<Button>() == null) missing.Add(nameof(rowTemplate) + "/" + RowDeleteName + "(Button)");
            }

            if (backButton == null) missing.Add(nameof(backButton));
            if (normalSprites != null && normalSprites.Length > 0 &&
                (selectedSprites == null || selectedSprites.Length != normalSprites.Length ||
                 unavailableSprites == null || unavailableSprites.Length != normalSprites.Length || emptySprite == null))
                missing.Add("卷轴三态图片数量或空槽图片");
            if (missing.Count > 0)
                throw new InvalidOperationException("SaveSlotsView 引用未接线：" + string.Join("、", missing));
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

        private void RaiseBack() => OnBack?.Invoke();

        private sealed class RowEntry
        {
            public int Slot;
            public GameObject Root;
            public Button Button;
            public TMP_Text SlotLabel;
            public TMP_Text Primary;
            public TMP_Text Secondary;
            public Button Delete;
            public Image Image;
            public Sprite SelectedSprite;
            public Vector2 PrimaryPosition;
        }
    }
}
