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
        /// <summary>
        /// 不让通用取消路由直接关：弹窗的开关由 DialogueController 持有（覆盖中标记、事件退订），被 UIService 越过 Controller 关掉
        /// 会让两边状态对不上。Esc 由 <see cref="DialogueKeyboardInput"/> 翻成「点取消」走 Controller 的正常收尾。
        /// </summary>
        public override bool CloseOnCancel => false;
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
            // 默认选中「取消」由预制体的 UIView.defaultSelected（= CancelButton）交给 UIService 在打开后设置：
            // 键盘 Enter / 手柄 A 走 UI Submit 点中选中项，误触不会跳过剧情；Esc 由对白键位处理为取消。
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
