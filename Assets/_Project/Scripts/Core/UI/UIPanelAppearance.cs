// 职责：显式切换配色，按当前可见头像调整左右布局；不自行判断场景或剧情。
// 为什么新建：TitleView 的切图绑在标题按钮上，不能复用；扩展 UIView 会让所有面板承担可选样式，因此独立挂件。
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI
{
    public sealed class UIPanelAppearance : MonoBehaviour
    {
        [SerializeField] private SpritePair[] sprites = Array.Empty<SpritePair>();
        [SerializeField] private TMP_Text[] texts = Array.Empty<TMP_Text>();
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private VertexGradient alternateGradient = new VertexGradient(Color.white);
        [SerializeField] private Material normalMaterial;
        [SerializeField] private Material alternateMaterial;
        [SerializeField] private RectTransform[] mirroredGraphics = Array.Empty<RectTransform>();
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private RectTransform advancePrompt;
        [SerializeField] private TMP_Text advanceHint;
        [SerializeField] private Image leftPortrait;
        [SerializeField] private Image rightPortrait;
        private bool rightAligned;

        private void OnEnable() => Canvas.willRenderCanvases += RefreshLayout;
        private void OnDisable() => Canvas.willRenderCanvases -= RefreshLayout;

        /// <summary>只读已接线的图片；过渡时跟随较清晰的一侧，不每帧查找对象。</summary>
        public void RefreshLayout()
        {
            bool left = leftPortrait != null && leftPortrait.isActiveAndEnabled && leftPortrait.sprite != null;
            bool right = rightPortrait != null && rightPortrait.isActiveAndEnabled && rightPortrait.sprite != null;
            SetRightAligned(right && (!left || rightPortrait.color.a >= leftPortrait.color.a));
        }

        private void SetRightAligned(bool right)
        {
            if (rightAligned == right) return;
            rightAligned = right;
            foreach (RectTransform rect in mirroredGraphics)
            {
                if (rect == null) continue;
                MirrorRect(rect);
                Vector3 scale = rect.localScale;
                scale.x = -scale.x;
                rect.localScale = scale;
            }
            if (nameLabel != null)
            {
                MirrorRect(nameLabel.rectTransform);
                nameLabel.horizontalAlignment = right ? HorizontalAlignmentOptions.Right : HorizontalAlignmentOptions.Left;
            }
            if (advancePrompt != null) MirrorRect(advancePrompt);
            if (advanceHint != null)
            {
                MirrorRect(advanceHint.rectTransform);
                advanceHint.horizontalAlignment = right ? HorizontalAlignmentOptions.Left : HorizontalAlignmentOptions.Right;
            }
        }

        private static void MirrorRect(RectTransform rect)
        {
            Vector2 min = rect.anchorMin;
            Vector2 max = rect.anchorMax;
            rect.anchorMin = new Vector2(1f - max.x, min.y);
            rect.anchorMax = new Vector2(1f - min.x, max.y);
            rect.pivot = new Vector2(1f - rect.pivot.x, rect.pivot.y);
            rect.anchoredPosition = new Vector2(-rect.anchoredPosition.x, rect.anchoredPosition.y);
        }

        [Serializable]
        private sealed class SpritePair
        {
            [SerializeField] private Image image;
            [SerializeField] private Sprite normal;
            [SerializeField] private Sprite alternate;

            public void Apply(bool useAlternate)
            {
                if (image != null) image.sprite = useAlternate ? alternate : normal;
            }
        }

        /// <summary>切换完整样式，也用于编辑器预览；不改文字、显隐与输入。</summary>
        public void Apply(bool alternate)
        {
            foreach (SpritePair pair in sprites) pair.Apply(alternate);
            foreach (TMP_Text text in texts)
            {
                if (text == null) continue;
                text.color = alternate ? Color.white : normalColor;
                text.enableVertexGradient = alternate;
                text.colorGradient = alternateGradient;
                text.fontSharedMaterial = alternate ? alternateMaterial : normalMaterial;
            }
        }
    }
}
