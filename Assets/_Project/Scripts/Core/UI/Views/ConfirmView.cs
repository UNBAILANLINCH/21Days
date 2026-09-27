// 职责：框架层通用确认弹窗——显示正文与确认 / 取消两个按钮，经 WaitAsync 交回选择结果（确认 true，其余 false）。
// 为什么新建（复用 → 扩展 → 新增）：
//   1. 复用不行：ExplorationConfirmView 是探索模块私有 View，别的模块引用它违反「跨模块只走公开接口」；
//      DialogueSkipConfirmView 正文写死、归对白模块。
//   2. 扩展不行：把 ExplorationConfirmView 提升进 Core 要动另一会话正在改的文件（PRP save-session D5 已写明留作后续任务），
//      Core 里现有面板（Title / PauseMenu / Settings / Toast / Notification）职责都不是「二次确认」。
//   所以在 Core 另建一份同构的通用版，形状照 ExplorationConfirmView，参数换成 ConfirmRequest（正文 + 两个按钮文字）。

using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 通用确认弹窗（Popup 层，不全屏）。预制体 <c>Assets/_Project/Prefabs/UI/ConfirmView.prefab</c>，
    /// Addressables 地址 <c>ConfirmView</c>。
    /// <code>
    /// ConfirmView view = await ui.OpenAsync&lt;ConfirmView&gt;(new ConfirmRequest("覆盖这个存档？", "覆盖", "取消"), ct);
    /// bool ok = await view.WaitAsync(ct);
    /// await ui.CloseAsync(view, ct);   // 关闭由调用方负责
    /// </code>
    /// <para>
    /// 被外部关掉（Esc / 手柄 B 走 UICancelRouter 关栈顶）或 <c>ct</c> 取消时 <see cref="WaitAsync"/> 按「取消」返回 false，
    /// 不抛异常、不留未观察的任务。默认选中的控件应拖「取消」：误按确认键的代价（覆盖 / 删除）比误按取消大。
    /// </para>
    /// </summary>
    public sealed class ConfirmView : UIView
    {
        private const string DefaultMessage = "确定吗？";
        private const string DefaultConfirmText = "确定";
        private const string DefaultCancelText = "取消";

        [Tooltip("正文。")]
        [SerializeField] private TMP_Text message;

        [Tooltip("确认按钮。")]
        [SerializeField] private Button confirmButton;

        [Tooltip("确认按钮文字（可空，空则保留预制体原文）。")]
        [SerializeField] private TMP_Text confirmLabel;

        [Tooltip("取消按钮；同时应拖到基类的 Default Selected 上。")]
        [SerializeField] private Button cancelButton;

        [Tooltip("取消按钮文字（可空，空则保留预制体原文）。")]
        [SerializeField] private TMP_Text cancelLabel;

        private UniTaskCompletionSource<bool> pending;

        public override UILayer Layer => UILayer.Popup;

        /// <summary>小弹窗，下层面板保持可见。</summary>
        public override bool IsFullScreen => false;

        /// <summary>Esc / 手柄 B 可关，关掉即按「取消」处理。</summary>
        public override bool CloseOnCancel => true;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            ConfirmRequest request = arg is ConfirmRequest given ? given : default;
            if (arg != null && !(arg is ConfirmRequest))
            {
                Log.Warn($"ConfirmView 的打开参数应为 ConfirmRequest，收到的是 {arg.GetType().Name}，改用默认文案");
            }

            if (message != null)
            {
                message.text = string.IsNullOrEmpty(request.Message) ? DefaultMessage : request.Message;
            }

            if (confirmLabel != null)
            {
                confirmLabel.text = string.IsNullOrEmpty(request.ConfirmText) ? DefaultConfirmText : request.ConfirmText;
            }

            if (cancelLabel != null)
            {
                cancelLabel.text = string.IsNullOrEmpty(request.CancelText) ? DefaultCancelText : request.CancelText;
            }

            Hook(confirmButton, HandleConfirm);
            Hook(cancelButton, HandleCancel);

            // 重复打开（已开着再 OpenAsync）时沿用未决的那一次，已决的换新。
            if (pending == null || pending.Task.Status != UniTaskStatus.Pending)
            {
                pending = new UniTaskCompletionSource<bool>();
            }

            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            Unhook(confirmButton, HandleConfirm);
            Unhook(cancelButton, HandleCancel);

            // 被关掉按取消处理：TrySetResult 而不是 TrySetCanceled，等待方不会收到异常。
            Complete(false);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 等玩家选择：确认 true；取消 / 面板被关 / <paramref name="ct"/> 取消都返回 false（不抛异常）。
        /// 面板没打开过时立即返回 false。
        /// </summary>
        public async UniTask<bool> WaitAsync(CancellationToken ct = default)
        {
            UniTaskCompletionSource<bool> source = pending;
            if (source == null)
            {
                return false;
            }

            using (ct.Register(() => source.TrySetResult(false)))
            {
                return await source.Task;
            }
        }

        private void Complete(bool result)
        {
            if (pending != null)
            {
                pending.TrySetResult(result);
            }
        }

        private void HandleConfirm() => Complete(true);

        private void HandleCancel() => Complete(false);
    }
}
