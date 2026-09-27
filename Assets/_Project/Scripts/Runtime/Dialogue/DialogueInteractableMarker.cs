// 职责：NPC 头顶交互标记——可交互时灰「…」，成为焦点时白「!」并显示名字，不可交互时全隐藏；
//   台词气泡显示期间让位（隐藏标记与名字），避免与气泡重叠（PRP 8.1 / 8.5）；
//   头顶图标被外部世界标记接管（DialogueInteractable.MarkerOverridden，如任务目标标记）时只隐「…/!」图标，焦点名字照常；
//   TryGetIconAnchor 给出图标的世界位置，接管方摆到同一处。
// 为什么新建：取代一期临时的 DialogueInteractableHint（染色 + 「点击交谈」文字），表现方式整件替换；
//   复用——Hint 的职责是按悬停染色，改成「按焦点切两张图 + 名字」后名字和职责都对不上；
//   扩展——塞进 DialogueInteractable 会让逻辑组件依赖 SpriteRenderer / TMP，与一期拆分 Hint 的理由相同。
using TMPro;
using UnityEngine;

namespace Game.Dialogue
{
    /// <summary>
    /// 三态标记：不可交互 → 全隐藏；可交互未聚焦 → <see cref="bubbleIdle"/>；焦点 → <see cref="bubbleFocused"/> + 名字。
    /// 图标被外部标记接管（<see cref="DialogueInteractable.MarkerOverridden"/>）时两张图都隐藏、焦点时名字仍显示——
    /// 由接管方的标记代替本组件的图标，而不是叠在一起。
    /// 朝向相机由各子物体上的 CameraBillboard 负责（2D 场景不挂）。每帧只做比较与赋值，不分配。
    /// </summary>
    public sealed class DialogueInteractableMarker : MonoBehaviour
    {
        [Tooltip("读取状态的交互组件。")]
        [SerializeField] private DialogueInteractable target;
        [Tooltip("可交互但不是焦点时显示（灰「…」）。")]
        [SerializeField] private SpriteRenderer bubbleIdle;
        [Tooltip("是焦点时显示（白「!」）。")]
        [SerializeField] private SpriteRenderer bubbleFocused;
        [Tooltip("头顶名字（TMP 3D），只在焦点时显示，文字取 target.DisplayName。")]
        [SerializeField] private TMP_Text nameLabel;
        [Tooltip("台词气泡，可空；显示中隐藏标记与名字。")]
        [SerializeField] private DialogueSpeechBubble speechBubble;

        private void Start()
        {
            if (nameLabel != null && target != null) nameLabel.text = target.DisplayName;
        }

        private void Update()
        {
            if (target == null) return;
            bool speechShowing = speechBubble != null && speechBubble.IsShowing;
            ResolveVisibility(target.CanInteract, target.Focused, speechShowing, target.HiddenByHud, target.MarkerOverridden,
                out bool iconFocused, out bool iconIdle, out bool nameShown);
            SetShown(bubbleFocused, iconFocused);
            SetShown(bubbleIdle, iconIdle);
            if (nameLabel != null && nameLabel.gameObject.activeSelf != nameShown) nameLabel.gameObject.SetActive(nameShown);
        }

        /// <summary>
        /// 三个显隐结果的纯判定（无 Unity 依赖，供 Update 与 EditMode 测试共用）：
        /// 台词气泡显示中或沉浸模式隐藏时全隐；焦点 → 白「!」+ 名字；可交互未聚焦 → 灰「…」；
        /// 图标被外部标记接管时两张图都隐，名字仍只看焦点。
        /// 公开是为了测试程序集可调（Game.Runtime 未对测试开 InternalsVisibleTo），同 QuestHudPresenter.ShouldOpenOnJournal。
        /// </summary>
        public static void ResolveVisibility(bool canInteract, bool focused, bool speechShowing, bool hiddenByHud,
            bool markerOverridden, out bool iconFocused, out bool iconIdle, out bool nameShown)
        {
            bool visible = !speechShowing && !hiddenByHud;
            nameShown = focused && visible;
            iconFocused = nameShown && !markerOverridden;
            iconIdle = !focused && visible && canInteract && !markerOverridden;
        }

        /// <summary>
        /// 头顶图标的世界锚点：外部世界标记接管本图标时摆到同一位置，看起来是同一个图标换了样子。
        /// 优先取焦点图（白「!」），其次取可交互图（灰「…」）；两张都没配返回 false。
        /// 图标物体当前隐藏也照样返回其位置。每帧可调：只读 transform，无分配、无 GetComponent。
        /// </summary>
        public bool TryGetIconAnchor(out Vector3 worldPosition)
        {
            SpriteRenderer icon = bubbleFocused != null ? bubbleFocused : bubbleIdle;
            if (icon == null)
            {
                worldPosition = default;
                return false;
            }

            worldPosition = icon.transform.position;
            return true;
        }

        private static void SetShown(SpriteRenderer sprite, bool shown)
        {
            if (sprite != null && sprite.gameObject.activeSelf != shown) sprite.gameObject.SetActive(shown);
        }
    }
}
