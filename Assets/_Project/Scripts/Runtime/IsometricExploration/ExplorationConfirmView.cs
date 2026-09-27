// 职责：探索层通用确认弹窗（首个用途：「重置进度」）——显示正文与确认 / 取消两个按钮，经 WaitAsync 交回选择结果。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：DialogueSkipConfirmView 正文写死「是否跳过剧情？」、归 Dialogue 模块，借来用会让重置流程依赖对白模块的界面。
//   2. 扩展不行：ExplorationHudView 是 Hud 层常驻面板，确认弹窗要进 Popup 栈、有自己的遮罩与生命周期，合不到一起。
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
    /// 确认弹窗（Popup 层）。预制体 Addressables 地址须为 <c>ExplorationConfirmView</c>。
    /// <c>OpenAsync&lt;ExplorationConfirmView&gt;(正文字符串)</c> 打开，再 <see cref="WaitAsync"/> 等选择；关闭本面板由调用方负责。
    /// 被别处关掉（返回键关顶层弹窗）时 <see cref="WaitAsync"/> 按「取消」返回 false，不抛异常、不留未观察的任务。
    /// </summary>
    public sealed class ExplorationConfirmView : UIView
    {
        [Tooltip("正文。")]
        [SerializeField] private TMP_Text message;
        [Tooltip("确认按钮。")]
        [SerializeField] private Button confirm;
        [Tooltip("确认按钮文字（可空）。")]
        [SerializeField] private TMP_Text confirmLabel;
        [Tooltip("取消按钮。")]
        [SerializeField] private Button cancel;
        [Tooltip("取消按钮文字（可空）。")]
        [SerializeField] private TMP_Text cancelLabel;
        [Tooltip("半透明遮罩（挡住下层点击，可空）。")]
        [SerializeField] private Image backdrop;
        [Tooltip("打开参数不是字符串时显示的正文。")]
        [SerializeField] private string defaultMessage = "确定吗？";

        private UniTaskCompletionSource<bool> pending;

        public override UILayer Layer => UILayer.Popup;
        public override bool IsFullScreen => false;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            message.text = arg is string text && !string.IsNullOrEmpty(text) ? text : defaultMessage;
            if (backdrop != null) backdrop.raycastTarget = true;
            confirm.onClick.RemoveListener(Confirm);
            cancel.onClick.RemoveListener(Cancel);
            confirm.onClick.AddListener(Confirm);
            cancel.onClick.AddListener(Cancel);
            // 重复打开（已开着再 OpenAsync）时沿用未决的那一次，已决的换新。
            if (pending == null || pending.Task.Status != UniTaskStatus.Pending)
            {
                pending = new UniTaskCompletionSource<bool>();
            }

            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (confirm != null) confirm.onClick.RemoveListener(Confirm);
            if (cancel != null) cancel.onClick.RemoveListener(Cancel);
            // 被关掉按取消处理：TrySetResult 而不是 TrySetCanceled，等待方不会收到异常。
            Complete(false);
            return UniTask.CompletedTask;
        }

        /// <summary>设置两个按钮的文字；标签未接线或传空时保留预制体原文。</summary>
        public void SetButtonLabels(string confirmText, string cancelText)
        {
            if (confirmLabel != null && !string.IsNullOrEmpty(confirmText)) confirmLabel.text = confirmText;
            if (cancelLabel != null && !string.IsNullOrEmpty(cancelText)) cancelLabel.text = cancelText;
        }

        /// <summary>
        /// 等玩家选择：确认 true、取消 / 面板被关 / <paramref name="ct"/> 取消都返回 false（不抛异常）。
        /// 面板没打开过时立即返回 false。
        /// </summary>
        public async UniTask<bool> WaitAsync(CancellationToken ct = default)
        {
            UniTaskCompletionSource<bool> source = pending;
            if (source == null) return false;
            using (ct.Register(() => source.TrySetResult(false)))
            {
                return await source.Task;
            }
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。标签与遮罩可空。
        private void Validate()
        {
            var missing = new List<string>();
            if (message == null) missing.Add(nameof(message));
            if (confirm == null) missing.Add(nameof(confirm));
            if (cancel == null) missing.Add(nameof(cancel));
            if (missing.Count > 0)
                throw new System.InvalidOperationException("ExplorationConfirmView 引用未接线：" + string.Join("、", missing));
        }

        private void Complete(bool result)
        {
            if (pending != null) pending.TrySetResult(result);
        }

        private void Confirm() => Complete(true);
        private void Cancel() => Complete(false);
    }
}
