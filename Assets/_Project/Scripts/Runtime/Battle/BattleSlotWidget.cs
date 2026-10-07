// 职责：战斗界面上一格可悬停的格子（招式格、道具格、玩家状态格、怒气槽、BOSS 状态字、剩余回合）——
//   持有自己的图元引用，按「高亮 / 置暗」换样子，鼠标悬停时把详情文案交给 BattleView 弹出来，点击转成事件。
//   键盘选中（方向键导航）不弹详情：开场默认选中招式 1，选中即弹会让详情框一开场就盖在招式栏上；选中态靠按钮的选中色表现。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：InventoryPanelView 的行是按名字找子物体（Highlight / Icon / Name）的一次性写法，没有悬停详情；
//      工程里没有任何 IPointerEnter 的悬停组件。
//   2. 扩展不行：塞进 BattleView 就得让 BattleView 自己实现每一格的指针接口，格子一多 BattleView 序列化字段爆炸。
// 只显示与抛事件，不读规则、不注入服务（同 UIView 子类的约定）。
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Battle
{
    /// <summary>战斗界面的一格。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleSlotWidget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("可点的格子才拖（招式 / 道具）；状态格、怒气槽留空。")]
        [SerializeField] private Button button;

        [Tooltip("边框 / 底色：高亮与置暗换它的颜色。")]
        [SerializeField] private Image frame;

        [SerializeField] private TMP_Text title;

        [SerializeField] private TMP_Text detail;

        [Tooltip("角标（右下角数字）。")]
        [SerializeField] private TMP_Text badge;

        [Tooltip("置暗时盖上的半透明黑罩。")]
        [SerializeField] private GameObject dim;

        private string tooltip = string.Empty;
        private bool hooked;

        /// <summary>鼠标悬停开始（带上本格，BattleView 读 <see cref="Tooltip"/> 并按本格位置摆详情框）。</summary>
        public event Action<BattleSlotWidget> OnHoverStart;

        /// <summary>鼠标悬停结束。</summary>
        public event Action<BattleSlotWidget> OnHoverEnd;

        /// <summary>点击（只有带 Button 的格子会触发）。</summary>
        public event Action<BattleSlotWidget> OnClicked;

        /// <summary>按钮（可能为空）。</summary>
        public Button Button => button;

        /// <summary>当前悬停详情；空串 = 不弹。</summary>
        public string Tooltip => tooltip;

        /// <summary>本格携带的数据键（道具 id 等），由 BattleView 写入。</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>当前是否高亮（测试 / 回放读）。</summary>
        public bool IsLit { get; private set; }

        private void Awake() => Hook();

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
        }

        /// <summary>写字：标题、副标题、角标（null = 不改，空串 = 清空并藏起角标）。</summary>
        public void SetTexts(string titleText, string detailText, string badgeText)
        {
            if (title != null && titleText != null) title.text = titleText;
            if (detail != null && detailText != null) detail.text = detailText;
            if (badge != null && badgeText != null)
            {
                badge.text = badgeText;
                badge.gameObject.SetActive(badgeText.Length > 0);
            }
        }

        /// <summary>换悬停详情。</summary>
        public void SetTooltip(string text) => tooltip = text ?? string.Empty;

        /// <summary>高亮 / 置暗；<paramref name="interactable"/> 决定按钮能不能点（置暗的格子也能悬停看详情）。</summary>
        public void SetLook(bool lit, Color litColor, Color dimColor, bool interactable)
        {
            IsLit = lit;
            if (frame != null) frame.color = lit ? litColor : dimColor;
            if (dim != null) dim.SetActive(!lit);
            if (button != null) button.interactable = interactable;
        }

        public void OnPointerEnter(PointerEventData eventData) => OnHoverStart?.Invoke(this);

        public void OnPointerExit(PointerEventData eventData) => OnHoverEnd?.Invoke(this);

        private void Hook()
        {
            if (hooked || button == null) return;
            hooked = true;
            button.onClick.AddListener(HandleClick);
        }

        private void HandleClick() => OnClicked?.Invoke(this);
    }
}
