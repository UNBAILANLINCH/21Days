// 职责：NPC 头顶交互标记——可交互时灰「…」，成为焦点时白「!」并显示名字，不可交互时全隐藏；
//   台词气泡显示期间让位（隐藏标记与名字），避免与气泡重叠（PRP 8.1 / 8.5）。
// 为什么新建：取代一期临时的 DialogueInteractableHint（染色 + 「点击交谈」文字），表现方式整件替换；
//   复用——Hint 的职责是按悬停染色，改成「按焦点切两张图 + 名字」后名字和职责都对不上；
//   扩展——塞进 DialogueInteractable 会让逻辑组件依赖 SpriteRenderer / TMP，与一期拆分 Hint 的理由相同。
using TMPro;
using UnityEngine;

namespace Game.Dialogue
{
    /// <summary>
    /// 三态标记：不可交互 → 全隐藏；可交互未聚焦 → <see cref="bubbleIdle"/>；焦点 → <see cref="bubbleFocused"/> + 名字。
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
            bool focused = target.Focused && !speechShowing;
            bool idle = !target.Focused && !speechShowing && target.CanInteract;
            SetShown(bubbleFocused, focused);
            SetShown(bubbleIdle, idle);
            if (nameLabel != null && nameLabel.gameObject.activeSelf != focused) nameLabel.gameObject.SetActive(focused);
        }

        private static void SetShown(SpriteRenderer sprite, bool shown)
        {
            if (sprite != null && sprite.gameObject.activeSelf != shown) sprite.gameObject.SetActive(shown);
        }
    }
}
