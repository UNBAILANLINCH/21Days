// 职责：右下角常驻「对话」按钮——有交互焦点时显示，点击抛 OnInteract（PRP 8.1）。
// 为什么新建：DialogueView 是对白进行中的主面板（Panel 层、拉起后才开），这个按钮在对白之外常驻 Hud 层，
//   生命周期与层级都不同；需要单独的预制体与 Addressables 地址，塞进现有 View 说不通职责。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Dialogue
{
    /// <summary>
    /// 交互 HUD。预制体 Addressables 地址须为 <c>DialogueInteractHudView</c>。
    /// 面板本身常驻打开，显隐只切 <see cref="root"/>，不走 UIService 的开关（避免每次进出范围都实例化 / 淡入淡出）。
    /// </summary>
    public sealed class DialogueInteractHudView : UIView
    {
        private const string DefaultLabel = "对话";

        [Tooltip("整张卡片；有焦点时激活。")]
        [SerializeField] private GameObject root;
        [Tooltip("整卡按钮。")]
        [SerializeField] private Button button;
        [Tooltip("卡片底部文字，固定「对话」。")]
        [SerializeField] private TMP_Text label;
        [Tooltip("卡片图标（占位，可空）。")]
        [SerializeField] private Image icon;

        public override UILayer Layer => UILayer.Hud;
        public override bool IsFullScreen => false;

        /// <summary>玩家点了「对话」按钮。</summary>
        public event Action OnInteract;

        /// <summary>卡片当前是否显示。</summary>
        public bool IsShown => root != null && root.activeSelf;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            label.text = DefaultLabel;
            button.onClick.RemoveListener(RaiseInteract);
            button.onClick.AddListener(RaiseInteract);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (button != null) button.onClick.RemoveListener(RaiseInteract);
            OnInteract = null;
            return UniTask.CompletedTask;
        }

        /// <summary>显示卡片。npcName 暂不显示（卡片只写「对话」），留作以后加名字副标题。</summary>
        public void Show(string npcName)
        {
            if (root != null && !root.activeSelf) root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。icon 是占位，可空。
        private void Validate()
        {
            var missing = new List<string>();
            if (root == null) missing.Add(nameof(root));
            if (button == null) missing.Add(nameof(button));
            if (label == null) missing.Add(nameof(label));
            if (missing.Count > 0)
                throw new InvalidOperationException("DialogueInteractHudView 引用未接线：" + string.Join("、", missing));
        }

        private void RaiseInteract() => OnInteract?.Invoke();
    }
}
