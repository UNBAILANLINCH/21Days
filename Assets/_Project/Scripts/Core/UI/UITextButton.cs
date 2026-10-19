// 职责：标准 Button 切换美术状态时同步 TMP 字色，保持深色选中底上的文字可读。
// 复用 Button 的 SpriteSwap 与导航；现有 UIButtonFeedback 只管缩放，不能同步状态字色。
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI
{
    public sealed class UITextButton : Button
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Color normalTextColor = new Color(.098f, .341f, .408f);
        [SerializeField] private Color focusedTextColor = new Color(.549f, .792f, .859f);
        [SerializeField] private Color disabledTextColor = new Color(.25f, .4f, .43f, .55f);

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (label == null) return;
            label.color = state == SelectionState.Disabled ? disabledTextColor
                : state == SelectionState.Normal ? normalTextColor : focusedTextColor;
        }
    }
}
