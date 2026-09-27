// 职责：探索场景常驻 Hud——左下角「沉浸」切换按钮（沉浸中改「退出沉浸」并半透明），右下角预留 RunSlot 给后续走 / 跑按钮；只显示与抛点击事件。
// 为什么新建：复用——现有 Hud View（DialogueInteractHudView 只管「对话」卡片、QuestHudView 只管任务栏）职责与布局都不同，
//   且二者沉浸时要隐藏，而这个面板必须留下（VisibleWhenHudHidden = true），不能合在同一个 View 里；
//   扩展——IsometricExploration 模块此前没有任何 UI 面板可扩展。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 探索 HUD。预制体 Addressables 地址须为 <c>ExplorationHudView</c>，根 RectTransform 铺满画布（根上不放 Graphic，不挡世界点击）。
    /// 沉浸模式下仍显示（<see cref="VisibleWhenHudHidden"/> = true），否则玩家点不到「退出沉浸」。
    /// 不注入服务，由 <see cref="ExplorationHudPresenter"/> 驱动。
    /// </summary>
    public sealed class ExplorationHudView : UIView
    {
        [Tooltip("左下角沉浸切换按钮。")]
        [SerializeField] private Button immersiveButton;
        [Tooltip("按钮文字。")]
        [SerializeField] private TMP_Text immersiveLabel;
        [Tooltip("按钮上的 CanvasGroup，沉浸中降低 alpha。")]
        [SerializeField] private CanvasGroup immersiveGroup;
        [Tooltip("右下角预留槽位（下一波放走 / 跑按钮），本面板不往里放东西。")]
        [SerializeField] private RectTransform runSlot;
        [Tooltip("非沉浸时按钮文字。")]
        [SerializeField] private string enterText = "沉浸";
        [Tooltip("沉浸中按钮文字。")]
        [SerializeField] private string exitText = "退出沉浸";
        [Tooltip("沉浸中按钮的 alpha（非沉浸时为 1）。")]
        [SerializeField, Range(0f, 1f)] private float immersiveAlpha = 0.5f;

        // —— PRP/exploration-whitebox 波 3 追加：走跑 / 摇杆 / 触屏键 / 重置 / 交互提示 / 万向标。
        //    全部可空容错，不列入 Validate 必填；由 ExplorationControlsPresenter / ExplorationCompassPresenter 驱动。
        [Tooltip("除沉浸按钮外全部控件的父物体（ControlsRoot）上的 CanvasGroup；沉浸时整体隐藏。可空。")]
        [SerializeField] private CanvasGroup controlsGroup;
        [Tooltip("RunSlot/RunToggle 的标签（「散步」/「奔跑」）。可空。")]
        [SerializeField] private TMP_Text runLabel;
        [Tooltip("RunSlot/RunToggle 上的 CanvasGroup：RunSlot 不在 ControlsRoot 下，沉浸时单独隐藏。可空。")]
        [SerializeField] private CanvasGroup runToggleGroup;
        [Tooltip("虚拟摇杆根物体（底盘 + Knob 上的 OnScreenStick）。可空。")]
        [SerializeField] private GameObject stick;
        [Tooltip("触屏三键（潜行 / 伪装 / 攻击）的父物体。可空。")]
        [SerializeField] private GameObject touchButtons;
        [Tooltip("左上角「重置进度」按钮。可空。")]
        [SerializeField] private Button resetButton;
        [Tooltip("屏幕下方居中的交互提示文字。可空。")]
        [SerializeField] private TMP_Text interactPrompt;
        [Tooltip("万向标父物体（铺满，不挡射线）。可空。")]
        [SerializeField] private RectTransform compassRoot;
        [Tooltip("万向标模板（箭头 Image + 子 Label），运行时隐藏，由呈现器复制。锚点须居中。可空。")]
        [SerializeField] private RectTransform compassMarkerTemplate;

        public override UILayer Layer => UILayer.Hud;
        public override bool IsFullScreen => false;
        public override bool VisibleWhenHudHidden => true;

        /// <summary>玩家点了沉浸切换按钮。</summary>
        public event Action OnImmersiveToggle;

        /// <summary>右下角预留槽位。</summary>
        public RectTransform RunSlot => runSlot;

        /// <summary>玩家点了「重置进度」按钮（波 3 追加）。</summary>
        public event Action OnResetClicked;

        /// <summary>万向标父物体；未接线时为 null（用 == null 判）。</summary>
        public RectTransform CompassRoot => compassRoot;

        /// <summary>万向标模板；未接线时为 null（用 == null 判）。</summary>
        public RectTransform CompassMarkerTemplate => compassMarkerTemplate;

        /// <summary>画布尺寸（本面板根 RectTransform 铺满画布）。</summary>
        public Vector2 CanvasSize => ((RectTransform)transform).rect.size;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            immersiveButton.onClick.RemoveListener(RaiseToggle);
            immersiveButton.onClick.AddListener(RaiseToggle);
            if (resetButton != null)
            {
                resetButton.onClick.RemoveListener(RaiseReset);
                resetButton.onClick.AddListener(RaiseReset);
            }
            if (compassMarkerTemplate != null) compassMarkerTemplate.gameObject.SetActive(false);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (immersiveButton != null) immersiveButton.onClick.RemoveListener(RaiseToggle);
            OnImmersiveToggle = null;
            if (resetButton != null) resetButton.onClick.RemoveListener(RaiseReset);
            OnResetClicked = null;
            return UniTask.CompletedTask;
        }

        /// <summary>按沉浸状态刷新按钮文字与透明度。事件驱动调用，不在每帧路径上。</summary>
        public void SetImmersive(bool immersive)
        {
            immersiveLabel.text = immersive ? exitText : enterText;
            if (immersiveGroup != null) immersiveGroup.alpha = immersive ? immersiveAlpha : 1f;
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。immersiveGroup 可空（不做半透明）。
        private void Validate()
        {
            var missing = new List<string>();
            if (immersiveButton == null) missing.Add(nameof(immersiveButton));
            if (immersiveLabel == null) missing.Add(nameof(immersiveLabel));
            if (runSlot == null) missing.Add(nameof(runSlot));
            if (missing.Count > 0)
                throw new InvalidOperationException("ExplorationHudView 引用未接线：" + string.Join("、", missing));
        }

        private void RaiseToggle() => OnImmersiveToggle?.Invoke();

        /// <summary>沉浸时整体隐藏 ControlsRoot 与 RunToggle（alpha / 交互 / 射线一起切）；未接线时空操作。</summary>
        public void SetControlsVisible(bool visible)
        {
            ApplyGroup(controlsGroup, visible);
            ApplyGroup(runToggleGroup, visible);
        }

        private static void ApplyGroup(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        /// <summary>走跑按钮标签。由呈现器只在状态变化时调用。</summary>
        public void SetRunLabel(string text)
        {
            if (runLabel != null) runLabel.text = text;
        }

        /// <summary>虚拟摇杆显隐。</summary>
        public void SetStickVisible(bool visible)
        {
            if (stick != null && stick.activeSelf != visible) stick.SetActive(visible);
        }

        /// <summary>触屏三键显隐。</summary>
        public void SetTouchButtonsVisible(bool visible)
        {
            if (touchButtons != null && touchButtons.activeSelf != visible) touchButtons.SetActive(visible);
        }

        /// <summary>交互提示；空串或 null 隐藏。</summary>
        public void SetPrompt(string text)
        {
            if (interactPrompt == null) return;
            bool show = !string.IsNullOrEmpty(text);
            interactPrompt.text = show ? text : string.Empty;
            if (interactPrompt.gameObject.activeSelf != show) interactPrompt.gameObject.SetActive(show);
        }

        private void RaiseReset() => OnResetClicked?.Invoke();

        // —— PRP/exploration-whitebox 波 6 追加：PC 优先，走跑按钮与摇杆 / 三键一样默认只在触屏显示。

        /// <summary>
        /// 走跑按钮（RunToggle）显隐：切它所在的 GameObject；与 <see cref="SetControlsVisible"/> 的沉浸 alpha 互不干扰。
        /// 未接线时空操作。
        /// </summary>
        public void SetRunToggleVisible(bool visible)
        {
            if (runToggleGroup == null) return;
            GameObject toggle = runToggleGroup.gameObject;
            if (toggle.activeSelf != visible) toggle.SetActive(visible);
        }
    }
}
