// 职责：跳过剧情前的确认弹窗；只显示与抛确认 / 取消事件，不持有播放策略（PRP 8.2）。
// 为什么新建：DialogueView 是对白主面板、DialogueHistoryView 是只读记录，确认弹窗是独立的 Popup 层界面，
//   需要单独的预制体与 Addressables 地址，塞进任一现有 View 都说不通职责。
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
    /// <summary>跳过确认弹窗。预制体 Addressables 地址须为 <c>DialogueSkipConfirmView</c>。</summary>
    public sealed class DialogueSkipConfirmView : UIView
    {
        private const string DefaultMessage = "是否跳过剧情？";

        [SerializeField] private TMP_Text message;
        [SerializeField] private Button confirm;
        [SerializeField] private Button cancel;

        public override UILayer Layer => UILayer.Popup;
        public override bool IsFullScreen => false;
        public event Action OnConfirm;
        public event Action OnCancel;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            message.text = DefaultMessage;
            confirm.onClick.RemoveListener(Confirm);
            cancel.onClick.RemoveListener(Cancel);
            confirm.onClick.AddListener(Confirm);
            cancel.onClick.AddListener(Cancel);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (confirm != null) confirm.onClick.RemoveListener(Confirm);
            if (cancel != null) cancel.onClick.RemoveListener(Cancel);
            OnConfirm = null;
            OnCancel = null;
            return UniTask.CompletedTask;
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。
        private void Validate()
        {
            var missing = new List<string>();
            if (message == null) missing.Add(nameof(message));
            if (confirm == null) missing.Add(nameof(confirm));
            if (cancel == null) missing.Add(nameof(cancel));
            if (missing.Count > 0)
                throw new InvalidOperationException("DialogueSkipConfirmView 引用未接线：" + string.Join("、", missing));
        }

        private void Confirm() => OnConfirm?.Invoke();
        private void Cancel() => OnCancel?.Invoke();
    }
}
