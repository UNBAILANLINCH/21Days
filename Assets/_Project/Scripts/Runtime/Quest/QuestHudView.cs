// 职责：常驻 Hud 层的任务栏——显示当前追踪任务标题与目标、目标指引标识（屏内悬浮 / 屏外贴边 + 箭头）与距离；只显示与抛点击事件。
// 为什么新建：任务系统首次落地（PRP/quest-system），现有 Hud View（DialogueInteractHudView）只管「对话」按钮，职责与层内布局都不同。
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
    /// 任务 HUD。预制体 Addressables 地址须为 <c>QuestHudView</c>，根 RectTransform 铺满画布。
    /// 面板本身常驻打开，显隐只切 <see cref="root"/> / <see cref="guidanceRoot"/>，不走 UIService 的开关。
    /// 不注入服务，由 <see cref="QuestHudPresenter"/> 驱动。
    /// </summary>
    public sealed class QuestHudView : UIView
    {
        [Tooltip("整条任务栏；对白进行中隐藏。")]
        [SerializeField] private GameObject root;
        [Tooltip("整条任务栏按钮，点击打开任务面板。")]
        [SerializeField] private Button button;
        [Tooltip("任务标题；未追踪时显示占位文字。")]
        [SerializeField] private TMP_Text title;
        [Tooltip("当前目标文本。")]
        [SerializeField] private TMP_Text objective;
        [Tooltip("目标指引标识，锚点须在 HUD 根的中心（anchoredPosition 以画布中心为原点）。")]
        [SerializeField] private RectTransform guidanceRoot;
        [Tooltip("屏外箭头，绕 Z 轴旋转（可空：不显示箭头）。")]
        [SerializeField] private RectTransform guidanceArrow;
        [Tooltip("到目标的距离文字，如「12 m」。")]
        [SerializeField] private TMP_Text distanceLabel;

        private bool visible = true;

        public override UILayer Layer => UILayer.Hud;
        public override bool IsFullScreen => false;

        /// <summary>玩家点了任务栏。</summary>
        public event Action OnClicked;

        /// <summary>画布尺寸（HUD 根铺满画布，取根 RectTransform 的尺寸）。</summary>
        public Vector2 CanvasSize => ((RectTransform)transform).rect.size;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            button.onClick.RemoveListener(RaiseClicked);
            button.onClick.AddListener(RaiseClicked);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (button != null) button.onClick.RemoveListener(RaiseClicked);
            OnClicked = null;
            return UniTask.CompletedTask;
        }

        /// <summary>显示追踪中的任务。</summary>
        public void SetTracked(string questTitle, string objectiveText)
        {
            title.text = questTitle;
            objective.text = objectiveText;
        }

        /// <summary>没有追踪任务：标题显示占位，目标清空。</summary>
        public void SetUntracked(string placeholder)
        {
            title.text = placeholder;
            objective.text = string.Empty;
        }

        /// <summary>
        /// 显示指引标识。每帧调用：只改位置与角度，<paramref name="distance"/> 为 null 时不动距离文本（避免每帧赋字符串）。
        /// 任务栏隐藏期间（<see cref="SetVisible"/>(false)）忽略。
        /// </summary>
        public void SetGuidance(in QuestGuidance g, string distance)
        {
            if (!visible) return;
            if (!guidanceRoot.gameObject.activeSelf) guidanceRoot.gameObject.SetActive(true);
            guidanceRoot.anchoredPosition = g.AnchoredPosition;
            if (guidanceArrow != null)
            {
                GameObject arrow = guidanceArrow.gameObject;
                if (arrow.activeSelf != g.ShowArrow) arrow.SetActive(g.ShowArrow);
                if (g.ShowArrow) guidanceArrow.localEulerAngles = new Vector3(0f, 0f, g.ArrowAngleDeg);
            }

            if (distance != null) distanceLabel.text = distance;
        }

        public void HideGuidance()
        {
            if (guidanceRoot != null && guidanceRoot.gameObject.activeSelf) guidanceRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// 整体显隐。false 同时隐藏任务栏与指引；true 只恢复任务栏，指引由下一次 <see cref="SetGuidance"/> 再显示。
        /// </summary>
        public void SetVisible(bool value)
        {
            visible = value;
            if (root != null && root.activeSelf != value) root.SetActive(value);
            if (!value) HideGuidance();
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。guidanceArrow 可空。
        private void Validate()
        {
            var missing = new List<string>();
            if (root == null) missing.Add(nameof(root));
            if (button == null) missing.Add(nameof(button));
            if (title == null) missing.Add(nameof(title));
            if (objective == null) missing.Add(nameof(objective));
            if (guidanceRoot == null) missing.Add(nameof(guidanceRoot));
            if (distanceLabel == null) missing.Add(nameof(distanceLabel));
            if (missing.Count > 0)
                throw new InvalidOperationException("QuestHudView 引用未接线：" + string.Join("、", missing));
        }

        private void RaiseClicked() => OnClicked?.Invoke();
    }
}
